using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Campfire.Link.Lobby;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>How the lobby writes tags, places and times.</summary>
    internal static class LobbyText
    {
        /// <summary>Region names the service gives for China, by the start of the English name.</summary>
        private static readonly (string English, string Chinese)[] Provinces =
        {
            ("Beijing", "北京"), ("Tianjin", "天津"), ("Hebei", "河北"), ("Shanxi", "山西"), ("Inner Mongolia", "内蒙古"),
            ("Liaoning", "辽宁"), ("Jilin", "吉林"), ("Heilongjiang", "黑龙江"), ("Shanghai", "上海"), ("Jiangsu", "江苏"),
            ("Zhejiang", "浙江"), ("Anhui", "安徽"), ("Fujian", "福建"), ("Jiangxi", "江西"), ("Shandong", "山东"),
            ("Henan", "河南"), ("Hubei", "湖北"), ("Hunan", "湖南"), ("Guangdong", "广东"), ("Guangxi", "广西"),
            ("Hainan", "海南"), ("Chongqing", "重庆"), ("Sichuan", "四川"), ("Guizhou", "贵州"), ("Yunnan", "云南"),
            ("Tibet", "西藏"), ("Shaanxi", "陕西"), ("Gansu", "甘肃"), ("Qinghai", "青海"), ("Ningxia", "宁夏"),
            ("Xinjiang", "新疆"),
        };

        /// <summary>The tag's name, or <c>null</c> for a tag this version does not know.</summary>
        public static string Tag(string id)
        {
            string key = "Lobby.Tag." + id;
            return Text.Has(key) ? Text.Get(key) : null;
        }

        /// <summary>Where the host is, roughly; <c>null</c> when it is private or unknown.</summary>
        public static string Place(LobbyRoom room)
        {
            string country = room.Country ?? "";
            if (country.Length == 0)
                return null;
            string placeKey = "Lobby.Place." + country;

            if (Text.Language == AppLanguage.Chinese)
            {
                if (country == "CN")
                {
                    var province = Provinces.FirstOrDefault(p => room.Region.StartsWith(p.English, StringComparison.OrdinalIgnoreCase));
                    return province.Chinese ?? Text.Get(placeKey);
                }
                return Text.Has(placeKey) ? Text.Get(placeKey) : null;
            }

            if (country == "CN" && room.Region.Length > 0)
                return room.Region;
            // Only these have their own English names; the other names in the table are Chinese.
            if (country == "CN" || country == "HK" || country == "MO" || country == "TW")
                return Text.Get(placeKey);
            try
            {
                return new RegionInfo(country).EnglishName;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static string Ago(DateTime utc)
        {
            var age = DateTime.UtcNow - utc;
            if (age.TotalMinutes < 1)
                return Text.Get("Lobby.JustNow");
            if (age.TotalHours < 1)
                return Text.Format("Lobby.MinutesAgo", (int)age.TotalMinutes);
            return Text.Format("Lobby.HoursAgo", (int)age.TotalHours);
        }

        public static string Players(LobbyRoom room)
        {
            return room.MaxPlayers > 0
                ? room.Players.ToString(CultureInfo.InvariantCulture) + "/" + room.MaxPlayers.ToString(CultureInfo.InvariantCulture)
                : room.Players.ToString(CultureInfo.InvariantCulture);
        }

        public static string HostName(LobbyRoom room)
        {
            return room.Host.Length > 0 ? room.Host : Text.Get("Lobby.UnknownHost");
        }

        public static string VersionName(LobbyRoom room)
        {
            return room.Version.Length > 0 ? room.Version : Text.Get("Lobby.UnknownVersion");
        }

        /// <summary>"Label value" with the label lighter, such as "Host Steve".</summary>
        public static TextBlock Field(string labelKey, string value, double fontSize = 12)
        {
            var text = new TextBlock { FontSize = fontSize, TextTrimming = TextTrimming.CharacterEllipsis };
            var label = new Run(Text.Get(labelKey) + " ");
            label.SetResourceReference(TextElement.ForegroundProperty, "BrushGray5");
            var content = new Run(value);
            content.SetResourceReference(TextElement.ForegroundProperty, "BrushGray3");
            text.Inlines.Add(label);
            text.Inlines.Add(content);
            return text;
        }

        /// <summary>The version first and stronger, then the tags.</summary>
        public static WrapPanel Pills(LobbyRoom room)
        {
            var panel = new WrapPanel();
            AddPills(panel, room);
            return panel;
        }

        /// <param name="withVersion">Whether the version leads the pills; the details page shows it on its own.</param>
        public static void AddPills(Panel panel, LobbyRoom room, bool withVersion = true)
        {
            if (withVersion)
                panel.Children.Add(Pill(VersionName(room), true));
            foreach (string tag in room.Tags.Select(Tag).Where(t => t != null))
                panel.Children.Add(Pill(tag, false));
        }

        private static Border Pill(string text, bool strong)
        {
            var label = new TextBlock { FontSize = 11, Text = text };
            label.SetResourceReference(TextBlock.ForegroundProperty, strong ? "Brush3" : "BrushGray3");
            var pill = new Border
            {
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(6, 1, 6, 2),
                CornerRadius = new CornerRadius(3),
                Child = label,
            };
            pill.SetResourceReference(Border.BackgroundProperty, strong ? "Brush7" : "Brush8");
            return pill;
        }

        /// <summary>The room's picture, or the default block picture when it has none.</summary>
        public static Border Picture(LobbyRoom room, double size)
        {
            var picture = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size >= 56 ? 6 : 4) };
            var image = RoomIcon.Decode(room.Icon);
            if (image != null)
            {
                var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                picture.Background = brush;
                return picture;
            }

            picture.Background = RoomIcon.DefaultBrush();
            return picture;
        }

        /// <summary>A small padlock, shown before the title of a room that asks for a password.</summary>
        public static FrameworkElement Lock(double size)
        {
            var glyph = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M3.5,6.5 L10.5,6.5 L10.5,12 L3.5,12 Z M5,6.5 L5,4.8 A2,2 0 0 1 9,4.8 L9,6.5"),
                Width = 14,
                Height = 14,
                Stretch = Stretch.None,
                StrokeThickness = 1.4,
                StrokeLineJoin = PenLineJoin.Round,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Text.Get("Lobby.Locked"),
            };
            glyph.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush3");
            return new Viewbox { Width = size, Height = size, Margin = new Thickness(0, 0, 5, 0), Child = glyph, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>The message shown when a room could not be listed or is no longer listed.</summary>
        public static string Problem(LobbyException error)
        {
            switch (error.Kind)
            {
                case LobbyError.Blocked:
                    return Text.Get("Lobby.TitleBlocked");
                case LobbyError.Removed:
                case LobbyError.Reported:
                case LobbyError.Banned:
                case LobbyError.Closed:
                case LobbyError.Full:
                case LobbyError.UpdateRequired:
                case LobbyError.AlreadyListed:
                case LobbyError.WrongPassword:
                case LobbyError.TooMany:
                case LobbyError.NoService:
                    return Text.Get("Lobby.Problem." + error.Kind);
                case LobbyError.Network:
                    return Text.Get("Lobby.NetworkError");
                default:
                    return Text.Get("Lobby.Problem.Other");
            }
        }
    }
}
