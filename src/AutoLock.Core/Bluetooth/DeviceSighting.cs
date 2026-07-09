using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoLock.Core;

public sealed class DeviceSighting : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private short _rssi;
    private int _seenCount;
    private bool _hasAppleManufacturerData;
    private bool _resolvesWithIrk;
    private DateTimeOffset _lastSeen;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Address { get; init; }

    public string DisplayAddress => AddressFormatter.Display(Address);

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public short Rssi
    {
        get => _rssi;
        set => SetField(ref _rssi, value);
    }

    public int SeenCount
    {
        get => _seenCount;
        set => SetField(ref _seenCount, value);
    }

    public bool HasAppleManufacturerData
    {
        get => _hasAppleManufacturerData;
        set => SetField(ref _hasAppleManufacturerData, value);
    }

    public bool ResolvesWithIrk
    {
        get => _resolvesWithIrk;
        set => SetField(ref _resolvesWithIrk, value);
    }

    public DateTimeOffset LastSeen
    {
        get => _lastSeen;
        set
        {
            if (SetField(ref _lastSeen, value))
            {
                OnPropertyChanged(nameof(LastSeenLocal));
            }
        }
    }

    public string LastSeenLocal => LastSeen.ToLocalTime().ToString("T");

    public void UpdateFrom(DeviceSighting sighting)
    {
        Name = string.IsNullOrWhiteSpace(sighting.Name) ? Name : sighting.Name;
        Rssi = sighting.Rssi;
        SeenCount += 1;
        HasAppleManufacturerData = HasAppleManufacturerData || sighting.HasAppleManufacturerData;
        ResolvesWithIrk = ResolvesWithIrk || sighting.ResolvesWithIrk;
        LastSeen = sighting.LastSeen;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
