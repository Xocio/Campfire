using System;
using System.Windows;
using System.Windows.Controls;
using Campfire.Localization;

namespace Campfire.Pages
{
    /// <summary>
    /// Sponsorship links for the Campfire developer and EasyTier.
    /// </summary>
    public partial class SupportPage : UserControl
    {
        public SupportPage()
        {
            InitializeComponent();
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
