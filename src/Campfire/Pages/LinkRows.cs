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
    /// Display model of one step in the progress card. Rows are updated in place.
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
    /// Display model of a player in the room. The connection line is updated in place.
    /// </summary>
    internal sealed class PlayerRow : INotifyPropertyChanged
    {
        /// <summary>Round-trip times up to these are shown as good and as fair; slower ones as poor.</summary>
        private const double GoodLatencyMs = 80;
        private const double FairLatencyMs = 200;

        /// <summary>Packet loss from this percentage on is shown.</summary>
        private const double ShownLossPercent = 1;

        private string _connection;
        private Brush _connectionForeground;
        private Visibility _allowVisibility;

        public PlayerRow(PlayerProfile profile, ImageSource head, bool manageable = false, bool allowable = false)
        {
            Profile = profile;
            ManageVisibility = manageable ? Visibility.Visible : Visibility.Collapsed;
            _allowVisibility = manageable && allowable ? Visibility.Visible : Visibility.Collapsed;
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
            Update(profile, allowable);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public PlayerProfile Profile { get; private set; }

        /// <summary>Latency, path and loss to the host; empty for the host and for players that do not report them.</summary>
        public string Connection
        {
            get => _connection;
            private set => Set(ref _connection, value);
        }

        public Brush ConnectionForeground
        {
            get => _connectionForeground;
            private set => Set(ref _connectionForeground, value);
        }

        /// <summary>Visible on the host's list for guests that are not on the allowlist yet.</summary>
        public Visibility AllowVisibility
        {
            get => _allowVisibility;
            private set => Set(ref _allowVisibility, value);
        }

        /// <summary>Takes the newest measurement of the same player.</summary>
        public void Update(PlayerProfile profile, bool allowable)
        {
            Profile = profile;
            if (ManageVisibility == Visibility.Visible)
                AllowVisibility = allowable ? Visibility.Visible : Visibility.Collapsed;
            if (profile.Kind == PlayerKind.Host || profile.LatencyMs == null)
            {
                Connection = profile.Kind != PlayerKind.Host && profile.Path != null ? PathName(profile.Path) : "";
                ConnectionForeground = Resource("BrushGray5");
                return;
            }

            double latency = profile.LatencyMs.Value;
            string text = Text.Format("Link.PlayerLatency", LinkSession.FormatLatency(latency));
            if (profile.Path != null)
                text += " · " + PathName(profile.Path);
            if (profile.LossPercent >= ShownLossPercent)
                text += " · " + Text.Format("Link.PlayerLoss", System.Math.Round(profile.LossPercent.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Connection = text;
            ConnectionForeground = Resource(latency <= GoodLatencyMs && !(profile.LossPercent >= 5) ? "BrushGreen"
                : latency <= FairLatencyMs && !(profile.LossPercent >= 15) ? "BrushOrange" : "BrushRed");
        }

        private static string PathName(string path)
        {
            return Text.Get(path == PlayerProfile.PathDirect ? "Link.Path.Direct" : "Link.Path.Relay");
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

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
