using System.Collections.Generic;

namespace Campfire.Link.Room
{
    /// <summary>The room's name, announcement and player limit, as the host sets them.</summary>
    internal sealed class RoomInfo
    {
        private const int MaxName = 48;
        private const int MaxNotice = 200;

        public RoomInfo(string name, string notice, int maxPlayers)
        {
            Name = Clip(name, MaxName);
            Notice = Clip(notice, MaxNotice);
            MaxPlayers = maxPlayers >= 2 && maxPlayers <= 100 ? maxPlayers : 0;
        }

        public string Name { get; }

        /// <summary>The announcement, or an empty text for none.</summary>
        public string Notice { get; }

        /// <summary>The most players the room takes, the host included; 0 for no limit.</summary>
        public int MaxPlayers { get; }

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object>
            {
                ["name"] = Name,
                ["notice"] = Notice,
                ["max"] = (double)MaxPlayers,
            };
        }

        /// <summary>Reads what the host sent; returns <c>null</c> when it is not an object.</summary>
        public static RoomInfo FromJson(object value)
        {
            if (!(value is Dictionary<string, object> json))
                return null;
            string name = json.TryGetValue("name", out object n) ? n as string : null;
            string notice = json.TryGetValue("notice", out object t) ? t as string : null;
            int max = json.TryGetValue("max", out object m) && m is double d && d >= 0 && d <= 100 ? (int)d : 0;
            return new RoomInfo(name, notice, max);
        }

        /// <summary>One line of plain text: control characters removed, cut to <paramref name="limit"/> characters.</summary>
        private static string Clip(string text, int limit)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            var clean = new System.Text.StringBuilder();
            foreach (char c in text.Trim())
                clean.Append(char.IsControl(c) ? ' ' : c);
            string result = clean.ToString();
            return result.Length > limit ? result.Substring(0, limit) : result;
        }
    }
}
