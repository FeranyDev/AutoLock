using System.Collections.ObjectModel;
using AutoLock.Core;
using AutoLock_WinUI.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoLock_WinUI.Pages;

public sealed partial class ScanDevicesPage : Page
{
    private readonly ObservableCollection<DeviceSighting> _devices = new();
    private bool _eventsSubscribed;

    public ScanDevicesPage()
    {
        InitializeComponent();
        DevicesList.ItemsSource = _devices;
        Loaded += ScanDevicesPage_Loaded;
        Unloaded += ScanDevicesPage_Unloaded;
    }

    private void ScanDevicesPage_Loaded(object sender, RoutedEventArgs e)
    {
        SubscribeMonitorEvents();
        ApplyText();
        ScanSecondsBox.Value = App.State.Settings.ScanSeconds;
        SetInfo(App.State.T("StatusReady"), App.State.T("InfoScanReady"), InfoBarSeverity.Informational);
    }

    private void ScanDevicesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeMonitorEvents();
    }

    private void SubscribeMonitorEvents()
    {
        if (_eventsSubscribed)
        {
            return;
        }

        App.State.Monitor.ScanDeviceSeen += Monitor_ScanDeviceSeen;
        App.State.LanguageChanged += AppState_LanguageChanged;
        _eventsSubscribed = true;
    }

    private void UnsubscribeMonitorEvents()
    {
        if (!_eventsSubscribed)
        {
            return;
        }

        App.State.Monitor.ScanDeviceSeen -= Monitor_ScanDeviceSeen;
        App.State.LanguageChanged -= AppState_LanguageChanged;
        _eventsSubscribed = false;
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadScanSeconds(out var scanSeconds) || !TryReadIrk(out var irk))
        {
            return;
        }

        _devices.Clear();
        App.State.SaveSettings(App.State.Settings with { ScanSeconds = scanSeconds });
        SetBusy(true);
        SetInfo(App.State.T("InfoScanningTitle"), App.State.F("InfoScanning", scanSeconds), InfoBarSeverity.Informational);

        await App.State.Monitor.ScanAsync(TimeSpan.FromSeconds(scanSeconds), irk);

        SetBusy(false);
        HistoryLogManager.Append("Scan", App.State.T("InfoScanCompleteTitle"), App.State.F("InfoScanComplete", _devices.Count));
        SetInfo(App.State.T("InfoScanCompleteTitle"), App.State.F("InfoScanComplete", _devices.Count), InfoBarSeverity.Success);
    }

    private async void IrkAssistantButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IrkAssistantDialog
        {
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(dialog.ResultIrk))
        {
            return;
        }

        IrkBox.Password = dialog.ResultIrk;
        SetInfo(App.State.T("IrkAssistantAppliedTitle"), App.State.T("IrkAssistantApplied"), InfoBarSeverity.Success);
    }

    private void BindSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (DevicesList.SelectedItem is not DeviceSighting selected)
        {
            SetInfo(App.State.T("InfoSelectDeviceTitle"), App.State.T("InfoSelectDevice"), InfoBarSeverity.Warning);
            return;
        }

        if (!TryReadIrk(out var irk))
        {
            return;
        }

        var existing = App.State.Binding;
        var binding = new BindingConfig(
            Version: 1,
            BoundAt: DateTimeOffset.UtcNow,
            Address: selected.Address,
            Name: selected.Name,
            HasAppleManufacturerData: selected.HasAppleManufacturerData,
            MissingSeconds: existing?.MissingSeconds ?? App.State.Settings.MissingSeconds,
            CooldownSeconds: existing?.CooldownSeconds ?? 60,
            MinRssi: existing?.MinRssi ?? App.State.Settings.MinRssi,
            Irk: irk);

        App.State.SaveBinding(binding);
        SetInfo(App.State.T("InfoBoundTitle"), App.State.F("InfoBound", binding.MaskedIdentity), InfoBarSeverity.Success);
    }

    private void Monitor_ScanDeviceSeen(object? sender, DeviceSightingEventArgs e)
    {
        Enqueue(() => AddOrUpdateDevice(e.Sighting));
    }

    private void AddOrUpdateDevice(DeviceSighting sighting)
    {
        var existing = _devices.FirstOrDefault(x => x.Address == sighting.Address);
        if (existing is null)
        {
            _devices.Add(sighting);
            return;
        }

        existing.UpdateFrom(sighting);
    }

    private bool TryReadScanSeconds(out int seconds)
    {
        seconds = 0;
        if (double.IsNaN(ScanSecondsBox.Value) || ScanSecondsBox.Value < 1)
        {
            SetInfo(App.State.T("InfoInvalidTitle"), App.State.T("InfoScanSecondsInvalid"), InfoBarSeverity.Warning);
            return false;
        }

        seconds = (int)Math.Round(ScanSecondsBox.Value);
        return true;
    }

    private bool TryReadIrk(out string irk)
    {
        irk = IrkHelper.Normalize(IrkBox.Password);
        if (IrkHelper.IsValidOrEmpty(irk))
        {
            return true;
        }

        SetInfo(App.State.T("InfoIrkInvalidTitle"), App.State.T("InfoIrkInvalid"), InfoBarSeverity.Warning);
        return false;
    }

    private void SetBusy(bool isBusy)
    {
        ScanButton.IsEnabled = !isBusy;
        BindSelectedButton.IsEnabled = !isBusy;
        IrkAssistantButton.IsEnabled = !isBusy;
    }

    private void SetInfo(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void Enqueue(Action action)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        DispatcherQueue.TryEnqueue(() => action());
    }

    private void AppState_LanguageChanged(object? sender, EventArgs e)
    {
        Enqueue(ApplyText);
    }

    private void ApplyText()
    {
        PageTitleText.Text = App.State.T("NavScanDevices");
        PageSubtitleText.Text = App.State.T("PageScanSubtitle");
        ScanSecondsBox.Header = App.State.T("LabelScanSeconds");
        IrkBox.Header = App.State.T("LabelIrk");
        IrkBox.PlaceholderText = App.State.T("PlaceholderIrk");
        IrkAssistantButtonText.Text = App.State.T("ButtonGetIrk");
        ScanButtonText.Text = App.State.T("ButtonScan");
        BindSelectedButtonText.Text = App.State.T("ButtonBindSelected");
        DevicesSectionText.Text = App.State.T("SectionDevices");
        BleText.Text = App.State.T("SectionBle");
        AddressColumnText.Text = App.State.T("ColumnAddress");
        RssiColumnText.Text = App.State.T("ColumnRssi");
        SeenColumnText.Text = App.State.T("ColumnSeen");
        AppleDataColumnText.Text = App.State.T("ColumnAppleData");
        IrkMatchColumnText.Text = App.State.T("ColumnIrkMatch");
        LastSeenColumnText.Text = App.State.T("ColumnLastSeen");
    }
}
