using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Campfire.Localization;
using Campfire.Pages;

namespace Campfire.Online
{
    internal enum CheckResult
    {
        UpToDate,
        UpdateAvailable,
        Failed,
    }

    /// <summary>
    /// What happens online while Campfire runs: checking for updates and notices at startup and then every few
    /// hours, asking once whether statistics may be sent, and sending them once a day.
    /// </summary>
    internal sealed class OnlineFlow
    {
        private const string ReleasesUrl = "https://github.com/Xocio/Campfire/releases/latest";
        private const int MaxNotesLength = 1500;

        private readonly IDialogHost _host;
        private readonly Func<bool> _isRoomActive;
        private readonly Action<Notice> _showBanner;
        private readonly Action<string> _exit;
        private readonly HashSet<string> _shownThisRun = new HashSet<string>();
        private Version _postponed;
        private bool _offering;
        private Manifest _manifest;

        /// <param name="host">Shows the dialogs.</param>
        /// <param name="isRoomActive">Whether a multiplayer room is open; updates do not interrupt one on their own.</param>
        /// <param name="showBanner">Shows a notice on the home page, or hides it when given <c>null</c>.</param>
        /// <param name="exit">Closes Campfire, then starts the given program when it is not <c>null</c>.</param>
        public OnlineFlow(IDialogHost host, Func<bool> isRoomActive, Action<Notice> showBanner, Action<string> exit)
        {
            _host = host;
            _isRoomActive = isRoomActive;
            _showBanner = showBanner;
            _exit = exit;
            Current = this;
        }

        public static OnlineFlow Current { get; private set; }

        public async void Start()
        {
            Updater.CleanUp();
            Telemetry.Count("launch");
            if (!OnlineService.IsAvailable)
                return;

            // Let the window settle first; nothing here is urgent.
            await Task.Delay(TimeSpan.FromSeconds(2));
            if (OnlineService.CheckEnabled)
                await CheckAsync(manual: false);
            if (Telemetry.Consent == null)
                await AskConsentAsync();
            await Telemetry.SendIfDueAsync();

            while (true)
            {
                await Task.Delay(_manifest?.CheckInterval ?? TimeSpan.FromHours(6));
                if (OnlineService.CheckEnabled)
                    await CheckAsync(manual: false);
                await Telemetry.SendIfDueAsync();
            }
        }

        /// <summary>Looks for an update and new notices. A manual check also offers an update postponed earlier.</summary>
        public async Task<CheckResult> CheckAsync(bool manual)
        {
            var manifest = await OnlineService.FetchAsync();
            if (manifest == null)
                return CheckResult.Failed;
            _manifest = manifest;

            var result = CheckResult.UpToDate;
            if (Updater.IsNewer(manifest.Update))
            {
                result = CheckResult.UpdateAvailable;
                bool required = Updater.IsRequired(manifest);
                if (required || manual || (_postponed != manifest.Update.Version && !_isRoomActive()))
                    await OfferUpdateAsync(manifest.Update, required, manual);
            }
            await ShowNoticesAsync(manifest);
            return result;
        }

        // ======================== Update ========================

        private async Task OfferUpdateAsync(UpdateInfo update, bool required, bool manual)
        {
            if (_offering)
                return;
            _offering = true;
            try
            {
                // The download is small; fetching it first means the update applies at once when the user agrees.
                // It also proves the update is real: the manifest is not signed, so a required update only keeps
                // Campfire closed once a correctly signed file exists. Without one, Campfire stays usable.
                string staged = await Updater.DownloadAsync(update);
                if (staged == null)
                {
                    if (manual || required)
                        await DownloadFailedAsync();
                    return;
                }

                if (!Updater.CanInstall())
                {
                    await OfferDownloadPageAsync(update, required);
                    return;
                }

                string version = update.Version.ToString(3);
                var choice = await _host.ShowDialogAsync(new DialogRequest
                {
                    Title = Text.Format(required ? "Update.RequiredTitle" : "Update.Title", version),
                    Message = CleanNotes(update.Notes),
                    Detail = Text.Get(_isRoomActive() ? "Update.RestartRoom" : "Update.Restart"),
                    PrimaryText = Text.Get("Update.Install"),
                    SecondaryText = Text.Get(required ? "Update.Exit" : "Update.Later"),
                });

                if (choice != DialogChoice.Primary)
                {
                    if (required)
                        _exit(null);
                    else
                        _postponed = update.Version;
                    return;
                }

                string path;
                try
                {
                    path = Updater.Install(staged);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    await _host.ErrorAsync(Text.Get("Update.InstallFailedTitle"), Text.Format("Update.InstallFailed", ex.Message));
                    OpenUrl(ReleasesUrl);
                    if (required)
                        _exit(null);
                    return;
                }
                Telemetry.Count("update.install");
                _exit(path);
            }
            finally
            {
                _offering = false;
            }
        }

