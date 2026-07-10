using System.Text.RegularExpressions;

namespace AutoLock.Core;

public static partial class DeviceIdentityFormatter
{
    public static bool UsesIrk(BindingConfig binding)
    {
        var irk = IrkHelper.Normalize(binding.Irk ?? string.Empty);
        return !string.IsNullOrWhiteSpace(irk) && IrkHelper.IsValidOrEmpty(irk);
    }

    public static string GetKind(BindingConfig binding) => UsesIrk(binding) ? "IRK" : "MAC";

    public static string Mask(BindingConfig binding) => UsesIrk(binding)
        ? MaskIrk(binding.Irk ?? string.Empty)
        : MaskMac(binding.Address);

    public static string MaskIrk(string irk)
    {
        var normalized = IrkHelper.Normalize(irk).ToUpperInvariant();
        if (normalized.Length != 32)
        {
            return "********";
        }

        return $"{normalized[..4]}********{normalized[^4..]}";
    }

    public static string MaskMac(string address)
    {
        var normalized = AddressFormatter.Normalize(address);
        if (normalized.Length != 12)
        {
            return "**:**:**:**:**:**";
        }

        return $"{normalized[..2]}:{normalized[2..4]}:**:**:{normalized[8..10]}:{normalized[10..12]}";
    }

    public static string MaskSensitiveText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var masked = MacAddressRegex().Replace(text, match => MaskMac(match.Value));
        return IrkRegex().Replace(masked, match => MaskIrk(match.Value));
    }

    [GeneratedRegex(@"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])")]
    private static partial Regex MacAddressRegex();

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{32}(?![0-9A-Fa-f])")]
    private static partial Regex IrkRegex();
}
