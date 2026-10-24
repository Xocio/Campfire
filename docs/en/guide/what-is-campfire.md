# What is Campfire

Campfire is a Windows toolbox for Minecraft: Java Edition players. It brings remote multiplayer, launcher repair and a few practical tools together in a single program of about 1.3 MB. It needs no installation, writes nothing to the registry, and runs on everything from Windows 7 SP1 to Windows 11.

## Principles

**Focused and lightweight.** Campfire does a few things and does each of them thoroughly. The program needs no installer, and the networking component used for multiplayer is downloaded only on first use, with every file verified.

**Launcher independent.** Multiplayer works with the running game, not with a particular launcher. Whichever launcher and account type start the game, you can host or join a room. Other clients that speak the same multiplayer protocol can join the same rooms as Campfire users.

**Transparent and verifiable.** All of Campfire's source code is public. Every change it makes to a launcher is limited to clearly defined methods, verified in memory before anything is written, and always backed up.

## Features

### Multiplayer

Once the host opens a singleplayer world to LAN, Campfire detects the world and creates a room. When friends join, the host's world appears in their LAN world list; a double-click takes them in, just as on a real local network.

- Players connect peer-to-peer whenever possible, and through relay nodes when networks are restrictive.
- Inside the world, chat greets each player and the Tab list header and footer show the host, player count and latency live.
- The host can see members and remove or block guests; guests can hide their IP address.

See [Hosting and Joining](./multiplayer).

### Tools

- **Launcher repair** restores game downloads, loader installation and modpack installation that a launcher has switched off, and lifts region-based download locks. See [Launcher Repair](./launcher-repair).
- **JRE Tool** lifts a launcher's default block on JREs, so a JRE can start the game just like a JDK. See [JRE Tool](./jre).

### Experience

- Light and dark modes, with a chosen color, the Windows accent color or colors taken from your wallpaper, in six palette styles.
- Carefully tuned transitions for pages, dialogs and controls, which can be turned off at any time.
- A notification area icon shows the multiplayer status live, and the room stays open after the window is closed.
- Simplified Chinese and English, following the system language by default.

## A quick tour

<p align="center">
  <img src="../../images/en/screenshot-main.png" width="640" alt="Campfire main window" />
</p>

The window has three tabs:

| Tab | Contents |
| --- | --- |
| Home | Multiplayer, the current room status and a short how-to |
| Tools | Launcher Repair Tool and JRE Tool; a red dot appears when a repairable launcher is found |
| Settings | Personalize, General, About and Licenses |

On first start, a short tour introduces these areas. You can replay it from **Settings › About**.

## Open source

Campfire is a free, open-source community project hosted on [GitHub](https://github.com/Xocio/Campfire). Third-party components and their licenses are listed in **Settings › Licenses**. Campfire is not affiliated with Mojang Studios or Microsoft.
