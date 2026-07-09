using System.IO;
using System.Text.Json;

namespace AutoLock.Core;

public sealed record HistoryLogEntry(
    DateTimeOffset TimestampUtc,
    string Kind,
    string Title,
    string Message)
{
    public string TimestampLocal => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
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

    public static void Append(string kind, string title, string message)
    {
        var entries = Load()
            .Prepend(new HistoryLogEntry(DateTimeOffset.UtcNow, kind, title, message))
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
