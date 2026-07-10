using System.Runtime.InteropServices;
using AutoLock.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using AutoLock_WinUI.Pages;
using AutoLock_WinUI.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace AutoLock_WinUI;

public sealed partial class MainWindow : Window
{
    private const int InitialWindowEffectiveWidth = 1440;
    private const int InitialWindowEffectiveHeight = 860;
    private const int DefaultDpi = 96;
    private const double MaxInitialWindowWorkAreaRatio = 0.92;
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int WmSize = 0x0005;
    private const int WmWtsSessionChange = 0x02B1;
    private const int SizeMinimized = 1;
    private const int NotifyForThisSession = 0;
    private const int WtsSessionLock = 0x7;
    private const int WtsSessionUnlock = 0x8;

    private readonly IntPtr _windowHandle;
    private readonly SubclassProc _subclassProc;
    private bool _allowExit;
    private bool _resumeScanInProgress;
    private bool _sessionNotificationRegistered;
    private bool _startupAutoScanAttempted;
    private DateTimeOffset _lastUnlockHandled = DateTimeOffset.MinValue;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayExitItem;

    public static MainWindow? ActiveWindow { get; private set; }

    public IntPtr WindowHandle => _windowHandle;

    public MainWindow()
    {
        InitializeComponent();

        ActiveWindow = this;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _subclassProc = WindowSubclassProc;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ResizeForCurrentScale(InitialWindowEffectiveWidth, InitialWindowEffectiveHeight);
        NavFrame.Navigate(typeof(ProtectionPage));
        InitializeTrayIcon();
        ApplyText();
        AppWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        App.State.LanguageChanged += AppState_LanguageChanged;
        SetWindowSubclass(_windowHandle, _subclassProc, UIntPtr.Zero, UIntPtr.Zero);
        _sessionNotificationRegistered = WTSRegisterSessionNotification(_windowHandle, NotifyForThisSession);
        DispatcherQueue.TryEnqueue(async () => await OnWindowReadyAsync());
    }

    public void NavigateToScanDevices()
    {
        ScanDevicesNavItem.IsSelected = true;
        NavFrame.Navigate(typeof(ScanDevicesPage));
    }

