using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Campfire.Pages
{
    /// <summary>
    /// "Tools" tab: entries to the tools.
    /// </summary>
    public partial class ToolsPage : UserControl
    {
        public ToolsPage()
        {
            InitializeComponent();
        }

        /// <summary>Raised when the repair entry is clicked.</summary>
        public event EventHandler RepairRequested;

        /// <summary>Raised when the JRE entry is clicked.</summary>
        public event EventHandler JreRequested;

        /// <summary>Shows the state of the launcher next to the repair entry; <c>null</c> hides it.</summary>
        internal void SetRepairStatus(string text, string brushKey)
        {
            PanRepairStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
            if (text == null)
                return;
            LabRepairStatus.Text = text;
            LabRepairStatus.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            DotRepairStatus.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        }

        private void EntryRepair_Click(object sender, RoutedEventArgs e)
        {
            RepairRequested?.Invoke(this, EventArgs.Empty);
        }

        private void EntryJre_Click(object sender, RoutedEventArgs e)
        {
            JreRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
