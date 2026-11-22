using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using EmbedIO;
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
        // EmbedIO reports every HttpException here as a warning with its stack trace. OnHttpException
        // already logs the status together with the request that caused it, so logging it twice only
        // buries the useful line under a stack for an expected 404.
        if (logEvent.Exception is IHttpException)
            return;

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
                // SABR and NicoNico HLS segment fetches fire constantly during playback — one per segment,
                // per viewer — and drown out everything else at Info. Keep them, but at Debug. A 404 is
                // included: a player retrying a segment it cannot get is one line per attempt, and the
                // session already reports why the media stopped arriving.
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
         message.Contains("304", StringComparison.Ordinal) ||
         message.Contains("404", StringComparison.Ordinal));
}