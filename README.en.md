<p align="center">
  <img src="docs/images/logo.png" width="128" alt="Campfire" />
</p>

<h1 align="center">Campfire</h1>

<p align="center">
  An advanced Minecraft toolbox
</p>

<p align="center">
  <a href="README.md">简体中文</a> | <b>English</b>
</p>

<p align="center">
  Friends: <a href="https://linux.do">LINUX DO</a>
</p>

<p align="center">
  <a href="https://github.com/Xocio/Campfire/releases/latest"><img src="https://img.shields.io/github/v/release/Xocio/Campfire?label=download&color=1370F3" alt="Release" /></a>
  <a href="https://github.com/Xocio/Campfire/releases"><img src="https://img.shields.io/github/downloads/Xocio/Campfire/total?label=downloads&color=1370F3" alt="Downloads" /></a>
  <a href="https://github.com/Xocio/Campfire/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Xocio/Campfire/build.yml?label=build" alt="Build" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%207%20%7C%208%20%7C%2010%20%7C%2011-0B5BCB" alt="Platform" />
</p>

<p align="center">
  <img src="docs/images/en/screenshot-main.png" width="640" alt="Campfire main window" />
</p>

## Features

Campfire brings multiplayer, launcher repair and handy tools together in a single file of about 1.3 MB. Nothing to install, and it runs on everything from Windows 7 SP1 to Windows 11.

### Multiplayer

- **Invite codes**: once the host opens a world to LAN, Campfire detects it and creates an invite code that friends simply paste to join. No public IP, no port forwarding, no port to type; a copied code can be joined straight from the Home tab.
- **Peer-to-peer**: direct connections come first, with latency close to a real LAN; when they fail, switch on the relay or bring your own nodes.
- **In-game room info**: the room appears right in the multiplayer LAN worlds list. Inside the world, the chat greets each player and the Tab header and footer show the host, player count and latency live, even on authenticated and encrypted LAN worlds.
- **Room management**: members show their skin faces; the host can remove or block guests and manage the blocklist in Multiplayer Settings.
- **Cross-launcher**: players on other launchers share the same invite codes and land in the same room.
- **Privacy first**: hide your IP address, keep the room to this computer, and never have your login credentials read; room name, port, nodes and search limit are all configurable.

### Launcher repair

- **Downloads restored**: brings back the Minecraft version list, game and mod loader installation, and modpack installation for CurseForge, Modrinth, MCBBS, HMCL, MMC and more in official PCL 2.99.
- **Region lock lifted**: restores the downloads, installs and file completion that PCL Community Edition 2.15.1 and later switch off by region.
- **JREs enabled**: lifts the Community Edition's default block on JREs so a JRE can launch the game just like a JDK.
- **Precise and reversible**: patch points are found by method name and code shape, never fixed offsets; every change is verified in memory before anything is written, the original is backed up, and one click restores it. Launcher auto-update is turned off so the repair is not overwritten.

### Experience

- **Personal themes**: light and dark modes, with Monet colors that build a whole color scheme from your desktop wallpaper in several palette styles.
- **Fluid motion**: carefully tuned transitions for pages, dialogs and controls, which can be turned off at any time.
- **Runs in the background**: a notification area icon shows the multiplayer status live, and the room stays open after the window is closed.
- **Secure updates**: official releases check for updates, and a new version is installed only if its digital signature checks out.
- **Bilingual**: Simplified Chinese and English, following the system language and switchable at any time.

## Usage

### Multiplayer

