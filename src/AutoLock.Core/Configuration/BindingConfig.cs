namespace AutoLock.Core;

public sealed record BindingConfig(
    int Version,
    DateTimeOffset BoundAt,
    string Address,
    string Name,
    bool HasAppleManufacturerData,
    int MissingSeconds,
    int CooldownSeconds,
    int? MinRssi,
    string? Irk)
{
    public string DisplayAddress => AddressFormatter.Display(Address);
}
