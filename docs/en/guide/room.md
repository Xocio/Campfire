# Managing a Room

## Members

The multiplayer page lists everyone in the room. The host and you are marked **Host** and **You**. Each member shows:

- **Name**: read from the game by default, or set yourself in the multiplayer settings.
- **Face**: premium players show the face and hat layer of their skin, others show the first letter of their name. Skins come from Mojang's public service and are cached locally for a day.
- **Client**: the multiplayer client and its version. When a player turns off "Show the program version to other players", only the client name is shown.

The connection to the host, direct or relayed together with the transport used, and its latency appear in the room information at the top of the page.

## Removing and blocking

The host can act on any guest:

| Action | Effect |
| --- | --- |
| **Remove** | The player leaves the world immediately and cannot rejoin for one minute |
| **Block** | The player leaves the world immediately and can no longer join any room you create |

Removed or blocked players see a message in the game, and Campfire tells them why joining failed.

This is enforced by a connection gate on the host's computer: every guest connection reaches the gate before it reaches the world. Even a guest who bypasses the client interface cannot enter a room that has turned them away.

### Managing the blocklist

The blocklist is stored on the host's computer and applies to every room created afterwards. **Multiplayer settings › Blocklist** shows the blocked players and when they were blocked, and lets you unblock them one by one.

## Closing and leaving

- **Host**: click **Close room**, or simply leave the world. If other players are still in the room, Campfire asks first.
- **Guest**: click **Leave room**.
- **Exiting Campfire** ends the room. With "Keep running in the notification area when closed" turned on, the window's close button only hides the window and the room stays open; use the notification area menu to exit.

When Campfire exits, the networking component exits with it, even if Campfire crashes, so nothing is left running in the background.

## Player name and account

Campfire reads the player name, UUID and account type from the running game's command line, so it identifies players correctly whichever launcher is used. Only these three values are read; **login credentials are never read or stored**.

Recent Minecraft versions allow only letters, digits and underscores in player names. If the game name contains other characters, Campfire warns before joining, because such a name cannot enter the world.

If you prefer not to read the game account, turn off "Read the player name and account type from the game" in the multiplayer settings and enter a name yourself.
