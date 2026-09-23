using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using VRCVideoCacher.Extensions;
using ILogger = Swan.Logging.ILogger;
using LogLevel = Swan.Logging.LogLevel;
using LogMessageReceivedEventArgs = Swan.Logging.LogMessageReceivedEventArgs;

namespace VRCVideoCacher.API;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public partial class WebServerLogger : ILogger
{
    public LogLevel LogLevel => LogLevel.Info;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Log(LogMessageReceivedEventArgs logEvent)
    {
        var rawMessage = RequestIdPrefix().Replace(logEvent.Message, "");
        var trace = logEvent.Exception != null ? logEvent.Exception.ToString() : string.Empty;
        var message = string.IsNullOrEmpty(trace) ? rawMessage : $"{rawMessage}\n{trace}";

        switch (logEvent.MessageType)
        {
            case LogLevel.Error:
            case LogLevel.Warning:
                WebServer.Instance.Logger.Warning("{WebServerLogEvent:l}", message);
                break;
            case LogLevel.Info:
                // SABR and NicoVideo HLS segment fetches (206 Partial Content / 200 OK) fire constantly during
                // playback — one per segment, per viewer — and drown out everything else at Info. Keep them, but at Debug.
                if (IsHlsStreamFetch(rawMessage))
                    WebServer.Instance.Logger.Debug("{WebServerLogEvent:l}", message);
                else
                    WebServer.Instance.Logger.Information("{WebServerLogEvent:l}", message);
                break;
        }
    }

    [GeneratedRegex(@"^\[.*?\]\s*", RegexOptions.Compiled)]
    private static partial Regex RequestIdPrefix();

    private static bool IsHlsStreamFetch(string message) =>
        (message.Contains("/hls/", StringComparison.Ordinal) || message.Contains("/nico/", StringComparison.Ordinal)) &&
        (message.Contains("206", StringComparison.Ordinal) ||
         message.Contains("200 OK", StringComparison.Ordinal) ||
         message.Contains("304", StringComparison.Ordinal));
}