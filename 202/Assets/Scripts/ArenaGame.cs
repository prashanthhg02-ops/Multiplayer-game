using System.Collections.Generic;
using UnityEngine;

public sealed class ArenaGame : MonoBehaviour
{
    private const int DefaultPort = 7777;
    private const float ArenaRadius = 12f;

    private readonly Dictionary<int, GameObject> _playerObjects = new Dictionary<int, GameObject>();
    private ArenaNetwork _network;
    private Camera _camera;
    private string _address = "127.0.0.1";
    private string _portText = DefaultPort.ToString();
    private string _status = "Host a game or join a host on your local network.";
    private float _tagCooldown;
    private int _taggedPlayerId;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<ArenaGame>() != null)
        {
            return;
        }

        GameObject game = new GameObject("Tag Arena");
        DontDestroyOnLoad(game);
        game.AddComponent<ArenaGame>();
    }

    private void Awake()
    {
        Application.runInBackground = true;
        CreateArena();
    }

    private void Update()
    {
        if (_network == null)
        {
            return;
        }

        Vector2 input = ReadMovementInput();
        _network.Tick(input, Time.deltaTime, Time.realtimeSinceStartup);
        _status = _network.Status;

        if (_network.IsServer)
        {
            UpdateTagging(Time.deltaTime);
        }

        SyncPlayerViews();
    }

    private void OnGUI()
    {
        GUI.skin.label.fontSize = 16;
        GUI.skin.button.fontSize = 16;
        GUI.skin.textField.fontSize = 16;

        GUILayout.BeginArea(new Rect(18, 18, 370, Screen.height - 36));
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("TAG ARENA", HeadingStyle());
        GUILayout.Label("WASD or arrow keys to move. Get close to pass the tag.");
        GUILayout.Space(8);

        if (_network == null)
        {
            GUILayout.Label("Join by IP address");
            _address = GUILayout.TextField(_address, 64);
            GUILayout.Label("UDP port");
            _portText = GUILayout.TextField(_portText, 5);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host game", GUILayout.Height(38)))
            {
                StartHost();
            }

            if (GUILayout.Button("Join game", GUILayout.Height(38)))
            {
                StartClient();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label("Share your IP address and UDP port 7777 with other players.");
        }
        else
        {
            GUILayout.Label(_network.IsServer ? "HOST" : "PLAYER");
            GUILayout.Label("Players: " + _network.PlayerCount);
            GUILayout.Label("You: " + (_network.LocalPlayerId == 0 ? "connecting..." : "#" + _network.LocalPlayerId));
            GUILayout.Label(_network.LocalPlayerId == _taggedPlayerId ? "YOU ARE IT! Tag someone!" : "Avoid the player marked IT.");

            if (GUILayout.Button("Leave game", GUILayout.Height(34)))
            {
                LeaveGame();
            }
        }

        GUILayout.Space(8);
        GUILayout.Label(_status, GUILayout.Height(54));
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        if (_network != null)
        {
            _network.Dispose();
            _network = null;
        }
    }

    private void StartHost()
    {
        if (!TryGetPort(out int port))
        {
            _status = "Enter a valid UDP port from 1 to 65535.";
            return;
        }

        try
        {
            _network = ArenaNetwork.CreateHost(port);
            _taggedPlayerId = _network.LocalPlayerId;
            _status = _network.Status;
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            _status = "Could not host on port " + port + ": " + exception.Message;
        }
    }

    private void StartClient()
    {
        if (!TryGetPort(out int port))
        {
            _status = "Enter a valid UDP port from 1 to 65535.";
            return;
        }

        if (!System.Net.IPAddress.TryParse(_address.Trim(), out System.Net.IPAddress ipAddress) ||
            ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            _status = "Enter a valid IPv4 address.";
            return;
        }

        try
        {
            _network = ArenaNetwork.CreateClient(ipAddress, port);
            _status = _network.Status;
        }
        catch (System.Net.Sockets.SocketException exception)
        {
            _status = "Could not start the network client: " + exception.Message;
        }
    }

    private bool TryGetPort(out int port)
    {
        return int.TryParse(_portText, out port) && port > 0 && port <= 65535;
    }

    private void LeaveGame()
    {
        if (_network != null)
        {
            _network.Dispose();
            _network = null;
        }

        foreach (GameObject playerObject in _playerObjects.Values)
        {
            Destroy(playerObject);
        }
        _playerObjects.Clear();
        _taggedPlayerId = 0;
        _status = "You left the game.";
    }

    private void CreateArena()
    {
        _camera = Camera.main;
        if (_camera == null)
        {
            GameObject cameraObject = new GameObject("Arena Camera");
            _camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
        }

        _camera.transform.position = new Vector3(0f, 25f, 0f);
        _camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        _camera.orthographic = true;
        _camera.orthographicSize = 15f;
        _camera.backgroundColor = new Color(0.055f, 0.09f, 0.13f);

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Arena Floor";
        ground.transform.localScale = new Vector3(2.6f, 1f, 2.6f);
        ground.GetComponent<Renderer>().material.color = new Color(0.12f, 0.3f, 0.29f);

        CreateWall(new Vector3(0f, 0.5f, ArenaRadius), new Vector3(ArenaRadius * 2f, 1f, 0.5f));
        CreateWall(new Vector3(0f, 0.5f, -ArenaRadius), new Vector3(ArenaRadius * 2f, 1f, 0.5f));
        CreateWall(new Vector3(ArenaRadius, 0.5f, 0f), new Vector3(0.5f, 1f, ArenaRadius * 2f));
        CreateWall(new Vector3(-ArenaRadius, 0.5f, 0f), new Vector3(0.5f, 1f, ArenaRadius * 2f));

        if (FindObjectOfType<Light>() == null)
        {
            GameObject lightObject = new GameObject("Arena Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }
    }

    private static void CreateWall(Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Arena Boundary";
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material.color = new Color(0.12f, 0.19f, 0.23f);
    }

    private static GUIStyle HeadingStyle()
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = 25,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.35f, 0.94f, 0.79f) }
        };
    }

    private static Vector2 ReadMovementInput()
    {
        float horizontal = 0f;
        float vertical = 0f;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontal += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) vertical -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) vertical += 1f;

        Vector2 input = new Vector2(horizontal, vertical);
        return input.sqrMagnitude > 1f ? input.normalized : input;
    }

    private void UpdateTagging(float deltaTime)
    {
        _tagCooldown = Mathf.Max(0f, _tagCooldown - deltaTime);
        if (_tagCooldown > 0f)
        {
            return;
        }

        PlayerSnapshot tagged = _network.FindPlayer(_taggedPlayerId);
        if (tagged == null)
        {
            return;
        }

        foreach (PlayerSnapshot other in _network.Players)
        {
            if (other.id == _taggedPlayerId)
            {
                continue;
            }

            Vector2 taggedPosition = new Vector2(tagged.x, tagged.z);
            Vector2 otherPosition = new Vector2(other.x, other.z);
            if (Vector2.Distance(taggedPosition, otherPosition) < 1.15f)
            {
                _taggedPlayerId = other.id;
                _network.SetTaggedPlayer(_taggedPlayerId);
                _tagCooldown = 1.25f;
                break;
            }
        }
    }

    private void SyncPlayerViews()
    {
        if (_network == null)
        {
            return;
        }

        _taggedPlayerId = _network.TaggedPlayerId;
        HashSet<int> visibleIds = new HashSet<int>();
        foreach (PlayerSnapshot player in _network.Players)
        {
            visibleIds.Add(player.id);
            if (!_playerObjects.TryGetValue(player.id, out GameObject playerObject))
            {
                playerObject = CreatePlayerView(player.id);
                _playerObjects.Add(player.id, playerObject);
            }

            playerObject.transform.position = new Vector3(player.x, 0.55f, player.z);
            playerObject.GetComponent<Renderer>().material.color =
                player.id == _taggedPlayerId ? new Color(1f, 0.24f, 0.19f) : PlayerColor(player.id);

            TextMesh label = playerObject.GetComponentInChildren<TextMesh>();
            label.text = player.id == _taggedPlayerId ? "IT" : "#" + player.id;
        }

        List<int> staleIds = new List<int>();
        foreach (KeyValuePair<int, GameObject> entry in _playerObjects)
        {
            if (!visibleIds.Contains(entry.Key))
            {
                Destroy(entry.Value);
                staleIds.Add(entry.Key);
            }
        }

        foreach (int staleId in staleIds)
        {
            _playerObjects.Remove(staleId);
        }
    }

    private static GameObject CreatePlayerView(int id)
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        player.name = "Player " + id;
        player.transform.localScale = new Vector3(0.85f, 0.55f, 0.85f);
        player.GetComponent<Renderer>().material.color = PlayerColor(id);

        GameObject labelObject = new GameObject("Player Label");
        labelObject.transform.SetParent(player.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        labelObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.18f;
        label.fontSize = 48;
        label.color = Color.white;
        return player;
    }

    private static Color PlayerColor(int id)
    {
        Color[] colors =
        {
            new Color(0.24f, 0.72f, 1f),
            new Color(0.3f, 0.9f, 0.52f),
            new Color(1f, 0.72f, 0.25f),
            new Color(0.75f, 0.45f, 1f),
            new Color(1f, 0.42f, 0.65f),
            new Color(0.3f, 0.9f, 0.88f),
            new Color(0.95f, 0.86f, 0.38f)
        };
        return colors[(id - 1) % colors.Length];
    }
}
