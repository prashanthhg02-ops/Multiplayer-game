using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

internal sealed class ArenaNetwork : IDisposable
{
    private const int MaxPlayers = 8;
    private const float MoveSpeed = 6f;
    private const float SnapshotInterval = 0.05f;
    private const float ClientTimeout = 12f;

    private readonly UdpClient _socket;
    private readonly bool _isServer;
    private readonly IPEndPoint _serverEndpoint;
    private readonly Dictionary<int, PlayerSnapshot> _players = new Dictionary<int, PlayerSnapshot>();
    private readonly Dictionary<int, Vector2> _inputs = new Dictionary<int, Vector2>();
    private readonly Dictionary<string, int> _clientIds = new Dictionary<string, int>();
    private readonly Dictionary<int, IPEndPoint> _clientEndpoints = new Dictionary<int, IPEndPoint>();
    private readonly Dictionary<int, float> _lastSeen = new Dictionary<int, float>();
    private float _nextHandshake;
    private float _nextInput;
    private float _nextSnapshot;
    private int _nextPlayerId = 2;
    private bool _disposed;

    public string Status { get; private set; }
    public int LocalPlayerId { get; private set; }
    public int TaggedPlayerId { get; private set; }
    public bool IsServer { get { return _isServer; } }
    public int PlayerCount { get { return _players.Count; } }
    public IEnumerable<PlayerSnapshot> Players { get { return _players.Values; } }

    private ArenaNetwork(UdpClient socket, bool isServer, IPEndPoint serverEndpoint)
    {
        _socket = socket;
        _isServer = isServer;
        _serverEndpoint = serverEndpoint;
        _socket.Client.Blocking = false;
    }

    public static ArenaNetwork CreateHost(int port)
    {
        UdpClient socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        ArenaNetwork network = new ArenaNetwork(socket, true, null);
        network.LocalPlayerId = 1;
        network.TaggedPlayerId = 1;
        network._players.Add(1, new PlayerSnapshot { id = 1, x = -3f, z = 0f });
        network._inputs.Add(1, Vector2.zero);
        network.Status = "Hosting on UDP port " + port + ".";
        return network;
    }

    public static ArenaNetwork CreateClient(IPAddress address, int port)
    {
        UdpClient socket = new UdpClient(address.AddressFamily);
        ArenaNetwork network = new ArenaNetwork(socket, false, new IPEndPoint(address, port));
        network.Status = "Connecting to " + address + ":" + port + "...";
        network.SendHello();
        return network;
    }

    public void Tick(Vector2 localInput, float deltaTime, float now)
    {
        if (_disposed)
        {
            return;
        }

        ReceiveAvailable(now);
        if (_isServer)
        {
            _inputs[LocalPlayerId] = localInput;
            StepPlayers(deltaTime);
            RemoveTimedOutClients(now);
            if (now >= _nextSnapshot)
            {
                BroadcastSnapshot();
                _nextSnapshot = now + SnapshotInterval;
            }
        }
        else
        {
            if (now >= _nextInput)
            {
                SendInput(localInput);
                _nextInput = now + SnapshotInterval;
            }

            if (LocalPlayerId == 0 && now >= _nextHandshake)
            {
                SendHello();
                _nextHandshake = now + 1f;
            }
        }
    }

    public void SetTaggedPlayer(int playerId)
    {
        if (_isServer && _players.ContainsKey(playerId))
        {
            TaggedPlayerId = playerId;
        }
    }

    public PlayerSnapshot FindPlayer(int playerId)
    {
        _players.TryGetValue(playerId, out PlayerSnapshot player);
        return player;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _socket.Close();
    }

