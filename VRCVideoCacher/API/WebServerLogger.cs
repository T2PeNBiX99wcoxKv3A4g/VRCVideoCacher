using System.Text.RegularExpressions;
using Swan.Logging;

// ReSharper disable ClassNeverInstantiated.Global

namespace VRCVideoCacher.API;

public partial class WebServerLogger : ILogger
{
    public LogLevel LogLevel { get; } = LogLevel.Info;
    private static readonly Regex RequestIdPrefix = RequestIdRegex();

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Log(LogMessageReceivedEventArgs logEvent)
    {
        var rawMessage = RequestIdPrefix.Replace(logEvent.Message, "");
        var trace = logEvent.Exception != null ? logEvent.Exception.ToString() : string.Empty;
        var message = string.IsNullOrEmpty(trace) ? rawMessage : $"{rawMessage}\n{trace}";

        switch (logEvent.MessageType)
        {
            case LogLevel.Error:
            case LogLevel.Warning:
                WebServer.Log.Warning("{WebServerLogEvent:l}", message);
                break;
            case LogLevel.Info:
                WebServer.Log.Information("{WebServerLogEvent:l}", message);
                break;
        }
    }

    [GeneratedRegex(@"^\[.*?\]\s*", RegexOptions.Compiled)]
    private static partial Regex RequestIdRegex();
}