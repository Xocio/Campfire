using System;
using System.Collections.Generic;
using System.Globalization;
using Campfire.Localization;

namespace Campfire.Link.Minecraft
{
    /// <summary>
    /// A snapshot of the room, as shown to a player inside the game.
    /// </summary>
    internal sealed class GameOverlayInfo
    {
        public GameOverlayInfo(string hostName, int players, double? latencyMs)
        {
            HostName = hostName ?? "";
            Players = players;
            LatencyMs = latencyMs;
        }

        /// <summary>The name the host uses in the game, or their Campfire name when the game is not running.</summary>
        public string HostName { get; }

        /// <summary>How many players are in the room, the host included.</summary>
        public int Players { get; }

        /// <summary>The joining player's round-trip time to the host, or <c>null</c> when not measured yet.</summary>
        public double? LatencyMs { get; }
    }

    /// <summary>One styled run of text in a chat component.</summary>
    internal sealed class GameText
    {
        public GameText(string text, string color = null, bool bold = false, bool underlined = false)
        {
            Text = text ?? "";
            Color = color;
            Bold = bold;
            Underlined = underlined;
        }

        public string Text { get; }

        public string Color { get; }

        public bool Bold { get; }

        public bool Underlined { get; }
    }

    /// <summary>
    /// The welcome message and the player list header and footer Campfire adds on the host's behalf.
    /// </summary>
    internal static class GameMessage
    {
        /// <summary>The website shown in the game, in Latin letters and without a scheme.</summary>
        public const string Website = "McCampfire.top";

        /// <summary>Three lines in the chat box: the room, the host and the live numbers, then the website.</summary>
        public static IReadOnlyList<GameText> Welcome(GameOverlayInfo info)
        {
            var parts = new List<GameText>();
            AddLine(parts,
                new GameText("Campfire", "gold", bold: true),
                new GameText(" · ", "dark_gray"),
                new GameText(Text.Get("Link.Game.Welcome"), "white"));
            AddStatus(parts, info);
            AddLine(parts, new GameText(Website, "aqua", bold: true, underlined: true));
            return parts;
        }

        /// <summary>Drawn at the top of the player list opened with Tab.</summary>
        public static IReadOnlyList<GameText> Header(GameOverlayInfo info)
        {
            var parts = new List<GameText>();
            AddLine(parts,
                new GameText("Campfire", "gold", bold: true),
                new GameText(" · ", "dark_gray"),
                new GameText(Text.Get("Link.Game.TabTitle"), "white"));
            AddStatus(parts, info);
            return parts;
        }

        /// <summary>Drawn at the bottom of the player list opened with Tab.</summary>
        public static IReadOnlyList<GameText> Footer()
        {
            var parts = new List<GameText>();
            AddLine(parts, new GameText(Website, "aqua", bold: true, underlined: true));
            return parts;
        }

        private static void AddStatus(List<GameText> parts, GameOverlayInfo info)
        {
            AddLine(parts,
                new GameText(Text.Get("Link.Game.Host"), "gray"),
                new GameText(" ", "gray"),
                new GameText(HostName(info), "white"),
                new GameText(" · ", "dark_gray"),
                new GameText(Text.Get("Link.Game.Online"), "gray"),
                new GameText(" ", "gray"),
                new GameText(Text.Format("Link.Game.PlayersValue", Math.Max(0, info.Players)), "white"),
                new GameText(" · ", "dark_gray"),
                new GameText(Text.Get("Link.Game.Latency"), "gray"),
                new GameText(" ", "gray"),
                new GameText(Latency(info), LatencyColor(info)));
        }

        private static string HostName(GameOverlayInfo info)
        {
            return string.IsNullOrWhiteSpace(info.HostName) ? "—" : info.HostName;
        }

        private static string Latency(GameOverlayInfo info)
        {
            if (!HasLatency(info))
                return Text.Get("Link.Game.LatencyMeasuring");
            int milliseconds = (int)Math.Round(info.LatencyMs.Value);
            return Text.Format("Link.Game.LatencyValue", milliseconds.ToString(CultureInfo.InvariantCulture));
        }

        private static string LatencyColor(GameOverlayInfo info)
        {
            if (!HasLatency(info))
                return "gray";
            if (info.LatencyMs.Value <= 80)
                return "green";
            if (info.LatencyMs.Value <= 160)
                return "yellow";
            return "red";
        }

        private static bool HasLatency(GameOverlayInfo info)
        {
            return info.LatencyMs.HasValue
                   && !double.IsNaN(info.LatencyMs.Value)
                   && !double.IsInfinity(info.LatencyMs.Value)
                   && info.LatencyMs.Value >= 0;
        }

        private static void AddLine(List<GameText> parts, params GameText[] line)
        {
            if (parts.Count > 0)
                parts.Add(new GameText("\n"));
            parts.AddRange(line);
        }
    }
}
