using AutoLock.Core;
using AutoLock_WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoLock_WinUI.Pages;

public sealed partial class ProtectionPage : Page
{
    private bool _eventsSubscribed;
    private bool _isLoading = true;

    public ProtectionPage()
    {
        InitializeComponent();
        Loaded += ProtectionPage_Loaded;
        Unloaded += ProtectionPage_Unloaded;
    }

    private void ProtectionPage_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeMonitorEvents();
        RefreshFromState();
    }

    private void ProtectionPage_Unloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeMonitorEvents();
    }

    private void SubscribeMonitorEvents()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        var monitor = App.State.Monitor;
        monitor.MonitoringStateChanged += Monitor_MonitoringStateChanged;
        monitor.MonitorDeviceSeen += Monitor_MonitorDeviceSeen;
        monitor.WeakSignalIgnored += Monitor_WeakSignalIgnored;
        monitor.MissingSignalChanged += Monitor_MissingSignalChanged;
        monitor.DryRunWouldLock += Monitor_DryRunWouldLock;
        monitor.LockFailed += Monitor_LockFailed;
        monitor.LockSucceededAndPaused += Monitor_LockSucceededAndPaused;
        monitor.LockSuppressed += Monitor_LockSuppressed;
        App.State.BindingChanged += AppState_BindingChanged;
        App.State.LanguageChanged += AppState_LanguageChanged;
        App.State.NotificationRaised += AppState_NotificationRaised;
        _eventsSubscribed = true;
    }

    private void UnsubscribeMonitorEvents()
    {
        if (!_eventsSubscribed)
        {
            return;
        }

        var monitor = App.State.Monitor;
        monitor.MonitoringStateChanged -= Monitor_MonitoringStateChanged;
        monitor.MonitorDeviceSeen -= Monitor_MonitorDeviceSeen;
        monitor.WeakSignalIgnored -= Monitor_WeakSignalIgnored;
        monitor.MissingSignalChanged -= Monitor_MissingSignalChanged;
        monitor.DryRunWouldLock -= Monitor_DryRunWouldLock;
        monitor.LockFailed -= Monitor_LockFailed;
        monitor.LockSucceededAndPaused -= Monitor_LockSucceededAndPaused;
        monitor.LockSuppressed -= Monitor_LockSuppressed;
        App.State.BindingChanged -= AppState_BindingChanged;
        App.State.LanguageChanged -= AppState_LanguageChanged;
        App.State.NotificationRaised -= AppState_NotificationRaised;
        _eventsSubscribed = false;
    }

    private void RefreshFromState()
    {
        _isLoading = true;
        var binding = App.State.Binding;
        ApplyText();
        try
        {
            ScanSecondsBox.Value = App.State.Settings.ScanSeconds;
            DryRunSwitch.IsOn = App.State.Settings.DryRun;
            MissingSecondsBox.Value = binding?.MissingSeconds ?? App.State.Settings.MissingSeconds;
            MinRssiBox.Value = binding?.MinRssi ?? App.State.Settings.MinRssi;
            BindingAddressText.Text = binding is null
                ? App.State.T("BindingNone")
                : App.State.F("BindingLoaded", binding.DisplayAddress);
            StartStopButton.IsEnabled = binding is not null;
            ClearBindingButton.IsEnabled = binding is not null;
            ApplyPauseButtonText();
        }
        finally
        {
            _isLoading = false;
        }

        if (binding is null)
        {
            SetInfo(App.State.T("InfoNoBindingTitle"), App.State.T("InfoNoBindingMessage"), InfoBarSeverity.Warning);
            return;
        }

        if (App.State.Monitor.IsMonitoring)
        {
            SetMonitoringUi(isMonitoring: true);
            SetInfo(App.State.T("InfoMonitoringTitle"), App.State.F("InfoMonitoring", binding.DisplayAddress), InfoBarSeverity.Success);
            return;
        }

        SetMonitoringUi(isMonitoring: false);
        SetInfo(App.State.T("StatusReady"), App.State.F("InfoReadyBound", binding.DisplayAddress), InfoBarSeverity.Informational);
    }

    private void ScanDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        AutoLock_WinUI.MainWindow.ActiveWindow?.NavigateToScanDevices();
    }

    private void ClearBindingButton_Click(object sender, RoutedEventArgs e)
    {
        App.State.Monitor.StopMonitor();
        App.State.ClearBinding();
        SetInfo(App.State.T("InfoBindingClearedTitle"), App.State.T("InfoBindingCleared"), InfoBarSeverity.Informational);
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        var monitor = App.State.Monitor;
        if (monitor.IsMonitoring)
        {
            monitor.StopMonitor();
            HistoryLogManager.Append("Monitor", App.State.T("InfoStoppedTitle"), App.State.T("InfoStoppedMessage"));
            SetInfo(App.State.T("InfoStoppedTitle"), App.State.T("InfoStoppedMessage"), InfoBarSeverity.Informational);
            return;
        }

        var binding = App.State.Binding;
        if (binding is null)
        {
            SetInfo(App.State.T("InfoNoBindingTitle"), App.State.T("InfoNoBindingMessage"), InfoBarSeverity.Warning);
            return;
        }

        if (!TryReadProtectionSettings(out var scanSeconds, out var missingSeconds, out var minRssi))
        {
            return;
        }

        binding = binding with
        {
            MissingSeconds = missingSeconds,
            MinRssi = minRssi
        };
        SaveProtectionSettings(scanSeconds, DryRunSwitch.IsOn, missingSeconds, minRssi, updateBinding: false);
        App.State.SaveBinding(binding, logHistory: false);

        var activeIrk = IrkHelper.Normalize(binding.Irk ?? string.Empty);
        monitor.StartMonitor(binding, activeIrk, BuildMonitorOptions(binding, minRssi, DryRunSwitch.IsOn));
        HistoryLogManager.Append("Monitor", App.State.T("InfoMonitoringTitle"), App.State.F("InfoMonitoring", binding.DisplayAddress));
        SetInfo(App.State.T("InfoMonitoringTitle"), App.State.F("InfoMonitoring", binding.DisplayAddress), InfoBarSeverity.Success);
    }

    private void TestLockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DryRunSwitch.IsOn)
        {
            HistoryLogManager.Append("Lock", App.State.T("InfoDryRunTitle"), App.State.T("InfoDryRunMessage"));
            SetInfo(App.State.T("InfoDryRunTitle"), App.State.T("InfoDryRunMessage"), InfoBarSeverity.Informational);
            return;
        }

        var result = LockService.LockWorkStation();
        if (result.Succeeded)
        {
            HistoryLogManager.Append("Lock", App.State.T("InfoManualLockTitle"), App.State.T("InfoManualLockMessage"));
            SetInfo(App.State.T("InfoManualLockTitle"), App.State.T("InfoManualLockMessage"), InfoBarSeverity.Success);
            return;
        }

        var message = result.ErrorMessage ?? result.ErrorCode.ToString();
        HistoryLogManager.Append("Lock", App.State.T("InfoLockFailedTitle"), App.State.F("InfoLockFailed", message));
        SetInfo(App.State.T("InfoLockFailedTitle"), App.State.F("InfoLockFailed", message), InfoBarSeverity.Error);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        var currentPause = App.State.Settings.PauseUntilUtc;
        DateTimeOffset? pauseUntil = currentPause is not null && currentPause > DateTimeOffset.UtcNow
            ? null
            : DateTimeOffset.UtcNow.AddMinutes(15);

        App.State.SaveSettings(App.State.Settings with { PauseUntilUtc = pauseUntil });
        App.State.Monitor.SetPauseUntil(pauseUntil);
        ApplyPauseButtonText();

        if (pauseUntil is null)
        {
            HistoryLogManager.Append("Monitor", App.State.T("InfoPauseClearedTitle"), App.State.T("InfoPauseCleared"));
            SetInfo(App.State.T("InfoPauseClearedTitle"), App.State.T("InfoPauseCleared"), InfoBarSeverity.Informational);
            return;
        }

        var localTime = pauseUntil.Value.ToLocalTime().ToString("T");
        HistoryLogManager.Append("Monitor", App.State.T("InfoPausedTitle"), App.State.F("InfoPausedUntil", localTime));
        SetInfo(App.State.T("InfoPausedTitle"), App.State.F("InfoPausedUntil", localTime), InfoBarSeverity.Informational);
    }

    private void DryRunSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading || ScanSecondsBox is null || MissingSecondsBox is null || MinRssiBox is null)
        {
            return;
        }

        SaveCurrentProtectionSettings();
    }

    private void ProtectionNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isLoading)
        {
            return;
        }

        SaveCurrentProtectionSettings();
    }

    private void SaveCurrentProtectionSettings()
    {
        if (!TryReadProtectionSettings(out var scanSeconds, out var missingSeconds, out var minRssi))
        {
            return;
        }

        SaveProtectionSettings(scanSeconds, DryRunSwitch.IsOn, missingSeconds, minRssi);
    }

    private bool TryReadProtectionSettings(out int scanSeconds, out int missingSeconds, out int minRssi)
    {
        scanSeconds = 0;
        missingSeconds = 0;
        minRssi = 0;

        return TryReadScanSeconds(out scanSeconds) &&
            TryReadMonitorOptions(out missingSeconds, out minRssi);
    }

    private bool TryReadMonitorOptions(out int missingSeconds, out int minRssi)
    {
        missingSeconds = 0;
        minRssi = 0;

        if (double.IsNaN(MissingSecondsBox.Value) || MissingSecondsBox.Value < 1)
        {
            SetInfo(App.State.T("InfoInvalidTitle"), App.State.T("InfoMissingInvalid"), InfoBarSeverity.Warning);
            return false;
        }

        if (double.IsNaN(MinRssiBox.Value) || MinRssiBox.Value < -120 || MinRssiBox.Value > 0)
        {
            SetInfo(App.State.T("InfoInvalidTitle"), App.State.T("InfoRssiInvalid"), InfoBarSeverity.Warning);
            return false;
        }

        missingSeconds = (int)Math.Round(MissingSecondsBox.Value);
        minRssi = (int)Math.Round(MinRssiBox.Value);
        return true;
    }

    private bool TryReadScanSeconds(out int seconds)
    {
        seconds = 0;
        if (double.IsNaN(ScanSecondsBox.Value) || ScanSecondsBox.Value < 1)
        {
            SetInfo(App.State.T("InfoInvalidTitle"), App.State.T("InfoScanSecondsInvalid"), InfoBarSeverity.Warning);
            return false;
        }

        seconds = (int)Math.Round(ScanSecondsBox.Value);
        return true;
    }

    private static void SaveProtectionSettings(
        int scanSeconds,
        bool dryRun,
        int missingSeconds,
        int minRssi,
        bool updateBinding = true)
    {
        scanSeconds = Math.Clamp(scanSeconds, 1, 3600);
        missingSeconds = Math.Clamp(missingSeconds, 1, 3600);
        minRssi = Math.Clamp(minRssi, -120, 0);

        var settings = App.State.Settings with
        {
            ScanSeconds = scanSeconds,
            MissingSeconds = missingSeconds,
            MinRssi = minRssi,
            DryRun = dryRun
        };
        App.State.SaveSettings(settings);

        if (updateBinding && App.State.Binding is { } binding)
        {
            App.State.SaveBinding(binding with
            {
                MissingSeconds = missingSeconds,
                MinRssi = minRssi
            }, logHistory: false);
        }

        App.State.Monitor.UpdateRuntimeOptions(
            missingSeconds,
            minRssi,
            settings.DryRun,
            settings.DisableOnExternalPower,
            settings.PauseUntilUtc,
            settings.TrustedWifiSsids);
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

    private void Monitor_MonitorDeviceSeen(object? sender, MonitorDeviceSeenEventArgs e)
    {
        Enqueue(() =>
        {
            SignalText.Text = e.IsIrkMatch
                ? App.State.F("SignalSeenIrk", e.Sighting.Rssi)
                : App.State.F("SignalSeenAddress", e.Sighting.Rssi);
            SetInfo(App.State.T("InfoSeenTitle"), App.State.F("InfoSeenDevice", e.Sighting.DisplayAddress), InfoBarSeverity.Success);
        });
    }

    private void Monitor_MonitoringStateChanged(object? sender, MonitoringStateChangedEventArgs e)
    {
        Enqueue(() => SetMonitoringUi(e.IsMonitoring));
    }

    private void Monitor_WeakSignalIgnored(object? sender, WeakSignalEventArgs e)
    {
        Enqueue(() => SetInfo(App.State.T("InfoWeakSignalTitle"), App.State.F("InfoWeakSignal", e.Rssi, e.MinRssi), InfoBarSeverity.Warning));
    }

    private void Monitor_MissingSignalChanged(object? sender, MissingSignalEventArgs e)
    {
        Enqueue(() => SignalText.Text = App.State.F("SignalMissing", e.MissingSeconds));
    }

    private void Monitor_DryRunWouldLock(object? sender, EventArgs e)
    {
        Enqueue(() =>
        {
            HistoryLogManager.Append("Lock", App.State.T("InfoDryRunTitle"), App.State.T("InfoDryRunMessage"));
            SetInfo(App.State.T("InfoDryRunTitle"), App.State.T("InfoDryRunMessage"), InfoBarSeverity.Informational);
        });
    }

    private void Monitor_LockFailed(object? sender, LockFailedEventArgs e)
    {
        Enqueue(() =>
        {
            var message = e.Result.ErrorMessage ?? e.Result.ErrorCode.ToString();
            HistoryLogManager.Append("Lock", App.State.T("InfoLockFailedTitle"), App.State.F("InfoLockFailed", message));
            SetInfo(App.State.T("InfoLockFailedTitle"), App.State.F("InfoLockFailed", message), InfoBarSeverity.Error);
        });
    }

    private void Monitor_LockSucceededAndPaused(object? sender, EventArgs e)
    {
        Enqueue(() =>
        {
            HistoryLogManager.Append("Lock", App.State.T("InfoLockedTitle"), App.State.T("InfoPausedAfterLock"));
            SetInfo(App.State.T("InfoLockedTitle"), App.State.T("InfoPausedAfterLock"), InfoBarSeverity.Success);
        });
    }

    private void Monitor_LockSuppressed(object? sender, LockSuppressedEventArgs e)
    {
        Enqueue(() =>
        {
            var (title, message) = e.Reason switch
            {
                LockSuppressionReason.ExternalPower => (
                    App.State.T("InfoSuppressedExternalPowerTitle"),
                    App.State.T("InfoSuppressedExternalPower")),
                LockSuppressionReason.TrustedWifi => (
                    App.State.T("InfoSuppressedTrustedWifiTitle"),
                    App.State.T("InfoSuppressedTrustedWifi")),
                _ => (
                    App.State.T("InfoSuppressedPausedTitle"),
                    App.State.F("InfoSuppressedPaused", e.UntilUtc?.ToLocalTime().ToString("T") ?? string.Empty))
            };

            HistoryLogManager.Append("Monitor", title, message);
            SetInfo(title, message, InfoBarSeverity.Informational);
        });
    }

    private void AppState_BindingChanged(object? sender, EventArgs e)
    {
        Enqueue(RefreshFromState);
    }

    private void AppState_LanguageChanged(object? sender, EventArgs e)
    {
        Enqueue(RefreshFromState);
    }

    private void AppState_NotificationRaised(object? sender, AppNotificationEventArgs e)
    {
        Enqueue(() => SetInfo(e.Title, e.Message, ToInfoBarSeverity(e.Kind)));
    }

    private void SetMonitoringUi(bool isMonitoring)
    {
        StartStopButtonIcon.Glyph = isMonitoring ? "\uE71A" : "\uE768";
        StartStopButtonText.Text = App.State.T(isMonitoring ? "ButtonStopMonitor" : "ButtonStartMonitor");
        SignalText.Text = App.State.T(isMonitoring ? "StatusMonitoringSignal" : "StatusIdle");
    }

    private void SetInfo(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private static InfoBarSeverity ToInfoBarSeverity(AppNotificationKind kind) => kind switch
    {
        AppNotificationKind.Success => InfoBarSeverity.Success,
        AppNotificationKind.Warning => InfoBarSeverity.Warning,
        AppNotificationKind.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational
    };

    private void ApplyText()
    {
        PageTitleText.Text = App.State.T("NavProtection");
        PageSubtitleText.Text = App.State.T("PageProtectionSubtitle");
        CurrentProtectionTitleText.Text = App.State.T("SectionCurrentProtection");
        ScanDevicesButtonText.Text = App.State.T("NavScanDevices");
        RulesSectionText.Text = App.State.T("SectionProtectionSettings");
        ScanSecondsTitleText.Text = App.State.T("LabelScanInterval");
        ScanSecondsHelpText.Text = App.State.T("HelpScanSeconds");
        ClearBindingButtonText.Text = App.State.T("ButtonClearBinding");
        ApplyPauseButtonText();
        TestLockButtonText.Text = App.State.T("ButtonTestLock");
        MissingSecondsTitleText.Text = App.State.T("LabelMissingSeconds");
        MissingSecondsHelpText.Text = App.State.T("HelpMissingSeconds");
        MinRssiTitleText.Text = App.State.T("LabelMinRssi");
        MinRssiHelpText.Text = App.State.T("HelpMinRssi");
        DryRunTitleText.Text = App.State.T("CheckDryRun");
        DryRunHelpText.Text = App.State.T("HelpDryRun");
        ActionsSectionText.Text = App.State.T("SectionActions");
        ActionsHelpText.Text = App.State.T("HelpProtectionActions");
    }

    private void Enqueue(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        DispatcherQueue.TryEnqueue(() => action());
    }

    private void ApplyPauseButtonText()
    {
        var pauseUntil = App.State.Settings.PauseUntilUtc;
        PauseButtonText.Text = pauseUntil is not null && pauseUntil > DateTimeOffset.UtcNow
            ? App.State.T("ButtonClearPause")
            : App.State.T("ButtonPause15");
    }
}
