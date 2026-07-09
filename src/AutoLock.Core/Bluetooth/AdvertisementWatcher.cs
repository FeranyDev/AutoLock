using Windows.Devices.Bluetooth.Advertisement;

namespace AutoLock.Core;

public sealed class AdvertisementWatcher : IDisposable
{
    private readonly Action<DeviceSighting> _onAdvertisement;
    private readonly BluetoothLEAdvertisementWatcher _watcher = new()
    {
        ScanningMode = BluetoothLEScanningMode.Active
    };

    public AdvertisementWatcher(Action<DeviceSighting> onAdvertisement)
    {
        _onAdvertisement = onAdvertisement;
        _watcher.Received += OnReceived;
    }

    public void Start() => _watcher.Start();

    public void Dispose()
    {
        _watcher.Stop();
        _watcher.Received -= OnReceived;
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var address = $"{args.BluetoothAddress:X12}";
        var name = args.Advertisement.LocalName ?? string.Empty;
        var hasManufacturerData = args.Advertisement.ManufacturerData.Any();

        _onAdvertisement(new DeviceSighting
        {
            Address = address,
            Name = name,
            Rssi = args.RawSignalStrengthInDBm,
            SeenCount = 1,
            HasAppleManufacturerData = hasManufacturerData,
            LastSeen = DateTimeOffset.UtcNow
        });
    }
}
