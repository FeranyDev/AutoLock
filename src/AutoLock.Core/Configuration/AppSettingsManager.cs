using System.IO;
using System.Text.Json;

namespace AutoLock.Core;

public sealed record AppSettings(
    bool RunInBackground,
    string Language = "zh-CN",
    int ScanSeconds = 8,
    int MissingSeconds = 30,
    int MinRssi = -90,
    bool DryRun = false,
    bool DisableOnExternalPower = false,
    DateTimeOffset? PauseUntilUtc = null,
    string TrustedWifiSsid = "",
    string[]? TrustedWifiSsids = null)
{
    public static AppSettings Default { get; } = new(
        RunInBackground: false,
        Language: "zh-CN",
        ScanSeconds: 8,
        MissingSeconds: 30,
        MinRssi: -90,
        DryRun: false,
        DisableOnExternalPower: false,
        PauseUntilUtc: null,
        TrustedWifiSsid: "",
        TrustedWifiSsids: []);
}

public static class AppSettingsManager
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoLock");

    private static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");

    public static string AppDirectory => AppDir;

    public static string SettingsFilePath => SettingsPath;

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return AppSettings.Default;
        }

        return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? AppSettings.Default;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