    private void ReceiveAvailable(float now)
    {
        while (true)
        {
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            byte[] bytes;
            try
            {
                bytes = _socket.Receive(ref sender);
            }
            catch (SocketException exception) when (
                exception.SocketErrorCode == SocketError.WouldBlock ||
                exception.SocketErrorCode == SocketError.IOPending ||
                exception.SocketErrorCode == SocketError.NoBufferSpaceAvailable)
            {
                return;
            }
            catch (SocketException exception)
            {
                Status = "Network receive failed: " + exception.Message;
                return;
            }

            WirePacket packet;
            try
            {
                packet = JsonUtility.FromJson<WirePacket>(Encoding.UTF8.GetString(bytes));
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (packet == null)
            {
                continue;
            }

            if (_isServer)
            {
                HandleServerPacket(packet, sender, now);
            }
            else if (sender.Equals(_serverEndpoint))
            {
                HandleClientPacket(packet);
            }
        }
    }

    private void HandleServerPacket(WirePacket packet, IPEndPoint sender, float now)
    {
        string endpointKey = sender.ToString();
        if (packet.type == "hello")
        {
            if (_clientIds.TryGetValue(endpointKey, out int existingId))
            {
                _lastSeen[existingId] = now;
                SendTo(sender, new WirePacket { type = "welcome", id = existingId });
                return;
            }

            if (_players.Count >= MaxPlayers)
            {
                SendTo(sender, new WirePacket { type = "full" });
                return;
            }

            int id = _nextPlayerId++;
            Vector2 spawn = SpawnPosition(id);
            _clientIds.Add(endpointKey, id);
            _clientEndpoints.Add(id, sender);
            _lastSeen.Add(id, now);
            _players.Add(id, new PlayerSnapshot { id = id, x = spawn.x, z = spawn.y });
            _inputs.Add(id, Vector2.zero);
            SendTo(sender, new WirePacket { type = "welcome", id = id });
            Status = "A player joined. Players: " + _players.Count + ".";
            return;
        }

        if (_clientIds.TryGetValue(endpointKey, out int playerId))
        {
            _lastSeen[playerId] = now;
            if (packet.type == "input")
            {
                Vector2 input = new Vector2(packet.inputX, packet.inputZ);
                _inputs[playerId] = input.sqrMagnitude > 1f ? input.normalized : input;
            }
        }
    }

    private void HandleClientPacket(WirePacket packet)
    {
        if (packet.type == "welcome")
        {
            LocalPlayerId = packet.id;
            Status = "Connected to the host.";
        }
        else if (packet.type == "full")
        {
            Status = "This game already has the maximum of " + MaxPlayers + " players.";
        }
        else if (packet.type == "state" && packet.players != null)
        {
            TaggedPlayerId = packet.taggedId;
            _players.Clear();
            foreach (WirePlayer player in packet.players)
            {
                if (player != null)
                {
                    _players[player.id] = new PlayerSnapshot { id = player.id, x = player.x, z = player.z };
                }
            }

            if (LocalPlayerId != 0)
            {
                Status = "Connected. Players: " + _players.Count + ".";
            }
        }
    }

    private void StepPlayers(float deltaTime)
    {
        foreach (KeyValuePair<int, PlayerSnapshot> entry in _players)
        {
            PlayerSnapshot player = entry.Value;
            Vector2 input = _inputs.TryGetValue(player.id, out Vector2 value) ? value : Vector2.zero;
            Vector2 position = new Vector2(player.x, player.z) + input * MoveSpeed * deltaTime;
            position.x = Mathf.Clamp(position.x, -ArenaRadius, ArenaRadius);
            position.y = Mathf.Clamp(position.y, -ArenaRadius, ArenaRadius);
            player.x = position.x;
            player.z = position.y;
        }
    }

    private void RemoveTimedOutClients(float now)
    {
        List<int> expiredIds = new List<int>();
        foreach (KeyValuePair<int, float> entry in _lastSeen)
        {
            if (now - entry.Value > ClientTimeout)
            {
                expiredIds.Add(entry.Key);
            }
        }

        foreach (int id in expiredIds)
        {
            _players.Remove(id);
            _inputs.Remove(id);
            _lastSeen.Remove(id);
            _clientEndpoints.Remove(id);

            string expiredEndpoint = null;
            foreach (KeyValuePair<string, int> entry in _clientIds)
            {
                if (entry.Value == id)
                {
                    expiredEndpoint = entry.Key;
                    break;
                }
            }

            if (expiredEndpoint != null)
            {
                _clientIds.Remove(expiredEndpoint);
            }

            if (TaggedPlayerId == id)
            {
                TaggedPlayerId = _players.Count > 0 ? FirstPlayerId() : 0;
            }
        }
    }

    private void SendHello()
    {
        SendTo(_serverEndpoint, new WirePacket { type = "hello" });
    }

    private void SendInput(Vector2 input)
    {
        SendTo(_serverEndpoint, new WirePacket
        {
            type = "input",
            inputX = input.x,
            inputZ = input.y
        });
    }

    private void BroadcastSnapshot()
    {
        WirePlayer[] players = new WirePlayer[_players.Count];
        int index = 0;
        foreach (PlayerSnapshot player in _players.Values)
        {
            players[index++] = new WirePlayer { id = player.id, x = player.x, z = player.z };
        }

        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new WirePacket
        {
            type = "state",
            taggedId = TaggedPlayerId,
            players = players
        }));

        foreach (IPEndPoint endpoint in _clientEndpoints.Values)
        {
            SendBytes(endpoint, bytes);
        }
    }

    private void SendTo(IPEndPoint endpoint, WirePacket packet)
    {
        if (endpoint == null)
        {
            return;
        }

        SendBytes(endpoint, Encoding.UTF8.GetBytes(JsonUtility.ToJson(packet)));
    }

    private void SendBytes(IPEndPoint endpoint, byte[] bytes)
    {
        try
        {
            _socket.Send(bytes, bytes.Length, endpoint);
        }
        catch (SocketException exception)
        {
            Status = "Network send failed: " + exception.Message;
        }
    }

    private int FirstPlayerId()
    {
        foreach (int id in _players.Keys)
        {
            return id;
        }

        return 0;
    }

    private static Vector2 SpawnPosition(int id)
    {
        float angle = id * 2.4f;
        return new Vector2(Mathf.Cos(angle) * 6f, Mathf.Sin(angle) * 6f);
    }

    [Serializable]
    private sealed class WirePacket
    {
        public string type;
        public int id;
        public float inputX;
        public float inputZ;
        public int taggedId;
        public WirePlayer[] players;
    }

    [Serializable]
    private sealed class WirePlayer
    {
        public int id;
        public float x;
        public float z;
    }
}

internal sealed class PlayerSnapshot
{
    public int id;
    public float x;
    public float z;
}
