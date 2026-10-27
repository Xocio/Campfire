# Updates and Privacy

These options live in **Settings › General**.

## Updates

### Checking for updates and notices

Official releases fetch the latest version information and notices at startup. The request carries no device identifier, and every user receives the same content. You can turn off **Check for updates and notices at startup** and use **Check now** when you want.

### Installing an update

When a new version is found, Campfire shows the release notes and asks first. Once you agree, it:

1. downloads the update;
2. verifies its **digital signature**; only files signed with Campfire's release key are accepted;
3. replaces the program file and restarts.

If a room is open, Campfire tells you beforehand that updating will close it. If the program folder is not writable, or the download or verification fails, the current version stays as it is and Campfire opens the download page for a manual update.

### Required updates

When an old version has a serious problem, a new version may be marked as required. Campfire insists on updating only once a correctly signed new version has been downloaded and verified; should the download or verification fail, Campfire remains fully usable.

::: info Builds from source
Builds made from the source code never contact the update and notice service and never send usage statistics.
:::

## Usage statistics

Usage statistics help us understand how Campfire is used and improve it. They are **sent only with your consent**; declining changes nothing, and you can turn them off at any time in **Settings › General**.

### What is sent

At most once a day per computer, and only:

- a statistics ID, generated randomly on first send, unrelated to the multiplayer device identity and resettable at any time;
- the program version, Windows version and architecture, .NET Framework version and display language;
- how often features such as creating a room, joining a room and repairing a launcher were used;
- the rough country and province, inferred by the server from the network origin.

**View what is sent** shows the complete content of the next report.

### What is never collected

IP addresses, game accounts and UUIDs, player names, rooms and invite codes, file paths, hardware serial numbers and network adapter addresses. The sole exception is a room you choose to list in the lobby; see [Lobby](./lobby#privacy).

### Retention and use

Detailed records are deleted automatically after 90 days; only aggregate numbers without statistics IDs are kept afterwards. The data is used solely to understand usage and improve the program, and is never sold or given to third parties.

### Withdrawing

After statistics are turned off, nothing more is sent. After **Reset statistics ID**, earlier reports can no longer be linked to this computer.

## Privacy in multiplayer

Multiplayer privacy options, such as hiding your IP address, reading the game account and sharing skins, are described in [Network and Privacy](./network#privacy).

The full privacy notice is also available in Campfire under **Settings › General › Privacy**. Every network connection Campfire makes is listed in [Network Security](./security).
