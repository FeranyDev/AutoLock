using AutoLock.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace AutoLock_WinUI.Dialogs;

public sealed partial class IrkAssistantDialog : ContentDialog
{
    private const ulong MaximumRegistryExportBytes = 10 * 1024 * 1024;

    public IrkAssistantDialog()
    {
        InitializeComponent();
        ApplyText();
        IsPrimaryButtonEnabled = false;
    }

    public string ResultIrk { get; private set; } = string.Empty;

    private async void ImportRegistryButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".reg");

        if (MainWindow.ActiveWindow is null)
        {
            SetResultInfo(WindowsResultInfo, WindowsResultText, App.State.T("IrkAssistantWindowUnavailable"), InfoBarSeverity.Error);
            return;
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.ActiveWindow.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > MaximumRegistryExportBytes)
        {
            SetResultInfo(WindowsResultInfo, WindowsResultText, App.State.T("IrkAssistantFileTooLarge"), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var content = await FileIO.ReadTextAsync(file);
            var targetAddress = BluetoothAddressBox.Text.Trim();
            var records = IrkImportHelper.FindRegistryRecords(content, targetAddress);
            if (records.Count == 0)
            {
                SetResultInfo(WindowsResultInfo, WindowsResultText, App.State.T("IrkAssistantNoRegistryMatch"), InfoBarSeverity.Warning);
                return;
            }

            var distinctIrks = records.Select(record => record.Irk)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (distinctIrks.Length > 1)
            {
                SetResultInfo(WindowsResultInfo, WindowsResultText, App.State.T("IrkAssistantMultipleRegistryMatches"), InfoBarSeverity.Warning);
                return;
            }

            SetIrk(distinctIrks[0]);
            SetResultInfo(WindowsResultInfo, WindowsResultText, App.State.T("IrkAssistantRegistrySuccess"), InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            SetResultInfo(
                WindowsResultInfo,
                WindowsResultText,
                App.State.F("IrkAssistantReadFailed", exception.Message),
                InfoBarSeverity.Error);
        }
    }

    private void DecodeKeychainButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IrkImportHelper.TryExtractKeychainIrk(KeychainContentBox.Text, out var irk))
        {
            SetResultInfo(MacResultInfo, MacResultText, App.State.T("IrkAssistantKeychainInvalid"), InfoBarSeverity.Warning);
            return;
        }

        SetIrk(irk);
        SetResultInfo(MacResultInfo, MacResultText, App.State.T("IrkAssistantKeychainSuccess"), InfoBarSeverity.Success);
    }

    private void SetIrk(string irk)
    {
        ResultIrk = IrkHelper.Normalize(irk);
        ResultIrkBox.Text = ResultIrk;
        IsPrimaryButtonEnabled = IrkHelper.IsValidOrEmpty(ResultIrk) && !string.IsNullOrWhiteSpace(ResultIrk);
    }

    private static void SetResultInfo(
        InfoBar infoBar,
        TextBlock messageText,
        string message,
        InfoBarSeverity severity)
    {
        messageText.Text = message;
        infoBar.Severity = severity;
        infoBar.IsOpen = true;
    }

    private void ApplyText()
    {
        Title = App.State.T("IrkAssistantTitle");
        PrimaryButtonText = App.State.T("IrkAssistantUseButton");
        CloseButtonText = App.State.T("ButtonCancel");
        WindowsTab.Header = App.State.T("IrkAssistantWindowsTab");
        MacTab.Header = App.State.T("IrkAssistantMacTab");
        WindowsGuideTitleText.Text = App.State.T("IrkAssistantWindowsTitle");
        WindowsGuideText.Text = App.State.T("IrkAssistantWindowsGuide");
        SystemCommandBox.Header = App.State.T("IrkAssistantSystemCommandLabel");
        RegistryPathBox.Header = App.State.T("IrkAssistantRegistryPathLabel");
        BluetoothAddressBox.Header = App.State.T("IrkAssistantAddressLabel");
        ImportRegistryButtonText.Text = App.State.T("IrkAssistantImportRegistry");
        MacGuideTitleText.Text = App.State.T("IrkAssistantMacTitle");
        MacGuideText.Text = App.State.T("IrkAssistantMacGuide");
        KeychainContentBox.Header = App.State.T("IrkAssistantKeychainLabel");
        KeychainContentBox.PlaceholderText = App.State.T("IrkAssistantKeychainPlaceholder");
        DecodeKeychainButtonText.Text = App.State.T("IrkAssistantDecodeKeychain");
        ResultLabelText.Text = App.State.T("IrkAssistantResultLabel");
        ResultIrkBox.PlaceholderText = App.State.T("IrkAssistantNoResult");
        PrivacyText.Text = App.State.T("IrkAssistantPrivacy");
    }
}
