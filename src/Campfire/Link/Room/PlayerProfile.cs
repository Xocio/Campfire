using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Campfire.Link.Minecraft;
using Campfire.Link.Runtime;

namespace Campfire.Link.Room
{
    internal enum PlayerKind
    {
        Host,
        Guest,
        /// <summary>The player on this computer, when it joined someone else's room.</summary>
        Local,
    }

    /// <summary>
    /// A player in a room. The machine id tells computers apart.
    /// </summary>
    internal sealed class PlayerProfile
    {
        public PlayerProfile(string name, string machineId, string vendor, PlayerKind kind, string uuid = null, string gameName = null,
            double? latencyMs = null)
        {
            GameName = string.IsNullOrEmpty(gameName) ? null : gameName;
            Name = name ?? "";
            MachineId = machineId ?? "";
            Vendor = vendor ?? "";
            Kind = kind;
            Uuid = uuid;
            LatencyMs = latencyMs.HasValue && !double.IsNaN(latencyMs.Value) && !double.IsInfinity(latencyMs.Value)
                        && latencyMs.Value >= 0 && latencyMs.Value <= 60000
                ? latencyMs
                : null;
        }

        public string Name { get; }

        public string MachineId { get; }

        /// <summary>Which program and version the player uses.</summary>
        public string Vendor { get; }

        public PlayerKind Kind { get; }

        /// <summary>
        /// The premium account's UUID when the player's game runs with one. An optional field.
        /// </summary>
        public string Uuid { get; }

        /// <summary>
        /// The player name in the running game, which the host uses to recognize the player inside the world.
        /// An optional field that other programs ignore.
        /// </summary>
        public string GameName { get; }

        /// <summary>
        /// The player's round-trip time to the host, reported with the heartbeat. An optional field.
        /// </summary>
        public double? LatencyMs { get; }

        /// <summary>The profile of the player on this computer.</summary>
        public static PlayerProfile CreateLocal(PlayerKind kind)
        {
            // The account id is only shared when skin faces are enabled.
            var account = GameAccount.Current;
            string uuid = LinkSettings.ShowSkins && account != null && account.IsPremium ? account.Uuid : null;
            return new PlayerProfile(LinkSettings.EffectivePlayerName, LinkSettings.MachineId, LocalVendor, kind, uuid, account?.Name);
        }

        /// <summary>The program and versions, or just the name when the user keeps the version private.</summary>
        public static string LocalVendor => LinkSettings.ShareVersion
            ? string.Format(CultureInfo.InvariantCulture, "Campfire {0}, EasyTier v{1}",
                Assembly.GetExecutingAssembly().GetName().Version.ToString(3), EasyTierPackage.Version)
            : "Campfire";

        public PlayerProfile WithKind(PlayerKind kind)
        {
            return new PlayerProfile(Name, MachineId, Vendor, kind, Uuid, GameName, LatencyMs);
        }

        /// <summary>The same player with the latest measured latency.</summary>
        public PlayerProfile WithLatency(double? latencyMs)
        {
            return new PlayerProfile(Name, MachineId, Vendor, Kind, Uuid, GameName, latencyMs);
        }

        public Dictionary<string, object> ToJson(bool includeKind)
        {
            var json = new Dictionary<string, object>
            {
                ["name"] = Name,
                ["machine_id"] = MachineId,
                ["vendor"] = Vendor,
            };
            if (includeKind)
                json["kind"] = Kind == PlayerKind.Host ? "HOST" : "GUEST";
            if (Uuid != null)
                json["uuid"] = Uuid;
            if (GameName != null)
                json["game_name"] = GameName;
            if (LatencyMs.HasValue)
                json["latency_ms"] = LatencyMs.Value;
            return json;
        }

        /// <summary>Reads a profile; returns <c>null</c> when a required field is missing.</summary>
        public static PlayerProfile FromJson(object value, PlayerKind? kindOverride)
        {
            if (!(value is IDictionary<string, object> json))
                return null;
            if (!(Get(json, "name") is string name) || !(Get(json, "machine_id") is string machineId) || machineId.Length == 0)
                return null;

            string vendor = Get(json, "vendor") as string ?? "";
            PlayerKind kind;
            if (kindOverride.HasValue)
                kind = kindOverride.Value;
            else if (string.Equals(Get(json, "kind") as string, "HOST", StringComparison.Ordinal))
                kind = PlayerKind.Host;
            else if (string.Equals(Get(json, "kind") as string, "GUEST", StringComparison.Ordinal))
                kind = PlayerKind.Guest;
            else
                return null;

            string uuid = Get(json, "uuid") as string;
            if (uuid != null && (uuid.Length != 32 || !IsHex(uuid)))
                uuid = null;

            // Minecraft names are at most 16 characters; anything else is not a game name.
            string gameName = Get(json, "game_name") as string;
            if (gameName != null && (gameName.Length == 0 || gameName.Length > 16))
                gameName = null;

            // Latency is a courtesy value from the joining player; ignore anything implausible.
            double? latency = Get(json, "latency_ms") is double measured && measured >= 0 && measured <= 60000
                ? measured
                : (double?)null;

            return new PlayerProfile(name, machineId, vendor, kind, uuid, gameName, latency);
        }

        private static bool IsHex(string text)
        {
            foreach (char c in text)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }
            return true;
        }

        private static object Get(IDictionary<string, object> json, string key)
        {
            return json.TryGetValue(key, out var value) ? value : null;
        }
    }
}
