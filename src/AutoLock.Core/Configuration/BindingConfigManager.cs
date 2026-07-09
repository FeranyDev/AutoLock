using System.IO;
using System.Text.Json;

namespace AutoLock.Core;

public static class BindingConfigManager
{
    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoLock");

    private static readonly string ConfigPath = Path.Combine(AppDir, "config.json");

    public static string ConfigFilePath => ConfigPath;

    public static BindingConfig? Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return null;
        }

        return JsonSerializer.Deserialize<BindingConfig>(File.ReadAllText(ConfigPath), JsonOptions);
    }

    public static void Save(BindingConfig binding)
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(binding, JsonOptions));
    }

    public static void Delete()
    {
        if (File.Exists(ConfigPath))
        {
            File.Delete(ConfigPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
