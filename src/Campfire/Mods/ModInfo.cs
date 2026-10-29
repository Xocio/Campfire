using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Campfire.Mods
{
    /// <summary>Where a mod has to be installed for players to play together.</summary>
    internal enum ModSide
    {
        /// <summary>On every computer in the world, or nothing says otherwise.</summary>
        Both,

        /// <summary>Only on the player's own game, such as a minimap or a shader loader.</summary>
        Client,

        /// <summary>Only where the world runs.</summary>
        Server,

        /// <summary>Works on either side alone; the other side may have it or not.</summary>
        Optional,
    }

    /// <summary>One jar in a mods folder: who it is, which version and whether the other side needs it too.</summary>
    internal sealed class ModInfo
    {
        private const int MaxText = 128;
        private const int MaxIds = 16;

        private static readonly Regex Sha1Pattern = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        /// <summary>Names Windows keeps for devices, whatever extension follows them.</summary>
        private static readonly Regex DeviceName = new Regex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public ModInfo(string fileName, string sha1, long size, IReadOnlyList<string> ids, string name, string version, ModSide side,
            uint? fingerprint = null)
        {
            Fingerprint = fingerprint;
            FileName = fileName;
            Sha1 = sha1;
            Size = size;
            Ids = ids ?? Array.Empty<string>();
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            Version = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            Side = side;
        }

        /// <summary>The jar's file name inside the mods folder.</summary>
        public string FileName { get; }

        /// <summary>SHA-1 of the jar in lower-case hex, which also names the file when it is transferred.</summary>
        public string Sha1 { get; }

        public long Size { get; }

        /// <summary>The fingerprint CurseForge files the jar under, or <c>null</c> when the host did not send one.</summary>
        public uint? Fingerprint { get; }

        /// <summary>The mod ids the jar declares, the main one first; empty for a jar without mod metadata.</summary>
        public IReadOnlyList<string> Ids { get; }

        /// <summary>The display name, or <c>null</c>.</summary>
        public string Name { get; }

        public string Version { get; }

        public ModSide Side { get; }

        /// <summary>What to show: the display name, else the id, else the file name.</summary>
        public string Title => Name ?? (Ids.Count > 0 ? Ids[0] : Path.GetFileNameWithoutExtension(FileName));

        /// <summary>Whether players must agree on this mod: content mods must be on both sides.</summary>
        public bool IsShared => Side == ModSide.Both;

        public Dictionary<string, object> ToJson()
        {
            var json = new Dictionary<string, object>
            {
                ["file"] = FileName,
                ["sha1"] = Sha1,
                ["size"] = (double)Size,
                ["ids"] = Ids.Cast<object>().ToArray(),
                ["side"] = Side.ToString().ToLowerInvariant(),
            };
            if (Fingerprint != null)
                json["cf"] = (double)Fingerprint.Value;
            if (Name != null)
                json["name"] = Name;
            if (Version != null)
                json["version"] = Version;
            return json;
        }

        /// <summary>Reads an entry the host sent; anything malformed is dropped rather than trusted.</summary>
        public static ModInfo FromJson(object value)
        {
            if (!(value is Dictionary<string, object> json))
                return null;
            string file = Text(json, "file");
            string sha1 = Text(json, "sha1")?.ToLowerInvariant();
            if (!IsSafeFileName(file) || sha1 == null || !Sha1Pattern.IsMatch(sha1))
                return null;
            if (!(json.TryGetValue("size", out object rawSize) && rawSize is double size) || size < 0 || size > 2L * 1024 * 1024 * 1024)
                return null;

            var ids = json.TryGetValue("ids", out object rawIds) && rawIds is object[] list
                ? list.OfType<string>().Where(IsModId).Take(MaxIds).ToList()
                : new List<string>();
            var side = Enum.TryParse(Text(json, "side"), true, out ModSide parsed) && Enum.IsDefined(typeof(ModSide), parsed)
                ? parsed
                : ModSide.Both;
            uint? fingerprint = json.TryGetValue("cf", out object rawPrint) && rawPrint is double print
                                && print >= 0 && print <= uint.MaxValue && print == Math.Floor(print)
                ? (uint)print
                : (uint?)null;
            return new ModInfo(file, sha1, (long)size, ids, Text(json, "name"), Text(json, "version"), side, fingerprint);
        }

        /// <summary>A plain jar name with no folder part, so a file from the host can only land in the mods folder.</summary>
        public static bool IsSafeFileName(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length <= 160
                   && name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                   && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                   && name.Trim() == name && name.Trim('.').Length > 4
                   && !DeviceName.IsMatch(name);
        }

        public static bool IsModId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= 64 && id.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.'));
        }

        private static string Text(Dictionary<string, object> json, string key)
        {
            if (!json.TryGetValue(key, out object value) || !(value is string text) || text.Length == 0)
                return null;
            return text.Length > MaxText ? text.Substring(0, MaxText) : text;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} ({2})", Title, Version ?? "?", FileName);
        }
    }
}
