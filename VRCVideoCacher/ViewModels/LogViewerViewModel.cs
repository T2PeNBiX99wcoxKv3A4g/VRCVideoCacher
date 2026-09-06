using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog.Events;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.ViewModels;

public partial class LogViewerViewModel : ViewModelBase
{
    [ObservableProperty] public partial string FilterText { get; set; } = string.Empty;

    // Off by default: Debug/trace is not captured at all (keeping it out of the log file and buffer). Turning
    // it on lowers the live log level so debug output starts flowing — see OnShowDebugChanged.
#if DEBUG
    [ObservableProperty] public partial bool ShowDebug { get; set; } = true;
#else
    [ObservableProperty] public partial bool ShowDebug { get; set; };
#endif

    [ObservableProperty] public partial bool ShowInfo { get; set; } = true;

    [ObservableProperty] public partial bool ShowWarning { get; set; } = true;

    [ObservableProperty] public partial bool ShowError { get; set; } = true;

    [ObservableProperty] public partial bool AutoScroll { get; set; } = true;

    [ObservableProperty] private partial int MaxLogEntries { get; set; } = 1000;
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<LogEntry> FilteredLogEntries { get; } = [];

    public LogViewerViewModel()
    {
        LoggerUtils.LevelSwitch.MinimumLevel = ShowDebug ? LogEventLevel.Debug : LogEventLevel.Information;

        // Load buffered logs that occurred before UI was ready
        foreach (var entry in LogService.GetBufferedLogs())
        {
            LogEntries.Add(entry);
            if (ShouldShowEntry(entry))
                FilteredLogEntries.Add(entry);
        }

        // Subscribe to new log entries
        LogService.OnLogEntry += OnLogEntry;
    }

    private void OnLogEntry(LogEntry entry)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            LogEntries.Add(entry);

            // Trim old entries
            while (LogEntries.Count > MaxLogEntries)
                LogEntries.RemoveAt(0);

            // Apply filter
            if (ShouldShowEntry(entry))
            {
                FilteredLogEntries.Add(entry);
                while (FilteredLogEntries.Count > MaxLogEntries)
                    FilteredLogEntries.RemoveAt(0);
            }
        });
    }

    private bool ShouldShowEntry(LogEntry entry)
    {
        // Filter by level
        var show = entry.Level switch
        {
            "DBG" => ShowDebug,
            "INF" => ShowInfo,
            "WRN" => ShowWarning,
            "ERR" or "FTL" => ShowError,
            _ => true
        };

        if (!show) return false;

        // Filter by text
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.ToLowerInvariant();
            if (!entry.Message.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !entry.Source.Contains(filter, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnShowDebugChanged(bool value)
    {
        // Capture Debug/trace only while the toggle is on, so it never spams the log file or buffer by
        // default. Information is the floor either way.
        LoggerUtils.LevelSwitch.MinimumLevel = value ? LogEventLevel.Debug : LogEventLevel.Information;
        ApplyFilter();
    }

    partial void OnShowInfoChanged(bool value) => ApplyFilter();
    partial void OnShowWarningChanged(bool value) => ApplyFilter();
    partial void OnShowErrorChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        FilteredLogEntries.Clear();
        foreach (var entry in LogEntries)
            if (ShouldShowEntry(entry))
                FilteredLogEntries.Add(entry);
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        FilteredLogEntries.Clear();
    }

    [RelayCommand]
    private async Task ExportLogs()
    {
        // Export logs to file
        var logPath = Path.Join(Program.DataPath, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        var lines = LogEntries.Select(e => $"[{e.Timestamp:HH:mm:ss} {e.Level} {e.Source}] {e.Message}");
        await File.WriteAllLinesAsync(logPath, lines);

        // Open file location
        if (OperatingSystem.IsWindows())
            Process.Start("explorer.exe", $"/select,\"{logPath}\"");
    }
}