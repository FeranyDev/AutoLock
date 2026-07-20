using System.Collections.ObjectModel;
using AutoLock.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoLock_WinUI.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<HistoryLogEntry> _entries = new();
    private readonly List<HistoryLogEntry> _allEntries = new();

    public HistoryPage()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _entries;
        FilterBox.SelectedIndex = 0;
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
        _allEntries.Clear();
        _allEntries.AddRange(HistoryLogManager.Load());
        ApplyFilter();
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (FilterBox.SelectedItem is not ComboBoxItem selected)
        {
            return;
        }

        var kind = selected.Tag?.ToString() ?? "All";
        IEnumerable<HistoryLogEntry> filtered = kind switch
        {
            "All" => _allEntries,
            "Notification" => _allEntries.Where(entry => IsNotificationKind(entry.Kind)),
            _ => _allEntries.Where(entry => string.Equals(entry.Kind, kind, StringComparison.OrdinalIgnoreCase))
        };

        _entries.Clear();
        foreach (var entry in filtered)
        {
            _entries.Add(entry);
        }

        HistoryCountText.Text = App.State.F("HistoryCount", _entries.Count);
        EmptyHistoryPanel.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = _entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
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
        FilterBox.Header = App.State.T("HistoryFilterLabel");
        FilterAllItem.Content = App.State.T("HistoryFilterAll");
        FilterMonitorItem.Content = App.State.T("HistoryFilterMonitor");
        FilterLockItem.Content = App.State.T("HistoryFilterLock");
        FilterBindingItem.Content = App.State.T("HistoryFilterBinding");
        FilterScanItem.Content = App.State.T("HistoryFilterScan");
        FilterSessionItem.Content = App.State.T("HistoryFilterSession");
        FilterNotificationItem.Content = App.State.T("HistoryFilterNotification");
        EmptyHistoryTitleText.Text = App.State.T("HistoryEmptyTitle");
        EmptyHistoryHelpText.Text = App.State.T("HistoryEmptyHelp");
        ApplyFilter();
    }

    private static bool IsNotificationKind(string kind) =>
        string.Equals(kind, "Information", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "Success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "Warning", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(kind, "Error", StringComparison.OrdinalIgnoreCase);
}
