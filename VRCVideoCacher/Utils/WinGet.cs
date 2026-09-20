using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using JetBrains.Annotations;
using VRCVideoCacher.Extensions;

namespace VRCVideoCacher.Utils;

public partial class WinGet : Singleton<WinGet>
{
    private static readonly string WingetPath =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\winget.exe");

    private static readonly Dictionary<string, string> WingetPackages = new()
    {
        {
            "VP9 Video Extensions", "9n4d0msmp0pt"
        },
        {
            "AV1 Video Extension", "9mvzqvxjbq9v"
        },
        {
            "Dolby Digital Plus decoder for PC OEMs", "9nvjqjbdkn97"
        },
        // Supplies the Opus/Vorbis decoders, which SABR needs — we mux Opus audio. Note this package
        // alone is NOT sufficient for Opus in MP4: that also needs a Windows new enough for the MF MP4
        // source to map the Opus sample entry, which is what OpusMp4Check actually verifies.
        {
            "Web Media Extensions", "9n5tdp8vcmhs"
        }
    };

    [SupportedOSPlatform("windows")]
    [PublicAPI]
    public async Task TryInstallPackages2()
    {
        Log.Information("Checking for missing codec packages...");
        if (!IsOurPackagesInstalled())
        {
            Log.Information("Installing missing codec packages...");
            await InstallAllPackages();
        }
    }

    private bool IsOurPackagesInstalled()
    {
        foreach (var package in WingetPackages.Values)
            if (!IsPackageInstalled(package))
                return false;

        Log.Information("Codec packages are already installed.");
        return true;
    }

    private bool IsPackageInstalled(string packageId)
    {
        return Try.Run(() =>
        {
            using var process = new Process();
            process.StartInfo = new()
            {
                FileName = WingetPath,
                Arguments = $"list \"{packageId}\" -s msstore --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            process.Start();
            return ChildProcessTracker.TrackWhile(process, () =>
            {
                process.WaitForExit(10_000);
                return process.ExitCode == 0;
            });
        }).GetOrElse((ex) =>
        {
            Log.Warning(ex, "Failed on IsPackageInstalled");
            return false;
        });
    }

    private async Task InstallAllPackages()
    {
        foreach (var package in WingetPackages.Values)
            await InstallPackage(package);
    }

    private async Task InstallPackage(string packageId)
    {
        await Try.Run(async () =>
        {
            using var process = new Process();
            process.StartInfo = new()
            {
                FileName = WingetPath,
                Arguments = $"install --id {packageId} -s msstore --accept-package-agreements --accept-source-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            process.Start();
            await ChildProcessTracker.TrackWhile(process, async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                    if (!string.IsNullOrEmpty(line.Trim()))
                        Log.Debug("{Winget}: {Line}", "winget", line);
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                    throw new($"Installation failed with exit code {process.ExitCode}. Error: {error}");

                var packageName = WingetPackages.FirstOrDefault(x => x.Value == packageId).Key;
                if (process.ExitCode == 0)
                    Log.Information("Successfully installed package: {PackageName}", packageName);
            });
        }).GetOrElse((ex) =>
        {
            Log.Warning(ex, "Failed on InstallPackage");
            return Unit.TaskValue;
        });
    }
}