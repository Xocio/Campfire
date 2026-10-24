namespace Campfire.Link.Room
{
    /// <summary>
    /// Request kinds of the room service and fixed values both sides must agree on.
    /// </summary>
    internal static class RoomProtocol
    {
        /// <summary>Echoes the body; used with <see cref="Fingerprint"/> to confirm the service is a room.</summary>
        public const string Ping = "c:ping";

        /// <summary>Lists supported kinds, separated by NUL characters.</summary>
        public const string Protocols = "c:protocols";

        /// <summary>Returns the host's game port as two big-endian bytes.</summary>
        public const string ServerPort = "c:server_port";

        /// <summary>A guest's heartbeat with its profile as JSON; sent every few seconds.</summary>
        public const string PlayerPing = "c:player_ping";

        /// <summary>Returns every player in the room as a JSON array.</summary>
        public const string PlayerList = "c:player_profiles_list";

        /// <summary>Virtual host name prefix of a room's host; the room service port follows it.</summary>
        public const string HostNamePrefix = "scaffolding-mc-server-";

        /// <summary>Virtual address of the host in its room network.</summary>
        public const string HostAddress = "10.144.144.1";

        public static readonly byte[] Fingerprint =
        {
            0x41, 0x57, 0x48, 0x44, 0x86, 0x37, 0x40, 0x59, 0x57, 0x44, 0x92, 0x43, 0x96, 0x99, 0x85, 0x01,
        };
    }
}
