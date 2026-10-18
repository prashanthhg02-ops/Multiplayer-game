# Tag Arena

A small, playable Unity LAN multiplayer game. Players move around a top-down arena; whoever is marked **IT** passes the tag by getting close to another player.

## Run it

1. Open this folder with Unity 2022.3 LTS or newer.
2. Open or create a scene, then press **Play**. The arena, camera, player visuals, and menus are generated at runtime.
3. On one machine, select **Host game**.
4. On other machines, enter the host's LAN IP address and select **Join game**. Leave the port at `7777`.
5. Allow the game through the host machine's firewall if prompted. Internet play requires forwarding UDP port `7777` on the host's router.

The host can also join its own game by opening a second game instance and connecting to `127.0.0.1`.

## Controls

- **WASD** or **arrow keys**: move
- Get close to another player while **IT** to pass the tag
- **Leave game**: disconnect and return to the menu

The host simulates player movement and tag transfers. The game supports up to 8 players and uses a lightweight UDP protocol, so no external networking package or service is required. To make a standalone build, save the play scene and add it under **File > Build Settings > Scenes In Build**.
