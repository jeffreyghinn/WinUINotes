using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel;

namespace WinUINotes.Views
{
    public sealed partial class AboutPage : Page
    {
        public event EventHandler? PurchaseRequested;

        public AboutPage()
        {
            InitializeComponent();
            var version = Package.Current.Id.Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        public void SetProOfferVisible(bool visible)
        {
            UnlockProButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetPurchaseInProgress(bool inProgress)
        {
            UnlockProButton.IsEnabled = !inProgress;
        }

        private void UnlockProButton_Click(object sender, RoutedEventArgs e)
        {
            PurchaseRequested?.Invoke(this, EventArgs.Empty);
        }

    }
}
