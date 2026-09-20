using System.Diagnostics;
using System.Text;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Models;
using VRCVideoCacher.Services.Sabr;
using VRCVideoCacher.Utils;
using VRCVideoCacher.YTDL;

namespace VRCVideoCacher.Services;

/// <summary>Result of verifying a required tool. <see cref="Present"/> distinguishes "missing" from "ran but failed".</summary>
public readonly record struct ToolCheck(bool Ok, bool Present, string Detail);

/// <summary>
/// Actively verifies that each required external tool is present AND functioning — it runs the binary
/// (<c>--version</c>) or pings the service, never just <c>File.Exists</c>. Used by the dashboard.
/// </summary>
public static class ToolVerifier
{
    // Stable keys shared with StatusService so the dashboard can tell which tool a download activity is for.
    public const string YtDlpKey = "yt-dlp";
    public const string FfmpegKey = "ffmpeg";
    public const string DenoKey = "deno";
    public const string PotProviderKey = "pot-provider";

    public static async Task<ToolCheck> VerifyYtDlpAsync()
    {
        // Run --version to confirm it actually works, but display the tracked release NAME instead: we ship
        // the bashonly SABR build and its name carries the "sabr" marker ("sabr 2026.08.19.233452"), which
        // `yt-dlp --version` alone omits (it prints just the date).
        var check = await RunVersionAsync(YtdlManager.YtdlPath, "--version");
        if (check.Ok && !string.IsNullOrWhiteSpace(Versions.CurrentVersion.Ytdlp))
            return check with
            {
                Detail = Versions.CurrentVersion.Ytdlp
            };
        return check;
    }

    public static Task<ToolCheck> VerifyDenoAsync() => RunVersionAsync(YtdlManager.DenoPath, "--version");
    public static Task<ToolCheck> VerifyFfmpegAsync() => RunVersionAsync(YtdlManager.FfmpegPath, "-version");

    public static async Task<ToolCheck> VerifyPotProviderAsync()
    {
        // The provider's Deno server needs a few seconds to bind and warm its BotGuard VM after launch, so a
        // single ping right after startup races it and reports a false "not working". When SABR is enabled we
        // own the server's lifecycle, so wait (bounded) for it to become ready — WaitReadyAsync returns the
        // instant it answers, and bails immediately if provisioning genuinely failed, so this only actually
        // waits while it's still coming up. With SABR off we don't start it, so a single ping is right (that
        // path only reports on an externally-managed provider).
        var ok = ConfigManager.Config.SabrRestreamEnabled
            ? await BgUtilPotProvider.WaitReadyAsync(TimeSpan.FromSeconds(20))
            : await BgUtilPotProvider.IsRespondingAsync();
        // A failed health check means "not working", not that the provider is missing.
        return new(ok, true, $"bgutil-ytdlp-pot-provider {Program.BgUtilsVersion} *:{BgUtilPotProvider.Port}");
    }

    private static async Task<ToolCheck> RunVersionAsync(string path, string arg)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return new(false, false, string.Empty);

        return await Try.Run(async () =>
        {
            using var process = new Process();
            process.StartInfo = new()
            {
                FileName = path,
                Arguments = arg,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            process.Start();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return await ChildProcessTracker.Tracking(process, async () =>
            {
                var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
                var stderr = process.StandardError.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode != 0)
                    return new ToolCheck(false, true, string.Empty);

                var raw = await stdout;
                if (string.IsNullOrWhiteSpace(raw))
                    raw = await stderr;
                return new(true, true, ExtractVersion(raw));
            });
        }).GetOrElse((_) => Task.FromResult(new ToolCheck(false, true, string.Empty)));
    }

    /// <summary>Best-effort version string from the first line of <c>--version</c> output.</summary>
    private static string ExtractVersion(string raw)
    {
        var line = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        // "ffmpeg version 7.1.1-full_build ..." -> "7.1.1-full_build"
        var idx = line.IndexOf("version ", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return line[(idx + "version ".Length)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? line;
        return line;
    }
}