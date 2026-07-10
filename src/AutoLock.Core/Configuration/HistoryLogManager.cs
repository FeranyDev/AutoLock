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
    private const int MaxEntries = 100;

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
            var entries = JsonSerializer.Deserialize<List<HistoryLogEntry>>(File.ReadAllText(HistoryPath), JsonOptions) ?? [];
            if (entries.Count <= MaxEntries)
            {
                return entries;
            }

            var trimmedEntries = entries.Take(MaxEntries).ToList();
            // Keep the in-memory limit even when an existing history file cannot be rewritten.
            try
            {
                SaveEntries(trimmedEntries);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return trimmedEntries;
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

        SaveEntries(entries);
    }

    public static void Clear()
    {
        if (File.Exists(HistoryPath))
        {
            File.Delete(HistoryPath);
        }
    }

    private static void SaveEntries(IReadOnlyCollection<HistoryLogEntry> entries)
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(entries, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
}
