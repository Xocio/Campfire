using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Campfire.Link;
using Campfire.Localization;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// The backups of the world a room runs: what the world is, each copy with its time and size, backing up now
    /// and restoring a copy as a new world.
    /// </summary>
    public partial class BackupPage : UserControl
    {
        private LinkSession _session;
        private string _world;
        private IReadOnlyList<BackupEntry> _entries = Array.Empty<BackupEntry>();
        private WorldInfo _info;
        private bool _busy;

        public BackupPage()
        {
            InitializeComponent();
            Text.LanguageChanged += (sender, e) => Render();
        }

        /// <summary>Shows the backups of the world <paramref name="session"/>'s room runs.</summary>
        internal void Show(LinkSession session)
        {
            _session = session;
            _ = ReloadAsync();
        }

        private async Task ReloadAsync()
        {
            var session = _session;
            string worldName = await Task.Run(() => session?.WorldName);
            _world = worldName;
            _entries = Array.Empty<BackupEntry>();
            _info = null;
            Render();
            if (worldName == null)
                return;

            var (entries, info) = await Task.Run(() =>
            {
                var list = WorldBackup.List(worldName);
                var newest = list.Count > 0 ? WorldSource.Open(list[0].Path) : null;
                return (list, newest?.Worlds.FirstOrDefault());
            });
            if (_world != worldName)
                return;
            _entries = entries;
            _info = info;
            Render();
        }

        private void Render()
        {
            CardWorld.Header = !string.IsNullOrWhiteSpace(_info?.LevelName) ? _info.LevelName : _world ?? Text.Get("Page.Backup");
            LabVersion.Text = _info?.VersionName ?? "—";
            LabMode.Text = _info == null ? "—"
                : _info.Hardcore ? Text.Get("World.Mode.Hardcore")
                : Text.Has("World.Mode." + _info.GameType) ? Text.Get("World.Mode." + _info.GameType)
                : "—";
            LabKept.Text = Text.Format("Backup.KeptValue", _entries.Count, MapText.Size(_entries.Sum(e => e.Size)));
            LabNote.Text = Text.Format("Backup.Note", WorldBackup.KeepPerWorld);

            ListBackups.ItemsSource = _entries.Select(e => new BackupRow(e, _session?.IsRoomBackup(e.Path) == true)).ToList();
            LabEmpty.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnBackupNow.IsEnabled = !_busy && _session?.Role == LinkRole.Host && _session.Stage == LinkStage.Ready;
            BtnBackupNow.Content = Text.Get(_busy ? "Backup.Working" : "Backup.Now");
        }

        private async void BtnBackupNow_Click(object sender, RoutedEventArgs e)
        {
            var session = _session;
            if (session == null || _busy)
                return;
            _busy = true;
            Render();
            BackupResult result;
            try
            {
                result = await session.BackupNowAsync();
            }
            finally
            {
                _busy = false;
            }
            await ReloadAsync();
            var dialogs = Dialogs.Of(this);
            if (result.State != BackupState.Done && dialogs != null)
                await dialogs.ErrorAsync(Text.Get("Backup.Now"),
                    Text.Get(result.State == BackupState.Skipped ? "Backup.Value.Skipped" : "Backup.Value.Failed"));
        }

        private async void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            var session = _session;
            if (!((sender as FrameworkElement)?.Tag is BackupEntry entry) || dialogs == null || session == null || _busy)
                return;
            string time = entry.Time.ToString(Text.Get("Backup.TimeFormat"), CultureInfo.InvariantCulture);
            if (!await dialogs.ConfirmAsync(Text.Get("Backup.RestoreTitle"), Text.Format("Backup.RestoreMessage", time), Text.Get("Backup.Restore")))
                return;

            _busy = true;
            Render();
            try
            {
                string folder = await session.RestoreBackupAsync(entry);
                await dialogs.AlertAsync(Text.Get("Backup.RestoredTitle"), Text.Format("Backup.Restored", System.IO.Path.GetFileName(folder)));
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException
                                       || ex is InvalidOperationException || ex is System.IO.InvalidDataException)
            {
                LinkLog.Warn("A backup could not be restored: " + ex.Message);
                await dialogs.ErrorAsync(Text.Get("Backup.RestoreTitle"), Text.Format("Settings.ActionFailed", ex.Message));
            }
            finally
            {
                _busy = false;
                Render();
            }
        }

        private async void BtnReveal_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string path)
                await OpenAsync("/select,\"" + path + "\"");
        }

        private async void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            System.IO.Directory.CreateDirectory(WorldBackup.Directory);
            await OpenAsync("\"" + WorldBackup.Directory + "\"");
        }

        private async Task OpenAsync(string arguments)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", arguments);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.ComponentModel.Win32Exception)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Backup.Open"), Text.Format("Settings.ActionFailed", ex.Message));
            }
        }

        /// <summary>One backup as the list shows it.</summary>
        private sealed class BackupRow
        {
            public BackupRow(BackupEntry entry, bool current)
            {
                Entry = entry;
                Path = entry.Path;
                Title = entry.Time.ToString(Text.Get("Backup.TimeFormat"), CultureInfo.InvariantCulture);
                var age = DateTime.Now - entry.Time;
                string ago = age.TotalDays >= 1 ? Text.Format("Backup.DaysAgo", (int)age.TotalDays) : LobbyText.Ago(entry.Time.ToUniversalTime());
                Detail = Text.Format("Backup.EntryDetail", ago, MapText.Size(entry.Size));
                CurrentVisibility = current ? Visibility.Visible : Visibility.Collapsed;
            }

            public BackupEntry Entry { get; }

            public string Path { get; }

            public string Title { get; }

            public string Detail { get; }

            public Visibility CurrentVisibility { get; }
        }
    }
}
