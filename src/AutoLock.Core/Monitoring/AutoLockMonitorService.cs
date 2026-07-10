namespace AutoLock.Core;

public sealed class AutoLockMonitorService : IDisposable
{
    private readonly object _syncRoot = new();
    private AdvertisementWatcher? _monitorWatcher;
    private Timer? _monitorTimer;
    private CancellationTokenSource? _startupAutoScanCts;
    private BindingConfig? _binding;
    private MonitorOptions _options = new(MissingSeconds: 30, MinRssi: -90, DryRun: false);
    private string _activeIrk = string.Empty;
    private DateTimeOffset _lastSeen = DateTimeOffset.MinValue;
    private DateTimeOffset _lastLock = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSuppressionNotice = DateTimeOffset.MinValue;
    private DateTimeOffset? _pauseUntilUtc;

    public event EventHandler<DeviceSightingEventArgs>? ScanDeviceSeen;
    public event EventHandler<MonitoringStateChangedEventArgs>? MonitoringStateChanged;
    public event EventHandler<MonitorDeviceSeenEventArgs>? MonitorDeviceSeen;
    public event EventHandler<WeakSignalEventArgs>? WeakSignalIgnored;
    public event EventHandler<MissingSignalEventArgs>? MissingSignalChanged;
    public event EventHandler? DryRunWouldLock;
    public event EventHandler<LockFailedEventArgs>? LockFailed;
    public event EventHandler? LockSucceededAndPaused;
    public event EventHandler<LockSuppressedEventArgs>? LockSuppressed;

    public bool IsMonitoring
    {
        get
        {
            lock (_syncRoot)
            {
                return _monitorWatcher is not null;
            }
        }
    }

    public bool ResumeMonitorAfterUnlock { get; private set; }

