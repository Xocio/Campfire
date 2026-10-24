using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// Version, author and repository, shown in the settings.
    /// </summary>
    public partial class AboutPage : UserControl
    {
        public AboutPage()
        {
            InitializeComponent();
            LabVersion.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        }

        private void BtnTour_Click(object sender, RoutedEventArgs e)
        {
            (Window.GetWindow(this) as MainWindow)?.StartTour();
        }

        private void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.Tag is string url))
                return;

            try
            {
                Browser.Open(url);
            }
            catch (Exception ex)
            {
                _ = Dialogs.Of(this)?.ErrorAsync(Text.Get("Dialog.BrowserFailedTitle"), Text.Format("Dialog.BrowserFailed", url, ex.Message));
            }
        }
    }
}
