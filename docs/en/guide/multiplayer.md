# Hosting and Joining

Multiplayer in Campfire is organized around **rooms**. The host opens a singleplayer world to LAN and creates a room; friends join with the invite code the host shares. The host's world then appears in the friends' LAN world list, and they enter it exactly as they would on a real local network.

No public IP, port forwarding or dedicated server is needed, and players do not have to use the same launcher.

## Before you begin

- Both sides must run the same game version.
- Both sides need Campfire running for the whole session. After the window is closed, Campfire can keep running in the notification area; see [Personalization](./personalization#notification-area).
- The first time, Campfire downloads a networking component of about 14 MB and verifies every file. Windows Firewall may ask whether it may use the network; choosing **Allow** improves the chance of a direct connection.

## Host: create a room

1. On **Home**, choose **Multiplayer** and click **Create a room**.
2. Start the game, enter the singleplayer world you want to share and open it to LAN:
   - press Esc, choose **Open to LAN** and click **Start LAN World**;
   - newer game versions have no such button; instead, open **World Options** and turn LAN on under **Multiplayer**.
3. Campfire finds the open world and continues on its own:

   | Step | What happens |
   | --- | --- |
   | Prepare the networking component | Downloaded and verified on first use, then reused |
   | Wait for Open to LAN | The game port on this computer is detected; nothing to type |
   | Create the room | The room and its invite code are created |
   | Connect to the network | Campfire connects to the nodes so friends can find the room |

4. Once the room is ready, the invite code is copied to the clipboard. Send it to your friends.

While the room is open, the multiplayer page shows the invite code, the game port, this computer's network type, the number of direct and relayed connections, and the players in the room.

::: tip Entering the port manually
If Campfire does not find the world, click **Enter the port manually** and type the LAN port shown in the game's chat. A dedicated server running on this computer can be shared the same way.
:::

::: info When the room closes
The room closes when the host leaves the world or turns LAN off, or when **Close room** is clicked. Everyone in the room is disconnected.
:::

## Friends: join a room

1. Copy the invite code from the host. Codes look like `U/XXXX-XXXX-XXXX-XXXX`; you can copy the whole message containing it and Campfire picks out the code.
2. Open Campfire. When Home sees a code on the clipboard, it offers to join that room. You can also open the multiplayer page, click **Join a room** and paste.
3. Campfire connects to the network, finds the host and enters the room. On restrictive networks, finding the host takes longer.
4. Open the game's **Multiplayer** screen, find the room among the LAN worlds at the bottom of the list and double-click it.

By default the entry is called **Campfire Room** and shows the player count and latency. You can rename it in the [multiplayer settings](./network#room).

::: warning Enter through the LAN world list
When the host opens LAN with a premium account, the game verifies every joining player. Offline accounts are let in only when the world is picked from the LAN world list; entering the address through Direct Connection or a server entry ends with an "invalid session" error.
:::

After joining, the multiplayer page shows the local server address, latency, connection type and the players in the room. Click **Leave room** to leave.

## Room information in the game

Inside the host's world, Campfire shows the room status in the game itself:

- **Chat** greets each player on arrival.
- **Tab list** header and footer show the host, player count and latency live.
- **LAN world list** shows the player count and latency on the room entry.

The chat and Tab information are sent by the game server itself through a small component attached to the host's game, so they work with premium authentication and encrypted connections, and the host sees them too. If the host's Java runtime cannot attach components, or a mod loader remaps the game, Campfire falls back to showing the information in the LAN world list only; the connection itself is unaffected. The reason is written to `%LocalAppData%\Campfire\agent.log`.

## The room in the notification area

With the notification area icon enabled, the icon follows the room: the flame stays lit while a room is open. Right-click it to see the members, copy the invite code, host or join a room, or exit Campfire.

## Next steps

- [Managing a Room](./room): members, removing and blocking.
- [Network and Privacy](./network): direct and relayed connections, custom nodes, hiding your IP address.
- [FAQ](./faq#multiplayer): troubleshooting joins, invalid sessions and more.