    public async Task ScanAsync(TimeSpan duration, string irk, CancellationToken cancellationToken = default)
    {
        using var watcher = new AdvertisementWatcher(sighting =>
        {
            if (IrkHelper.IsValidOrEmpty(irk) && !string.IsNullOrWhiteSpace(irk))
            {
                sighting.ResolvesWithIrk = IrkHelper.ResolvesWithIrk(sighting.Address, irk);
            }

            ScanDeviceSeen?.Invoke(this, new DeviceSightingEventArgs(sighting));
        });

        watcher.Start();

        try
        {
            await Task.Delay(duration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task<DeviceSighting?> ScanForIrkMatchAsync(
        string irk,
        TimeSpan duration,
        int minRssi,
        CancellationToken cancellationToken = default)
    {
        CancelStartupAutoScan();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startupAutoScanCts = linkedCts;
        var match = new TaskCompletionSource<DeviceSighting?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watcher = new AdvertisementWatcher(sighting =>
        {
            var resolves = IrkHelper.ResolvesWithIrk(sighting.Address, irk);
            sighting.ResolvesWithIrk = resolves;
            ScanDeviceSeen?.Invoke(this, new DeviceSightingEventArgs(sighting));

            if (resolves && sighting.Rssi >= minRssi)
            {
                match.TrySetResult(sighting);
            }
        });

        try
        {
            watcher.Start();
            var delay = Task.Delay(duration, linkedCts.Token);
            var completed = await Task.WhenAny(match.Task, delay);
            return completed == match.Task ? await match.Task : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_startupAutoScanCts, linkedCts))
            {
                _startupAutoScanCts = null;
            }

            linkedCts.Dispose();
        }
    }

    public async Task<DeviceSighting?> ScanUntilBindingMatchAsync(
        BindingConfig binding,
        string activeIrk,
        int minRssi,
        CancellationToken cancellationToken = default)
    {
        CancelStartupAutoScan();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _startupAutoScanCts = linkedCts;
        activeIrk = string.IsNullOrWhiteSpace(activeIrk)
            ? IrkHelper.Normalize(binding.Irk ?? string.Empty)
            : activeIrk;
        var match = new TaskCompletionSource<DeviceSighting?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var watcher = new AdvertisementWatcher(sighting =>
        {
            var isAddressMatch = string.Equals(sighting.Address, binding.Address, StringComparison.OrdinalIgnoreCase);
            var isIrkMatch = !isAddressMatch && IrkHelper.ResolvesWithIrk(sighting.Address, activeIrk);
            sighting.ResolvesWithIrk = isIrkMatch;
            ScanDeviceSeen?.Invoke(this, new DeviceSightingEventArgs(sighting));

            if ((isAddressMatch || isIrkMatch) && sighting.Rssi >= minRssi)
            {
                match.TrySetResult(sighting);
            }
        });

        try
        {
            watcher.Start();
            return await match.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_startupAutoScanCts, linkedCts))
            {
                _startupAutoScanCts = null;
            }

            linkedCts.Dispose();
        }
    }

    public void CancelStartupAutoScan()
    {
        _startupAutoScanCts?.Cancel();
        _startupAutoScanCts = null;
    }

    public void StartMonitor(BindingConfig binding, string activeIrk, MonitorOptions options)
    {
        CancelStartupAutoScan();
        lock (_syncRoot)
        {
            _binding = binding;
            _options = options;
            _pauseUntilUtc = options.PauseUntilUtc;
            _activeIrk = string.IsNullOrWhiteSpace(activeIrk)
                ? IrkHelper.Normalize(binding.Irk ?? string.Empty)
                : activeIrk;
            _lastSeen = DateTimeOffset.UtcNow;
            ResumeMonitorAfterUnlock = false;

            _monitorWatcher?.Dispose();
            _monitorWatcher = new AdvertisementWatcher(OnMonitorAdvertisement);
            _monitorWatcher.Start();

            _monitorTimer?.Dispose();
            _monitorTimer = new Timer(EvaluateMissingSignal, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        MonitoringStateChanged?.Invoke(this, new MonitoringStateChangedEventArgs(true));
    }

    public void StopMonitor()
    {
        CancelStartupAutoScan();
        lock (_syncRoot)
        {
            StopMonitorCore();
            ResumeMonitorAfterUnlock = false;
        }

        MonitoringStateChanged?.Invoke(this, new MonitoringStateChangedEventArgs(false));
    }

    public void ResetTimeout()
    {
        lock (_syncRoot)
        {
            _lastSeen = DateTimeOffset.UtcNow;
        }
    }

    public void SetPauseUntil(DateTimeOffset? pauseUntilUtc)
    {
        lock (_syncRoot)
        {
            _pauseUntilUtc = pauseUntilUtc?.ToUniversalTime();
            _options = _options with { PauseUntilUtc = _pauseUntilUtc };
            _lastSeen = DateTimeOffset.UtcNow;
        }
    }

    public void UpdateRuntimeOptions(
        int missingSeconds,
        int minRssi,
        bool dryRun,
        bool disableOnExternalPower,
        DateTimeOffset? pauseUntilUtc,
        string[]? trustedWifiSsids)
    {
        lock (_syncRoot)
        {
            _pauseUntilUtc = pauseUntilUtc?.ToUniversalTime();
            _options = _options with
            {
                MissingSeconds = Math.Clamp(missingSeconds, 1, 3600),
                MinRssi = Math.Clamp(minRssi, -120, 0),
                DryRun = dryRun,
                DisableOnExternalPower = disableOnExternalPower,
                PauseUntilUtc = _pauseUntilUtc,
                TrustedWifiSsids = NormalizeTrustedWifi(trustedWifiSsids)
            };
            _lastSeen = DateTimeOffset.UtcNow;
        }
    }

    public void Dispose()
    {
        CancelStartupAutoScan();
        lock (_syncRoot)
        {
            StopMonitorCore();
        }
    }

    private void OnMonitorAdvertisement(DeviceSighting sighting)
    {
        BindingConfig? binding;
        string activeIrk;
        int minRssi;

        lock (_syncRoot)
        {
            binding = _binding;
            activeIrk = _activeIrk;
            minRssi = _options.MinRssi;
        }

        if (binding is null)
        {
            return;
        }

        var isMatch = string.Equals(sighting.Address, binding.Address, StringComparison.OrdinalIgnoreCase);
        var irkMatch = !isMatch && IrkHelper.ResolvesWithIrk(sighting.Address, activeIrk);

        if (!isMatch && !irkMatch)
        {
            return;
        }

        if (sighting.Rssi < minRssi)
        {
            WeakSignalIgnored?.Invoke(this, new WeakSignalEventArgs(sighting.Rssi, minRssi));
            return;
        }

        lock (_syncRoot)
        {
            _lastSeen = DateTimeOffset.UtcNow;
        }

        MonitorDeviceSeen?.Invoke(this, new MonitorDeviceSeenEventArgs(sighting, irkMatch));
    }

    private void EvaluateMissingSignal(object? state)
    {
        BindingConfig? binding;
        MonitorOptions options;
        DateTimeOffset lastSeen;
        DateTimeOffset lastLock;
        DateTimeOffset? pauseUntilUtc;

        lock (_syncRoot)
        {
            binding = _binding;
            options = _options;
            lastSeen = _lastSeen;
            lastLock = _lastLock;
            pauseUntilUtc = _pauseUntilUtc;
        }

        if (binding is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var missingFor = now - lastSeen;
        MissingSignalChanged?.Invoke(this, new MissingSignalEventArgs(Math.Max(0, (int)missingFor.TotalSeconds)));

        if (missingFor.TotalSeconds < options.MissingSeconds)
        {
            return;
        }

        if (pauseUntilUtc is not null && now < pauseUntilUtc.Value)
        {
            SuppressLock(LockSuppressionReason.Paused, now, pauseUntilUtc);
            return;
        }

        if (options.DisableOnExternalPower && PowerStatusService.IsExternalPowerConnected())
        {
            SuppressLock(LockSuppressionReason.ExternalPower, now, null);
            return;
        }

        if (WifiStatusService.IsConnectedToTrustedWifi(options.TrustedWifiSsids))
        {
            SuppressLock(LockSuppressionReason.TrustedWifi, now, null);
            return;
        }

        if ((now - lastLock).TotalSeconds < binding.CooldownSeconds)
        {
            return;
        }

        lock (_syncRoot)
        {
            _lastLock = now;
        }

        if (options.DryRun)
        {
            DryRunWouldLock?.Invoke(this, EventArgs.Empty);
            return;
        }

        var result = LockService.LockWorkStation();
        if (!result.Succeeded)
        {
            LockFailed?.Invoke(this, new LockFailedEventArgs(result));
            return;
        }

        lock (_syncRoot)
        {
            ResumeMonitorAfterUnlock = true;
            StopMonitorCore();
        }

        MonitoringStateChanged?.Invoke(this, new MonitoringStateChangedEventArgs(false));
        LockSucceededAndPaused?.Invoke(this, EventArgs.Empty);
    }

    private void StopMonitorCore()
    {
        _monitorTimer?.Dispose();
        _monitorTimer = null;
        _monitorWatcher?.Dispose();
        _monitorWatcher = null;
    }

    private static string[] NormalizeTrustedWifi(IEnumerable<string>? trustedWifiSsids)
    {
        return (trustedWifiSsids ?? [])
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void SuppressLock(LockSuppressionReason reason, DateTimeOffset now, DateTimeOffset? untilUtc)
    {
        var shouldNotify = false;
        lock (_syncRoot)
        {
            _lastSeen = now;
            if ((now - _lastSuppressionNotice).TotalSeconds >= 30)
            {
                _lastSuppressionNotice = now;
                shouldNotify = true;
            }
        }

        if (shouldNotify)
        {
            LockSuppressed?.Invoke(this, new LockSuppressedEventArgs(reason, untilUtc));
        }
    }
}

public sealed record MonitorOptions(
    int MissingSeconds,
    int MinRssi,
    bool DryRun,
    bool DisableOnExternalPower = false,
    DateTimeOffset? PauseUntilUtc = null,
    string[]? TrustedWifiSsids = null);

public sealed class MonitoringStateChangedEventArgs(bool isMonitoring) : EventArgs
{
    public bool IsMonitoring { get; } = isMonitoring;
}

public sealed class DeviceSightingEventArgs(DeviceSighting sighting) : EventArgs
{
    public DeviceSighting Sighting { get; } = sighting;
}

public sealed class MonitorDeviceSeenEventArgs(DeviceSighting sighting, bool isIrkMatch) : EventArgs
{
    public DeviceSighting Sighting { get; } = sighting;

    public bool IsIrkMatch { get; } = isIrkMatch;
}

public sealed class WeakSignalEventArgs(short rssi, int minRssi) : EventArgs
{
    public short Rssi { get; } = rssi;

    public int MinRssi { get; } = minRssi;
}

public sealed class MissingSignalEventArgs(int missingSeconds) : EventArgs
{
    public int MissingSeconds { get; } = missingSeconds;
}

public sealed class LockFailedEventArgs(LockResult result) : EventArgs
{
    public LockResult Result { get; } = result;
}

public enum LockSuppressionReason
{
    Paused,
    ExternalPower,
    TrustedWifi
}

public sealed class LockSuppressedEventArgs(LockSuppressionReason reason, DateTimeOffset? untilUtc) : EventArgs
{
    public LockSuppressionReason Reason { get; } = reason;

    public DateTimeOffset? UntilUtc { get; } = untilUtc;
}
