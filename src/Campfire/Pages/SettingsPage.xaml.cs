using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private bool _loading;

        public SettingsPage()
        {
            InitializeComponent();

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
                    Load();
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
                ChkRelay.IsChecked = LinkSettings.AllowRelay || LinkSettings.HideAddress;
                ChkRelay.IsEnabled = !LinkSettings.HideAddress;
                ChkLanShare.IsChecked = LinkSettings.ShareWithLan;
                ChkPublicNodes.IsChecked = LinkSettings.UsePublicNodes;
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
            if (ChkRelay.IsEnabled)
                LinkSettings.AllowRelay = ChkRelay.IsChecked == true;
            LinkSettings.ShareWithLan = ChkLanShare.IsChecked == true;
            LinkSettings.ReadGameAccount = ChkGameAccount.IsChecked == true;
            LinkSettings.ShowSkins = ChkSkins.IsChecked == true;
            LinkSettings.ShareVersion = ChkVersion.IsChecked == true;
            Changed();
        }

        // ======================== Network ========================

        private async void ChkPublicNodes_Click(object sender, RoutedEventArgs e)
        {
            bool use = ChkPublicNodes.IsChecked == true;
            if (!use && LinkSettings.ParseNodes(TxtNodes.Text, out _).Count == 0)
            {
                ChkPublicNodes.IsChecked = true;
                await Dialogs.Of(this).ErrorAsync(Text.Get("Settings.NoNodesTitle"), Text.Get("Settings.NoNodes"));
                return;
            }
            LinkSettings.UsePublicNodes = use;
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
