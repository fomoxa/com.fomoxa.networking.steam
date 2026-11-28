# Fomoxa Networking Steam

`com.fomoxa.networking.steam` adds a Steam transport to `com.fomoxa.networking`. A client finds the host by its SteamID instead of an IP address, and Steam carries the traffic, through Valve's relay network when the two computers cannot reach each other directly. A player can therefore host a game from behind NAT without opening a port.

| | |
|---|---|
| Version | 0.1.4 |
| Needs | `com.fomoxa.networking` 0.6.0, Steamworks.NET 2025.165.0 or later |
| Unity | 6000.5 or later |
| Platforms | Windows, Linux and macOS standalone builds, with the Steam client |
| License | Apache-2.0 |

## Installation

Install `com.fomoxa.networking` first. Then open **Window > Package Manager**, click **+**, choose **Install package from git URL**, and add Steamworks.NET:

```
https://github.com/rlabrecque/Steamworks.NET.git?path=/com.rlabrecque.steamworks.net#2025.165.0
```

Then add this package the same way:

```
https://github.com/fomoxa/com.fomoxa.networking.steam.git#0.1.4
```

The package does not ship Steam's libraries; Steamworks.NET brings them. `SteamNetworkTransport` compiles only when Steamworks.NET is installed and the build target is Windows, Linux or macOS.

The Unity Package Manager does not accept a Git URL as a dependency, so the package checks the version of `com.fomoxa.networking` when the `NetworkManager` creates the transport, and throws `InvalidOperationException` on a mismatch.

## Usage

1. Start Steam in your game: call `SteamAPI.InitEx` before you start a server or a client, call `SteamAPI.RunCallbacks()` every frame, and call `SteamAPI.Shutdown()` on exit. The transport does not start Steam.
2. Put `steam_appid.txt` with your AppID (480 for development) in the project's root folder and next to your builds.
3. Add `SteamNetworkTransport` to a GameObject and assign it to the **Transport** field of the `NetworkManager`.
4. The host starts a server on a Steam virtual port and joins in host mode. Other players connect with the host's SteamID64 as the address:

```csharp
networkManager.ClientManager.StartConnection("76561198000000001", 7777);
```

On the server, `ServerAuthenticationContext.ConnectionData` holds the client's SteamID as 8 bytes, little-endian.

The package has no dedicated or console server over Steam, and no lobbies, invites or matchmaking.

## Documentation

The [Steam transport page](https://github.com/fomoxa/unity-docs/blob/main/connections/transports/steam.md) covers the setup, the connection data, the behavior of the transport and troubleshooting.