    private void ResizeForCurrentScale(int effectiveWidth, int effectiveHeight)
    {
        var dpi = GetDpiForWindow(_windowHandle);
        var scale = dpi > 0 ? dpi / (double)DefaultDpi : 1;
        var physicalWidth = Math.Max(1, (int)Math.Round(effectiveWidth * scale));
        var physicalHeight = Math.Max(1, (int)Math.Round(effectiveHeight * scale));

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (displayArea is not null)
        {
            var workArea = displayArea.WorkArea;
            var maxWidth = Math.Max(1, (int)Math.Round(workArea.Width * MaxInitialWindowWorkAreaRatio));
            var maxHeight = Math.Max(1, (int)Math.Round(workArea.Height * MaxInitialWindowWorkAreaRatio));
            physicalWidth = Math.Min(physicalWidth, maxWidth);
            physicalHeight = Math.Min(physicalHeight, maxHeight);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

        if (displayArea is not null)
        {
            var workArea = displayArea.WorkArea;
            var x = workArea.X + Math.Max(0, (workArea.Width - physicalWidth) / 2);
            var y = workArea.Y + Math.Max(0, (workArea.Height - physicalHeight) / 2);
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "protection":
                    NavFrame.Navigate(typeof(ProtectionPage));
                    break;
                case "devices":
                    NavFrame.Navigate(typeof(ScanDevicesPage));
                    break;
                case "history":
                    NavFrame.Navigate(typeof(HistoryPage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
                case "settings":
                    NavFrame.Navigate(typeof(SettingsPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }

    private async Task OnWindowReadyAsync()
    {
        if (App.State.BindingDecryptionFailed)
        {
            App.State.Notify(
                App.State.T("InfoIrkDecryptFailedTitle"),
                App.State.T("InfoIrkDecryptFailed"),
                AppNotificationKind.Error);
        }

        if (App.State.Settings.RunInBackground && Environment.GetCommandLineArgs().Any(x => string.Equals(x, "--background", StringComparison.OrdinalIgnoreCase)))
        {
            HideToTray();
        }

        await TryAutoStartMonitorFromIrkAsync();
    }

    private async Task TryAutoStartMonitorFromIrkAsync()
    {
        if (_startupAutoScanAttempted || App.State.Monitor.IsMonitoring)
        {
            return;
        }

        _startupAutoScanAttempted = true;

        var binding = App.State.Binding;
        if (binding is null)
        {
            return;
        }

        if (!IrkHelper.IsValidOrEmpty(binding.Irk ?? string.Empty))
        {
            App.State.Notify(App.State.T("InfoIrkInvalidTitle"), App.State.T("InfoIrkInvalid"), AppNotificationKind.Warning, binding);
            return;
        }

        var irk = IrkHelper.Normalize(binding.Irk ?? string.Empty);
        var minRssi = binding.MinRssi ?? -90;
        if (string.IsNullOrWhiteSpace(irk))
        {
            App.State.Monitor.StartMonitor(binding, irk, BuildMonitorOptions(binding, minRssi, dryRun: false));
            App.State.Notify(App.State.T("InfoAutoMonitoringTitle"), App.State.F("InfoAutoMonitoring", binding.MaskedIdentity), AppNotificationKind.Success, binding);
            return;
        }

        var scanSeconds = Math.Clamp(binding.MissingSeconds > 0 ? Math.Min(binding.MissingSeconds, 8) : 8, 3, 15);

        App.State.Notify(App.State.T("InfoAutoScanTitle"), App.State.F("InfoAutoScan", scanSeconds), binding: binding);
        var sighting = await App.State.Monitor.ScanForIrkMatchAsync(irk, TimeSpan.FromSeconds(scanSeconds), minRssi);
        if (sighting is null || App.State.Monitor.IsMonitoring)
        {
            App.State.Notify(App.State.T("InfoAutoNoMatchTitle"), App.State.T("InfoAutoNoMatch"), AppNotificationKind.Warning, binding);
            return;
        }

        binding = binding with
        {
            Address = sighting.Address,
            Name = sighting.Name,
            HasAppleManufacturerData = sighting.HasAppleManufacturerData
        };
        App.State.SaveBinding(binding);
        App.State.Monitor.StartMonitor(binding, irk, BuildMonitorOptions(binding, minRssi, dryRun: false));
        App.State.Notify(App.State.T("InfoAutoMonitoringTitle"), App.State.F("InfoAutoMonitoring", binding.MaskedIdentity), AppNotificationKind.Success, binding);
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason != SessionSwitchReason.SessionUnlock)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(async () => await HandleSessionUnlockAsync());
    }

    private async Task HandleSessionUnlockAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastUnlockHandled).TotalSeconds < 2)
        {
            return;
        }

        _lastUnlockHandled = now;

        var monitor = App.State.Monitor;
        monitor.ResetTimeout();

        if (!monitor.ResumeMonitorAfterUnlock)
        {
            if (monitor.IsMonitoring)
            {
                App.State.Notify(App.State.T("InfoUnlockTitle"), App.State.T("InfoUnlockReset"), AppNotificationKind.Information, App.State.Binding);
            }

            return;
        }

        if (_resumeScanInProgress)
        {
            return;
        }

        var binding = App.State.Binding;
        if (binding is null)
        {
            App.State.Notify(App.State.T("InfoResumeFailedTitle"), App.State.T("InfoResumeFailed"), AppNotificationKind.Warning, binding);
            return;
        }

        var irk = IrkHelper.Normalize(binding.Irk ?? string.Empty);
        if (!IrkHelper.IsValidOrEmpty(irk))
        {
            App.State.Notify(App.State.T("InfoIrkInvalidTitle"), App.State.T("InfoIrkInvalid"), AppNotificationKind.Warning, binding);
            return;
        }

        var minRssi = binding.MinRssi ?? -90;
        _resumeScanInProgress = true;
        App.State.Notify(App.State.T("InfoResumeWaitingTitle"), App.State.T("InfoResumeWaiting"), AppNotificationKind.Information, binding);

        try
        {
            var sighting = await monitor.ScanUntilBindingMatchAsync(binding, irk, minRssi);
            if (sighting is null || monitor.IsMonitoring)
            {
                return;
            }

            var currentBinding = App.State.Binding;
            if (currentBinding is null)
            {
                App.State.Notify(App.State.T("InfoResumeFailedTitle"), App.State.T("InfoResumeFailed"), AppNotificationKind.Warning, currentBinding);
                return;
            }

            var resumedBinding = currentBinding with
            {
                Address = sighting.Address,
                Name = sighting.Name,
                HasAppleManufacturerData = sighting.HasAppleManufacturerData
            };

            App.State.SaveBinding(resumedBinding);
            monitor.StartMonitor(resumedBinding, irk, BuildMonitorOptions(resumedBinding, minRssi, dryRun: false));
            App.State.Notify(App.State.T("InfoResumeTitle"), App.State.T("InfoResume"), AppNotificationKind.Success, resumedBinding);
        }
        finally
        {
            _resumeScanInProgress = false;
        }
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "AutoLock",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _trayOpenItem = new Forms.ToolStripMenuItem(App.State.T("TrayOpen"));
        _trayOpenItem.Click += (_, _) => DispatcherQueue.TryEnqueue(ShowMainWindow);
        _trayExitItem = new Forms.ToolStripMenuItem(App.State.T("TrayExit"));
        _trayExitItem.Click += (_, _) => DispatcherQueue.TryEnqueue(ExitApplication);

        _trayIcon.ContextMenuStrip.Items.Add(_trayOpenItem);
        _trayIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _trayIcon.ContextMenuStrip.Items.Add(_trayExitItem);
        _trayIcon.DoubleClick += (_, _) => DispatcherQueue.TryEnqueue(ShowMainWindow);
    }

    private static MonitorOptions BuildMonitorOptions(BindingConfig binding, int minRssi, bool dryRun)
    {
        return new MonitorOptions(
            binding.MissingSeconds,
            minRssi,
            dryRun,
            App.State.Settings.DisableOnExternalPower,
            App.State.Settings.PauseUntilUtc,
            App.State.Settings.TrustedWifiSsids);
    }

    private static Drawing.Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!File.Exists(iconPath))
        {
            return Drawing.SystemIcons.Shield;
        }

        using var icon = new Drawing.Icon(iconPath);
        return (Drawing.Icon)icon.Clone();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowExit || !App.State.Settings.RunInBackground)
        {
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        ShowWindow(_windowHandle, SwHide);
        App.State.Notify(App.State.T("InfoHiddenTitle"), App.State.T("InfoHidden"), AppNotificationKind.Information);
    }

