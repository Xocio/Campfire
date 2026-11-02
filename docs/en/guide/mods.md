# Mod Sync

After you join a room, mod sync compares your game version and mods with the host's, lists what differs, and makes your mods match the host's in one step.

## Host: sharing the mod list

Mod sync is on by default. While a room is open, switch it on or off at any time with **Turn On** or **Turn Off** under **Tools › Mod sync** on the multiplayer page; turning it off stops serving the mod list and files at once. The defaults live in **Settings › Multiplayer › Mods**:

| Option | Description |
| --- | --- |
| **Let players sync my mods** | Players in the room can see your game version, mod loader and the name and version of each mod, and sync to them |
| **Let players download mods from me** | Mods the official sources do not carry are sent from your computer over the room network, which uses upload bandwidth. Only files listed in your mods folder can be downloaded |

## Players: syncing mods

1. After joining a room, click **Sync** under **Tools › Mod sync** on the multiplayer page to open the Mod Sync page.
2. The top of the page shows the host's game version, loader and number of mods.
3. Under **Local Version**, choose the installed version that matches the host's. If it is not listed, click **Add Game Folder**; if it still cannot be found, install that version in your launcher first.
4. Review the differences:

   | Mark | Meaning |
   | --- | --- |
   | **Missing** | The host has it, you do not |
   | **Different** | Both have it, in different versions |
   | **Not on host** | You have it, the host does not. Tick **Remove mods the host does not have** to move them to a backup folder |

   Mods only one side needs, such as minimaps and shaders, are not counted.

5. Choose a **Source** and click **Sync Mods**.

| Source | Description |
| --- | --- |
| **Official first** | Downloads the host's exact files from Modrinth and CurseForge, and fetches the rest from the host |
| **Official only** | Uses only Modrinth and CurseForge; mods that cannot be fetched are listed at the end for you to install |
| **All from host** | Every file is sent from the host's computer over the room network |

::: warning Fetch mods from the host only if you trust the host
Mods are programs that run with the game. Before fetching files from the host, Campfire tells you how many there are and asks for your consent.
:::

## Safeguards

- Whatever the source, each file must match the size and SHA-1 the host listed, or it is discarded.
- The mods folder is changed only after every file has been downloaded and verified.
- Replaced or removed mods are moved to a backup folder, never deleted. Click **Open Backup Folder** after the sync to find them.
- The host can serve only the files on its list from its mods folder; players cannot read any other file through it.

The full details are in [Network Security](./security#mod-sync).

## Common messages

| Message | What to do |
| --- | --- |
| The host has mod sync off | Ask the host to turn it on under **Tools › Mod sync** |
| No installed version matches the host's game | Install the host's version in your launcher, then return to the page |
| The game of this version is running | Close the game before syncing |
| Files in the mods folder are in use | Close the game and try again |
| The host did not provide the file | The host may have turned mod downloads off or changed the mod |
| A downloaded file did not match the host's and was discarded | Try again, or choose another source |
| You have left the room | Join the room again before syncing |
