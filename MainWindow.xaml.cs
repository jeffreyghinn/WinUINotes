using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Storage;
using Windows.UI.Input;
using Windows.Services.Store;
using WinUINotes.Services;
using WinUINotes.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUINotes
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private const string WindowStateKey = "MainWindowState";
        private const double ExpandedSidebarWidthThreshold =
            240 + (24 * 2) + (200 * 3) + (12 * 2);
        private const int MinimumWindowWidth = 320;
        private const int MinimumWindowHeight = 320;
        private const uint WmGetMinMaxInfo = 0x0024;
        private const uint WmKeyDown = 0x0100;
        private const uint WmSysKeyDown = 0x0104;
        private const uint WmAppCommand = 0x0319;
        private const ulong AppCommandBrowserBack = 1;
        private const ulong VkBrowserBack = 0xA6;
        private static readonly UIntPtr WindowSubclassId = new(1);
        private readonly SubclassProc _windowSubclassProc;
        private readonly EnumChildWindowsProc _enumChildWindowsProc;
        private readonly List<IntPtr> _subclassedChildWindows = new();
        private AppWindow? _appWindow;
        private RectInt32 _lastNormalBounds;
        private bool _hasNormalBounds;
        private bool _restoringWindowState;
        private bool _storeLicenseLoaded;
        private bool? _isLargeWindow;

        public MainWindow()
        {
            _windowSubclassProc = WindowSubclassCallback;
            _enumChildWindowsProc = SubclassChildWindow;
            InitializeComponent();
            ProLicense.Initialize(WindowNative.GetWindowHandle(this));
#if DEBUG
            var settings = ApplicationData.Current.LocalSettings;
            var simulatePro = settings.Values.TryGetValue("SimulateProEnabled", out var savedValue) &&
                              savedValue is bool enabled && enabled;
            ProLicense.SetDebugProOverride(simulatePro ? true : null);
#endif
            RootGrid.AddHandler(
                UIElement.PointerPressedEvent,
                new PointerEventHandler(RootGrid_PointerPressed),
                true);

            InitializeWindowState();

            // Hide the default system title bar.
            ExtendsContentIntoTitleBar = true;
            // Replace system title bar with the WinUI TitleBar.
            SetTitleBar(AppTitleBar);
            // Update the title bar icon when the Windows theme changes.
            UpdateTitleBarIcon();
            AppTitleBar.ActualThemeChanged += AppTitleBar_ActualThemeChanged;
        }

        private void InitializeWindowState()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            SetWindowSubclass(hwnd, _windowSubclassProc, WindowSubclassId, UIntPtr.Zero);
            EnumChildWindows(hwnd, _enumChildWindowsProc, UIntPtr.Zero);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.Changed += AppWindow_Changed;
            Closed += MainWindow_Closed;

            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue(WindowStateKey, out var savedValue) &&
                savedValue is ApplicationDataCompositeValue savedState)
            {
                var bounds = new RectInt32(
                    ReadInt(savedState, "X", _appWindow.Position.X),
                    ReadInt(savedState, "Y", _appWindow.Position.Y),
                    ReadInt(savedState, "Width", _appWindow.Size.Width),
                    ReadInt(savedState, "Height", _appWindow.Size.Height));

                _lastNormalBounds = MakeBoundsVisible(bounds);
                _hasNormalBounds = true;

                _restoringWindowState = true;
                try
                {
                    _appWindow.Resize(new SizeInt32(_lastNormalBounds.Width, _lastNormalBounds.Height));
                    _appWindow.Move(new PointInt32(_lastNormalBounds.X, _lastNormalBounds.Y));

                    var mode = savedState.TryGetValue("Mode", out var savedMode)
                        ? savedMode as string
                        : null;
                    if (mode == "FullScreen")
                    {
                        _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                    }
                    else if (mode == "Maximized" && _appWindow.Presenter is OverlappedPresenter presenter)
                    {
                        presenter.Maximize();
                    }
                }
                finally
                {
                    _restoringWindowState = false;
                }
            }

            RememberNormalBoundsIfApplicable();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_restoringWindowState)
            {
                return;
            }

            RememberNormalBoundsIfApplicable();
            SaveWindowState();
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            RememberNormalBoundsIfApplicable();
            SaveWindowState();
            foreach (var childHwnd in _subclassedChildWindows)
            {
                RemoveWindowSubclass(childHwnd, _windowSubclassProc, WindowSubclassId);
            }
            RemoveWindowSubclass(WindowNative.GetWindowHandle(this), _windowSubclassProc, WindowSubclassId);
        }

        private void RememberNormalBoundsIfApplicable()
        {
            if (_appWindow?.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Restored)
            {
                _lastNormalBounds = new RectInt32(
                    _appWindow.Position.X,
                    _appWindow.Position.Y,
                    _appWindow.Size.Width,
                    _appWindow.Size.Height);
                _hasNormalBounds = true;
            }
        }

        private void SaveWindowState()
        {
            if (_appWindow is null)
            {
                return;
            }

            var settings = ApplicationData.Current.LocalSettings;
            var savedState = new ApplicationDataCompositeValue();
            var bounds = _hasNormalBounds
                ? _lastNormalBounds
                : new RectInt32(_appWindow.Position.X, _appWindow.Position.Y, _appWindow.Size.Width, _appWindow.Size.Height);
            savedState["X"] = bounds.X;
            savedState["Y"] = bounds.Y;
            savedState["Width"] = bounds.Width;
            savedState["Height"] = bounds.Height;
            savedState["Mode"] = GetWindowMode();
            settings.Values[WindowStateKey] = savedState;
        }

        private string GetWindowMode()
        {
            if (_appWindow?.Presenter.Kind == AppWindowPresenterKind.FullScreen)
            {
                return "FullScreen";
            }

            return _appWindow?.Presenter is OverlappedPresenter presenter &&
                   presenter.State == OverlappedPresenterState.Maximized
                ? "Maximized"
                : "Normal";
        }

        private RectInt32 MakeBoundsVisible(RectInt32 bounds)
        {
            if (_appWindow is null)
            {
                return bounds;
            }

            var display = DisplayArea.GetFromRect(bounds, DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;
            var minimumWidth = Math.Min(MinimumWindowWidth, workArea.Width);
            var minimumHeight = Math.Min(MinimumWindowHeight, workArea.Height);
            var width = Math.Clamp(bounds.Width, minimumWidth, workArea.Width);
            var height = Math.Clamp(bounds.Height, minimumHeight, workArea.Height);
            var x = Math.Clamp(bounds.X, workArea.X, workArea.X + workArea.Width - width);
            var y = Math.Clamp(bounds.Y, workArea.Y, workArea.Y + workArea.Height - height);

            return new RectInt32(x, y, width, height);
        }

        private static int ReadInt(ApplicationDataCompositeValue values, string key, int fallback)
        {
            return values.TryGetValue(key, out var value) && value is int number
                ? number
                : fallback;
        }

        private void AppTitleBar_ActualThemeChanged(FrameworkElement sender, object args)
        {
            UpdateTitleBarIcon();
        }

        private void UpdateTitleBarIcon()
        {
            var iconPath = AppTitleBar.ActualTheme == ElementTheme.Dark
                ? "ms-appx:///Assets/TitleBarIcon.Dark.png"
                : "ms-appx:///Assets/TitleBarIcon.png";

            AppTitleBar.IconSource = new ImageIconSource
            {
                ImageSource = new BitmapImage(new Uri(iconPath))
            };
        }

        private void AppTitleBar_BackRequested(TitleBar sender, object args)
        {
            if (rootFrame.CanGoBack == true)
            {
                rootFrame.GoBack();
            }
        }

        private async void AppNavigationView_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateSidebarForWindowWidth(AppNavigationView.ActualWidth);

            if (_storeLicenseLoaded)
            {
                return;
            }

            _storeLicenseLoaded = true;
            bool isPro = await ProLicense.HasProAsync();
            UpdateProControls(isPro);
        }

        private void AppNavigationView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSidebarForWindowWidth(e.NewSize.Width);
        }

        private void UpdateSidebarForWindowWidth(double width)
        {
            var isLargeWindow = width >= ExpandedSidebarWidthThreshold;
            if (_isLargeWindow == isLargeWindow)
            {
                return;
            }

            _isLargeWindow = isLargeWindow;
            AppNavigationView.PaneDisplayMode = isLargeWindow
                ? NavigationViewPaneDisplayMode.Left
                : NavigationViewPaneDisplayMode.LeftMinimal;
            AppNavigationView.IsPaneOpen = isLargeWindow;
            AppTitleBar.IsPaneToggleButtonVisible = !isLargeWindow;
        }

        private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
        {
            AppNavigationView.IsPaneOpen = !AppNavigationView.IsPaneOpen;
        }

        private async void AppNavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer is not NavigationViewItem item)
            {
                return;
            }

            switch (item.Tag as string)
            {
                case "Notes":
                    if (rootFrame.Content is not AllNotesPage)
                    {
                        rootFrame.Navigate(typeof(AllNotesPage));
                    }
                    break;
                case "Trash":
                    if (FeatureFlags.ProFeaturesEnabled &&
                        await ProLicense.HasProAsync() &&
                        rootFrame.Content is not TrashPage)
                    {
                        rootFrame.Navigate(typeof(TrashPage));
                    }
                    break;
                case "About":
                    if (rootFrame.Content is not AboutPage)
                    {
                        rootFrame.Navigate(typeof(AboutPage));
                    }
                    if (rootFrame.Content is AboutPage aboutPage)
                    {
                        aboutPage.PurchaseRequested -= AboutPage_PurchaseRequested;
                        aboutPage.PurchaseRequested += AboutPage_PurchaseRequested;
                        aboutPage.SimulatedLicenseChanged -= AboutPage_SimulatedLicenseChanged;
                        aboutPage.SimulatedLicenseChanged += AboutPage_SimulatedLicenseChanged;
                        aboutPage.SetProOfferVisible(UnlockProItemVisibility());
                    }
                    break;
            }
        }

        private async void AboutPage_SimulatedLicenseChanged(object? sender, EventArgs e)
        {
            UpdateProControls(await ProLicense.HasProAsync());
        }

        private async Task PurchaseProAsync()
        {
            try
            {
                StorePurchaseResult result = await ProLicense.PurchaseAsync();
                if (result.Status == StorePurchaseStatus.Succeeded && await ProLicense.HasProAsync())
                {
                    UpdateProControls(true);
                }
                else if (result.Status == StorePurchaseStatus.NotPurchased)
                {
                    await ShowStoreMessageAsync("Purchase wasn't completed.");
                }
            }
            catch (Exception exception)
            {
                await ShowStoreMessageAsync($"The Store couldn't start this purchase. {exception.Message}");
            }
        }

        private bool UnlockProItemVisibility() =>
            FeatureFlags.ProFeaturesEnabled && TrashItem.Visibility != Visibility.Visible;

        private async void AboutPage_PurchaseRequested(object? sender, EventArgs e)
        {
            if (sender is AboutPage page)
            {
                page.SetPurchaseInProgress(true);
                await PurchaseProAsync();
                page.SetPurchaseInProgress(false);
            }
        }

        private void UpdateProControls(bool isPro)
        {
            TrashItem.Visibility = FeatureFlags.ProFeaturesEnabled && isPro
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (rootFrame.Content is AboutPage aboutPage)
            {
                aboutPage.SetProOfferVisible(FeatureFlags.ProFeaturesEnabled && !isPro);
            }
            if (rootFrame.Content is AllNotesPage notesPage)
            {
                notesPage.ApplyProLicense(isPro);
            }
            else if (rootFrame.Content is NotePage notePage)
            {
                notePage.ApplyProLicense(isPro);
            }
        }

        private async Task ShowStoreMessageAsync(string message)
        {
            ContentDialog dialog = new()
            {
                Title = "WinUI Notes Pro",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = rootFrame.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private IntPtr WindowSubclassCallback(
            IntPtr hwnd,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
            if (message == WmGetMinMaxInfo && hwnd == WindowNative.GetWindowHandle(this))
            {
                var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                var dpi = GetDpiForWindow(hwnd);
                var scale = dpi == 0 ? 1.0 : dpi / 96.0;
                minMaxInfo.MinimumTrackSize.X = (int)Math.Ceiling(MinimumWindowWidth * scale);
                minMaxInfo.MinimumTrackSize.Y = (int)Math.Ceiling(MinimumWindowHeight * scale);
                Marshal.StructureToPtr(minMaxInfo, lParam, false);
            }

            var appCommand = (unchecked((ulong)lParam.ToInt64()) >> 16) & 0xFFFF;
            var isBrowserBack = message == WmAppCommand && appCommand == AppCommandBrowserBack;
            var isBrowserBackKey = (message == WmKeyDown || message == WmSysKeyDown) &&
                                   wParam.ToUInt64() == VkBrowserBack;
            if ((isBrowserBack || isBrowserBackKey) && rootFrame.CanGoBack)
            {
                QueueGoBack();
                return new IntPtr(1);
            }

            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;

            if ((properties.IsXButton1Pressed || properties.PointerUpdateKind.ToString() == "XButton1Pressed") &&
                rootFrame.CanGoBack)
            {
                e.Handled = true;
                QueueGoBack();
            }
        }

        private void QueueGoBack()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (rootFrame.CanGoBack)
                {
                    rootFrame.GoBack();
                }
            });
        }

        private bool SubclassChildWindow(IntPtr childHwnd, IntPtr referenceData)
        {
            if (SetWindowSubclass(childHwnd, _windowSubclassProc, WindowSubclassId, UIntPtr.Zero))
            {
                _subclassedChildWindows.Add(childHwnd);
            }

            return true;
        }

        private delegate IntPtr SubclassProc(
            IntPtr hwnd,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData);

        private delegate bool EnumChildWindowsProc(IntPtr hwnd, IntPtr referenceData);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaximumSize;
            public NativePoint MaximumPosition;
            public NativePoint MinimumTrackSize;
            public NativePoint MaximumTrackSize;
        }

        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowSubclass(
            IntPtr hwnd,
            SubclassProc callback,
            UIntPtr subclassId,
            UIntPtr referenceData);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr subclassId);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumChildWindows(IntPtr parentHwnd, EnumChildWindowsProc callback, UIntPtr referenceData);

        [DllImport("comctl32.dll", ExactSpelling = true)]
        private static extern IntPtr DefSubclassProc(
            IntPtr hwnd,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);

    }
}
