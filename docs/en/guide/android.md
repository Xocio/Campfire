# Android

Campfire for Android lets players who run Minecraft: Java Edition on a phone or tablet create and join rooms too. It speaks the same multiplayer protocol as the desktop app, so players on phones and computers can share a room, and invite codes and the lobby work on both.

## Requirements

- Android 7.0 or later.
- A 64-bit ARM processor, which every recent phone and tablet has.
- An Android launcher that runs Minecraft: Java Edition. Campfire works on the running game and is not tied to any launcher.

## Download and install

1. Download `Campfire.apk` from the [download page](/en/download#android).
2. Open the downloaded file. When Android asks whether to allow installing apps from unknown sources, allow it for your browser or file manager, then continue.
3. On first start, Campfire checks the system version, notifications, background running and local network access, and explains what each one is for.

::: tip Running in the background
While the game is in the foreground, Campfire keeps the room connected in the background. Allow Campfire to run in the background and set its battery usage to unrestricted. While a room is open, a notification shows its state; you can copy the invite code or close the room from it.
:::

## A quick tour

| Tab | Contents |
| --- | --- |
| Home | Create a room, join a room, the current room and the multiplayer steps; cards can be reordered with a long press or removed from Home |
| Lobby | Browse the rooms listed in the lobby |
| Settings | Multiplayer settings: player, network, privacy, game folders, room and diagnostics |

The button in the top right corner of Home opens **App settings**, with the theme, language, updates and notices, Support the project and About.

## Creating a room

1. On **Home**, tap **Create a room**, then **Start**.
2. Go back to your launcher, enter a singleplayer world and choose **Open to LAN** in the game menu.
3. On Wi-Fi, Campfire finds the world on its own and opens the room. On mobile data, use **Enter the port** instead and type the port number the game shows in chat.
4. Once the room is open, tap **Copy code** or **Share** to send the invite code to other players.

::: warning Open to LAN yourself
Android does not let one app read or change what another app is running, so the Android app cannot open your world to LAN automatically the way the desktop app does, and it shows no room information in the game's chat or Tab list. The room closes when you leave the world.
:::

The **Tools** card on the room page holds **Room settings**, **List in the lobby**, **Allowlist**, **Voice** and **Multiplayer diagnostics**; from the player list you can remove a player or add them to the allowlist. Room name, player limit and announcement are described in [Managing a Room](./room).

## Joining a room

1. Copy the invite code from the host, then open Campfire and it offers to join that room. You can also tap **Join a room** on **Home** and paste the code or the whole chat message.
2. Once connected, start the game from your launcher, open **Multiplayer** and pick **Campfire Room** at the bottom of the list.

Invite codes start with `U/` and have four groups of characters, exactly as on the desktop. When the host plays with a premium account, enter through the LAN world entry rather than connecting directly to the address shown on the room page.

## Lobby

The **Lobby** tab lists every public room, from the desktop and Android apps alike. When a room has a password, you enter it after tapping **Join room**. If a room's name or content is inappropriate, you can report it from the room details.

### Listing your room

The host opens **List in the lobby** from the **Tools** card on the room page, adds a description and an optional password, picks up to 3 tags and taps **List**. The room name comes from Room settings, and the lobby shows its first 24 characters; Campfire reads the game version and world name on its own. While listed, the player count and voice state stay in sync with the lobby. The room leaves the lobby when it closes, or at any time with **Remove from lobby**.

## Voice

When the host turns on voice on the room page, Campfire installs the Simple Voice Chat mod for the game and forwards voice traffic between players. Voice needs:

- **A granted game folder**: grant your launcher's game folder under **Settings › Game folders**. Campfire recognizes common Android launchers, and you can pick the `.minecraft` folder of any launcher. Campfire only reads version information and writes the mods it installs.
- **A version with a mod loader**: the voice mod needs Fabric, Forge, NeoForge or Quilt.
- **A game restart**: after the mod is installed, restart the game and enter the world for voice to work.

See [Voice Chat](./voice) for the full description.

## Multiplayer settings

The **Settings** tab holds the multiplayer settings:

- **Player**: your player name and in-game name.
- **Network**: allow relay, hide my IP address, host search time, detect invite codes, and **Relay servers**, where you can test speeds, choose built-in servers or add your own nodes.
- **Privacy**: show skin faces, show the app version to others, and reset the device ID.
- **Game**: game folders.
- **Room**: room settings and the allowlist.
- **Diagnostics**: **Multiplayer diagnostics** checks the system, the multiplayer component, the network connection, relay servers, the local port and the LAN world, with advice for each; the **Multiplayer log** records how rooms were joined and created, and can be shared with others for troubleshooting.

## Differences from the desktop app

| Feature | Android |
| --- | --- |
| Invite codes, the lobby, room settings, allowlist, voice, skin faces, diagnostics, usage statistics | Supported |
| Opening the world to LAN automatically | Not supported; open it in the game |
| Welcome message and Tab list information in game | Not supported |
| Mod sync, online maps, launcher repair, JRE tool | Not available |
| Multiplayer component | Included in the package, never downloaded separately |

## Updates and privacy

- **Notices**: fetched at startup; turn off **Receive notices** in **App settings**.
- **Usage statistics**: the first time you reach the main screen, Campfire asks whether to send anonymous usage statistics; nothing is sent by default. Once allowed, a report goes out at most once a day; **App settings** can turn it off, show the next report or reset the statistics ID.
- **Skin faces**: when premium players are in the room, the player list shows their skin faces; turn off **Show skin faces** in **Settings**.
- **Check for updates**: off by default. Once turned on in **App settings**, Campfire looks for a new version at startup; when one is found, **Update** opens the download address in your browser. The app itself downloads and installs no programs.
- All network activity of the Android app is listed in [Network Security](./security#android).

## FAQ

### My world is not found automatically

Make sure the world is open to LAN and that Campfire may access the local network, either from the first-start checks or in system settings. On mobile data and some public Wi-Fi networks the world cannot be found automatically; use **Enter the port** instead.

### Players are disconnected soon after joining

This usually means the system is restricting Campfire in the background. Allow Campfire to run in the background and turn off battery optimization for it.

### I cannot join a room

Run **Multiplayer diagnostics** first. When both sides are behind strict networks, a direct connection may be impossible: allow relay in the multiplayer settings, or let the player with the better network host. See the [FAQ](./faq) for more.
