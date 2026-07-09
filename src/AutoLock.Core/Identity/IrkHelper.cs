using System.Security.Cryptography;

namespace AutoLock.Core;

public static class IrkHelper
{
    public static string Normalize(string irk)
    {
        if (string.IsNullOrWhiteSpace(irk))
        {
            return string.Empty;
        }

        var hexCandidate = irk.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace("<", "", StringComparison.Ordinal)
            .Replace(">", "", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();

        if (hexCandidate.Length == 32 && IsHex(hexCandidate))
        {
            return hexCandidate;
        }

        var base64Candidate = irk.Replace("base64:", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.Ordinal)
            .Replace("\t", "", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Trim();

        if (Convert.TryFromBase64String(base64Candidate, new byte[16], out var bytesWritten) && bytesWritten == 16)
        {
            var bytes = Convert.FromBase64String(base64Candidate);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        return hexCandidate;
    }

    public static bool IsValidOrEmpty(string irk)
    {
        if (string.IsNullOrWhiteSpace(irk))
        {
            return true;
        }

        if (irk.Length != 32)
        {
            return false;
        }

        try
        {
            Convert.FromHexString(irk);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool ResolvesWithIrk(string address, string irk)
    {
        irk = Normalize(irk);
        var compact = AddressFormatter.Normalize(address);

        if (!IsValidOrEmpty(irk) || string.IsNullOrWhiteSpace(irk) || compact.Length != 12)
        {
            return false;
        }

        var addressBytes = Convert.FromHexString(compact);
        var keyCandidates = BuildKeyCandidates(Convert.FromHexString(irk)).ToArray();

        foreach (var addressCandidate in BuildAddressCandidates(addressBytes))
        {
            foreach (var key in keyCandidates)
            {
                using var aes = Aes.Create();
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;

                foreach (var block in BuildBlocks(addressCandidate.Prand))
                {
                    var encrypted = aes.EncryptEcb(block, PaddingMode.None);
                    var candidateHashes = new[]
                    {
                        encrypted[..3],
                        encrypted[^3..],
                        encrypted[..3].Reverse().ToArray(),
                        encrypted[^3..].Reverse().ToArray()
                    };

                    if (addressCandidate.Hashes.Any(target => candidateHashes.Any(candidate => Matches(target, candidate))))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<byte[]> BuildKeyCandidates(byte[] key)
    {
        yield return key;
        yield return key.Reverse().ToArray();
    }

    private static IEnumerable<RpaCandidate> BuildAddressCandidates(byte[] address)
    {
        var reversedAddress = address.Reverse().ToArray();

        foreach (var candidate in BuildAddressCandidatesCore(address))
        {
            yield return candidate;
        }

        foreach (var candidate in BuildAddressCandidatesCore(reversedAddress))
        {
            yield return candidate;
        }
    }

    private static IEnumerable<RpaCandidate> BuildAddressCandidatesCore(byte[] address)
    {
        var first = address[..3];
        var last = address[3..];

        yield return new RpaCandidate(
            BuildHashCandidates(last),
            first);

        yield return new RpaCandidate(
            BuildHashCandidates(first),
            last);
    }

    private static byte[][] BuildHashCandidates(byte[] hash)
    {
        return
        [
            hash,
            hash.Reverse().ToArray()
        ];
    }

    private static IEnumerable<byte[]> BuildBlocks(byte[] prand)
    {
        var reversedPrand = prand.Reverse().ToArray();
        yield return PadLeft(prand);
        yield return PadRight(prand);
        yield return PadLeft(reversedPrand);
        yield return PadRight(reversedPrand);
    }

    private static byte[] PadLeft(byte[] value)
    {
        var block = new byte[16];
        value.CopyTo(block, 13);
        return block;
    }

    private static byte[] PadRight(byte[] value)
    {
        var block = new byte[16];
        value.CopyTo(block, 0);
        return block;
    }

    private static bool Matches(byte[] left, byte[] right)
    {
        return left.SequenceEqual(right);
    }

    private static bool IsHex(string value)
    {
        return value.All(Uri.IsHexDigit);
    }

    private sealed record RpaCandidate(byte[][] Hashes, byte[] Prand);
}
