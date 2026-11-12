using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Campfire.Controls;
using Campfire.Link;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>The room's name, player limit, announcement, description and tags, and the presets that fill them in one step.</summary>
    public partial class RoomSettingsPage : UserControl
    {
        /// <summary>Player limits offered; 0 is no limit.</summary>
        private static readonly int[] Limits = { 0, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 30, 50 };

        private LinkSession _session;
        private readonly DispatcherTimer _savedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };

        public RoomSettingsPage()
        {
            InitializeComponent();
            Motion.SetCardScroll(ScrollRoom, true);
            _savedTimer.Tick += (sender, e) =>
            {
                _savedTimer.Stop();
                LabSaved.Text = "";
            };
            Text.LanguageChanged += (sender, e) =>
            {
                FillLimits(SelectedLimit());
                FillPresets(SelectedPreset()?.Name);
                RenderNote();
            };
            RoomPresets.Changed += (sender, e) => Dispatcher.BeginInvoke(new Action(() => FillPresets(SelectedPreset()?.Name)));
        }

        /// <summary>Whether the page edits an open room rather than the settings for the next one.</summary>
        private bool IsHosting => _session != null && _session.Role == LinkRole.Host && _session.Stage == LinkStage.Ready;

        /// <summary>Fills the fields from the saved room settings.</summary>
        /// <param name="session">The session whose room takes the changes at once, or <c>null</c> for the next room.</param>
        internal void Show(LinkSession session)
        {
            _session = session;
            FillFields(LinkSettings.RoomName, LinkSettings.MaxPlayers, LinkSettings.RoomNotice, LinkSettings.LobbyDescription,
                LinkSettings.LobbyTags, LinkSettings.LobbyShowRegion);
            FillPresets(SelectedPreset()?.Name);
            RenderNote();
            LabSaved.Text = "";
            ScrollRoom.ScrollToTop();
        }

        private void FillFields(string name, int limit, string notice, string description, IEnumerable<string> tags, bool region)
        {
            TxtName.Text = name;
            FillLimits(limit);
            TxtNotice.Text = notice;
            TxtDescription.Text = description;
            FillTags(tags);
            ChkRegion.IsChecked = region;
        }

        private void RenderNote()
        {
            LabNote.Text = Text.Get(IsHosting ? "RoomSettings.Note" : "RoomSettings.NoteIdle");
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
            await SaveAsync();
        }

        /// <summary>Saves the fields; returns <c>false</c> when the room name is empty.</summary>
        private async Task<bool> SaveAsync()
        {
            var dialogs = Dialogs.Of(this);
            string name = TxtName.Text.Trim();
            if (name.Length == 0)
            {
                if (dialogs != null)
                    await dialogs.ErrorAsync(Text.Get("RoomSettings.Header"), Text.Get("Lobby.TitleEmpty"));
                TxtName.Focus();
                return false;
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
                if (IsHosting)
                    await _session.ApplyRoomSettingsAsync();
            }
            finally
            {
                BtnSave.IsEnabled = true;
            }
            ShowSaved(Text.Get("RoomSettings.Saved"));
            return true;
        }

        private void ShowSaved(string text)
        {
            LabSaved.Text = text;
            _savedTimer.Stop();
            _savedTimer.Start();
        }

        // ======================== Presets ========================

        private void FillPresets(string selected)
        {
            var presets = RoomPresets.All;
            CmbPreset.ItemsSource = presets.Select(p => p.Name).ToList();
            int index = selected == null ? -1 : presets.ToList().FindIndex(p => string.Equals(p.Name, selected, StringComparison.OrdinalIgnoreCase));
            CmbPreset.SelectedIndex = index >= 0 ? index : presets.Count > 0 ? 0 : -1;
            RenderPreset();
        }

        private RoomPreset SelectedPreset()
        {
            var presets = RoomPresets.All;
            int index = CmbPreset.SelectedIndex;
            return index >= 0 && index < presets.Count ? presets[index] : null;
        }

        private void CmbPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderPreset();
        }

        /// <summary>Describes the selected preset below the list.</summary>
        private void RenderPreset()
        {
            var preset = SelectedPreset();
            CmbPreset.IsEnabled = CmbPreset.Items.Count > 0;
            BtnApplyPreset.IsEnabled = preset != null;
            BtnDeletePreset.Visibility = preset == null ? Visibility.Collapsed : Visibility.Visible;
            if (preset == null)
            {
                LabPreset.Text = Text.Get("Presets.Empty");
                return;
            }

            var parts = new List<string>
            {
                preset.Title.Length > 0 ? preset.Title : Text.Get("Presets.DefaultTitle"),
                preset.MaxPlayers > 0 ? Text.Format("RoomSettings.Limit", preset.MaxPlayers) : Text.Get("RoomSettings.Unlimited"),
                Text.Get(preset.Voice ? "Presets.VoiceOn" : "Presets.VoiceOff"),
                Text.Get(preset.ShareMods ? "Presets.ModsOn" : "Presets.ModsOff"),
            };
            if (preset.AllowlistOnly)
                parts.Add(Text.Get("Presets.AllowlistOn"));
            if (preset.Notice.Length > 0)
                parts.Add(Text.Get("RoomSettings.HasNotice"));
            LabPreset.Text = string.Join(" · ", parts);
        }

        private async void BtnApplyPreset_Click(object sender, RoutedEventArgs e)
        {
            var preset = SelectedPreset();
            if (preset == null)
                return;
            FillFields(preset.Title.Length > 0 ? preset.Title : Text.Format("Lobby.DefaultTitle", LinkSettings.EffectivePlayerName),
                preset.MaxPlayers, preset.Notice, preset.Description, preset.Tags, preset.ShowRegion);

            // Voice chat, mod sync and the allowlist take effect in the open room at once.
            if (IsHosting)
            {
                if (preset.Voice != LinkSettings.Voice)
                    _session.SetVoice(preset.Voice);
                if (preset.ShareMods != _session.ModSyncAllowed)
                    _session.SetModSync(preset.ShareMods);
                if (preset.AllowlistOnly != _session.AllowlistOnly)
                    _session.SetAllowlistOnly(preset.AllowlistOnly);
            }
            else
            {
                LinkSettings.Voice = preset.Voice;
                LinkSettings.ShareMods = preset.ShareMods;
                LinkSettings.AllowlistOnly = preset.AllowlistOnly;
            }

            if (await SaveAsync())
                ShowSaved(Text.Format("Presets.Applied", preset.Name));
        }

        private async void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dialogs = Dialogs.Of(this);
            if (dialogs == null)
                return;
            string title = TxtName.Text.Trim();
            if (title.Length == 0)
            {
                await dialogs.ErrorAsync(Text.Get("RoomSettings.Header"), Text.Get("Lobby.TitleEmpty"));
                TxtName.Focus();
                return;
            }

            var request = new DialogRequest
            {
                Title = Text.Get("Presets.SaveTitle"),
                Message = Text.Get("Presets.SaveMessage"),
                InputHint = Text.Get("Presets.NameHint"),
                InputMaxLength = RoomPreset.MaxName,
                PrimaryText = Text.Get("Presets.SaveConfirm"),
                SecondaryText = Text.Get("Dialog.Cancel"),
            };
            if (await dialogs.ShowDialogAsync(request) != DialogChoice.Primary)
                return;
            string name = (request.InputText ?? "").Trim();
            if (name.Length == 0)
                return;

            bool voice = LinkSettings.Voice;
            bool mods = IsHosting ? _session.ModSyncAllowed : LinkSettings.ShareMods;
            bool allowlist = IsHosting ? _session.AllowlistOnly : LinkSettings.AllowlistOnly;
            RoomPresets.Save(new RoomPreset(name, title, SelectedLimit(), TxtNotice.Text.Trim(), TxtDescription.Text.Trim(), SelectedTags(),
                ChkRegion.IsChecked == true, voice, mods, allowlist));
            FillPresets(name);
            ShowSaved(Text.Format("Presets.Saved", name));
        }

        private async void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            var preset = SelectedPreset();
            var dialogs = Dialogs.Of(this);
            if (preset == null || dialogs == null)
                return;
            if (!await dialogs.ConfirmAsync(Text.Get("Presets.DeleteTitle"), Text.Format("Presets.DeleteMessage", preset.Name), Text.Get("Presets.Delete")))
                return;
            RoomPresets.Remove(preset.Name);
            FillPresets(null);
        }
    }
}