        /// <summary>Campfire sits in a folder it may not write to, so the user downloads the update.</summary>
        private async Task OfferDownloadPageAsync(UpdateInfo update, bool required)
        {
            var choice = await _host.ShowDialogAsync(new DialogRequest
            {
                Title = Text.Format(required ? "Update.RequiredTitle" : "Update.Title", update.Version.ToString(3)),
                Message = CleanNotes(update.Notes),
                Detail = Text.Get("Update.NoPermission"),
                PrimaryText = Text.Get("Update.OpenPage"),
                SecondaryText = Text.Get(required ? "Update.Exit" : "Update.Later"),
            });
            if (choice == DialogChoice.Primary)
                OpenUrl(ReleasesUrl);
            else
                _postponed = update.Version;
            if (required)
                _exit(null);
        }

        /// <summary>No source delivered a correctly signed file; Campfire keeps running, even for a required update.</summary>
        private async Task DownloadFailedAsync()
        {
            var choice = await _host.ShowDialogAsync(new DialogRequest
            {
                Title = Text.Get("Update.DownloadFailedTitle"),
                Message = Text.Get("Update.DownloadFailed"),
                PrimaryText = Text.Get("Update.OpenPage"),
                SecondaryText = Text.Get("Dialog.Close"),
                IsError = true,
            });
            if (choice == DialogChoice.Primary)
                OpenUrl(ReleasesUrl);
        }

        /// <summary>Release notes are Markdown; the dialog shows plain text, without the checksum section.</summary>
        internal static string CleanNotes(string notes)
        {
            var lines = new List<string>();
            foreach (string raw in (notes ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("#") && (line.Contains("文件校验") || line.Contains("Checksum")))
                    break;
                line = line.TrimStart('#').Trim().Replace("**", "").Replace("`", "");
                if (line.StartsWith("- ") || line.StartsWith("* "))
                    line = "• " + line.Substring(2);
                if (line.Length > 0 || (lines.Count > 0 && lines[lines.Count - 1].Length > 0))
                    lines.Add(line);
            }
            string text = string.Join("\n", lines).Trim();
            if (text.Length == 0)
                return Text.Get("Update.NoNotes");
            return text.Length > MaxNotesLength ? text.Substring(0, MaxNotesLength).TrimEnd() + "…" : text;
        }

        // ======================== Notices ========================

        private async Task ShowNoticesAsync(Manifest manifest)
        {
            var notices = Notices.Applicable(manifest).Where(n => !_shownThisRun.Contains(n.Id)).ToList();

            // The newest banner stays on the home page until it is closed.
            _showBanner(notices.FirstOrDefault(n => n.IsBanner));

            foreach (var notice in notices.Where(n => !n.IsBanner))
            {
                _shownThisRun.Add(notice.Id);
                bool hasButton = notice.ButtonUrl != null && !string.IsNullOrEmpty(notice.ButtonText);
                var choice = await _host.ShowDialogAsync(new DialogRequest
                {
                    Title = notice.Title,
                    Message = notice.Body,
                    IsError = notice.IsWarning,
                    TitleColor = notice.TitleColor,
                    MessageColor = notice.BodyColor,
                    PrimaryText = hasButton ? notice.ButtonText : Text.Get("Dialog.OK"),
                    SecondaryText = hasButton ? Text.Get("Dialog.Close") : null,
                });
                Notices.MarkSeen(notice);
                if (hasButton && choice == DialogChoice.Primary)
                    OpenUrl(notice.ButtonUrl);
            }
        }

        /// <summary>Called when the user closes the banner on the home page.</summary>
        public void BannerClosed(Notice notice)
        {
            _shownThisRun.Add(notice.Id);
            Notices.MarkSeen(notice);
        }

        // ======================== Statistics ========================

        private async Task AskConsentAsync()
        {
            while (true)
            {
                var choice = await _host.ShowDialogAsync(new DialogRequest
                {
                    Title = Text.Get("Stats.AskTitle"),
                    Message = Text.Get("Stats.AskMessage"),
                    Detail = Text.Get("Stats.AskDetail"),
                    PrimaryText = Text.Get("Stats.Allow"),
                    SecondaryText = Text.Get("Stats.Deny"),
                    LinkText = Text.Get("Stats.Privacy"),
                    LinkCloses = true,
                });
                // Reading the privacy notice comes back to the question.
                if (choice == DialogChoice.Link)
                {
                    await ShowPrivacyAsync(_host);
                    continue;
                }
                Telemetry.Consent = choice == DialogChoice.Primary;
                return;
            }
        }

        /// <summary>What is sent and kept, shown inside Campfire.</summary>
        public static Task ShowPrivacyAsync(IDialogHost host)
        {
            return host.ShowDialogAsync(new DialogRequest
            {
                Title = Text.Get("Privacy.Title"),
                Message = Text.Get("Privacy.Body"),
                PrimaryText = Text.Get("Dialog.Close"),
            });
        }

        internal void OpenUrl(string url)
        {
            try
            {
                Browser.Open(url);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                _ = _host.ErrorAsync(Text.Get("Dialog.BrowserFailedTitle"), Text.Format("Dialog.BrowserFailed", url, ex.Message));
            }
        }
    }
}
