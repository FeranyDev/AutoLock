using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoLock.Core;

public sealed record HistoryLogEntry(
    DateTimeOffset TimestampUtc,
    string Kind,
    string Title,
    string Message,
    string? DeviceIdentityKind = null,
    string? DeviceIdentity = null)
{
    [JsonIgnore]
    public string TimestampLocal => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string DisplayTitle => DeviceIdentityFormatter.MaskSensitiveText(Title);

    [JsonIgnore]
    public string DisplayMessage => DeviceIdentityFormatter.MaskSensitiveText(Message);

    [JsonIgnore]
    public string DeviceIdentityDisplay => string.IsNullOrWhiteSpace(DeviceIdentity)
        ? string.Empty
        : $"{DeviceIdentityKind}  {DeviceIdentity}";
}

public static class HistoryLogManager
{
    private const int MaxEntries = 300;

    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoLock");

    private static readonly string HistoryPath = Path.Combine(AppDir, "history.json");

    public static string HistoryFilePath => HistoryPath;

    public static IReadOnlyList<HistoryLogEntry> Load()
    {
        if (!File.Exists(HistoryPath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<HistoryLogEntry>>(File.ReadAllText(HistoryPath), JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static void Append(string kind, string title, string message, BindingConfig? binding = null)
    {
        var identityKind = binding?.IdentityKind;
        var identity = binding?.MaskedIdentity;
        var entries = Load()
            .Prepend(new HistoryLogEntry(
                DateTimeOffset.UtcNow,
                kind,
                title,
                DeviceIdentityFormatter.MaskSensitiveText(message),
                identityKind,
                identity))
            .Take(MaxEntries)
            .ToList();

        Directory.CreateDirectory(AppDir);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    public static void Clear()
    {
        if (File.Exists(HistoryPath))
        {
            File.Delete(HistoryPath);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
