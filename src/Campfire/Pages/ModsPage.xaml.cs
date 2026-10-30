using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Campfire.Link;
using Campfire.Localization;
using Campfire.Mods;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// "Mod Sync" page: compares the host's mods with a version on this computer and makes them match. Opened from
    /// the multiplayer page after joining a room.
    /// </summary>
    public partial class ModsPage : UserControl
    {
        private static readonly ModSource[] Sources = { ModSource.OfficialFirst, ModSource.OfficialOnly, ModSource.Host };

        private LinkSession _session;
        private IReadOnlyList<string> _launcherDirectories = Array.Empty<string>();
        private ModCheck _check;
        private bool _checking;
        private bool _syncing;
        private bool _filling;

        public ModsPage()
        {
            InitializeComponent();
            FillSources();
            Render();

            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Raised after every comparison.</summary>
        internal event EventHandler<ModCheck> CheckChanged;

        public bool IsBusy => _syncing;

        /// <summary>Shows the host's mods of <paramref name="session"/> and compares them again.</summary>
        /// <param name="launcherDirectories">Folders that hold a launcher, searched for game folders as well.</param>
        internal async Task ShowAsync(LinkSession session, IEnumerable<string> launcherDirectories)
        {
            if (_session != session)
            {
                if (_session != null)
                    _session.Changed -= Session_Changed;
                _session = session;
                _session.Changed += Session_Changed;
            }
            _launcherDirectories = launcherDirectories.Where(d => !string.IsNullOrEmpty(d)).ToList();
            if (_syncing)
                return;
            if (_check?.Host != session.HostMods)
                _check = null;
            await CheckAsync(null);
        }

        // ======================== Checking ========================

        private async Task CheckAsync(InstalledVersion version)
        {
            var host = _session?.HostMods;
            if (host == null || _checking)
            {
                Render();
                return;
            }

            _checking = true;
            Render();
            try
            {
                _check = version != null && _check != null
                    ? await _check.SelectAsync(version)
                    : await ModCheck.RunAsync(host, _launcherDirectories);
            }
            finally
            {
                _checking = false;
            }
            Render();
            CheckChanged?.Invoke(this, _check);
        }

        private void Session_Changed(object sender, EventArgs e)
        {
            // The room only matters for fetching from the host; the comparison itself stays valid.
            if (IsVisible)
                UpdateButtons();
        }

        // ======================== Rendering ========================

        private void Render()
        {
            _filling = true;
            try
            {
                RenderHost();
                RenderVersions();
                RenderChanges();
                RenderSources();
            }
            finally
            {
                _filling = false;
            }
            UpdateButtons();
        }

        private void RenderHost()
        {
            var host = _session?.HostMods;
            LabHostGame.Text = host == null ? "—" : GameText(host);
            LabHostCount.Text = host == null ? "" : Text.Format("Mods.HostCount", host.Mods.Count);
        }

        /// <summary>"1.20.1 · Fabric 0.16.5", or "1.20.1 · Vanilla" without a loader.</summary>
        internal static string GameText(ModList host)
        {
            string game = host.GameVersion ?? Text.Get("Mods.UnknownVersion");
            if (host.Loader == null)
                return game;
            string loader = host.Loader == VersionLoader.Vanilla ? Text.Get("Mods.Vanilla") : host.Loader.ToString();
            return host.LoaderVersion == null || host.Loader == VersionLoader.Vanilla
                ? game + " · " + loader
                : game + " · " + loader + " " + host.LoaderVersion;
        }

        private void RenderVersions()
        {
            string message = null;
            if (_check == null)
                message = _session?.HostMods == null ? Text.Get("Mods.State.Loading") : Text.Get("Mods.Scanning");
            else if (_check.Candidates.Count == 0)
                message = _check.Host.GameVersion != null || _check.Host.Loader != null
                    ? Text.Format("Mods.NoVersion", GameText(_check.Host))
                    : Text.Get("Mods.NoVersionAny");

            LabVersions.Text = message ?? "";
            LabVersions.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            CmbVersion.Visibility = message == null ? Visibility.Visible : Visibility.Collapsed;
            if (message == null)
            {
                bool many = _check.Candidates.Select(v => v.Folder.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
                CmbVersion.ItemsSource = _check.Candidates.Select(v => many ? v.Name + "  ·  " + v.Folder.Path : v.Name).ToList();
                CmbVersion.SelectedIndex = _check.Candidates.ToList().IndexOf(_check.Version);
            }

            string folder = _check?.ModsDirectory;
            LabTargetPath.Text = folder == null ? "" : Text.Format("Mods.TargetPath", folder);
            LabTargetPath.ToolTip = folder;
        }

        private void RenderChanges()
        {
            ListChanges.Children.Clear();
            var comparison = _check?.Comparison;
            CardChanges.Header = Text.Format("Mods.Changes", comparison?.Changes.Count ?? 0);

            if (comparison != null)
            {
                foreach (var change in comparison.Changes)
                    ListChanges.Children.Add(ChangeRow(change, ListChanges.Children.Count == 0));
            }
            ScrollChanges.Visibility = ListChanges.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            string message = comparison == null ? (_checking || _check == null ? Text.Get("Mods.State.Checking") : "")
                : comparison.IsMatch ? Text.Get("Mods.NoChanges")
                : null;
            LabChanges.Text = message ?? "";
            LabChanges.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;

            LabIgnored.Text = comparison == null ? "" : Text.Format("Mods.Ignored", comparison.Ignored);
            LabIgnored.Visibility = comparison != null && comparison.Ignored > 0 ? Visibility.Visible : Visibility.Collapsed;
            PanRemoveExtra.Visibility = comparison != null && comparison.Count(ModDifference.Extra) > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static FrameworkElement ChangeRow(ModChange change, bool first)
        {
            var row = new Grid { Margin = new Thickness(0, first ? 0 : 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = change.Title, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = new TextBlock
            {
                Margin = new Thickness(0, 1, 0, 0),
                FontSize = 11,
                Text = Detail(change),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray5");
            text.Children.Add(detail);
            row.Children.Add(text);

            var tag = new Border
            {
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
            };
            tag.SetResourceReference(Border.BackgroundProperty, "Brush7");
            var label = new TextBlock { FontSize = 11, Text = Text.Get("Mods.Kind." + change.Kind) };
            label.SetResourceReference(TextBlock.ForegroundProperty,
                change.Kind == ModDifference.Missing ? "BrushOrange" : change.Kind == ModDifference.Different ? "Brush3" : "BrushGray4");
            tag.Child = label;
            Grid.SetColumn(tag, 1);
            row.Children.Add(tag);
            return row;
        }

        private static string Detail(ModChange change)
        {
            switch (change.Kind)
            {
                case ModDifference.Different:
                    return Text.Format("Mods.Detail.Different", change.Local.Version ?? change.Local.FileName,
                        change.Host.Version ?? change.Host.FileName);
                case ModDifference.Missing:
                    return Join(change.Host.Version, change.Host.FileName);
                default:
                    return Join(change.Local.Version, change.Local.FileName);
            }
        }

        private static string Join(string version, string file)
        {
            return version == null ? file : version + "  ·  " + file;
        }

        private void FillSources()
        {
            int index = CmbSource.SelectedIndex >= 0 ? CmbSource.SelectedIndex : Array.IndexOf(Sources, LinkSettings.ModSource);
            _filling = true;
            CmbSource.ItemsSource = Sources.Select(s => Text.Get("Mods.Source." + s)).ToList();
            CmbSource.SelectedIndex = Math.Max(0, index);
            _filling = false;
        }

        /// <summary>A host that does not hand out files leaves the official source as the only choice.</summary>
        private void RenderSources()
        {
            bool offered = _session?.HostMods?.FilesOffered == true;
            CmbSource.IsEnabled = offered && !_syncing;
            CmbSource.ToolTip = offered ? null : Text.Get("Mods.HostNoFiles");
            if (!offered)
                CmbSource.SelectedIndex = Array.IndexOf(Sources, ModSource.OfficialOnly);
            else if (CmbSource.SelectedIndex == Array.IndexOf(Sources, ModSource.OfficialOnly) && LinkSettings.ModSource != ModSource.OfficialOnly)
                CmbSource.SelectedIndex = Array.IndexOf(Sources, LinkSettings.ModSource);
        }

        private ModSource SelectedSource => CmbSource.SelectedIndex >= 0 ? Sources[CmbSource.SelectedIndex] : ModSource.OfficialFirst;

        private void UpdateButtons()
        {
            Cursor = _checking || _syncing ? Cursors.AppStarting : null;
            var comparison = _check?.Comparison;
            bool work = comparison != null && (comparison.Changes.Any(c => c.Host != null)
                                               || ChkRemoveExtra.IsChecked == true && comparison.Count(ModDifference.Extra) > 0);
            BtnSync.IsEnabled = !_syncing && !_checking && work;
            CmbVersion.IsEnabled = !_syncing && !_checking;
            BtnAddFolder.IsEnabled = !_syncing && !_checking;
            BtnOpenFolder.IsEnabled = _check?.ModsDirectoryExists == true;
            ChkRemoveExtra.IsEnabled = !_syncing;
            PanSource.Visibility = _syncing ? Visibility.Hidden : Visibility.Visible;
            PanProgress.Visibility = _syncing ? Visibility.Visible : Visibility.Hidden;
        }

        // ======================== Actions ========================

        private async void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_filling || _check == null || CmbVersion.SelectedIndex < 0 || CmbVersion.SelectedIndex >= _check.Candidates.Count)
                return;
            var version = _check.Candidates[CmbVersion.SelectedIndex];
            if (version != _check.Version)
                await CheckAsync(version);
        }

        private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_filling && CmbSource.IsEnabled && CmbSource.SelectedIndex >= 0)
                LinkSettings.ModSource = SelectedSource;
        }

        private void ChkRemoveExtra_Click(object sender, RoutedEventArgs e)
        {
            UpdateButtons();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            string picked = FolderPicker.Pick(new WindowInteropHelper(Window.GetWindow(this)).Handle, Text.Get("World.AddFolderTitle"));
            if (picked == null)
                return;

            string folder = GameFolders.Resolve(picked);
            if (folder == null)
            {
                _ = Dialogs.Of(this)?.AlertAsync(Text.Get("World.AddFolderInvalidTitle"), Text.Get("World.AddFolderInvalid"));
                return;
            }
            GameFolders.Add(folder);
            _check = null;
            await CheckAsync(null);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            string folder = _check?.ModsDirectory;
            if (folder != null && Directory.Exists(folder))
                Process.Start("explorer.exe", "\"" + folder + "\"");
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            var check = _check;
            var session = _session;
            if (check?.Comparison == null || check.Version == null || session == null || _syncing)
                return;
            var dialogs = Dialogs.Of(this);
            var source = SelectedSource;
            bool removeExtra = ChkRemoveExtra.IsChecked == true;

            _syncing = true;
            BarSync.Value = 0;
            LabProgress.Text = Text.Format("Mods.Syncing", 0);
            Render();

            var progress = new Progress<(double Fraction, string Title)>(p =>
            {
                BarSync.Value = p.Fraction;
                LabProgress.Text = p.Title == null
                    ? Text.Format("Mods.Syncing", (int)(p.Fraction * 100))
                    : Text.Format("Mods.SyncingFile", p.Title);
                BtnSync.Content = Text.Format("Mods.Syncing", (int)(p.Fraction * 100));
            });
            Func<int, Task<bool>> confirmHost = count => Dispatcher.Invoke(() => dialogs == null
                ? Task.FromResult(true)
                : dialogs.ConfirmAsync(Text.Get("Mods.ConfirmHostTitle"), Text.Format("Mods.ConfirmHost", count), Text.Get("Mods.ConfirmContinue")));

            ModSyncResult result = null;
            string error = null;
            try
            {
                result = await ModSync.RunAsync(check.Comparison, check.Version, source, removeExtra, check.Host.FilesOffered,
                    session.ConnectForModsAsync, confirmHost, progress, CancellationToken.None);
            }
            catch (ModSyncException ex)
            {
                error = ex.Key == "HostGone" && session.Stage != LinkStage.Ready ? Text.Get("Mods.RoomClosed") : Text.Get("Mods.Error." + ex.Key);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException
                                       || ex is ArgumentException || ex is NotSupportedException)
            {
                LinkLog.Error("Mod sync failed.", ex);
                error = Text.Format("Mods.Error.Unexpected", ex.Message);
            }
            finally
            {
                _syncing = false;
                BtnSync.Content = Text.Get("Mods.Sync");
            }

            await CheckAsync(check.Version);
            if (error != null)
            {
                if (dialogs != null)
                    await dialogs.ErrorAsync(Text.Get("Mods.FailedTitle"), error);
                return;
            }
            if (result != null && dialogs != null)
                await ShowResultAsync(dialogs, result);
        }

        private static async Task ShowResultAsync(IDialogHost dialogs, ModSyncResult result)
        {
            string message = result.Installed == 0 && result.Removed == 0
                ? Text.Get("Mods.DoneNothing")
                : Text.Format("Mods.Done", result.Installed, result.Removed);
            if (result.Unavailable.Count > 0)
                message += "\n\n" + Text.Get("Mods.DoneUnavailable");
            string backup = result.BackupDirectory;
            await dialogs.ShowDialogAsync(new DialogRequest
            {
                Title = Text.Get("Mods.DoneTitle"),
                Message = message,
                Detail = result.Unavailable.Count == 0 ? null : string.Join("\n", result.Unavailable.Select(m => m.Title + "  ·  " + m.FileName)),
                PrimaryText = Text.Get("Dialog.OK"),
                LinkText = backup == null ? null : Text.Get("Mods.OpenBackup"),
                LinkAction = backup == null ? null : (Action)(() => Process.Start("explorer.exe", "\"" + backup + "\"")),
            });
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            FillSources();
            if (!_syncing)
                BtnSync.Content = Text.Get("Mods.Sync");
            Render();
        }
    }
}
