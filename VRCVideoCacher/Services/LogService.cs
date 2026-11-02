using System.Collections.Concurrent;
using Avalonia.Threading;
using Serilog.Core;
using Serilog.Events;
using VRCVideoCacher.Models;
using VRCVideoCacher.Views;

namespace VRCVideoCacher.Services;

public static class LogService
{
    private const int MaxBufferSize = 500;

    // Buffer to store logs before UI subscribes
    private static readonly ConcurrentQueue<LogEntry> LogBuffer = new();
    public static event Action<LogEntry>? OnLogEntry;

    public static void EmitLogEntry(LogEvent logEvent)
    {
        var level = logEvent.Level switch
        {
            LogEventLevel.Verbose => "VRB",
            LogEventLevel.Debug => "DBG",
            LogEventLevel.Information => "INF",
            LogEventLevel.Warning => "WRN",
            LogEventLevel.Error => "ERR",
            LogEventLevel.Fatal => "FTL",
            _ => "???"
        };

        var source = "Unknown";
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            var sourceStr = sourceContext.ToString().Trim('"');
            var lastDot = sourceStr.LastIndexOf('.');
            source = lastDot >= 0 ? sourceStr[(lastDot + 1)..] : sourceStr;
        }

        var message = logEvent.RenderMessage();
        if (logEvent.Exception != null)
            message += Environment.NewLine + logEvent.Exception;

        var entry = new LogEntry
        {
            Timestamp = logEvent.Timestamp.DateTime,
            Level = level,
            Source = source,
            Message = message
        };

        // Add to buffer
        LogBuffer.Enqueue(entry);
        while (LogBuffer.Count > MaxBufferSize)
            LogBuffer.TryDequeue(out _);

        // Emit to subscribers
        OnLogEntry?.Invoke(entry);
    }

    // Get all buffered logs (for UI initialization)
    public static IEnumerable<LogEntry> GetBufferedLogs() => [.. LogBuffer];
}

public class UiLogSink : ILogEventSink
{
    // A world that keeps retrying the same dead URL can log the same error dozens of times in a row.
    // Without a cooldown every one of those opened a popup and started a PowerShell process for a toast,
    // which is both unusable and expensive. Identical errors now surface at most once per window; the
    // windows are per error key, so a genuinely new failure is never hidden.
    private static readonly TimeSpan PopupRepeatWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ToastRepeatWindow = TimeSpan.FromMinutes(10);
    private const int MaxRememberedErrors = 128;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, DateTime> PopupHistory = new();
    private static readonly Dictionary<string, DateTime> ToastHistory = new();

    private static PopupWindow? _currentPopup;
    private static string? _currentPopupKey;
    private static int _currentPopupCount;

    public void Emit(LogEvent logEvent)
    {
        if (ConfigManager.IsInitialized && logEvent.Level >= LogEventLevel.Error)
        {
            var source = logEvent.Properties.TryGetValue("SourceContext", out var sourceContext)
                ? sourceContext.ToString().Trim('"')
                : "Unknown";

            // Build the key from the rendered message and the exception identity only (not its full
            // stack trace), so the same failure keeps matching even if a stack frame moves.
            var message = logEvent.RenderMessage();
            var key = BuildKey(source, message, logEvent.Exception);
            if (logEvent.Exception != null)
                message += Environment.NewLine + logEvent.Exception;

            if (ConfigManager.Config is { ErrorPopups: true } &&
                TryAcquire(PopupHistory, key, PopupRepeatWindow))
                Dispatcher.UIThread.Post(() => ShowPopup(key, source, message));

            if (ConfigManager.Config is { ErrorNotifications: true } &&
                TryAcquire(ToastHistory, key, ToastRepeatWindow))
                NotificationService.ShowErrorNotification($"Error from {source}", message);
        }

        LogService.EmitLogEntry(logEvent);
    }

    private static string BuildKey(string source, string message, Exception? exception) =>
        exception == null
            ? $"{source}\0{message}"
            : $"{source}\0{message}\0{exception.GetType().FullName}\0{exception.Message}";

    /// <summary>
    /// Returns true when this key has not been shown within <paramref name="window"/>. Remembered keys
    /// are pruned once the table is full, so a long-running process cannot accumulate them forever.
    /// </summary>
    private static bool TryAcquire(Dictionary<string, DateTime> history, string key, TimeSpan window)
    {
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if (history.TryGetValue(key, out var lastShown))
            {
                // Slide the window forward on every repeat. A failure that keeps happening therefore
                // stays silent; only a recurrence after a genuinely quiet period surfaces again.
                history[key] = now;
                if (now - lastShown < window)
                    return false;
            }

            if (history.Count >= MaxRememberedErrors)
            {
                foreach (var stale in history.Where(pair => now - pair.Value >= window).Select(pair => pair.Key)
                             .ToArray())
                    history.Remove(stale);

                if (history.Count >= MaxRememberedErrors)
                    history.Clear();
            }

            history[key] = now;
            return true;
        }
    }

    private static void ShowPopup(string key, string source, string message)
    {
        // Same error still on screen: just note that it happened again instead of closing and
        // reopening the window (which is what made it flash).
        if (_currentPopup is { } open && _currentPopupKey == key)
        {
            _currentPopupCount++;
            open.Title = $"Error from {source} (x{_currentPopupCount})";
            return;
        }

        _currentPopup?.Close();
        _currentPopup = null;
        _currentPopupKey = null;

        var popup = new PopupWindow(message)
        {
            Title = $"Error from {source}",
            Topmost = true,
            // Do not steal focus from the game for an error the user may not even need to act on.
            ShowActivated = false
        };
        _currentPopup = popup;
        _currentPopupKey = key;
        _currentPopupCount = 1;
        popup.Closed += (_, _) =>
        {
            if (_currentPopup != popup)
                return;

            _currentPopup = null;
            _currentPopupKey = null;
            _currentPopupCount = 0;
        };
        popup.Show();
    }
}