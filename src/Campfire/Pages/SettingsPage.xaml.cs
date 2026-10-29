using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Campfire.Link;
using Campfire.Link.Runtime;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// All preferences in one place. Every change is saved at once and applies to the next room.
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        private readonly List<ServerRow> _servers = new List<ServerRow>();
        private bool _loading;
        private bool _componentMissing;
        private string _testError;

        public SettingsPage()
        {
            InitializeComponent();

            foreach (var node in PublicNodes.All)
                PanServers.Children.Add(CreateServerRow(node));
            PublicNodes.Changed += (sender, e) => Dispatcher.BeginInvoke(new Action(UpdateServers));

            foreach (int seconds in LinkSettings.SearchTimeoutChoices)
            {
                var option = new RadioButton
                {
                    GroupName = "Timeout",
                    Tag = seconds,
                    Margin = new Thickness(PanTimeout.Children.Count == 0 ? 0 : 24, 0, 0, 0),
                    Style = (Style)FindResource("PclRadioButton"),
                };
                option.Checked += Timeout_Checked;
                PanTimeout.Children.Add(option);
            }

            Text.LanguageChanged += (sender, e) => Load();
            Blocklist.Changed += (sender, e) => Dispatcher.BeginInvoke(new Action(LoadBlocklist));
            IsVisibleChanged += (sender, e) =>
            {
                if (IsVisible)
                {
                    Load();
                    TestServersIfStale();
                }
            };
            Load();
        }

        /// <summary>Raised after a setting changed.</summary>
        public event EventHandler SettingsChanged;

        /// <summary>Tells whether a room is running, which some actions must not interrupt.</summary>
        internal Func<bool> IsRoomActive { get; set; }

        private void Load()
        {
            _loading = true;
            try
            {
                TxtPlayerName.Text = LinkSettings.PlayerName;
                TxtPlayerName.Tag = Text.Format("Settings.PlayerNamePlaceholder", LinkSettings.FallbackPlayerName);
                TxtLanName.Text = LinkSettings.LanEntryName;
                TxtLanName.Tag = Text.Get("Link.LanEntryPlain");
                TxtLocalPort.Text = LinkSettings.LocalPort > 0 ? LinkSettings.LocalPort.ToString(CultureInfo.InvariantCulture) : "";
                TxtNodes.Text = LinkSettings.CustomNodes;

                foreach (RadioButton option in PanTimeout.Children)
                {
                    option.Content = Text.Format("Settings.Seconds", option.Tag);
                    option.IsChecked = (int)option.Tag == LinkSettings.SearchTimeoutSeconds;
                }

                ChkAutoCopy.IsChecked = LinkSettings.AutoCopyCode;
                ChkClipboard.IsChecked = LinkSettings.DetectClipboard;
                ChkAutoLan.IsChecked = LinkSettings.AutoOpenLan;
                ChkShareMods.IsChecked = LinkSettings.ShareMods;
                ChkServeMods.IsChecked = LinkSettings.ServeMods;
                ChkServeMods.IsEnabled = LinkSettings.ShareMods;
                ChkRelay.IsChecked = LinkSettings.AllowRelay || LinkSettings.HideAddress;
                ChkRelay.IsEnabled = !LinkSettings.HideAddress;
                ChkLanShare.IsChecked = LinkSettings.ShareWithLan;
                foreach (var row in _servers)
                {
                    row.Check.Content = Text.Get(row.Node.RegionKey);
                    row.Check.IsChecked = LinkSettings.IsNodeEnabled(row.Node);
                    string provider = Text.Format("Settings.ServerProvider", row.Node.Provider);
                    row.Provider.Text = row.Node.IsLimited ? provider + " · " + Text.Get("Settings.ServerLimited") : provider;
                }
                UpdateServers();
                ChkHideAddress.IsChecked = LinkSettings.HideAddress;
                ChkGameAccount.IsChecked = LinkSettings.ReadGameAccount;
                ChkSkins.IsChecked = LinkSettings.ShowSkins;
                ChkVersion.IsChecked = LinkSettings.ShareVersion;
                LoadBlocklist();
            }
            finally
            {
                _loading = false;
            }
        }

        private void LoadBlocklist()
        {
            var blocked = Blocklist.All.OrderByDescending(p => p.Time).ToList();
            ListBlocked.ItemsSource = blocked.Select(p => new
            {
                p.MachineId,
                Title = p.GameName == null ? p.Name : p.Name + " · " + p.GameName,
                Detail = Text.Format("Settings.BlockedAt", p.Time.ToString("yyyy-MM-dd HH:mm")),
            }).ToList();
            LabNoBlocked.Visibility = blocked.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnUnblock_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is string machineId)
                Blocklist.Remove(machineId);
        }

        private void Changed()
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        // ======================== Room ========================

        private void TxtPlayerName_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (LinkSettings.PlayerName == TxtPlayerName.Text.Trim())
                return;
            LinkSettings.PlayerName = TxtPlayerName.Text;
            Changed();
        }

        private void TxtLanName_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (LinkSettings.LanEntryName == TxtLanName.Text.Trim())
                return;
            LinkSettings.LanEntryName = TxtLanName.Text;
            Changed();
        }

        private async void TxtLocalPort_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            string text = TxtLocalPort.Text.Trim();
            if (text.Length == 0)
            {
                LinkSettings.LocalPort = 0;
                Changed();
                return;
            }

            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port < 1024 || port > 65535)
            {
                TxtLocalPort.Text = LinkSettings.LocalPort > 0 ? LinkSettings.LocalPort.ToString(CultureInfo.InvariantCulture) : "";
                await Dialogs.Of(this).ErrorAsync(Text.Get("Settings.InvalidLocalPortTitle"), Text.Format("Settings.InvalidLocalPort", text));
                return;
            }

            LinkSettings.LocalPort = port;
            Changed();
        }

        private void Timeout_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading || !(sender is RadioButton option))
                return;
            LinkSettings.SearchTimeoutSeconds = (int)option.Tag;
            Changed();
        }

        /// <summary>Saves the check boxes that need no confirmation.</summary>
        private void Flag_Click(object sender, RoutedEventArgs e)
        {
            LinkSettings.AutoCopyCode = ChkAutoCopy.IsChecked == true;
            LinkSettings.DetectClipboard = ChkClipboard.IsChecked == true;
            LinkSettings.AutoOpenLan = ChkAutoLan.IsChecked == true;
            LinkSettings.ShareMods = ChkShareMods.IsChecked == true;
            LinkSettings.ServeMods = ChkServeMods.IsChecked == true;
            ChkServeMods.IsEnabled = LinkSettings.ShareMods;
            if (ChkRelay.IsEnabled)
                LinkSettings.AllowRelay = ChkRelay.IsChecked == true;
            LinkSettings.ShareWithLan = ChkLanShare.IsChecked == true;
            LinkSettings.ReadGameAccount = ChkGameAccount.IsChecked == true;
            LinkSettings.ShowSkins = ChkSkins.IsChecked == true;
            LinkSettings.ShareVersion = ChkVersion.IsChecked == true;
            Changed();
        }

        // ======================== Network ========================

        // ======================== Relay servers ========================

        private sealed class ServerRow
        {
            public PublicNode Node;
            public CheckBox Check;
            public TextBlock Provider;
            public TextBlock Latency;
        }

        private FrameworkElement CreateServerRow(PublicNode node)
        {
            var row = new ServerRow { Node = node };
            var grid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            row.Check = new CheckBox
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = (Style)FindResource("PclCheckBox"),
            };
            row.Check.Click += Server_Click;
            grid.Children.Add(row.Check);

            // Lines up with the check box text: 17 for the box and 9 for the gap.
            row.Provider = new TextBlock { Margin = new Thickness(26, 2, 0, 0), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            row.Provider.SetResourceReference(TextBlock.ForegroundProperty, "BrushGray4");
            Grid.SetRow(row.Provider, 1);
            grid.Children.Add(row.Provider);

            row.Latency = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(row.Latency, 1);
            grid.Children.Add(row.Latency);

            _servers.Add(row);
            return grid;
        }

        /// <summary>Scrolls to the relay servers, for the status lamp on the title bar.</summary>
        public void ShowServers()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                double top = CardServers.TranslatePoint(new Point(0, 0), (UIElement)ScrollSettings.Content).Y;
                ScrollSettings.ScrollToVerticalOffset(Math.Max(0, top - 8));
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateServers()
        {
            bool testing = PublicNodes.IsTesting;
            foreach (var row in _servers)
            {
                string brush;
                if (PublicNodes.TryGetLatency(row.Node, out var latency))
                {
                    row.Latency.Text = latency == null
                        ? Text.Get("Settings.ServerUnreachable")
                        : Text.Format("Settings.ServerLatency", Math.Round(latency.Value).ToString(CultureInfo.InvariantCulture));
                    brush = latency == null ? "BrushRed" : latency <= PublicNodes.HighLatencyMs ? "BrushGreen" : "BrushOrange";
                }
                else
                {
                    row.Latency.Text = Text.Get(testing ? "Settings.ServerTesting" : "Settings.ServerUntested");
                    brush = "BrushGray5";
                }
                row.Latency.SetResourceReference(TextBlock.ForegroundProperty, brush);
            }

            BtnTestServers.IsEnabled = !testing && EasyTierPackage.IsSupportedSystem;
            if (testing)
                LabServers.Text = Text.Get("Settings.ServersTesting");
            else if (_testError != null)
                LabServers.Text = Text.Format("Settings.ServersTestFailed", _testError);
            else if (PublicNodes.TestedAt.HasValue)
                LabServers.Text = Text.Format("Settings.ServersTestedAt", PublicNodes.TestedAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture));
            else
                LabServers.Text = _componentMissing ? Text.Get("Settings.ServersFirstTest") : "";
        }

        /// <summary>Measures the servers when the page opens, unless the last results are recent.</summary>
        private async void TestServersIfStale()
        {
            if (PublicNodes.IsTesting || !EasyTierPackage.IsSupportedSystem
                || PublicNodes.TestedAt.HasValue && DateTime.Now - PublicNodes.TestedAt.Value < PublicNodes.Freshness)
                return;

            // The first test downloads the component, so that only happens when asked for.
            _componentMissing = !await Task.Run(() => EasyTierPackage.IsInstalled());
            if (_componentMissing)
                UpdateServers();
            else
                await TestServersAsync();
        }

        private async void BtnTestServers_Click(object sender, RoutedEventArgs e)
        {
            await TestServersAsync();
        }

        private async Task TestServersAsync()
        {
            _testError = null;
            try
            {
                await PublicNodes.TestAsync();
                _componentMissing = false;
            }
            catch (Exception ex)
            {
                _testError = ex.Message;
            }
            UpdateServers();
        }

        private async void Server_Click(object sender, RoutedEventArgs e)
        {
            var enabled = _servers.Where(r => r.Check.IsChecked == true).Select(r => r.Node).ToList();
            if (enabled.Count == 0 && LinkSettings.ParseNodes(TxtNodes.Text, out _).Count == 0)
            {
                ((CheckBox)sender).IsChecked = true;
                await Dialogs.Of(this).ErrorAsync(Text.Get("Settings.NoNodesTitle"), Text.Get("Settings.NoNodes"));
                return;
            }
            LinkSettings.SetEnabledNodes(enabled);
            PublicNodes.Evaluate();
            Changed();
        }

        private async void TxtNodes_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (LinkSettings.CustomNodes == TxtNodes.Text.Trim())
                return;

            var nodes = LinkSettings.ParseNodes(TxtNodes.Text, out var invalid);
            if (nodes.Count == 0 && !LinkSettings.UsePublicNodes)
            {
                TxtNodes.Text = LinkSettings.CustomNodes;
                await Dialogs.Of(this).ErrorAsync(Text.Get("Settings.NoNodesTitle"), Text.Get("Settings.NoNodes"));
                return;
            }

            LinkSettings.CustomNodes = TxtNodes.Text;
            Changed();
            if (invalid.Count > 0)
                await Dialogs.Of(this).ErrorAsync(Text.Get("Settings.InvalidNodesTitle"),
                    Text.Format("Settings.InvalidNodes", string.Join("\n", invalid)));
        }

        // ======================== Privacy ========================

        private async void ChkHideAddress_Click(object sender, RoutedEventArgs e)
        {
            bool hide = ChkHideAddress.IsChecked == true;
            if (hide)
            {
                bool confirmed = await Dialogs.Of(this).ConfirmAsync(Text.Get("Settings.HideAddressConfirmTitle"),
                    Text.Get("Settings.HideAddressConfirm"), Text.Get("Settings.Enable"));
                if (!confirmed)
                {
                    ChkHideAddress.IsChecked = false;
                    return;
                }
            }

            LinkSettings.HideAddress = hide;
            Load();
            Changed();
        }

        private async void BtnResetIdentity_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            if (IsRoomActive?.Invoke() == true)
            {
                await dialogs.ErrorAsync(Text.Get("Settings.RoomActiveTitle"), Text.Get("Settings.RoomActive"));
                return;
            }
            if (!await dialogs.ConfirmAsync(Text.Get("Settings.ResetIdentity"), Text.Get("Settings.ResetIdentityConfirm"),
                    Text.Get("Settings.Reset")))
                return;

            LinkSettings.ResetMachineId();
            Load();
            Changed();
            await dialogs.AlertAsync(Text.Get("Settings.ResetIdentity"), Text.Get("Settings.ResetIdentityDone"));
        }

        private async void BtnClearHeads_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            try
            {
                string folder = Path.Combine(LinkSettings.DataDirectory, "heads");
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
                await dialogs.AlertAsync(Text.Get("Settings.ClearHeads"), Text.Get("Settings.ClearHeadsDone"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await dialogs.ErrorAsync(Text.Get("Settings.ClearHeads"), Text.Format("Settings.ActionFailed", ex.Message));
            }
        }

        // ======================== General ========================

        private async void BtnOpenData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                Process.Start("explorer.exe", "\"" + AppSettings.DataDirectory + "\"");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception)
            {
                await Dialogs.Of(this).ErrorAsync(Text.Get("Settings.OpenData"), Text.Format("Settings.ActionFailed", ex.Message));
            }
        }

        private async void BtnRemoveComponent_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            if (IsRoomActive?.Invoke() == true)
            {
                await dialogs.ErrorAsync(Text.Get("Settings.RoomActiveTitle"), Text.Get("Settings.RoomActive"));
                return;
            }
            if (!await dialogs.ConfirmAsync(Text.Get("Settings.RemoveComponent"), Text.Get("Settings.RemoveComponentConfirm"),
                    Text.Get("Settings.Remove")))
                return;

            try
            {
                EasyTierPackage.Uninstall();
                await dialogs.AlertAsync(Text.Get("Settings.RemoveComponent"), Text.Get("Settings.RemoveComponentDone"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await dialogs.ErrorAsync(Text.Get("Settings.RemoveComponent"), Text.Format("Settings.ActionFailed", ex.Message));
            }
        }
    }
}
