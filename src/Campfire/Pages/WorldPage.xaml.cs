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
using System.Windows.Threading;
using Microsoft.Win32;
using Campfire.Link.Minecraft;
using Campfire.Localization;
using Campfire.Worlds;

namespace Campfire.Pages
{
    /// <summary>
    /// "Import Map" page: reads a downloaded map, lists the installed versions that can open it and copies it
    /// into the chosen version's saves folder. Once the game runs, the saves folder is checked against the one
    /// the game really uses and the map is moved there if Campfire guessed wrong.
    /// </summary>
    public partial class WorldPage : UserControl
    {
        private enum HintKind
        {
            Info,
            Success,
            Warning,
            Error,
        }

        /// <summary>How often to look for the started game, and for how long after an import.</summary>
        private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan WatchDuration = TimeSpan.FromMinutes(30);

        private readonly DispatcherTimer _watch;
        private DateTime _watchUntil;
        private bool _watching;

        private string _launcherDirectory;
        private IReadOnlyList<GameFolder> _folders = new GameFolder[0];
        private IReadOnlyList<InstalledVersion> _versions;
        private bool _scanning;
        private bool _reading;
        private bool _importing;

        private WorldSource _source;
        private WorldInfo _world;
        private IReadOnlyList<WorldMatch> _matches = new WorldMatch[0];
        private InstalledVersion _selected;

        /// <summary>The imported world folder, and the version it was imported for.</summary>
        private string _imported;
        private InstalledVersion _importedFor;
        private bool _savesConfirmed;

        // The hint is produced on demand so it follows language changes.
        private HintKind _hintKind;
        private Func<string> _hintText;

