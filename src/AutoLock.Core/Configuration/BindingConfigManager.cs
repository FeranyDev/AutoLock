using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoLock.Core;

public static class BindingConfigManager
{
    private const string IrkProtectionScheme = "DPAPI.CurrentUser.v1";

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

        var document = JsonSerializer.Deserialize<BindingConfigDocument>(File.ReadAllText(ConfigPath), JsonOptions);
        if (document is null)
        {
            return null;
        }

        var (irk, migratePlaintext) = ReadIrk(document);
        var binding = new BindingConfig(
            document.Version,
            document.BoundAt,
            document.Address,
            document.Name,
            document.HasAppleManufacturerData,
            document.MissingSeconds,
            document.CooldownSeconds,
            document.MinRssi,
            irk);

        if (migratePlaintext)
        {
            Save(binding);
        }

        return binding;
    }

    public static void Save(BindingConfig binding)
    {
        var serializedBinding = SerializeForStorage(binding);

        Directory.CreateDirectory(AppDir);
        var temporaryPath = ConfigPath + ".tmp";
        File.WriteAllText(temporaryPath, serializedBinding);
        File.Move(temporaryPath, ConfigPath, overwrite: true);
    }

    private static string SerializeForStorage(BindingConfig binding)
    {
        var irk = IrkHelper.Normalize(binding.Irk ?? string.Empty);
        if (!IrkHelper.IsValidOrEmpty(irk))
        {
            throw new ArgumentException("IRK must be empty or a 32-character hexadecimal value.", nameof(binding));
        }

        var hasIrk = !string.IsNullOrWhiteSpace(irk);
        var document = new BindingConfigDocument
        {
            Version = binding.Version,
            BoundAt = binding.BoundAt,
            Address = binding.Address,
            Name = binding.Name,
            HasAppleManufacturerData = binding.HasAppleManufacturerData,
            MissingSeconds = binding.MissingSeconds,
            CooldownSeconds = binding.CooldownSeconds,
            MinRssi = binding.MinRssi,
            IrkProtection = hasIrk ? IrkProtectionScheme : null,
            ProtectedIrk = hasIrk ? CurrentUserDataProtector.ProtectString(irk) : null
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    public static void Delete()
    {
        if (File.Exists(ConfigPath))
        {
            File.Delete(ConfigPath);
        }

        var temporaryPath = ConfigPath + ".tmp";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }
    }

    private static (string Irk, bool MigratePlaintext) ReadIrk(BindingConfigDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.ProtectedIrk))
        {
            if (!string.Equals(document.IrkProtection, IrkProtectionScheme, StringComparison.Ordinal))
            {
                throw new CryptographicException("The IRK protection scheme is missing or unsupported.");
            }

            var irk = IrkHelper.Normalize(CurrentUserDataProtector.UnprotectString(document.ProtectedIrk));
            if (string.IsNullOrWhiteSpace(irk) || !IrkHelper.IsValidOrEmpty(irk))
            {
                throw new CryptographicException("The decrypted IRK is invalid.");
            }

            return (irk, false);
        }

        var plaintextIrk = IrkHelper.Normalize(document.Irk ?? string.Empty);
        var shouldMigrate = !string.IsNullOrWhiteSpace(plaintextIrk) && IrkHelper.IsValidOrEmpty(plaintextIrk);
        return (plaintextIrk, shouldMigrate);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class BindingConfigDocument
    {
        public int Version { get; init; }

        public DateTimeOffset BoundAt { get; init; }

        public string Address { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public bool HasAppleManufacturerData { get; init; }

        public int MissingSeconds { get; init; }

        public int CooldownSeconds { get; init; }

        public int? MinRssi { get; init; }

        public string? Irk { get; init; }

        public string? IrkProtection { get; init; }

        public string? ProtectedIrk { get; init; }
    }
}
