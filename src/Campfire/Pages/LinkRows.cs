using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Campfire.Link;
using Campfire.Link.Room;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// Display model of one step in the progress card. Rows are updated in place so their spinners keep turning.
    /// </summary>
    internal sealed class StepRow : INotifyPropertyChanged
    {
        private string _title;
        private string _detail;
        private StepState _state;

        public StepRow(LinkStep step)
        {
            Step = step;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public LinkStep Step { get; }

        public string Title
        {
            get => _title;
            set => Set(ref _title, value);
        }

        public string Detail
        {
            get => _detail;
            set => Set(ref _detail, value);
        }

        public StepState State
        {
            get => _state;
            set => Set(ref _state, value);
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// Display model of a player in the room.
    /// </summary>
    internal sealed class PlayerRow
    {
        public PlayerRow(PlayerProfile profile, ImageSource head, bool manageable = false)
        {
            Profile = profile;
            ManageVisibility = manageable ? Visibility.Visible : Visibility.Collapsed;
            Name = profile.Name.Length > 0 ? profile.Name : "?";
            Initial = char.IsSurrogate(Name, 0) ? Name.Substring(0, 2) : Name.Substring(0, 1).ToUpperInvariant();
            Head = head;

            // "Campfire 1.1.0, EasyTier v2.6.4" is shown as "Campfire 1.1.0".
            int comma = profile.Vendor.IndexOf(',');
            Vendor = comma > 0 ? profile.Vendor.Substring(0, comma) : profile.Vendor;

            switch (profile.Kind)
            {
                case PlayerKind.Host:
                    Tag = Text.Get("Link.Tag.Host");
                    TagBackground = Resource("Brush7");
                    TagForeground = Resource("Brush3");
                    break;
                case PlayerKind.Local:
                    Tag = Text.Get("Link.Tag.You");
                    TagBackground = Resource("BrushGreenLight");
                    TagForeground = Resource("BrushGreen");
                    break;
                default:
                    Tag = null;
                    break;
            }
        }

        public PlayerProfile Profile { get; }

        /// <summary>Visible on the host's list for guests, who can be removed or blocked.</summary>
        public Visibility ManageVisibility { get; }

        public string Name { get; }

        public string Initial { get; }

        /// <summary>Skin face of a premium player, or <c>null</c> to show the initial.</summary>
        public ImageSource Head { get; }

        public string Tag { get; }

        public Brush TagBackground { get; }

        public Brush TagForeground { get; }

        public Visibility TagVisibility => Tag == null ? Visibility.Collapsed : Visibility.Visible;

        public string Vendor { get; }

        private static Brush Resource(string key)
        {
            // Theme colors live on the main window.
            return (Brush)(Application.Current.MainWindow?.TryFindResource(key) ?? Application.Current.FindResource(key));
        }
    }
}
