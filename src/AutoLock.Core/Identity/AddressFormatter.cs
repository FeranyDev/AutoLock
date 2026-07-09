namespace AutoLock.Core;

public static class AddressFormatter
{
    public static string Display(string address)
    {
        var compact = Normalize(address);
        return compact.Length == 12
            ? string.Join(":", Enumerable.Range(0, 6).Select(i => compact.Substring(i * 2, 2)))
            : address;
    }

    public static string Normalize(string address)
    {
        return address.Replace(":", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
    }
}
