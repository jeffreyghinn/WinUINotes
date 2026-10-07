using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel;
using Windows.Storage;
using WinUINotes.Services;

namespace WinUINotes.Views
{
    public sealed partial class AboutPage : Page
    {
        private const string SimulateProSettingKey = "SimulateProEnabled";
        private bool _loadingDebugSetting;
        public event EventHandler? PurchaseRequested;
        public event EventHandler? SimulatedLicenseChanged;

        public AboutPage()
        {
            InitializeComponent();
            var version = Package.Current.Id.Version;
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
#if DEBUG
            _loadingDebugSetting = true;
            var settings = ApplicationData.Current.LocalSettings;
            var simulatePro = settings.Values.TryGetValue(SimulateProSettingKey, out var savedValue) && savedValue is bool enabled && enabled;
            DebugProToggle.IsOn = simulatePro;
            DebugProToggle.Visibility = FeatureFlags.ProFeaturesEnabled ? Visibility.Visible : Visibility.Collapsed;
            ProLicense.SetDebugProOverride(simulatePro ? true : null);
            _loadingDebugSetting = false;
#else
            DebugProToggle.Visibility = Visibility.Collapsed;
#endif
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

        private void DebugProToggle_Toggled(object sender, RoutedEventArgs e)
        {
#if DEBUG
            if (_loadingDebugSetting)
            {
                return;
            }

            var settings = ApplicationData.Current.LocalSettings;
            ProLicense.SetDebugProOverride(DebugProToggle.IsOn ? true : null);
            if (DebugProToggle.IsOn)
            {
                settings.Values[SimulateProSettingKey] = true;
            }
            else
            {
                settings.Values.Remove(SimulateProSettingKey);
            }

            SimulatedLicenseChanged?.Invoke(this, EventArgs.Empty);
#endif
        }
    }
}
