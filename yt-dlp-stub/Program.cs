using System.Net.Sockets;
using Serilog;

namespace yt_dlp;

internal static class Program
{
    private const string BaseUrl = "http://127.0.0.1:9696";

    // Next to the executable (it is installed as VRChat's / Resonite's yt-dlp), not a hardcoded path,
    // and file-only: VRChat reads the URL from stdout, so the logger must never write there.
    private static readonly string LogsPath = Path.Join(AppContext.BaseDirectory, "Logs");

    private static void InitializeLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(LogsPath, "ytdl.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5)
            .CreateLogger();
    }

    public static async Task Main(string[] args)
    {
        InitializeLogger();

        try
        {
            var url = string.Empty;
            var avPro = true;
            var source = "vrchat";
            foreach (var arg in args)
            {
                if (arg.Contains("[protocol^=http]"))
                {
                    avPro = false;
                    continue;
                }

                // --flat-playlist -i -J -s --no-playlist
                if (arg.Contains("--flat-playlist"))
                {
                    source = "resonite";
                    continue;
                }

                if (!arg.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    continue;

                url = arg;
                break;
            }

            Log.Information("Starting with args: {Args}, avPro: {AvPro}, source: {Source}",
                string.Join(" ", args), avPro, source);

            if (string.IsNullOrEmpty(url))
            {
                Log.Error("No URL found in arguments");
                await Console.Error.WriteLineAsync("ERROR: [VRCVideoCacher] No URL found in arguments");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.UserAgent.Add(new("VRCVideoCacher", "1.0"));
                var inputUrl = Uri.EscapeDataString(url);
                var response =
                    await httpClient.GetAsync($"{BaseUrl}/api/getvideo?url={inputUrl}&avpro={avPro}&source={source}");
                var output = await response.Content.ReadAsStringAsync();
                Log.Information("[Response] {Output}", output);
                if (!response.IsSuccessStatusCode)
                    throw new(output);
                Console.WriteLine(output);
            }
            catch (HttpRequestException ex) when (ex.InnerException is SocketException
                                                  {
                                                      SocketErrorCode: SocketError.ConnectionRefused
                                                  })
            {
                Log.Error("Connection refused. Is the server running?");
                await Console.Error.WriteLineAsync(
                    "ERROR: [VRCVideoCacher] Connection refused. Is VRCVideoCacher running?");
                ClearReadOnly();
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Request failed");
                await Console.Error.WriteLineAsync($"ERROR: [VRCVideoCacher] {ex.GetType().Name}: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    // VRCVideoCacher marks the patched yt-dlp read-only while it runs; clear that on the running exe
    // once the server has gone (works on Windows, under Proton on Linux, and on native Linux).
    private static void ClearReadOnly()
    {
        if (Environment.ProcessPath is not { } path || !File.Exists(path))
            return;
        var attr = File.GetAttributes(path);
        if (attr.HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
    }
}
