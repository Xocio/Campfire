using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Campfire.Link;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>The open room's name, player limit, announcement, description and tags.</summary>
    public partial class RoomSettingsPage : UserControl
    {
        /// <summary>Player limits offered; 0 is no limit.</summary>
        private static readonly int[] Limits = { 0, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 30, 50 };

        private LinkSession _session;
        private readonly DispatcherTimer _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        public RoomSettingsPage()
        {
            InitializeComponent();
            _savedTimer.Tick += (sender, e) =>
            {
                _savedTimer.Stop();
                LabSaved.Text = "";
            };
            Text.LanguageChanged += (sender, e) => FillLimits(SelectedLimit());
        }

        /// <summary>Fills the fields from the saved room settings.</summary>
        internal void Show(LinkSession session)
        {
            _session = session;
            TxtName.Text = LinkSettings.RoomName;
            FillLimits(LinkSettings.MaxPlayers);
            TxtNotice.Text = LinkSettings.RoomNotice;
            TxtDescription.Text = LinkSettings.LobbyDescription;
            FillTags(LinkSettings.LobbyTags);
            ChkRegion.IsChecked = LinkSettings.LobbyShowRegion;
            LabSaved.Text = "";
        }

        private void FillLimits(int selected)
        {
            CmbMaxPlayers.ItemsSource = Limits
                .Select(n => n == 0 ? Text.Get("RoomSettings.Unlimited") : Text.Format("RoomSettings.Limit", n))
                .ToList();
            int index = Array.IndexOf(Limits, selected);
            CmbMaxPlayers.SelectedIndex = index < 0 ? 0 : index;
        }

        private int SelectedLimit()
        {
            int index = CmbMaxPlayers.SelectedIndex;
            return index >= 0 && index < Limits.Length ? Limits[index] : 0;
        }

        private void FillTags(IEnumerable<string> selected)
        {
            var chosen = new HashSet<string>(selected.Take(LinkPage.MaxLobbyTags));
            PanTags.Children.Clear();
            foreach (string id in LinkPage.LobbyTagIds)
            {
                string name = LobbyText.Tag(id);
                if (name == null)
                    continue;
                var chip = new CheckBox { Style = (Style)FindResource("PclSegment"), Content = name, Tag = id, IsChecked = chosen.Contains(id) };
                chip.Checked += (sender, e) =>
                {
                    if (SelectedTags().Count > LinkPage.MaxLobbyTags)
                        ((CheckBox)sender).IsChecked = false;
                };
                PanTags.Children.Add(chip);
            }
        }

        private List<string> SelectedTags()
        {
            return PanTags.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            string name = TxtName.Text.Trim();
            if (name.Length == 0)
            {
                if (dialogs != null)
                    await dialogs.ErrorAsync(Text.Get("RoomSettings.Header"), Text.Get("Lobby.TitleEmpty"));
                TxtName.Focus();
                return;
            }

            LinkSettings.LobbyTitle = name;
            LinkSettings.MaxPlayers = SelectedLimit();
            LinkSettings.RoomNotice = TxtNotice.Text;
            LinkSettings.LobbyDescription = TxtDescription.Text;
            LinkSettings.LobbyTags = SelectedTags();
            LinkSettings.LobbyShowRegion = ChkRegion.IsChecked == true;

            BtnSave.IsEnabled = false;
            try
            {
                if (_session != null)
                    await _session.ApplyRoomSettingsAsync();
            }
            finally
            {
                BtnSave.IsEnabled = true;
            }
            LabSaved.Text = Text.Get("RoomSettings.Saved");
            _savedTimer.Stop();
            _savedTimer.Start();
        }
    }
}
