# Network and Privacy

This page covers the multiplayer settings. Open them with **More settings** on the multiplayer page.

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

What each party can see while you play, and why the networking component cannot reach other services on your computer, is described in [Network Security](./security#multiplayer).

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

## Network

### Allow relay connections when a direct connection fails

When on, a failed direct connection falls back to a relay node, which adds latency. When off, a failed direct connection prevents joining.

### Allow other devices on this local network to join through this computer

When on, other devices on your real local network also see the room in their LAN world list and can join through this computer. When off, the room appears only in this computer's LAN world list.

### Built-in public nodes and custom nodes

Public nodes let players discover each other. With the built-in public nodes turned off, Campfire uses only your custom nodes, and every player in the room must use the same nodes to find each other.

Custom nodes accept `tcp`, `udp`, `ws`, `wss`, `quic`, `http` and `https` addresses, separated by spaces:

```
tcp://example.com:11010 wss://example.com:443
```

Invalid addresses are reported when saved and ignored when connecting.

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

Each computer has a randomly generated identity in the multiplayer network. After a reset, other players see this computer as a new device.

## Data

| Action | Description |
| --- | --- |
| **Open data folder** | Opens `%LocalAppData%\Campfire\Link`, which holds the multiplayer log and the blocklist |
| **Clear skin cache** | Deletes cached skin faces; they are fetched again when needed |
| **Remove networking component** | Deletes the downloaded component; it is downloaded and verified again next time |

These actions are available only while no room is open.
