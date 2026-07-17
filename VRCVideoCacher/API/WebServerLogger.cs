using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Swan.Logging;

namespace VRCVideoCacher.API;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public partial class WebServerLogger : ILogger
{
    private static readonly Regex RequestIdPrefix = RequestIdRegex();
    public LogLevel LogLevel => LogLevel.Info;

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
                // SABR HLS segment fetches (206 Partial Content) fire constantly during playback — one per
                // segment, per viewer — and drown out everything else at Info. Keep them, but at Debug.
                if (IsHlsPartialContent(rawMessage))
                    WebServer.Log.Debug("{WebServerLogEvent:l}", message);
                else
                    WebServer.Log.Information("{WebServerLogEvent:l}", message);
                break;
        }
    }

    [GeneratedRegex(@"^\[.*?\]\s*", RegexOptions.Compiled)]
    private static partial Regex RequestIdRegex();

    private static bool IsHlsPartialContent(string message) =>
        message.Contains("/hls/", StringComparison.Ordinal) &&
        message.Contains("206", StringComparison.Ordinal);
}