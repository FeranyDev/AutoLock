using System.Collections.ObjectModel;
using AutoLock.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoLock_WinUI.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<HistoryLogEntry> _entries = new();

    public HistoryPage()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _entries;
        Loaded += HistoryPage_Loaded;
    }

    private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyText();
        LoadHistory();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadHistory();
    }

    private void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryLogManager.Clear();
        LoadHistory();
        SetInfo(App.State.T("InfoHistoryClearedTitle"), App.State.T("InfoHistoryCleared"), InfoBarSeverity.Informational);
    }

    private void LoadHistory()
    {
        _entries.Clear();
        foreach (var entry in HistoryLogManager.Load())
        {
            _entries.Add(entry);
        }

        SetInfo(App.State.T("StatusReady"), App.State.F("InfoHistoryLoaded", _entries.Count), InfoBarSeverity.Informational);
    }

    private void SetInfo(string title, string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private void ApplyText()
    {
        PageTitleText.Text = App.State.T("NavHistory");
        PageSubtitleText.Text = App.State.T("PageHistorySubtitle");
        RefreshButtonText.Text = App.State.T("ButtonRefresh");
        ClearHistoryButtonText.Text = App.State.T("ButtonClearHistory");
    }
}
