# Network Connections

<p>
  <a href="NETWORK.md">简体中文</a> | <b>English</b>
</p>

This document sets out every network connection Campfire makes: the party it reaches, when, what is transferred, whether it can be turned off, and why none of it can compromise your computer or your account. Each statement can be verified against the source code; see [Check it yourself](#check-it-yourself).

## Design principles

The following principles are enforced in code and held as lasting design constraints:

1. **Remote content cannot control your computer.** Campfire receives only text, version numbers and list data from a server, such as notices, release notes and the lobby's room list. None of it can change a setting, run a command, name a local path or start a program.
2. **Campfire itself downloads and runs only two kinds of program, and both are verified.**
   - **Updates to Campfire itself** must carry the digital signature of the Campfire release key, and the file must be the version it claims to be; otherwise it is not installed.
   - **The multiplayer component**: the SHA-256 of each file is fixed in the program, and a single differing byte causes the file to be discarded.

   Mod sync writes mod files into the mods folder of the version you choose. The game loads these files, not Campfire, and they are written only after you confirm; see [Mod sync](#mod-sync).
3. **No administrator rights.** Campfire runs as a standard user. It installs no driver, creates no virtual network adapter and writes nothing to system folders.
4. **Accounts are left untouched.** Campfire never reads, stores or uploads login credentials.
5. **Launcher repair is entirely offline.** It modifies files on your computer only, and adds no network behavior to the launcher.
6. **Builds from source never contact the Campfire service.** The service address is written into official builds only. A self-built Campfire has no update check, notices, usage statistics, lobby, online maps or CurseForge mod lookup; multiplayer with invite codes is unaffected.

## Overview

| Feature | Connects to | When | Can be turned off |
| --- | --- | --- | --- |
| Update and notice check | Campfire service | At startup, then every few hours | Yes, in Settings › General |
| Downloading an update | HTTPS addresses provided by the Campfire service | When a new version is available | Together with the update check |
| Usage statistics | Campfire service | At most once a day, with consent | Off until consent is given, and can be withdrawn at any time |
| Lobby | Campfire service | When browsing the lobby or listing a room | Not contacted unless used |
| Online maps | Campfire service, CurseForge file servers | When browsing or downloading maps | Not contacted unless used |
| Multiplayer component download | Gitee, GitHub and two GitHub download accelerators | On first use of multiplayer | Not downloaded unless multiplayer is used |
| Multiplayer | Network nodes and the other players in the room | While a room is open | Nodes, relays and IP hiding are configurable |
| Relay server latency test | The enabled built-in network nodes | Once the multiplayer component is downloaded: after start and every half hour thereafter; when the multiplayer settings are opened or a test is requested | Never performed if multiplayer is not used |
| Skin faces | Mojang's official services | When a room has premium players | Yes, in Multiplayer settings › Privacy |
| Mod sync | Modrinth, the Campfire service, CurseForge file servers and the host of the room | When a sync is started on the Mod Sync page | Not contacted unless used; the host can stop mod sync and mod downloads at any time |
| Name mappings for the in-game component | Mojang's official services and a mirror in China | When the host runs a release that needs them | Not downloaded unless multiplayer is used |
| Opening web pages | The system browser | Only when a link or button is clicked | — |

Campfire makes no network connections beyond those listed above.

## Campfire service

Official builds connect to `https://api.mccampfire.top`, which is provided and protected by Cloudflare. Every request is encrypted over HTTPS and identifies itself as `Campfire/<version>`.

### Updates and notices

- **Sent**: nothing, and no device identifier; every user receives the same response.
- **Received**: the latest version, release notes, download and signature addresses, notice texts and the check interval.
- **Constraints**:
  - Notices can present text only. A notice button can open only an `https://` web page, only when clicked, and can never start a program.
  - The check interval is bounded between 1 hour and 7 days.
  - The server cannot change any setting this way.

### Installing updates

1. After downloading a new version and its signature, Campfire verifies the signature against the public key built into the program and checks the file's actual version number, so an older signed release cannot pose as a newer one.
2. Only once verification succeeds does Campfire ask for consent, replace its program file and restart.
3. A file that fails verification is discarded.

Even if the server or a download mirror were controlled by someone else, it could not make Campfire install a modified program.

> **About required updates**
> When an old version has a serious problem, the server may mark a new version as required. Campfire insists on updating only once a correctly signed new version **has actually been downloaded and verified**. Should the download or verification fail, Campfire remains fully usable; no server message can prevent it from starting.

### Usage statistics

Sent only with consent, at most once a day, and limited to:

- a randomly generated statistics ID, unrelated to the multiplayer device ID and resettable at any time;
- the software version, Windows version and architecture, .NET Framework version and interface language;
- usage counts for each feature, such as creating and joining rooms.

Reports contain no IP address, game account or UUID, player name, room or invite code, file path, hardware serial number or network adapter address. The server infers a rough country and province from the connection and does not retain the IP address. The exact content of the next report is shown under **Settings › General › View what is sent**.

### Lobby

- **Browsing**: retrieves the room list only, and sends nothing.
- **Listing a room**: sends the room name, tags, host name, game version, world name, player count, invite code, an optional password and picture, and a random ID used only by the lobby. That ID is unrelated to the device ID and the statistics ID. While the room is open, the player count is updated every few minutes; the listing is withdrawn when the room closes.
- **Joining**: requests the room's invite code from the server, submitting the entered password for protected rooms.
- **Reporting**: sends only the room number and the reason.

The server retains only a digest of a room password, never the password itself, and keeps IP addresses only as salted digests, used to limit listing frequency and to address abuse. The rough province can be withheld from display. When a room is unlisted or closed, it leaves the lobby immediately and its records are deleted within a day.

Rooms in the lobby are listed by other players. Joining a stranger's room carries the same considerations as joining any unfamiliar Minecraft server.

### Online maps

- While browsing and searching, the Campfire service queries CurseForge's map catalog on your behalf, passing on only search terms and filters.
- Map files and preview images are downloaded only from CurseForge's file servers at `forgecdn.net`; every other address is refused. Downloads are verified with SHA-1.
- Maps are save data, not programs. Importing unpacks only into the chosen saves folder, and any file that would land outside it is refused.

## Multiplayer

### Multiplayer component

Multiplayer is built on the open-source network component EasyTier 2.6.4. On first use, Campfire probes the following addresses and downloads from the fastest:

- `gitee.com` and `github.com`: the official release pages;
- `ghfast.top` and `gh-proxy.com`: GitHub download accelerators.

The SHA-256 of all three files is fixed in Campfire's code and matches the official release exactly. Whatever the source, a file that does not match is neither kept nor run. An accelerator learns only your IP address and that you are downloading the component.

The component runs as a standard user, installs no virtual network adapter and never relays traffic for other networks. A host exposes only the ports required by the game and the room; a joining player exposes none. Players in a room cannot use it to reach any other service on your computer.

### Nodes and connections

- **Network nodes** help the players of a room discover one another. The built-in public nodes are the EasyTier community nodes `225284.xyz`, `easytier.weiai.org.cn`, `et.basd1.de`, `et.sumnight.top`, `et.chinokou.cn`, `et-public.lctn.top`, `et.南梁.com` and `etnode.zkitefly.eu.org`, plus one China Mobile node in Chongqing that is reached by IP address. Each node may be enabled or disabled under Multiplayer settings › Relay servers, and you may also rely solely on nodes of your own.
- **Latency test**: Campfire connects to each built-in node through a temporary network with a random name that accepts no connections, measures the round-trip time and disconnects at once. No game data is transmitted; node operators learn only your IP address and the time of the connection. The status lamp on the title bar reflects the result.
- Campfire prefers direct connections between players. To establish one, the component may query public STUN services about your network type and may create a temporary port mapping on your router through UPnP.
- Game data passes through a relay node only when relaying is allowed and a direct connection fails.
- Game data is encrypted between players and cannot be read by nodes. Node operators can see your IP address and when you are connected.
- With **Hide my IP address** enabled, all data is relayed. Other players cannot learn your IP address, though the relay's operator still can.

### Information shared in a room

Other players in a room can see your player name, your device ID and whether you use a premium account; with the corresponding options enabled, they can also see your premium UUID and your Campfire version. The device ID is random and can be reset at any time.

As a host with mod sync allowed, players in the room can also see your game version, mod loader, and the file name, name, version, SHA-1 and CurseForge fingerprint of each mod in your mods folder. Mod sync is set by **Let players sync my mods** in the multiplayer settings and can be switched on the room page at any time, taking effect at once.

### Skin faces

With **Show skin faces of premium players** enabled, Campfire looks up a player's skin on `sessionserver.mojang.com` by UUID and downloads the image from `textures.minecraft.net`. When disabled, no lookups are made.

### The in-game component

Campfire attaches a small Java component to the game running **on the host's own computer**, in order to open the world to LAN and to present the welcome message and Tab list information. The component makes no network connections, starts no programs and loads no external code.

Releases from 1.14.4 to 1.21.11 require Mojang's official name mapping file. It is downloaded from `piston-data.mojang.com`, or from the mirror `bmclapi2.bangbang93.com` in China when Mojang is unreachable, and verified against the SHA-1 in the game's own version file. A mapping file is a text table of names, not a program.

### Mod sync

After joining a room, Campfire asks the host for its mod list and compares it with the version you choose on this computer. The comparison only reads local files and makes no network connections. Once a sync is started:

- **Official sources**: the SHA-1 values the host lists are first looked up on `api.modrinth.com`; for files Modrinth lacks, the CurseForge fingerprints the host lists are sent to the Campfire service, which looks them up on CurseForge. Only HTTPS download addresses on `cdn.modrinth.com` and `forgecdn.net` are accepted, and only files identical to the host's are used. Both lookups send nothing but file hashes or fingerprints.
- **Host**: files the official sources do not carry, or whose authors do not allow others to distribute them, can be fetched from the host over the room network. Campfire states how many files that involves and asks for consent first. The host can only serve files by the SHA-1 values on its list, so players cannot use this to read anything outside the host's mods folder; the host can turn off **Let players download mods from me** in the multiplayer settings.
- **Verification**: whatever the source, each file must match the size and SHA-1 the host listed, or it is discarded. The mods folder is changed only after every file has been downloaded and verified.
- **What is written**: only the mods folder of the chosen version, under file names that cannot contain a path. Replaced or removed mods are moved to a backup folder in Campfire's data folder and are never deleted. Downloaded files are cached for 14 days.

Mods are programs that run with the game. Taking mods from the host is no different from accepting a modpack the host sends you, so use it only with hosts you trust.

## Limits of exposure

Even if one party were controlled by someone else, the consequences are clearly bounded:

| Party controlled | Possible effect | Not possible |
| --- | --- | --- |
| Campfire service | Display misleading notice texts and web links; alter the lobby list and map catalog; announce an "update" that cannot be installed | Install or run any program; change your settings; read your files |
| A download mirror or accelerator | Cause downloads to fail | Replace an update or the multiplayer component, as signature and hash checks reject it |
| A network or relay node | Observe your IP address and connection times; cause connections to fail | Read game data; reach any port other than the host's game |
| Another player in the room | In-game interaction, as in any Minecraft multiplayer session | Reach other services or files on your computer |
| The host, through mod sync | Supply a mod containing malicious code, which the game runs once you confirm | Write files without your confirmation; write outside the mods folder; read your files |

## Privacy options

- **Launcher repair only**: the repair itself is offline; **Check for updates and notices at startup** can also be turned off under **Settings › General**.
- **No usage statistics**: decline when asked, or turn off **Send anonymous usage statistics** under **Settings › General**.
- **Greater privacy in multiplayer**: in the multiplayer settings, enable **Hide my IP address**, disable **Show skin faces of premium players** and **Show the program version to other players**, disable **Let players sync my mods** when hosting, or rely solely on your own nodes.
- **No contact with the Campfire service at all**: [build from source](README.en.md#building-from-source).

## Check it yourself

| Subject | Source |
| --- | --- |
| The service address is written into official builds only | `ReadServiceUrl` in `src/Campfire/Online/OnlineService.cs` |
| Every field the server can deliver | `Parse` in `src/Campfire/Online/OnlineService.cs` |
| Signature and version checks for updates | `Verify` in `src/Campfire/Online/Updater.cs` |
| Required updates apply only to correctly signed files | `OfferUpdateAsync` in `src/Campfire/Online/OnlineFlow.cs` |
| Statistics content and the consent check | `src/Campfire/Online/Telemetry.cs` |
| Fixed hashes and download addresses of the multiplayer component | `src/Campfire/Link/Runtime/EasyTierPackage.cs` |
| Configuration of the multiplayer component | `src/Campfire/Link/Runtime/EasyTierConfig.cs` |
| Built-in public nodes | `src/Campfire/Link/PublicNodes.cs` |
| Node latency test | `src/Campfire/Link/Runtime/NodeProbe.cs` |
| Fields sent by the lobby | `src/Campfire/Link/Lobby/LobbyClient.cs` |
| Map address limits and unpacking protection | `src/Campfire/Worlds/MapImages.cs`, `MapDownloader.cs`, `WorldImporter.cs` |
| What the host's mod list and file serving cover | `src/Campfire/Mods/ModHost.cs`, `src/Campfire/Link/Room/RoomServer.cs` |
| Download address limits and checks for mods | `src/Campfire/Mods/ModrinthLookup.cs`, `CurseForgeLookup.cs`, `ModSync.cs` |
| The in-game Java component | `src/java-agent/src` |

Should Campfire behave differently from this document, or should you discover a security issue, please report it in [Issues](https://github.com/Xocio/Campfire/issues).
