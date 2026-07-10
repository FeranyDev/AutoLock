using AutoLock.Core;
using System.Security.Cryptography;

namespace AutoLock_WinUI.Services;

public sealed class AutoLockAppState : IDisposable
{
    public AutoLockMonitorService Monitor { get; } = new();

    public event EventHandler? BindingChanged;

    public event EventHandler? SettingsChanged;

    public event EventHandler? LanguageChanged;

    public event EventHandler<AppNotificationEventArgs>? NotificationRaised;

    public BindingConfig? Binding { get; private set; }

    public bool BindingDecryptionFailed { get; private set; }

    public AppSettings Settings { get; private set; } = AppSettings.Default;

    public string Language => WinUiLocalizer.NormalizeLanguage(Settings.Language);

    public void Load()
    {
        BindingDecryptionFailed = false;
        try
        {
            Binding = BindingConfigManager.Load();
        }
        catch (CryptographicException)
        {
            Binding = null;
            BindingDecryptionFailed = true;
        }

        Settings = NormalizeSettings(AppSettingsManager.Load());
    }

    public void SaveBinding(BindingConfig binding, bool logHistory = true)
    {
        BindingConfigManager.Save(binding);
        Binding = binding;
        BindingDecryptionFailed = false;
        if (logHistory)
        {
            HistoryLogManager.Append("Binding", T("InfoBoundTitle"), F("InfoBound", binding.MaskedIdentity), binding);
        }

        BindingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearBinding()
    {
        var previousBinding = Binding;
        BindingConfigManager.Delete();
        Binding = null;
        BindingDecryptionFailed = false;
        HistoryLogManager.Append("Binding", T("InfoBindingClearedTitle"), T("InfoBindingCleared"), previousBinding);
        BindingChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveSettings(AppSettings settings)
    {
        AppSettingsManager.Save(settings);
        var oldLanguage = Language;
        Settings = NormalizeSettings(settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);

        if (!string.Equals(oldLanguage, Language, StringComparison.OrdinalIgnoreCase))
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetLanguage(string language)
    {
        SaveSettings(Settings with { Language = WinUiLocalizer.NormalizeLanguage(language) });
    }

    public void Notify(
        string title,
        string message,
        AppNotificationKind kind = AppNotificationKind.Information,
        BindingConfig? binding = null)
    {
        HistoryLogManager.Append(kind.ToString(), title, message, binding);
        NotificationRaised?.Invoke(this, new AppNotificationEventArgs(title, message, kind));
    }

    public void Dispose()
    {
        Monitor.Dispose();
    }

    public string T(string key) => WinUiLocalizer.Text(Language, key);

    public string F(string key, params object[] args) => WinUiLocalizer.Format(Language, key, args);

    private static AppSettings NormalizeSettings(AppSettings settings)
    {
        return settings with
        {
            Language = WinUiLocalizer.NormalizeLanguage(settings.Language),
            ScanSeconds = Math.Clamp(settings.ScanSeconds, 1, 3600),
            MissingSeconds = Math.Clamp(settings.MissingSeconds, 1, 3600),
            MinRssi = Math.Clamp(settings.MinRssi, -120, 0),
            PauseUntilUtc = settings.PauseUntilUtc is { } pauseUntil && pauseUntil <= DateTimeOffset.UtcNow
                ? null
                : settings.PauseUntilUtc,
            TrustedWifiSsids = NormalizeTrustedWifi(settings)
        };
    }

    private static string[] NormalizeTrustedWifi(AppSettings settings)
    {
        return (settings.TrustedWifiSsids ?? [])
            .Concat([settings.TrustedWifiSsid])
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed record AppNotificationEventArgs(string Title, string Message, AppNotificationKind Kind);

public enum AppNotificationKind
{
    Information,
    Success,
    Warning,
    Error
}
