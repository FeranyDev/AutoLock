using System.Text.Json.Serialization;

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
    [JsonIgnore]
    public string DisplayAddress => AddressFormatter.Display(Address);

    [JsonIgnore]
    public bool UsesIrk => DeviceIdentityFormatter.UsesIrk(this);

    [JsonIgnore]
    public string IdentityKind => DeviceIdentityFormatter.GetKind(this);

    [JsonIgnore]
    public string MaskedIdentity => DeviceIdentityFormatter.Mask(this);
}