        public WorldPage()
        {
            InitializeComponent();

            _watch = new DispatcherTimer { Interval = WatchInterval };
            _watch.Tick += async (sender, e) => await CheckRunningGameAsync();

            ShowHint(HintKind.Info, () => Text.Get("World.Start"));
            Render();

            Text.LanguageChanged += OnLanguageChanged;
            Unloaded += (sender, e) => Text.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Raised when the user wants to open a room for the imported map.</summary>
        public event EventHandler CreateRoomRequested;

        public bool IsBusy => _scanning || _reading || _importing;

        // ======================== Loading ========================

        /// <summary>Looks for installed versions again, also next to <paramref name="launcherPath"/> when given.</summary>
        internal async Task RefreshVersionsAsync(string launcherPath)
        {
            if (!string.IsNullOrEmpty(launcherPath))
                _launcherDirectory = Path.GetDirectoryName(launcherPath);
            if (_scanning || _importing)
                return;

            _scanning = true;
            Render();
            try
            {
                var directories = new[] { AppDomain.CurrentDomain.BaseDirectory, _launcherDirectory };
                var result = await Task.Run(() =>
                {
                    var folders = GameFolders.Find(directories);
                    return (Folders: folders, Versions: folders.SelectMany(InstalledVersion.Scan).ToList());
                });
                _folders = result.Folders;
                _versions = result.Versions;
            }
            finally
            {
                _scanning = false;
            }
            UpdateMatches();
            Render();
        }

        /// <summary>Reads the map at <paramref name="path"/>: a zip file, a world folder, a folder of worlds or a level.dat.</summary>
        internal async Task SelectAsync(string path)
        {
            if (_importing || string.IsNullOrEmpty(path))
                return;
            if (string.Equals(Path.GetFileName(path), "level.dat", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                path = Path.GetDirectoryName(path);

            ResetImport();
            _reading = true;
            _source = null;
            _world = null;
            UpdateMatches();
            ShowHint(HintKind.Info, () => Text.Get("World.Reading"));
            Render();
            try
            {
                _source = await Task.Run(() => WorldSource.Open(path));
            }
            finally
            {
                _reading = false;
            }

            if (_source.Problem != WorldSourceProblem.None)
            {
                var problem = _source.Problem;
                ShowHint(HintKind.Error, () => Text.Get("World.Problem." + problem));
            }
            SelectWorld(_source.Worlds.FirstOrDefault());
        }

        private void SelectWorld(WorldInfo world)
        {
            _world = world;
            ResetImport();
            if (world != null)
                ShowHint(Kind(world), () => Describe(world));
            UpdateMatches();
            Render();
        }

        private void UpdateMatches()
        {
            _matches = _world == null || _versions == null ? new WorldMatch[0] : WorldMatch.Rank(_world, _versions);
            if (_selected == null || _matches.All(m => m.Version != _selected))
                _selected = _matches.FirstOrDefault()?.Version;
        }

        // ======================== Rendering ========================

        private void Render()
        {
            RenderWorld();
            RenderVersions();
            UpdateButtons();
        }

        private void RenderWorld()
        {
            if (_source == null)
            {
                LabWorldName.Text = Text.Get("World.NoMap");
                LabSourcePath.Text = Text.Get("World.PickHint");
                LabSourcePath.ToolTip = null;
                PanVersion.Visibility = Visibility.Collapsed;
                CmbWorld.Visibility = Visibility.Collapsed;
                return;
            }

            LabWorldName.Text = _world?.LevelName is string name && name.Length > 0
                ? name
                : _world != null ? WorldImporter.FolderName(_source, _world) : Path.GetFileName(_source.Path);
            LabSourcePath.Text = _source.Path;
            LabSourcePath.ToolTip = _source.Path;
            PanVersion.Visibility = _world == null ? Visibility.Collapsed : Visibility.Visible;
            LabVersion.Text = _world?.VersionName ?? Text.Get("World.VersionUnknown");

            if (_source.Worlds.Count > 1)
            {
                CmbWorld.Visibility = Visibility.Visible;
                CmbWorld.ItemsSource = _source.Worlds.Select(w => w.Directory + "  ·  " + (w.VersionName ?? Text.Get("World.VersionUnknown"))).ToList();
                CmbWorld.SelectedIndex = Math.Max(0, _source.Worlds.ToList().IndexOf(_world));
            }
            else
            {
                CmbWorld.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderVersions()
        {
            ListVersions.Children.Clear();
            bool many = _folders.Count > 1;
            foreach (var match in _matches)
            {
                var option = new RadioButton
                {
                    GroupName = "WorldVersion",
                    Tag = match.Version,
                    Margin = new Thickness(0, ListVersions.Children.Count == 0 ? 0 : 10, 0, 0),
                    Style = (Style)FindResource("PclRadioButton"),
                    IsChecked = match.Version == _selected,
                    IsEnabled = !_importing,
                    Content = VersionRow(match, many, _world),
                };
                option.Checked += Version_Checked;
                ListVersions.Children.Add(option);
            }
            ScrollVersions.Visibility = _matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            string message = null;
            if (_scanning || _versions == null)
                message = Text.Get("World.Scanning");
            else if (_folders.Count == 0)
                message = Text.Get("World.NoFolder");
            else if (_world == null)
                message = Text.Get("World.TargetHint");
            else if (_matches.Count == 0)
                message = _world.VersionName != null ? Text.Format("World.NoVersion", _world.VersionName) : Text.Get("World.NoVersionAny");
            LabVersions.Text = message ?? "";
            LabVersions.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;

            string saves = _imported != null ? Path.GetDirectoryName(_imported) : _selected?.SavesDirectory;
            if (saves != null && _world != null)
            {
                bool guessed = _imported != null ? !_savesConfirmed && _importedFor?.SavesCertainty == SavesCertainty.Guessed
                                                 : _selected.SavesCertainty == SavesCertainty.Guessed;
                string key = _savesConfirmed ? "World.SavesConfirmed" : guessed ? "World.SavesGuessed" : "World.SavesAt";
                LabSaves.Text = Text.Format(key, saves);
                LabSaves.ToolTip = saves;
                LabSaves.Visibility = Visibility.Visible;
            }
            else
            {
                LabSaves.Visibility = Visibility.Collapsed;
            }
            RenderAutoLan();
        }

        private static FrameworkElement VersionRow(WorldMatch match, bool showFolder, WorldInfo world)
        {
            var parts = new List<string>();
            // The folder name usually is the game version already; only a renamed or modded version needs it spelled out.
            if (!string.Equals(match.Version.GameVersion, match.Version.Name, StringComparison.OrdinalIgnoreCase))
                parts.Add(match.Version.GameVersion ?? Text.Get("World.VersionUnknown"));
            parts.Add(FitText(match.Fit));
            if (match.LoaderMissing)
                parts.Add(Text.Format("World.LoaderMissing", world.Loader == WorldLoader.NeoForge ? "NeoForge" : "Forge"));
            if (showFolder)
                parts.Add(match.Version.Folder.Path);

            var panel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            panel.Children.Add(new TextBlock { Text = match.Version.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            var detail = new TextBlock
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 12,
                Text = string.Join("  ·  ", parts),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty,
                match.LoaderMissing || match.Fit == WorldFit.MajorUpgrade ? "BrushOrange" : "BrushGray4");
            panel.Children.Add(detail);
            return panel;
        }

        private static string FitText(WorldFit fit)
        {
            return Text.Get("World.Fit." + fit);
        }

        private static string WorldLoaderText(WorldInfo world)
        {
            string name = world.Loader == WorldLoader.NeoForge ? "NeoForge" : "Forge";
            return world.LoaderVersion == null ? name : name + " " + world.LoaderVersion;
        }

        /// <summary>The summary of a world: its version and mode, then what to watch out for.</summary>
        private static string Describe(WorldInfo world)
        {
            var lines = new List<string>();
            if (world.VersionName == null)
            {
                lines.Add(Text.Get("World.SummaryUnknown"));
            }
            else
            {
                string mode = world.Hardcore ? Text.Get("World.Mode.Hardcore")
                    : Text.Has("World.Mode." + world.GameType) ? Text.Get("World.Mode." + world.GameType) : Text.Get("World.Mode.0");
                lines.Add(Text.Format("World.Summary", world.VersionName, mode));
            }
            if (world.VersionInferred)
                lines.Add(Text.Get("World.Inferred"));
            if (world.Loader != WorldLoader.None)
                lines.Add(Text.Format("World.Mods", WorldLoaderText(world), world.Mods.Count));
            if (world.FromBackup)
                lines.Add(Text.Get("World.Backup"));
            return string.Join("\n", lines);
        }

        private static HintKind Kind(WorldInfo world)
        {
            return world.VersionName == null || world.Loader != WorldLoader.None || world.FromBackup ? HintKind.Warning : HintKind.Info;
        }

        private void UpdateButtons()
        {
            Cursor = IsBusy ? Cursors.AppStarting : null;
            BtnBrowse.IsEnabled = !_importing;
            CmbWorld.IsEnabled = !_importing;
            BtnAddFolder.IsEnabled = !_importing && !_scanning;
            BtnOpenSaves.IsEnabled = _imported != null && Directory.Exists(_imported);

            if (_importing)
                return;
            if (_imported != null)
            {
                BtnImport.Content = Text.Get("World.CreateRoom");
                BtnImport.IsEnabled = true;
            }
            else
            {
                BtnImport.Content = Text.Get("World.Import");
                BtnImport.IsEnabled = !IsBusy && _world != null && _selected != null;
            }
        }

        // ======================== Import ========================

        private async void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            if (_imported != null)
            {
                CreateRoomRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (_world == null || _selected == null || IsBusy)
                return;

            var match = _matches.First(m => m.Version == _selected);
            var host = Dialogs.Of(this);
            if (host != null && match.LoaderMissing
                && !await host.ConfirmAsync(Text.Get("World.ConfirmLoaderTitle"), Text.Format("World.ConfirmLoader", WorldLoaderText(_world)),
                    Text.Get("World.ConfirmContinue")))
                return;
            if (host != null && match.Fit == WorldFit.MajorUpgrade
                && !await host.ConfirmAsync(Text.Get("World.ConfirmMajorTitle"),
                    Text.Format("World.ConfirmMajor", _world.VersionName ?? Text.Get("World.VersionUnknown"), _selected.GameVersion ?? _selected.Name),
                    Text.Get("World.ConfirmContinue")))
                return;

            var source = _source;
            var world = _world;
            var version = _selected;
            _importing = true;
            BarImport.Value = 0;
            BarImport.Visibility = Visibility.Visible;
            BtnImport.IsEnabled = false;
            BtnImport.Content = Text.Format("World.Importing", 0);
            Render();

            var progress = new Progress<double>(value =>
            {
                BarImport.Value = value;
                if (_importing)
                    BtnImport.Content = Text.Format("World.Importing", (int)(value * 100));
            });
            try
            {
                string target = await Task.Run(() => WorldImporter.Import(source, world, version.SavesDirectory, progress));
                _imported = target;
                _importedFor = version;
                _savesConfirmed = version.SavesCertainty == SavesCertainty.Found;
                string folderName = Path.GetFileName(target);
                string doneKey = WantsAutoLan(version) ? "World.DoneAuto" : "World.Done";
                ShowHint(HintKind.Success, () => Text.Format(doneKey, folderName, version.Name));
                StartWatching();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException
                                       || ex is NotSupportedException || ex is ArgumentException)
            {
                Func<string> message = ex is DiskFullException full
                    ? (Func<string>)(() => Text.Format("World.NoSpace", MapText.Size(full.Needed)))
                    : () => Text.Format("World.ImportFailed", ex.Message);
                ShowHint(HintKind.Error, message);
                _ = host?.ErrorAsync(Text.Get("World.ImportFailedTitle"), message());
            }
            finally
            {
                _importing = false;
                BarImport.Visibility = Visibility.Hidden;
            }
            Render();
        }

        /// <summary>Forgets the last import, so the next click imports again; the copy stays where it is.</summary>
        private void ResetImport()
        {
            StopWatching();
            StopLan();
            _imported = null;
            _importedFor = null;
            _savesConfirmed = false;
        }

        // ======================== Checking the running game ========================

        private void StartWatching()
        {
            // Nothing to check when the saves folder is certain and the game need not be found for LAN either.
            if (_savesConfirmed && !WantsAutoLan(_importedFor))
                return;
            _watchUntil = DateTime.UtcNow + WatchDuration;
            _watch.Start();
            _ = CheckRunningGameAsync();
        }

        private void StopWatching()
        {
            _watch.Stop();
        }

        /// <summary>
        /// Once the chosen version runs, compares its real saves folder with the one the map went into and moves
        /// the map when they differ.
        /// </summary>
        private async Task CheckRunningGameAsync()
        {
            if (_watching || _imported == null || _importedFor == null)
                return;
            if (DateTime.UtcNow > _watchUntil)
            {
                StopWatching();
                return;
            }

            _watching = true;
            try
            {
                var version = _importedFor;
                var game = (await Task.Run(() => RunningGame.Find())).FirstOrDefault(g => g.Runs(version));
                if (game == null || _imported == null || version != _importedFor)
                    return;

                StopWatching();
                string current = Path.GetDirectoryName(_imported);
                if (SamePath(current, game.SavesDirectory))
                {
                    _savesConfirmed = true;
                    string name = Path.GetFileName(_imported);
                    ShowHint(HintKind.Success, () => Text.Format("World.DoneRunning", name));
                    Render();
                    await StartLanAsync(game, version);
                    return;
                }

                string from = _imported;
                try
                {
                    string moved = await Task.Run(() => WorldImporter.Move(from, game.SavesDirectory));
                    _imported = moved;
                    _savesConfirmed = true;
                    string folderName = Path.GetFileName(moved);
                    string saves = game.SavesDirectory;
                    ShowHint(HintKind.Success, () => Text.Format("World.Moved", saves) + "\n" + Text.Format("World.DoneRunning", folderName));
                    Render();
                    await StartLanAsync(game, version);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    string saves = game.SavesDirectory;
                    _ = Dialogs.Of(this)?.ErrorAsync(Text.Get("World.MoveFailedTitle"), Text.Format("World.MoveFailed", saves, ex.Message));
                }
                Render();
            }
            finally
            {
                _watching = false;
            }
        }

        // ======================== Opening to LAN ========================

        private const string AutoLanKey = "worlds.autolan";

        /// <summary>The first release with official mappings; older ones cannot be reached by the agent.</summary>
        private const int FirstMappedDataVersion = 1976;

        private static readonly TimeSpan LanPollInterval = TimeSpan.FromSeconds(2);

        private LanOpener _lan;
        private DispatcherTimer _lanPoll;

        private static bool AutoLanEnabled => AppSettings.Get(AutoLanKey) != "0";

        /// <summary>
        /// Whether the agent can open worlds of <paramref name="version"/> to LAN: a release since 1.14.4, with no
        /// loader or one that keeps Minecraft's own names, or one of the older releases Campfire carries names for
        /// when it runs without a loader. Fabric and Quilt rename the game's classes while it runs.
        /// </summary>
        private static bool SupportsAutoLan(InstalledVersion version)
        {
            if (version == null || version.Loader == VersionLoader.Fabric || version.Loader == VersionLoader.Quilt)
                return false;
            return version.DataVersion >= FirstMappedDataVersion
                   || version.Loader == VersionLoader.Vanilla && GameMappings.LegacyVersions.Contains(version.GameVersion);
        }

        private static bool WantsAutoLan(InstalledVersion version) => AutoLanEnabled && SupportsAutoLan(version);

        /// <summary>Asks the started game to open the imported map to LAN once the host enters it.</summary>
        private async Task StartLanAsync(RunningGame game, InstalledVersion version)
        {
            StopLan();
            if (!WantsAutoLan(version) || _imported == null)
                return;

            string name = Path.GetFileName(_imported);
            var lan = new LanOpener(game.ProcessId, name);
            _lan = lan;
            ShowHint(HintKind.Success, () => Text.Format("World.LanWaiting", name));

            bool loaded = await JavaAgent.LoadAsync(game.ProcessId, CancellationToken.None);
            if (_lan != lan)
                return;
            if (!loaded)
            {
                StopLan();
                ShowHint(HintKind.Warning, () => Text.Get("World.LanFailed"));
                return;
            }

            _lanPoll = new DispatcherTimer { Interval = LanPollInterval };
            _lanPoll.Tick += (sender, e) => PollLan(lan, name);
            _lanPoll.Start();
        }

        private void PollLan(LanOpener lan, string name)
        {
            if (_lan != lan)
                return;
            if (!IsRunning(lan.ProcessId))
            {
                // The game was closed before the map was opened; nothing is waiting any more.
                StopLan();
                return;
            }

            int? result = lan.ReadResult();
            if (result == null)
                return;
            StopLan();
            if (result > 0)
            {
                int port = result.Value;
                ShowHint(HintKind.Success, () => Text.Format("World.LanOpened", name, port));
            }
            else if (result == 0)
                ShowHint(HintKind.Success, () => Text.Format("World.LanOpenedByHost", name));
            else
                ShowHint(HintKind.Warning, () => Text.Get("World.LanFailed"));
        }

        private void StopLan()
        {
            _lanPoll?.Stop();
            _lanPoll = null;
            _lan?.Dispose();
            _lan = null;
        }

        private static bool IsRunning(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        private void RenderAutoLan()
        {
            var version = _importedFor ?? _selected;
            ChkAutoLan.Visibility = _world != null && version != null ? Visibility.Visible : Visibility.Collapsed;
            bool supported = SupportsAutoLan(version);
            ChkAutoLan.IsEnabled = supported && !_importing;
            ChkAutoLan.IsChecked = supported && AutoLanEnabled;
            ChkAutoLan.Content = Text.Get(supported ? "World.AutoLan" : "World.AutoLanUnsupported");
        }

        private void ChkAutoLan_Click(object sender, RoutedEventArgs e)
        {
            AppSettings.Set(AutoLanKey, ChkAutoLan.IsChecked == true ? null : "0");
            if (ChkAutoLan.IsChecked != true)
                StopLan();
            else if (_imported != null && _lan == null)
                // The game may already run; finding it again starts the request.
                StartWatching();
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

        // ======================== Other actions ========================

        private async void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = Text.Get("World.SelectTitle"),
                Filter = Text.Get("World.SelectFilter"),
                CheckFileExists = true,
            };
            if (_source != null)
            {
                string directory = _source.IsArchive ? Path.GetDirectoryName(_source.Path) : _source.Path;
                if (Directory.Exists(directory))
                    dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                await SelectAsync(dialog.FileName);
        }

        private void CmbWorld_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_source == null || CmbWorld.SelectedIndex < 0 || CmbWorld.SelectedIndex >= _source.Worlds.Count)
                return;
            var world = _source.Worlds[CmbWorld.SelectedIndex];
            if (world != _world)
                SelectWorld(world);
        }

        private void Version_Checked(object sender, RoutedEventArgs e)
        {
            if (!(sender is RadioButton option) || !(option.Tag is InstalledVersion version) || version == _selected)
                return;
            _selected = version;
            if (_imported != null)
            {
                // A different version may keep its worlds elsewhere; the next click imports for that one.
                ResetImport();
                if (_world != null)
                    ShowHint(Kind(_world), () => Describe(_world));
            }
            RenderVersions();
            UpdateButtons();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            string picked;
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
                   {
                       Description = Text.Get("World.AddFolderTitle"),
                       ShowNewFolderButton = false,
                   })
            {
                var owner = new Win32Owner(new WindowInteropHelper(Window.GetWindow(this)).Handle);
                if (dialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                    return;
                picked = dialog.SelectedPath;
            }

            string folder = GameFolders.Resolve(picked);
            if (folder == null)
            {
                _ = Dialogs.Of(this)?.AlertAsync(Text.Get("World.AddFolderInvalidTitle"), Text.Get("World.AddFolderInvalid"));
                return;
            }
            GameFolders.Add(folder);
            await RefreshVersionsAsync(null);
        }

        private void BtnOpenSaves_Click(object sender, RoutedEventArgs e)
        {
            if (_imported != null && Directory.Exists(_imported))
                Process.Start("explorer.exe", $"/select,\"{_imported}\"");
        }

        // ======================== Helpers ========================

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Render();
            ShowHint(_hintKind, _hintText);
        }

        private void ShowHint(HintKind kind, Func<string> text)
        {
            _hintKind = kind;
            _hintText = text;

            string styleKey;
            switch (kind)
            {
                case HintKind.Success:
                    styleKey = "PclHintGreen";
                    break;
                case HintKind.Warning:
                    styleKey = "PclHintOrange";
                    break;
                case HintKind.Error:
                    styleKey = "PclHintRed";
                    break;
                default:
                    styleKey = "PclHint";
                    break;
            }
            HintStatus.Style = (Style)FindResource(styleKey);
            LabStatus.Text = text();
        }

        private sealed class Win32Owner : System.Windows.Forms.IWin32Window
        {
            public Win32Owner(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }
        }
    }
}