1. Download and open `Campfire.exe` from [Releases](https://github.com/Xocio/Campfire/releases/latest), then click **Multiplayer** on the **Home** tab.
2. Host: click **Create a room**, then in the game enter a singleplayer world and press Esc → Open to LAN → Start LAN World. The invite code is copied once the room is ready; send it to your friends.
3. Friends: click **Join a room** and paste the code. Then find "Campfire Room" at the bottom of the multiplayer list and double-click it.

The first time, a networking component of about 14 MB is downloaded and every file is verified. Windows may ask whether the component may use the network; allowing it improves the chance of a direct connection. Keep Campfire open while playing.

### Launcher repair

1. Download `Campfire.exe` from [Releases](https://github.com/Xocio/Campfire/releases/latest).
2. Put it in the launcher's folder (the official build is `Plain Craft Launcher 2.exe`; the Community Edition is `PCL2_CE_Release_x64.exe` and similar names). You can also select the launcher after starting Campfire, or drag it into the window.
3. **Close PCL**, open Campfire, click **Launcher Repair Tool** on the **Tools** tab, then click **Repair**.

<p align="center">
  <img src="docs/images/en/screenshot-fixed.png" width="640" alt="Repair completed" />
</p>

Launch PCL as usual afterwards. The original launcher is kept as `Plain Craft Launcher 2.exe.bak` in the same folder; click **Restore** to bring it back.

**Requirements**: Windows 7 SP1 / 8 / 8.1 / 10 / 11 with .NET Framework 4.8. PCL 2.99 itself requires .NET Framework 4.8, so any PC that runs PCL can run Campfire. Windows 10 / 11 include it; on Windows 7 / 8, download it from [Microsoft](https://dotnet.microsoft.com/download/dotnet-framework/net48) if needed. On Windows 7, install KB3063858 and KB4474419 through Windows Update before using multiplayer.

## Supported versions

| Launcher | Result |
| --- | --- |
| Official PCL 2.99.0.0 | ✅ Repairable |
| Official PCL before 2.99 | Downloads work, no repair needed |
| [PCL Community Edition](https://github.com/PCL-Community/PCL-CE) 2.15.1 and later | ✅ Repairable (region lock) |
| PCL Community Edition 2.15.0 and earlier | Downloads work, no repair needed |

Campfire locates its patch points by method name and code shape instead of fixed offsets, so minor launcher updates will most likely keep working. If a version is not recognized, Campfire reports it as unsupported and **does not modify any file**.

The Community Edition ships as a .NET single-file bundle, so Campfire first locates the `PCL.Core` assembly inside it and rewrites the region check in place. The file keeps its size and bundle layout; nothing is repacked.

## FAQ

**Whose servers carry the game traffic?**
Game traffic never passes through Campfire's servers. The host and friends connect directly whenever possible; public nodes only help them find each other. Game traffic goes through a relay node only if you turn the relay on and a direct connection fails.

**"Invalid session" when joining?**
When the host opens LAN with a premium account, the game verifies every joining player. Ask the host to open LAN with an offline account.

**Can't join a room?**
Check the invite code and that the room is still open. If both networks are strict (such as NAT4), a direct connection may be impossible; turn on the relay in Settings or let someone with a better network host. "View multiplayer log" on the failure page shows the details.

**Why is the in-game Tab information missing?**
Campfire tries to attach a small Java agent to the host's game process, which sends the welcome message and Tab header/footer from the server. Vanilla 26.3 / 26.4 are supported for now. If the game runs on a JRE without `jdk.attach`, or a mod loader remaps the class names, Campfire falls back to the LAN entry (at the bottom of the multiplayer list) and the game connection is unaffected. The reason is written to `%LocalAppData%\Campfire\agent.log`.

**Is it safe?**
Campfire only changes download-related methods of the launcher: four in the official build and one region check in the Community Edition (`RegionAssessment.IsRestricted`). It adds no network activity. The repaired launcher is built in memory, reloaded and verified item by item before anything is written to disk; if any step fails, the original file stays untouched.

**My antivirus flags it. What now?**
Tools that modify other programs are occasionally flagged by mistake. Campfire is fully open source: review the code and [build it yourself](#building-from-source).

**Can PCL still update after the repair?**
To keep the repair in place, Campfire disables PCL's auto-update. To upgrade, download the new PCL manually, then run Campfire again to check whether a repair is needed.

**How do I undo the repair?**
Open Campfire and click **Restore**, or delete `Plain Craft Launcher 2.exe` and rename `Plain Craft Launcher 2.exe.bak` back to it.

## How it works

| Repair | Method changed | Description |
| --- | --- | --- |
| Minecraft version list | `PageDownloadInstall.LoaderInit` | Binds the Download page back to the version list loader `ModDownload.DlClientListLoader` |
| Install modpack from file | `ModModpack.ModpackInstall()` | Restores file selection and installs in a background thread |
| Modpack installation | `ModModpack.ModpackInstall(File, …)` | Calls the installer that matches the detected modpack format |
| Disable auto-update | `ModSecret.UpdateStart` | Returns immediately |
| Community region lock | `RegionAssessment.get_IsRestricted` | Returns `false`; every guarded entry (download, install, repair, modpack) reads this one property |

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) 8 or later, or Visual Studio 2022 or later.

```bash
git clone https://github.com/Xocio/Campfire.git
cd Campfire
dotnet build src/Campfire/Campfire.csproj -c Release
```

You can also open `Campfire.slnx` in Visual Studio.

Start with `--lang=en` or `--lang=zh` to override the display language.

The Java agent attached to the game for multiplayer is prebuilt in `src/java-agent/dist`, so building Campfire needs no JDK. After changing the agent sources, run `powershell -File src/java-agent/build.ps1` with JDK 21 or later, then build Campfire.

Builds from source never contact Campfire's update and notice service and never send usage statistics.

## Project layout

```
Campfire/
├─ docs/                         Documentation and images
├─ src/Campfire/
│  ├─ Patching/                  Patch framework
│  │  ├─ Patch.cs                Patch base class
│  │  ├─ PatchCatalog.cs         All patches in application order
│  │  ├─ ModuleQuery.cs          Lookup by name and code shape
│  │  ├─ IlEditor.cs             Safe IL editing
│  │  ├─ BundleScanner.cs        Finds assemblies inside a .NET single-file bundle
│  │  └─ Patches/                One class per repair
│  ├─ Launcher/                  Launcher inspection, repair, restore, process check
│  ├─ Link/                      Multiplayer: rooms, networking and the game connection proxy
│  ├─ Online/                    Update check, notices and anonymous usage statistics
│  ├─ Tools/                     JRE Tool
│  ├─ Pages/                     Pages and settings
│  ├─ Controls/                  Animations and custom controls
│  ├─ Localization/              Chinese and English string tables
│  ├─ Themes/                    Control styles, palette generation and wallpaper colors
│  ├─ Assets/                    Icons
│  ├─ MainWindow.xaml            Main window and page changes
│  ├─ TrayIcon.cs                Notification area icon
│  └─ EmbeddedAssemblyLoader.cs  Loads embedded dependencies
├─ src/java-agent/               Java agent attached to the host's game for the welcome message and Tab info
├─ tools/                        Release signing scripts
└─ Campfire.slnx
```

To add a repair: derive from `Patch`, implement `TextKey` / `FindTarget` / `GetState` / `Apply`, add it to `PatchCatalog.All`, and add `Patch.<TextKey>.Title` / `Description` to `Localization/Text.Chinese.cs` and `Text.English.cs`. The Community Edition goes through `CommunityLauncher` instead: its patch point lives in the bundled `PCL.Core` assembly and can only be rewritten in place.

## Dependencies

- [dnlib](https://github.com/0xd4d/dnlib): reads and writes .NET assemblies, MIT License
- [EasyTier](https://github.com/EasyTier/EasyTier): networking component for multiplayer, downloaded on demand the first time, LGPL-3.0
- [ASM](https://asm.ow2.io): Java bytecode library, packaged in the component attached to the game for multiplayer, BSD-3-Clause

Full license texts are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Notes

- Campfire is a community project, not affiliated with PCL or its author.
- Repairs run locally; nothing is uploaded or downloaded.
- Official releases check for updates and notices. Anonymous usage statistics are sent only after you agree; you can see what is sent and turn it off at any time under Settings › General.
- Please report issues on the [issue tracker](https://github.com/Xocio/Campfire/issues).
