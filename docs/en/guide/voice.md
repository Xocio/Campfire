# Voice Chat

Campfire brings voice chat to rooms with the open-source mod Simple Voice Chat. Once the host turns voice chat on, Campfire installs and enables the mod in the game, and voice travels encrypted over the multiplayer network with no port forwarding. Players in the room who have the same mod can talk with each other once they are in the world.

## Before you begin

- Voice chat needs a mod loader: play with a Fabric, Forge, NeoForge or Quilt version of the game. Vanilla is not supported.
- Players need an installed version with the same game version and mod loader as the host.
- Voice chat works with worlds the host's game opens to LAN; it is not available for dedicated servers.
- Voice chat is off by default and is offered only after the host turns it on.

## Host: turning on voice chat

1. After creating a room, find **Voice** in the **Tools** card of the multiplayer page and click **Turn On**.
2. If the game does not have the mod yet, Campfire downloads the latest release for that game version and loader from Modrinth, verifies it and puts it into the game's mods folder. A disabled copy of the same mod is enabled again.
3. A newly installed mod takes effect after the game restarts. Campfire tells you so: close and restart the game, then enter the same world.

While the game restarts, the room stays open with the same invite code, and the players in it do not need to join again. Return to the same world within 10 minutes and Campfire opens it to LAN and resumes the room; with automatic Open to LAN turned off, open it to LAN yourself.

The **Voice** row shows the current state:

| State | Meaning |
| --- | --- |
| Off | Click **Turn On** to install the voice chat mod in your game |
| Downloading the voice chat mod | Downloading from Modrinth and verifying |
| Installed; takes effect once you restart the game | Restart the game; the room stays open |
| On | The voice server runs in the game, and players with the mod can talk with each other |

Click **Turn Off** and Campfire stops carrying voice at once, so the players' voice chat ends immediately. The mod stays in the game; remove it in your launcher if you no longer need it.

## Players: installing the voice chat mod

Once the host turns voice chat on, the player's multiplayer page offers the mod under **Tools › Voice**:

1. Click **Install** and confirm the mod and the version it goes to.
2. Campfire prefers the exact file the host uses; if Modrinth does not carry it, the latest release for the game version is used instead. The file is written only to the mods folder of the local version with the host's game version and loader.
3. Start that version, enter the host's world and talk. If the game was already running during the installation, restart it.

| State | Meaning |
| --- | --- |
| The host has voice chat off | Available once the host turns it on |
| The host turned on voice chat. It works once the host restarts the game | The host's game has not loaded the mod yet |
| The host turned on voice chat. Install the mod for version … | Click **Install** |
| No installed version has the host's game version and mod loader | Install that version in your launcher first |
| UDP port … is in use on this computer | Close the program that uses the port and try again |
| Ready | Talk once you enter the host's world |

In the world, keys, microphone and volume are set in the mod's own settings screen, opened with **V** by default.

## In the lobby

Rooms that offer voice chat carry a **Voice** tag in the lobby, and the room details mention it. After joining, install the mod from the room page.

## Privacy

The mod carries voice data between players and the host, encrypted by the multiplayer network. Campfire neither reads nor keeps any voice content. The host exposes only the one UDP port its voice server uses, and only while that server runs; it is closed as soon as voice chat is turned off. See [Network Security](./security#voice-chat).
