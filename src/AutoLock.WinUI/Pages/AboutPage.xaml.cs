using System.Diagnostics;
using AutoLock.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoLock_WinUI.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        Loaded += AboutPage_Loaded;
    }

    private void AboutPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyText();
        LoadValues();
    }

    private void OpenDataDirButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettingsManager.AppDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = AppSettingsManager.AppDirectory,
            UseShellExecute = true
        });
    }

    private void LoadValues()
    {
        VersionValueText.Text = typeof(App).Assembly.GetName().Version?.ToString() ?? "1.0.0.0";
        DataDirValueText.Text = AppSettingsManager.AppDirectory;
        BindingPathValueText.Text = BindingConfigManager.ConfigFilePath;
        SettingsPathValueText.Text = AppSettingsManager.SettingsFilePath;
        HistoryPathValueText.Text = HistoryLogManager.HistoryFilePath;
        CrashLogValueText.Text = Path.Combine(AppSettingsManager.AppDirectory, "winui-crash.log");
    }

    private void ApplyText()
    {
        PageTitleText.Text = App.State.T("NavAbout");
        PageSubtitleText.Text = App.State.T("PageAboutSubtitle");
        VersionTitleText.Text = App.State.T("LabelVersion");
        DataDirTitleText.Text = App.State.T("LabelDataDirectory");
        BindingPathTitleText.Text = App.State.T("LabelBindingConfig");
        SettingsPathTitleText.Text = App.State.T("LabelAppSettings");
        HistoryPathTitleText.Text = App.State.T("LabelHistoryLog");
        CrashLogTitleText.Text = App.State.T("LabelCrashLog");
        OpenDataDirButtonText.Text = App.State.T("ButtonOpenDataDirectory");
    }
}
