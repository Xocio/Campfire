# Getting Started

## Before you begin

### Requirements

- Windows 7 SP1, 8, 8.1, 10 or 11.
- .NET Framework 4.8. Windows 10 and 11 include it; on Windows 7 and 8, download it from [Microsoft](https://dotnet.microsoft.com/download/dotnet-framework/net48) if needed.
- For multiplayer on Windows 7, install **KB3063858** and **KB4474419** through Windows Update and restart. If Windows Update is unavailable, download them by number from the Microsoft Update Catalog.

### Download

Get the latest version from the [download page](/en/download) or from [GitHub Releases](https://github.com/Xocio/Campfire/releases/latest).

## First start

1. Put `Campfire.exe` in any folder and double-click it. If you plan to repair a launcher, placing it in the launcher's folder lets Campfire find the launcher automatically.
2. A short tour introduces Home, Maps, Tools and Settings in about thirty seconds. You can skip it and replay it later from **Settings › About**.
3. Official releases ask whether to send anonymous usage statistics. This is entirely optional and declining changes nothing; see [Updates and Privacy](./updates-privacy).

::: tip One instance at a time
Only one Campfire runs per Windows session. Opening the program again brings the running window to the front, and any open room is unaffected.
:::

## Your first room

The shortest path is below; see [Hosting and Joining](./multiplayer) for the full guide.

**Host**

1. On **Home**, click **Create a room**.
2. Start the game and enter a singleplayer world; Campfire opens it to LAN for you.
3. The room is created and its invite code copied to the clipboard; share it with your friends, or publish the room to the [Lobby](./lobby).

**Friends**

1. Copy the code from the host, then open Campfire. Home offers to join the room at once; alternatively, choose **Join a room** on **Home** and paste it, or select a room in the **Lobby**.
2. Once connected, find the room at the bottom of the game's **Multiplayer** list and double-click it.

::: warning The networking component is downloaded once
The first time you host or join, Campfire downloads a networking component of about 14 MB and verifies every file. Windows Firewall may ask whether it may use the network; choosing **Allow** improves the chance of a direct connection.
:::

## Your first launcher repair

1. Close the launcher.
2. Open Campfire and choose **Launcher Repair Tool** on the **Tools** tab. Campfire looks for a launcher in its own folder; you can also click **Browse** or drag the launcher into the window.
3. Review the state of each item under **Repairs**, then click **Repair**.

The launcher is ready to use afterwards. The original is kept next to it with a `.bak` suffix and can be brought back with **Restore** at any time. See [Launcher Repair](./launcher-repair).

## Where data is stored

Settings, the networking component, logs and caches live in `%LocalAppData%\Campfire`. The program file itself can be moved or replaced freely without losing settings.

| Path | Contents |
| --- | --- |
| `settings.ini` | All settings |
| `EasyTier\` | Networking component |
| `Link\link.log` | Multiplayer log for troubleshooting |
| `Link\blocked.json` | The host's blocklist |
| `Link\heads\` | Skin faces of premium players, cached for a day |
| `agent.log` | Log of the in-game component |
| `mappings\` | Mojang name mapping files used by the in-game component |
| `downloads\maps\` | Downloaded map files, removed after a week unused |

**Multiplayer settings › Data** opens this folder or removes the networking component so it is downloaded again next time.
