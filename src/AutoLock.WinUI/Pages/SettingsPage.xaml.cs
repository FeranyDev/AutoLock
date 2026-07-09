using AutoLock.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoLock_WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _isLoading = true;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;
        try
        {
            StartupSwitch.IsOn = StartupManager.IsEnabled();
            BackgroundSwitch.IsOn = App.State.Settings.RunInBackground;
            ExternalPowerSwitch.IsOn = App.State.Settings.DisableOnExternalPower;
            TrustedWifiBox.Text = FormatTrustedWifi(App.State.Settings.TrustedWifiSsids);
            LanguageBox.SelectedIndex = App.State.Language == "zh-CN" ? 0 : 1;
            ApplyText();
            SetInfo(App.State.T("StatusReady"), App.State.T("StatusLoadedSettings"), InfoBarSeverity.Informational);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            StartupManager.SetEnabled(StartupSwitch.IsOn, App.State.Settings.RunInBackground);
            SetInfo(
                App.State.T(StartupSwitch.IsOn ? "InfoStartupEnabledTitle" : "InfoStartupDisabledTitle"),
                App.State.T(StartupSwitch.IsOn ? "InfoStartupEnabled" : "InfoStartupDisabled"),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            SetInfo(App.State.T("InfoSettingFailedTitle"), App.State.F("InfoStartupFailed", ex.Message), InfoBarSeverity.Error);
            _isLoading = true;
            try
            {
                StartupSwitch.IsOn = StartupManager.IsEnabled();
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    private void BackgroundSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        var settings = App.State.Settings with { RunInBackground = BackgroundSwitch.IsOn };
        App.State.SaveSettings(settings);

        if (StartupSwitch.IsOn)
        {
            StartupManager.SetEnabled(enabled: true, startInBackground: settings.RunInBackground);
        }

        SetInfo(
            App.State.T(BackgroundSwitch.IsOn ? "InfoBackgroundEnabledTitle" : "InfoBackgroundDisabledTitle"),
            App.State.T(BackgroundSwitch.IsOn ? "InfoBackgroundEnabled" : "InfoBackgroundDisabled"),
            InfoBarSeverity.Success);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (LanguageBox.SelectedItem is not ComboBoxItem item || item.Tag is not string language)
        {
            return;
        }

        App.State.SetLanguage(language);
        ApplyText();
        SetInfo(App.State.T("InfoLanguageTitle"), App.State.T("InfoLanguageChanged"), InfoBarSeverity.Success);
    }

    private void ExternalPowerSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        App.State.SaveSettings(App.State.Settings with { DisableOnExternalPower = ExternalPowerSwitch.IsOn });
        App.State.Monitor.UpdateRuntimeOptions(
            CurrentMissingSeconds(),
            CurrentMinRssi(),
            App.State.Settings.DryRun,
            App.State.Settings.DisableOnExternalPower,
            App.State.Settings.PauseUntilUtc,
            App.State.Settings.TrustedWifiSsids);
        SetInfo(
            App.State.T(ExternalPowerSwitch.IsOn ? "InfoExternalPowerEnabledTitle" : "InfoExternalPowerDisabledTitle"),
            App.State.T(ExternalPowerSwitch.IsOn ? "InfoExternalPowerEnabled" : "InfoExternalPowerDisabled"),
            InfoBarSeverity.Success);
    }

    private void TrustedWifiBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        SaveTrustedWifi(ParseTrustedWifi(TrustedWifiBox.Text));
    }

    private void UseCurrentWifiButton_Click(object sender, RoutedEventArgs e)
    {
        var ssid = WifiStatusService.GetConnectedSsid();
        if (string.IsNullOrWhiteSpace(ssid))
        {
            SetInfo(App.State.T("InfoWifiUnavailableTitle"), App.State.T("InfoWifiUnavailable"), InfoBarSeverity.Warning);
            return;
        }

        _isLoading = true;
        try
        {
            var ssids = ParseTrustedWifi(TrustedWifiBox.Text)
                .Append(ssid.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            TrustedWifiBox.Text = FormatTrustedWifi(ssids);
        }
        finally
        {
            _isLoading = false;
        }

        SaveTrustedWifi(ParseTrustedWifi(TrustedWifiBox.Text));
        SetInfo(App.State.T("InfoTrustedWifiSavedTitle"), App.State.F("InfoTrustedWifiSaved", ssid), InfoBarSeverity.Success);
    }

    private static void SaveTrustedWifi(string[] ssids)
    {
        var settings = App.State.Settings with
        {
            TrustedWifiSsid = string.Empty,
            TrustedWifiSsids = ssids
        };
        App.State.SaveSettings(settings);
        App.State.Monitor.UpdateRuntimeOptions(
            CurrentMissingSeconds(),
            CurrentMinRssi(),
            settings.DryRun,
            settings.DisableOnExternalPower,
            settings.PauseUntilUtc,
            settings.TrustedWifiSsids);
    }

    private static int CurrentMissingSeconds()
    {
        return Math.Clamp(App.State.Binding?.MissingSeconds ?? App.State.Settings.MissingSeconds, 1, 3600);
    }

    private static int CurrentMinRssi()
    {
        return Math.Clamp(App.State.Binding?.MinRssi ?? App.State.Settings.MinRssi, -120, 0);
    }

    private static string[] ParseTrustedWifi(string text)
    {
        return text
            .Split(['\r', '\n', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatTrustedWifi(IEnumerable<string>? ssids)
    {
        return string.Join(Environment.NewLine, ssids ?? []);
    }

    private void SetInfo(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void ApplyText()
    {
        PageTitleText.Text = App.State.T("NavSettings");
        PageSubtitleText.Text = App.State.T("PageSettingsSubtitle");
        StartupTitleText.Text = App.State.T("CheckStartup");
        StartupHelpText.Text = App.State.T("HelpStartup");
        BackgroundTitleText.Text = App.State.T("CheckBackground");
        BackgroundHelpText.Text = App.State.T("HelpBackground");
        ExternalPowerTitleText.Text = App.State.T("CheckExternalPower");
        ExternalPowerHelpText.Text = App.State.T("HelpExternalPower");
        TrustedWifiTitleText.Text = App.State.T("LabelTrustedWifi");
        TrustedWifiHelpText.Text = App.State.T("HelpTrustedWifi");
        TrustedWifiBox.PlaceholderText = App.State.T("PlaceholderTrustedWifi");
        UseCurrentWifiButtonText.Text = App.State.T("ButtonUseCurrentWifi");
        LanguageTitleText.Text = App.State.T("LabelLanguage");
        LanguageHelpText.Text = App.State.T("HelpLanguage");
        LanguageChineseItem.Content = App.State.T("LanguageChinese");
        LanguageEnglishItem.Content = App.State.T("LanguageEnglish");

    }
}
