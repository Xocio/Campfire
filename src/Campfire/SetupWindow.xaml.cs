using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Campfire.Localization;
using Campfire.Pages;
using Campfire.Themes;

namespace Campfire
{
    /// <summary>The first page of a new Campfire, where the data location is chosen before anything is saved.</summary>
    public partial class SetupWindow : Window
    {
        private string _target;

        public SetupWindow()
        {
            ThemeManager.Initialize(Resources);
            InitializeComponent();
            ShowTarget(DataLocation.DefaultDirectory);
        }

        /// <summary>Whether the user went on; otherwise Campfire ends.</summary>
        public bool Confirmed { get; private set; }

        private void ShowTarget(string target)
        {
            _target = target;
            TxtPath.Text = target;
            LabError.Visibility = Visibility.Collapsed;
            BtnReset.Visibility = DataLocation.SamePath(target, DataLocation.DefaultDirectory) ? Visibility.Collapsed : Visibility.Visible;

            long? free = DataLocation.FreeSpace(target);
            LabInfo.Text = DataLocation.HasData(target) ? Text.Get("Setup.Existing")
                : free.HasValue ? Text.Format("Setup.FreeSpace", MapText.Size(free.Value))
                : "";
        }

        private void ShowError(string key)
        {
            LabError.Text = Text.Get(key);
            LabError.Visibility = Visibility.Visible;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            string picked = FolderPicker.Pick(new WindowInteropHelper(this).Handle, Text.Get("Storage.PickTitle"));
            if (picked != null)
                ShowTarget(DataLocation.ToDataFolder(picked));
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            ShowTarget(DataLocation.DefaultDirectory);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (!DataLocation.SamePath(_target, DataLocation.DefaultDirectory))
            {
                string problem = DataLocation.Check(_target, DataLocation.DefaultDirectory);
                if (problem != null)
                {
                    ShowError(problem);
                    return;
                }
                try
                {
                    DataLocation.Record(_target, null);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    ShowError("Storage.Error.NotWritable");
                    return;
                }
                AppSettings.UseDataDirectory(_target);
            }
            Confirmed = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}
