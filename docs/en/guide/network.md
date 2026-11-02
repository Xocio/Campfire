# Network and Privacy

This page covers the multiplayer settings, found under **Settings › Multiplayer**. They can also be opened with the **Multiplayer settings** tile on Home, or by clicking the relay server lamp on the title bar.

::: info
Changes are saved immediately and apply the next time a room is created or joined.
:::

## How players connect

Campfire places the players in an encrypted virtual network, and game traffic travels inside it. There are two kinds of connection:

| Kind | Path | Characteristics |
| --- | --- | --- |
| **Direct** | Peer-to-peer between players | Lowest latency, close to a local network |
| **Relay** | Forwarded by a relay node | Works when both networks are restrictive, at the cost of extra latency |

Campfire always tries a direct connection first. Whether it succeeds depends mostly on both networks: the multiplayer page shows this computer's NAT type (Open, NAT1 to NAT4) and whether it has IPv6. Two players with IPv6 can almost always connect directly; two players behind NAT4 usually need a relay.

**Nodes** only help players find each other; game traffic never passes through Campfire's servers. Traffic goes through a relay node only when relaying is allowed and a direct connection fails.

What each party can see while you play, and why the networking component cannot reach other services on your computer, is described in [Network Security](./security#multiplayer). When connecting fails, run [Diagnostics](./room#diagnostics) first.

## Room

| Option | Description |
| --- | --- |
| **Player name** | The name shown in the room. When empty, the game name is used |
| **Room display name** | The room name shown in the multiplayer list after joining |
| **Local port** | The port on this computer used to reach the host's game when joining. Usually left empty for automatic selection |
| **Host search limit** | How long to wait for the host when joining: 30, 60 or 120 seconds |
| **Copy the invite code after creating a room** | Puts the code on the clipboard as soon as the room is ready |
| **Detect invite codes on the clipboard** | Home offers to join as soon as you copy a code |
| **Open to LAN automatically when creating a room** | After creating a room, Campfire opens your singleplayer world to LAN once you enter it; see [Opening to LAN automatically](./multiplayer#opening-to-lan-automatically) |
| **Back up the world before opening a room** | Saves a zip copy of the world being played when a room opens. The latest 5 copies of each world are kept; worlds above 2 GB are skipped. See [World backups](./room#world-backups) |

## Mods

| Option | Description |
| --- | --- |
| **Let players sync my mods** | Players in the room can compare their mods with yours and sync them. It can also be switched on the room page while a room is open |
| **Let players download mods from me** | Mods the official sources do not carry are sent from your computer over the room network, which uses upload bandwidth |

See [Mod Sync](./mods).

## Network

### Allow relay connections when a direct connection fails

When on, a failed direct connection falls back to a relay node, which adds latency. When off, a failed direct connection prevents joining.

### Allow other devices on this local network to join through this computer

When on, other devices on your real local network also see the room in their LAN world list and can join through this computer. When off, the room appears only in this computer's LAN world list.

## Relay servers

Relay servers help players find each other and carry traffic when a direct connection fails. The settings list every built-in server with its region, provider and measured latency, and each can be turned on or off; click **Test latency** to measure again. Servers that limit relay speed are marked in the list.

Both sides of a room must share at least one enabled server to find each other. These servers are provided free of charge by community members.

### Custom nodes

You can add nodes of your own. `tcp`, `udp`, `ws`, `wss`, `quic`, `http`, `https`, `txt` and `srv` addresses are accepted, separated by spaces:

```
tcp://example.com:11010 wss://example.com:443
```

Invalid addresses are reported when saved and ignored when connecting. With every built-in server turned off, all players in the room must use the same custom nodes to find each other.

## Privacy

### Hide my IP address

All traffic passes through relay nodes, so neither the host nor other players can learn your IP address; the operator of a relay node still can. Latency increases noticeably and players limited to direct connections cannot reach you, so Campfire asks for confirmation first.

### Read the player name and account type from the game

Reads only the player name, UUID and account type of the running game, to show your name and recognize premium players. Login credentials are neither read nor stored.

### Show skin faces of premium players

Shares your account UUID with players in the room, and skins are retrieved from the official Mojang service to draw faces.

### Show the program version to other players

When off, other players can only see that you use Campfire, not which version.

### Reset device identity

Each computer has a randomly generated identity in the multiplayer network. After a reset, other players see this computer as a new device. Available only while no room is open.

## Blocklist

Lists the players you blocked and when, and lets you unblock them one by one. Blocked players cannot join rooms you create; see [Removing and blocking](./room#removing-and-blocking).

## Data

| Action | Description |
| --- | --- |
| **Open data folder** | Opens `%LocalAppData%\Campfire`, which holds the settings, the multiplayer log and the blocklist |
| **Open backup folder** | Opens `%LocalAppData%\Campfire\backups\worlds`, which holds the world backups made before opening rooms |
| **Clear skin cache** | Deletes cached skin faces; they are fetched again when needed |
| **Remove networking component** | Deletes the downloaded component; it is downloaded and verified again next time |

Removing the networking component is available only while no room is open.
