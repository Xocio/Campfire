using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Campfire.Link;
using Campfire.Localization;

namespace Campfire.Online
{
    /// <summary>
    /// Anonymous usage statistics, sent at most once a day and only after the user agreed.
    /// </summary>
    /// <remarks>
    /// The statistics id is a random number of its own, not the machine id that multiplayer shares with other players.
    /// </remarks>
    internal static class Telemetry
    {
        private const string ConsentKey = "stats";
        private const string LastSentKey = "stats.sent";
        private const string CountsKey = "stats.counts";

        private static readonly object Sync = new object();

        /// <summary><c>true</c> allowed, <c>false</c> declined, <c>null</c> not asked yet.</summary>
        public static bool? Consent
        {
            get
            {
                string value = AppSettings.Get(ConsentKey);
                return value == "1" ? true : value == "0" ? (bool?)false : null;
            }
            set
            {
                AppSettings.Set(ConsentKey, value == null ? null : value.Value ? "1" : "0");
                if (value != true)
                    AppSettings.Set(CountsKey, null);
            }
        }

        private static string IdPath => Path.Combine(AppSettings.DataDirectory, "stats-id");

        /// <summary>Counts one use of a feature, kept until the next report. Nothing is counted without consent.</summary>
        public static void Count(string feature)
        {
            if (Consent != true)
                return;
            lock (Sync)
            {
                var counts = LoadCounts();
                counts[feature] = Math.Min(100000, (counts.TryGetValue(feature, out int n) ? n : 0) + 1);
                AppSettings.Set(CountsKey, string.Join(";", counts.Select(c => c.Key + "=" + c.Value.ToString(CultureInfo.InvariantCulture))));
            }
        }

        private static Dictionary<string, int> LoadCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (string pair in (AppSettings.Get(CountsKey) ?? "").Split(';'))
            {
                int split = pair.IndexOf('=');
                if (split > 0 && int.TryParse(pair.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                    counts[pair.Substring(0, split)] = n;
            }
            return counts;
        }

        /// <summary>Replaces the statistics id.</summary>
        public static void ResetId()
        {
            lock (Sync)
                WriteNewId();
        }

        private static string LoadOrCreateId()
        {
            lock (Sync)
            {
                try
                {
                    if (File.Exists(IdPath))
                    {
                        string stored = File.ReadAllText(IdPath).Trim();
                        if (stored.Length == 32 && stored.All(Uri.IsHexDigit))
                            return stored.ToLowerInvariant();
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
                return WriteNewId();
            }
        }

        private static string WriteNewId()
        {
            string id = Hex.Random(16);
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                File.WriteAllText(IdPath, id);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }
            return id;
        }

        /// <summary>Exactly what the next report sends.</summary>
        private static Dictionary<string, object> BuildReport(string id)
        {
            var features = new Dictionary<string, object>();
            lock (Sync)
            {
                foreach (var count in LoadCounts())
                    features[count.Key] = (double)count.Value;
            }
            return new Dictionary<string, object>
            {
                ["id"] = id,
                ["version"] = OnlineService.ShortVersion.ToString(3),
                ["os"] = Environment.OSVersion.Version.ToString(3),
                ["arch"] = Architecture(),
                ["dotnet"] = FrameworkVersion(),
                ["lang"] = Text.Language == AppLanguage.English ? "en" : "zh",
                ["features"] = features,
            };
        }

        /// <summary>The report as readable text, for the settings page.</summary>
        public static string Preview()
        {
            // Looking must not create an id; one is only made for the first real report.
            string id = File.Exists(IdPath) ? LoadOrCreateId() : Text.Get("Prefs.StatsIdPending");
            var report = BuildReport(id);
            var builder = new StringBuilder();
            foreach (var entry in report)
            {
                if (entry.Value is Dictionary<string, object> features)
                {
                    builder.Append(entry.Key).Append(": ");
                    builder.Append(features.Count == 0 ? "{}" : string.Join(", ", features.Select(f => f.Key + "=" + f.Value)));
                    builder.AppendLine();
                }
                else
                {
                    builder.Append(entry.Key).Append(": ").Append(entry.Value).AppendLine();
                }
            }
            return builder.ToString().TrimEnd();
        }

        /// <summary>Sends today's report when allowed and not sent yet today.</summary>
        public static async Task SendIfDueAsync(CancellationToken cancellation = default)
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!OnlineService.IsAvailable || Consent != true || AppSettings.Get(LastSentKey) == today)
                return;
            if (Interlocked.Exchange(ref _sending, 1) == 1)
                return;
            try
            {
                await SendAsync(today, cancellation).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _sending, 0);
            }
        }

        private static int _sending;

        private static async Task SendAsync(string today, CancellationToken cancellation)
        {
            var report = BuildReport(LoadOrCreateId());
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var content = new StringContent(Json.Serialize(report), Encoding.UTF8, "application/json"))
                using (var response = await OnlineService.Client.PostAsync(OnlineService.BaseUrl + "/v1/report", content,
                    OnlineService.WithTimeout(timeout)).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return;
            }

            lock (Sync)
            {
                // Uses counted while the report was on its way stay for the next one.
                var counts = LoadCounts();
                foreach (var sent in (Dictionary<string, object>)report["features"])
                {
                    if (counts.TryGetValue(sent.Key, out int now))
                        counts[sent.Key] = Math.Max(0, now - (int)(double)sent.Value);
                }
                AppSettings.Set(CountsKey, string.Join(";", counts.Where(c => c.Value > 0)
                    .Select(c => c.Key + "=" + c.Value.ToString(CultureInfo.InvariantCulture))));
                AppSettings.Set(LastSentKey, today);
            }
        }

        private static string Architecture()
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case System.Runtime.InteropServices.Architecture.Arm64:
                    return "arm64";
                case System.Runtime.InteropServices.Architecture.X64:
                    return "x64";
                default:
                    return "x86";
            }
        }

        /// <summary>The installed .NET Framework 4.x version, from the release number Windows records.</summary>
        private static string FrameworkVersion()
        {
            try
            {
                using (var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key?.GetValue("Release") is int release)
                        return release >= 533320 ? "4.8.1" : release >= 528040 ? "4.8" : "4.7";
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
            }
            return "unknown";
        }
    }
}