    private void ShowMainWindow()
    {
        ShowWindow(_windowHandle, SwShow);
        Activate();
        SetForegroundWindow(_windowHandle);
    }

    private void ExitApplication()
    {
        _allowExit = true;
        _trayIcon?.Dispose();
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        AppWindow.Closing -= AppWindow_Closing;
        App.State.LanguageChanged -= AppState_LanguageChanged;
        if (_sessionNotificationRegistered)
        {
            WTSUnRegisterSessionNotification(_windowHandle);
            _sessionNotificationRegistered = false;
        }

        RemoveWindowSubclass(_windowHandle, _subclassProc, UIntPtr.Zero);
        _trayIcon?.Dispose();
        if (ReferenceEquals(ActiveWindow, this))
        {
            ActiveWindow = null;
        }
    }

    private void AppState_LanguageChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ApplyText);
    }

    private void ApplyText()
    {
        AppTitleBar.Title = App.State.T("AppTitle");
        ProtectionNavItem.Content = App.State.T("NavProtection");
        ScanDevicesNavItem.Content = App.State.T("NavScanDevices");
        HistoryNavItem.Content = App.State.T("NavHistory");
        AboutNavItem.Content = App.State.T("NavAbout");
        SettingsNavItem.Content = App.State.T("NavSettings");

        if (_trayOpenItem is not null)
        {
            _trayOpenItem.Text = App.State.T("TrayOpen");
        }

        if (_trayExitItem is not null)
        {
            _trayExitItem.Text = App.State.T("TrayExit");
        }
    }

    private IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData)
    {
        if (message == WmSize &&
            wParam.ToUInt32() == SizeMinimized &&
            !_allowExit &&
            App.State.Settings.RunInBackground)
        {
            DispatcherQueue.TryEnqueue(HideToTray);
            return IntPtr.Zero;
        }

        if (message == WmWtsSessionChange)
        {
            var reason = wParam.ToUInt32();
            if (reason == WtsSessionUnlock)
            {
                DispatcherQueue.TryEnqueue(async () => await HandleSessionUnlockAsync());
            }
            else if (reason == WtsSessionLock)
            {
                HistoryLogManager.Append("Session", App.State.T("InfoSessionLockedTitle"), App.State.T("InfoSessionLocked"));
            }
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    private delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd,
        SubclassProc subclassProc,
        UIntPtr subclassId,
        UIntPtr refData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd,
        SubclassProc subclassProc,
        UIntPtr subclassId);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);
}
