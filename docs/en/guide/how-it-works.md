# How It Works

This page is for readers interested in the implementation. It is not needed for everyday use.

## Multiplayer

### Overview

A game connection passes through these stages:

1. **The friend's game** enters the room from the LAN world list and connects to a port on `127.0.0.1`;
2. **The friend's networking component** carries the connection through the encrypted virtual network, directly or through a relay, to the host;
3. **The host's connection gate** identifies the player and lets them in or turns them away;
4. **The host's LAN world** accepts the connection and play begins.

Both networking components also connect to nodes, but only so the players can find each other. A room service on the host's side keeps the player list in sync.

### Invite codes and the virtual network

Each room is its own encrypted virtual network. The 16 characters of an invite code encode both the network name and its secret: the first half determines the name, the second half the secret. The code alone is therefore enough to enter the network, while anyone without it can neither find nor join the room. Codes carry a checksum, so typing mistakes are caught immediately.

The network is carried by an open-source networking component. It is downloaded on first use, and every file is compared with a built-in SHA-256 value, so no mirror can substitute a modified program. The component runs as a separate process inside a Windows job object: when Campfire exits, even after a crash, the component exits too and nothing is left running in the background.

### Finding the host's world

The host never types a port. Campfire reads the system table of listening TCP ports, picks those owned by Java processes and sends each one a local Minecraft status query to recognize the LAN world. Because nothing listens for network broadcasts, Windows Firewall does not ask for permission. A dedicated server running on the same computer is found the same way.

### Putting the room in the LAN list

On the friend's side, Campfire opens a local port that forwards through the virtual network to the host's world, and announces it on this computer in Minecraft's own Open to LAN format. The room then appears among the LAN worlds at the bottom of the multiplayer screen.

Entering from that list is not only convenient; it is what lets offline accounts in. When the host opens LAN with a premium account, the game verifies every joining player, and only a world picked from the LAN list carries on after the check fails.

### Room service

A lightweight room service runs on the host's side. Joining players use it to confirm they reached the right room, learn the game port, and keep the player list, names and faces in sync. All messages stay inside the virtual network.

### Connection gate

The host's world is not exposed to the virtual network directly. Guest connections first reach a gate on the host's computer, which reads the Minecraft login packets to identify the player. Blocked players are refused at login, and removed players receive a disconnect packet.

### In-game room information

A normal LAN connection is encrypted right after login, so no outside program can add anything to it. To show room information in chat and the Tab list, Campfire attaches a small Java component to the host's game process, and the game server itself sends the welcome message, header and footer. The component does not turn off authentication and reads no login credentials. If attaching fails, Campfire falls back to showing the information in the LAN list only.

### Reading the game account

Every launcher passes the player name, UUID and account type to the game on its command line. Campfire reads these three values from the running game's command line, which works whichever launcher is used and never touches login credentials.

## Launcher repair

### Finding the patch points

Campfire reads the launcher with a .NET assembly library and locates the methods to change by **method name and code shape**, not by fixed file offsets. Later minor builds with the same structure can therefore usually still be repaired; as soon as a shape does not match, Campfire reports the build as unsupported and changes nothing.

| Item | Method changed | Change |
| --- | --- | --- |
| Minecraft version list | `PageDownloadInstall.LoaderInit` | Binds the Download page back to the version list loader |
| Install modpack from file | `ModModpack.ModpackInstall()` | Restores the file picker and installs on a background thread |
| Modpack installation | `ModModpack.ModpackInstall(File, …)` | Calls the installer that matches the detected modpack format |
| Disable auto-update | `ModSecret.UpdateStart` | Returns immediately |
| Region lock | `RegionAssessment.get_IsRestricted` | Returns `false`; every guarded entry for downloads, installation and completion reads this one property |

### Rewriting single-file bundles in place

Some launchers ship as a .NET single-file bundle. Campfire first locates the target assembly inside the bundle, then rewrites the method bodies in place. The file keeps its size and bundle layout, and nothing is repacked.

### Write sequence

1. Read the launcher and locate every patch point;
2. build the repaired file in memory;
3. reload the result and confirm each change took effect;
4. back up the original as `.bak`;
5. write the repaired file.

If any step fails, the original file stays untouched. When the same launcher is inspected again, Campfire recognizes that it has been repaired.

## Update signatures

Every official update is signed with an ECDSA P-256 private key whose public key is built into the program. Campfire installs only updates whose signature checks out; even if a download channel were tampered with, an unsigned program could not be installed.
