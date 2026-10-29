# FAQ

## Multiplayer

### Do I need a particular launcher?

No. Campfire works with the running game, so you can host or join a room whichever launcher and account type start the game. Other clients that speak the same multiplayer protocol can join the same rooms as Campfire users.

### Whose servers carry the game traffic?

Game traffic never passes through Campfire's servers. Players connect peer-to-peer whenever possible; nodes only help them find each other. Traffic goes through a relay node only when relaying is allowed and a direct connection fails.

### "Invalid session" when joining

When the host opens LAN with a premium account, the game verifies every joining player. Enter the room from the LAN worlds at the bottom of the multiplayer list rather than typing the address into Direct Connection. If the message persists, ask the host to open LAN with an offline account.

### The room is not found, or finding the host never finishes

Check, in order:

1. that the invite code is complete and correct;
2. that the host's room is still open;
3. that both sides can reach the same nodes. If either side turned off the built-in public nodes, both must enter the same custom nodes;
4. if the host uses another client, both sides likewise need to reach the same nodes.

### The host was found, but no direct connection is possible

This usually happens when both networks are restrictive, for example both behind NAT4. Turn on **Allow relay connections when a direct connection fails** in the multiplayer settings, or let the player with the better network host. Direct connections are most reliable when both sides have IPv6.

### The host cannot see me, or there is no response after connecting

With **Hide my IP address** turned on, all traffic must pass through relay nodes, and the host must allow relayed connections. The host may also have closed the room, or the network may be unstable; try again later.

### The welcome message or Tab information is missing, or the world is not opened to LAN

These features rely on a component attached to the host's game, which supports vanilla 1.7.2 and later as well as loaders that keep the official names. When the host's Java runtime cannot attach components, or a loader such as Fabric or Quilt remaps the game, open to LAN manually; room information then falls back to the LAN list, and the connection is unaffected. The reason is written to `%LocalAppData%\Campfire\agent.log` on the host's computer.

### A listed room does not appear in the lobby

Confirm that **List in the lobby** was chosen on the multiplayer page and that the bottom left reads **Listed in the lobby**. A room cannot be listed when its name contains inappropriate content, when several players have reported it, or when too many rooms are listed from your network; the page states the specific cause. See [Lobby](./lobby#messages-when-listing).

### The networking component fails to download

Check your network connection. If you use a proxy or an accelerator, try turning it off and retry.

### Windows 7 updates are reported missing

Multiplayer on Windows 7 needs KB3063858 and KB4474419, installed through Windows Update followed by a restart. If Windows Update is unavailable, download them by number from the Microsoft Update Catalog.

### Where can I see why it failed?

The failure page has a **View multiplayer log** button. The log is saved in `%LocalAppData%\Campfire\Link\link.log`; please attach it when reporting a problem.

## Maps

### An imported map does not appear in the game

Confirm that the version you start is the one chosen during import. Launchers with version isolation keep saves within the version folder; once the game starts, Campfire confirms the saves folder from the running game and relocates the map if it differs. If the map still does not appear, **Open Saves Folder** on the import page shows where it resides.

### A game version is missing from the import list

Campfire searches the default `.minecraft` folder, `.minecraft` folders beside launchers, and the game folders recorded in launcher settings. For any other location, choose **Add Game Folder**.

### Command blocks or redstone break after opening the map

When a map from before 1.13 is imported into 1.13 or later, the game converts its format, which frequently breaks commands and redstone. A version labeled **Same version as the map** is the preferred choice.

## Launcher repair

### Is it safe?

Campfire changes only the few methods directly involved in downloads and installation, and adds no network activity. The repaired file is built in memory and checked item by item before anything is written; if any step fails, the original stays untouched. The original is always backed up as `.bak`. See [How It Works](./how-it-works#launcher-repair).

### "Not supported yet"

The launcher's code differs from known builds, so Campfire cannot confirm where to change it and changes nothing. You can report the launcher build in [GitHub Issues](https://github.com/Xocio/Campfire/issues).

### Writing the file fails

The launcher may still be running, or it may sit in a protected folder such as `Program Files`. Close the launcher and try again; if it still fails, run Campfire as administrator.

### Can the launcher still update after a repair?

The repair disables the launcher's auto-update so that an update cannot switch the features off again. To upgrade, download the new launcher manually, then run Campfire to check whether it needs a repair.

### How do I undo a repair?

Click **Restore** in the Launcher Repair Tool, or delete the repaired launcher and rename the `.bak` file back.

## Other

### Could Campfire be controlled remotely?

No. Campfire receives only text and list data from a server, which cannot change settings, run commands or start programs; only updates that pass signature verification and the multiplayer component with matching hashes are ever run. Every connection and its limits of exposure are described in [Network Security](./security).

### My antivirus flags it

Tools that modify other programs are occasionally flagged by mistake. Campfire is fully open source: review the code and [build it yourself](https://github.com/Xocio/Campfire/blob/main/README.en.md#building-from-source). Official files can be checked against the SHA-256 on the [download page](/en/download).

### Where did Campfire go after I closed the window?

With **Keep running in the notification area when closed** turned on, Campfire keeps running in the notification area and the room stays open. Click the icon to bring the window back. Windows 11 may place the icon in the **^** overflow menu at the right of the taskbar.

### Opening the program again shows no new window

Only one Campfire runs per session. Opening it again brings the running window to the front, and shows it again if it was hidden in the notification area.

### How do I report a problem?

Open an issue in [GitHub Issues](https://github.com/Xocio/Campfire/issues), ideally with your Windows version, the steps you took, and the multiplayer log or a screenshot.

You can also chat in the [Telegram group](https://t.me/CampfireGroup) or follow the [Telegram channel](https://t.me/McCampfire) for updates.
