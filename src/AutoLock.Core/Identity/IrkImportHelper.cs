using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AutoLock.Core;

public sealed record IrkRegistryRecord(string Section, IReadOnlyList<string> AddressCandidates, string Irk);

public static partial class IrkImportHelper
{
    public static IReadOnlyList<IrkRegistryRecord> ParseRegistryExport(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var records = new List<IrkRegistryRecord>();
        var section = string.Empty;
        var addressBytes = Array.Empty<byte>();
        var irk = string.Empty;

        void CommitSection()
        {
            if (!string.IsNullOrWhiteSpace(irk))
            {
                var candidates = BuildAddressCandidates(section, addressBytes);
                records.Add(new IrkRegistryRecord(section, candidates, irk));
            }

            irk = string.Empty;
            addressBytes = [];
        }

        foreach (var line in BuildLogicalRegistryLines(content))
        {
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                CommitSection();
                section = line[1..^1];
                continue;
            }

            var valueMatch = RegistryValueRegex().Match(line);
            if (!valueMatch.Success)
            {
                continue;
            }

            var name = valueMatch.Groups["name"].Value;
            var bytes = ParseRegistryBytes(valueMatch.Groups["data"].Value);
            if (name.Equals("Address", StringComparison.OrdinalIgnoreCase))
            {
                addressBytes = bytes;
            }
            else if (name.Equals("IRK", StringComparison.OrdinalIgnoreCase) && bytes.Length == 16)
            {
                irk = Convert.ToHexString(bytes).ToLowerInvariant();
            }
        }

        CommitSection();
        return records;
    }

    public static IReadOnlyList<IrkRegistryRecord> FindRegistryRecords(string content, string? bluetoothAddress)
    {
        var records = ParseRegistryExport(content);
        var target = AddressFormatter.Normalize(bluetoothAddress ?? string.Empty);
        if (string.IsNullOrWhiteSpace(target))
        {
            return records;
        }

        return records
            .Where(record => record.AddressCandidates.Any(candidate =>
                string.Equals(AddressFormatter.Normalize(candidate), target, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    public static bool TryExtractKeychainIrk(string content, out string irk)
    {
        irk = NormalizeValidIrk(content);
        if (!string.IsNullOrWhiteSpace(irk))
        {
            return true;
        }

        if (TryDecodeBase64Text(content, out var decoded) &&
            !string.Equals(decoded, content, StringComparison.Ordinal) &&
            TryExtractKeychainIrk(decoded, out irk))
        {
            return true;
        }

        try
        {
            var document = XDocument.Parse(content, LoadOptions.None);
            var keys = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("key", StringComparison.OrdinalIgnoreCase));

            foreach (var key in keys)
            {
                if (!NormalizeLabel(key.Value).Equals("remoteirk", StringComparison.Ordinal))
                {
                    continue;
                }

                var valueElement = key.ElementsAfterSelf().FirstOrDefault();
                irk = NormalizeValidIrk(valueElement?.Value ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(irk))
                {
                    return true;
                }
            }

            var dataValues = document.Descendants()
                .Where(element => element.Name.LocalName.Equals("data", StringComparison.OrdinalIgnoreCase))
                .Select(element => NormalizeValidIrk(element.Value))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (dataValues.Length == 1)
            {
                irk = dataValues[0];
                return true;
            }
        }
        catch (System.Xml.XmlException)
        {
        }

        var remoteIrkMatch = RemoteIrkDataRegex().Match(content);
        if (remoteIrkMatch.Success)
        {
            irk = NormalizeValidIrk(remoteIrkMatch.Groups["value"].Value);
            return !string.IsNullOrWhiteSpace(irk);
        }

        return false;
    }

    private static IEnumerable<string> BuildLogicalRegistryLines(string content)
    {
        var pending = new StringBuilder();
        foreach (var rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (pending.Length > 0)
            {
                pending.Append(line);
            }
            else
            {
                pending.Append(line);
            }

            if (line.EndsWith('\\'))
            {
                pending.Length--;
                continue;
            }

            yield return pending.ToString();
            pending.Clear();
        }

        if (pending.Length > 0)
        {
            yield return pending.ToString();
        }
    }

    private static byte[] ParseRegistryBytes(string data)
    {
        var values = data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var bytes = new List<byte>(values.Length);
        foreach (var value in values)
        {
            if (byte.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var parsed))
            {
                bytes.Add(parsed);
            }
        }

        return bytes.ToArray();
    }

    private static IReadOnlyList<string> BuildAddressCandidates(string section, byte[] bytes)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in section.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.Length == 12 && segment.All(Uri.IsHexDigit))
            {
                AddAddressCandidate(candidates, segment);
            }
        }

        for (var offset = 0; offset + 6 <= bytes.Length; offset++)
        {
            var value = bytes.AsSpan(offset, 6).ToArray();
            AddAddressCandidate(candidates, Convert.ToHexString(value));
            Array.Reverse(value);
            AddAddressCandidate(candidates, Convert.ToHexString(value));
        }

        if (bytes.Length >= sizeof(ulong))
        {
            var littleEndian = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            AddAddressCandidate(candidates, (littleEndian & 0x0000FFFFFFFFFFFFUL).ToString("X12"));
            var bigEndian = BinaryPrimitives.ReadUInt64BigEndian(bytes);
            AddAddressCandidate(candidates, (bigEndian & 0x0000FFFFFFFFFFFFUL).ToString("X12"));
        }

        return candidates.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddAddressCandidate(ISet<string> candidates, string value)
    {
        var normalized = AddressFormatter.Normalize(value);
        if (normalized.Length == 12)
        {
            candidates.Add(AddressFormatter.Display(normalized));
        }
    }

    private static string NormalizeValidIrk(string value)
    {
        var normalized = IrkHelper.Normalize(value);
        return !string.IsNullOrWhiteSpace(normalized) && IrkHelper.IsValidOrEmpty(normalized)
            ? normalized
            : string.Empty;
    }

    private static bool TryDecodeBase64Text(string value, out string decoded)
    {
        decoded = string.Empty;
        var compact = string.Concat(value.Where(character => !char.IsWhiteSpace(character)));
        try
        {
            var bytes = Convert.FromBase64String(compact);
            if (bytes.Length <= 16)
            {
                return false;
            }

            decoded = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeLabel(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    [GeneratedRegex("^\\\"(?<name>[^\\\"]+)\\\"=hex(?:\\([0-9a-fA-F]+\\))?:(?<data>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex RegistryValueRegex();

    [GeneratedRegex("(?is)remote\\s*irk.*?<data[^>]*>(?<value>[^<]+)</data>", RegexOptions.CultureInvariant)]
    private static partial Regex RemoteIrkDataRegex();
}
