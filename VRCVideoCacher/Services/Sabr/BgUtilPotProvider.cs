using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Jeek.Avalonia.Localization;
using JetBrains.Annotations;
using Serilog;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services.Sabr;

/// <summary>
/// Provisions and supervises the bgutil PO token provider using the bundled Deno runtime.
/// </summary>
internal static class BgUtilPotProvider
{
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(BgUtilPotProvider));
    private static bool _isExit;

    private static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            {
                "User-Agent", "VRCVideoCacher"
            }
        },
        Timeout = TimeSpan.FromMinutes(5)
    };

    private static readonly string RootPath = Path.Join(Program.UtilsPath, "bgutil");
    private static readonly string ServerPath = Path.Join(RootPath, "server");
    private static readonly string MainJsPath = Path.Join(ServerPath, "build", "main.js");

    /// <summary>
    /// yt-dlp searches each child of <c>--plugin-dirs</c> for a <c>yt_dlp_plugins</c> namespace.
    /// </summary>
    [PublicAPI]
    public static string PluginSearchDir =>
        IsAutoManaged ? Path.Join(ServerPath, "yt-dlp-plugins") : Program.UtilsPath;

    // Port conflicts change only the runtime URL, not the configured preference.

    [PublicAPI] public static string BaseUrl { get; private set; } = ConfigManager.Config.SabrPotBaseUrl.TrimEnd('/');

    /// <summary>
    /// Non-blocking readiness snapshot; use <see cref="WaitReadyAsync"/> to wait for startup.
    /// </summary>
    public static bool IsReady => _isReady;

    /// <summary>
    /// Shared yt-dlp provider arguments, already quoted for the command line.
    /// </summary>
    public static string[] ExtractorArgs =>
    [
        $"--plugin-dirs \"{PluginSearchDir}\"",
        $"--extractor-args \"youtubepot-bgutilhttp:base_url={BaseUrl}\""
    ];

    public static int Port => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ? uri.Port : 4416;

    private static string PingUrl => $"{BaseUrl}/ping";

    // External providers are health-checked only; their lifecycle is managed by the operator.
    private static bool IsAutoManaged => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static readonly Lock InitLock = new();
    private static Task? _init;
    private static volatile bool _backendReady;
    private static volatile bool _isReady;
    private static volatile bool _initFailed;
    private static Process? _server;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static BgUtilPotProvider()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopServer(true);
    }

    /// <summary>
    /// Kills leftover processes matching the bundled Deno path or holding the pot server port; skips global Deno.
    /// Call once at startup, before starting the server.
    /// </summary>
    public static void KillOrphanedInstances()
    {
        // 1. If auto-managed, check if our preferred port is currently held by an orphaned deno/node process
        if (IsAutoManaged)
        {
            var preferredPort = Port;
            if (PortAudit.IsInUse(preferredPort))
                if (PortAudit.TryKillListener(preferredPort, "deno"))
                    Log.Information("Freed POT server port {Port} by terminating leftover Deno process", preferredPort);
        }

        // 2. Kill leftover Deno processes originating from our bundled/utils path
        var denoPath = YtdlManager.DenoPath;
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "deno"
        };
        if (!string.IsNullOrEmpty(denoPath))
            processNames.Add(Path.GetFileNameWithoutExtension(denoPath));

        var fullDenoPath = !string.IsNullOrEmpty(denoPath)
            ? Try.Run(() => Path.GetFullPath(denoPath)).GetOrNull()
            : null;
        var fullUtilsPath = Try.Run(() => Path.GetFullPath(Program.UtilsPath)).GetOrNull();

        foreach (var process in processNames.SelectMany(Process.GetProcessesByName))
            Try.Run(() =>
            {
                var pid = process.Id;
                if (pid == Environment.ProcessId) return;
                var exePath = Try.Run(() => process.MainModule?.FileName).GetOrNull();
                if (string.IsNullOrEmpty(exePath)) return;
                var fullExePath = Path.GetFullPath(exePath);
                var matches = !string.IsNullOrEmpty(fullDenoPath) &&
                              string.Equals(fullExePath, fullDenoPath, PathComparison) ||
                              !string.IsNullOrEmpty(fullUtilsPath) &&
                              fullExePath.StartsWith(fullUtilsPath, PathComparison);

                if (!matches) return;
                Log.Information("Killing leftover Deno process {Pid} from a previous run", pid);
                process.Kill(true);
                process.WaitForExit(3000);
            }).OnFailure(ex => Log.Debug(ex, "Could not kill Deno process")).OnFinally(() => process.Dispose());
    }

    /// <summary>
    /// Allows initialization after startup cleanup and dependency preparation have completed.
    /// Called by the backend, not by readiness checks. Safe to call repeatedly.
    /// </summary>
    public static void EnableStartup()
    {
        _backendReady = true;
        Ensure();
    }

    /// <summary>
    /// Starts initialization in the background once the backend is ready. Safe to call repeatedly.
    /// </summary>
    [PublicAPI]
    public static void Ensure()
    {
        if (!ConfigManager.Config.SabrRestreamEnabled || !_backendReady)
            return;

        lock (InitLock)
        {
            if (!_backendReady)
                return;

            if (_init is { IsFaulted: true } or { IsCanceled: true } || _initFailed)
            {
                _init = null;
                _initFailed = false;
            }

            _init ??= Task.Run(InitAsync);
        }
    }

    /// <summary>
    /// Waits for readiness, returning false on initialization failure, timeout, or cancellation.
    /// </summary>
    public static async Task<bool> WaitReadyAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        if (!ConfigManager.Config.SabrRestreamEnabled)
            return false;

        Ensure();

        if (_isReady)
            return true;

        using var activity = StatusService.Begin(StatusCategory.Provisioning,
            Localizer.Get("StatusProviderWaiting"), key: ToolVerifier.PotProviderKey);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_isReady)
                return true;
            if (_initFailed)
                return false;

            try
            {
                await Task.Delay(500, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return _isReady;
    }

    private static async Task InitAsync()
    {
        if (IsAutoManaged)
        {
            ReassignPortIfInUse();
            using var activity = StatusService.Begin(StatusCategory.Provisioning,
                Localizer.Get("StatusProviderSetup"), key: ToolVerifier.PotProviderKey);
            try
            {
                await EnsureInstalledAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex,
                    "Failed to provision the bgutil PO token provider; SABR web playback will be unavailable until this is resolved");
                _initFailed = true;
                return;
            }
        }
        else
            Log.Information("Using an externally-managed bgutil PO token provider at {Url}",
                ConfigManager.Config.SabrPotBaseUrl);

        await SuperviseAsync();
    }

    private static void ReassignPortIfInUse()
    {
        var preferred = Port;
        if (!PortAudit.IsInUse(preferred))
            return;

        // Try killing leftover deno listener first
        if (PortAudit.TryKillListener(preferred, "deno"))
        {
            Log.Information("Freed bgutil port {Port} after terminating leftover process", preferred);
            return;
        }

        var who = PortAudit.DescribeListener(preferred);
        var free = PortAudit.FindFreePort(preferred);
        if (free == preferred)
        {
            Log.Warning("bgutil port {Port} is in use by {Process} and no free port was found nearby; " +
                        "the provider may fail to start", preferred, who);
            return;
        }

        BaseUrl = new UriBuilder(BaseUrl)
        {
            Port = free
        }.Uri.ToString().TrimEnd('/');
        Log.Warning("bgutil port {Preferred} is in use by {Process}; using port {Free} instead",
            preferred, who, free);
    }

    private static bool HasProcessExited(Process? proc) =>
        proc is null || Try.Run(() => proc.HasExited).GetOrElse(_ => true);

    private static async Task SuperviseAsync()
    {
        while (!Volatile.Read(ref _isExit))
        {
            if (Volatile.Read(ref _isExit)) break;

            await Try.Run(async () =>
            {
                if (IsAutoManaged && (_server is null || HasProcessExited(_server)))
                {
                    _isReady = false;
                    await StartServerAsync();
                    // Give the BotGuard VM a moment to come up before the first health poll.
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                _isReady = await PingAsync();
            }).OnFailure(ex =>
            {
                _isReady = false;
                Log.Debug(ex, "bgutil supervisor iteration failed");
                return Unit.TaskValue;
            });

            await Task.Delay(TimeSpan.FromSeconds(_isReady ? 15 : 3));
        }
    }

    private static async Task EnsureInstalledAsync()
    {
        if (!File.Exists(YtdlManager.DenoPath))
            throw new SabrException(
                $"Deno runtime not found at {YtdlManager.DenoPath}; cannot run the PO token provider");

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            throw new SabrException("The embedded bgutil server supports only Windows x64 and Linux x64");

        var packageName = OperatingSystem.IsWindows()
            ? "bgutil-pot-server-win-x64"
            : "bgutil-pot-server-linux-x64";
        var resourceName = $"VRCVideoCacher.{packageName}" + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");
        var canvasPath = Path.Join("node_modules", "canvas", "build", "Release", "canvas.node");
        var installed = File.Exists(MainJsPath) && File.Exists(Path.Join(ServerPath, canvasPath));

        if (installed && Versions.CurrentVersion.BgUtil == Program.BgUtilsVersion)
        {
            Log.Debug("bgutil provider {Tag} already installed", Program.BgUtilsVersion);
            return;
        }

        SafeDelete(ServerPath);
        SafeDelete(Path.Join(Program.UtilsPath, "yt-dlp-plugins")); // legacy plugin location
        Log.Information("Installing bgutil PO token provider {Tag}...", Program.BgUtilsVersion);

        await using var resource = typeof(BgUtilPotProvider).Assembly.GetManifestResourceStream(resourceName)
                                   ?? throw new SabrException(
                                       $"Embedded bgutil server resource not found: {resourceName}");

        var stagingPath = Path.Join(RootPath, $"install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        using (UsingUntil.Run(() => SafeDelete(stagingPath)))
        {
            if (OperatingSystem.IsWindows())
                await ZipFile.ExtractToDirectoryAsync(resource, stagingPath);
            else
            {
                await using var gzip = new GZipStream(resource, CompressionMode.Decompress, true);
                await TarFile.ExtractToDirectoryAsync(gzip, stagingPath, false);
            }

            var extractedPath = Path.Join(stagingPath, packageName);
            if (!File.Exists(Path.Join(extractedPath, "build", "main.js")) ||
                !File.Exists(Path.Join(extractedPath, canvasPath)))
                throw new SabrException($"Embedded bgutil server package is incomplete: {resourceName}");

            Directory.Move(extractedPath, ServerPath);
        }

        Versions.CurrentVersion.BgUtil = Program.BgUtilsVersion;
        Versions.Save();
        Log.Information("bgutil PO token provider {Tag} installed.", Program.BgUtilsVersion);
    }

    private static async Task StartServerAsync()
    {
        await StopServerAsync();

        // Check if port is still in use before spawning
        if (PortAudit.IsInUse(Port))
            PortAudit.TryKillListener(Port, "deno");

        // Keep the compiled entrypoint and bundled dependencies within the cwd-scoped permissions.
        var process = new Process
        {
            StartInfo =
            {
                FileName = YtdlManager.DenoPath,
                Arguments =
                    $"run --no-config --no-lock --node-modules-dir=manual --cached-only --allow-env --allow-net --allow-ffi=. --allow-read=. build/main.js -p {Port}",
                WorkingDirectory = ServerPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Log.Debug("[bgutil] {Line}", e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) Log.Debug("[bgutil] {Line}", e.Data);
        };

        process.Start();
        ChildProcessTracker.Track(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _server = process;
        Log.Information("Started bgutil PO token server on port {Port} (pid {Pid})", Port, process.Id);
    }

    [PublicAPI]
    public static async Task StopServerAsync(bool programExit = false)
    {
        if (Volatile.Read(ref _isExit)) return;
        if (programExit)
            if (Interlocked.Exchange(ref _isExit, true))
                return;
        var process = Interlocked.Exchange(ref _server, null);
        if (process is null)
            return;
        ChildProcessTracker.Untrack(process);
        await Try.Run(async () =>
        {
            if (HasProcessExited(process)) return;
            process.Kill(true);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(cts.Token);
        }).OnFailure(ex =>
        {
            if (ex is InvalidOperationException) return Unit.TaskValue;
            Log.Debug(ex, "Failed to stop bgutil server");
            return Unit.TaskValue;
        }).OnFinally(() =>
        {
            process.TryDispose();
            return Unit.TaskValue;
        });
    }

    [PublicAPI]
    public static void StopServer(bool programExit = false)
    {
        if (Volatile.Read(ref _isExit)) return;
        if (programExit)
            if (Interlocked.Exchange(ref _isExit, true))
                return;
        var process = Interlocked.Exchange(ref _server, null);
        if (process is null)
            return;
        ChildProcessTracker.Untrack(process);
        Try.Run(() =>
        {
            if (HasProcessExited(process)) return;
            process.Kill(true);
            process.WaitForExit(3000);
        }).OnFailure(ex =>
        {
            if (ex is InvalidOperationException) return;
            Log.Debug(ex, "Failed to stop bgutil server");
        }).OnFinally(() => process.TryDispose());
    }

    /// <summary>
    /// Checks health regardless of the SABR toggle, since legacy yt-dlp also uses the provider.
    /// </summary>
    public static Task<bool> IsRespondingAsync() => PingAsync();

    private static async Task<bool> PingAsync()
    {
        return await Try.Run(async () =>
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var response = await HttpClient.GetAsync(PingUrl, cts.Token);
            return response.IsSuccessStatusCode;
        }).GetOrElse(_ => Task.FromResult(false));
    }

    private static void SafeDelete(string dir)
    {
        Try.Run(() =>
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }).OnFailure(ex => Log.Debug(ex, "Could not delete {Dir} before reinstall", dir));
    }
}