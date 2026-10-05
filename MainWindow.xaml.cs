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
using Windows.Graphics;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Storage;
using Windows.UI.Input;

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

        public MainWindow()
        {
            _windowSubclassProc = WindowSubclassCallback;
            _enumChildWindowsProc = SubclassChildWindow;
            InitializeComponent();
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
            var minimumWidth = Math.Min(320, workArea.Width);
            var minimumHeight = Math.Min(240, workArea.Height);
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

        private IntPtr WindowSubclassCallback(
            IntPtr hwnd,
            uint message,
            UIntPtr wParam,
            IntPtr lParam,
            UIntPtr subclassId,
            UIntPtr referenceData)
        {
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
