using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Avalonia.Platform;
using JetBrains.Annotations;
using Swan;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;
using OperatingSystem = System.OperatingSystem;

namespace VRCVideoCacher.Services;

public static class NotificationService
{
    private const string AppId = "VRCVideoCacher";
    private static readonly string? ScriptPath;
    private static readonly string? IconPath;

    static NotificationService()
    {
        if (!OperatingSystem.IsWindows()) return;
        ScriptPath = EnsureAssetExtracted("ToastNotification.ps1",
            "avares://VRCVideoCacher/Assets/ToastNotification.ps1");
        IconPath = EnsureAssetExtracted("icon.ico", "avares://VRCVideoCacher/Assets/icon.ico");
    }

    [SupportedOSPlatform("windows")]
    private static string? EnsureAssetExtracted(string fileName, string resourcePath)
    {
        return Try.Run(() =>
        {
            var targetDir = !string.IsNullOrEmpty(Program.UtilsPath)
                ? Program.UtilsPath
                : Path.Combine(Path.GetTempPath(), "VRCVideoCacher");

            Directory.CreateDirectory(targetDir);
            var targetFile = Path.Combine(targetDir, fileName);
            var resourceUri = new Uri(resourcePath);

            if (File.Exists(targetFile))
            {
                var isMatch = Try.Run(() =>
                {
                    using var resStream = AssetLoader.Open(resourceUri);
                    using var fileStream = File.OpenRead(targetFile);

                    var resHash = SHA256.HashData(resStream);
                    var fileHash = SHA256.HashData(fileStream);

                    return resHash.AsSpan().SequenceEqual(fileHash);
                }).GetOrElse(_ => false);

                if (isMatch)
                    return targetFile;
            }

            using var resourceStream = AssetLoader.Open(resourceUri);
            using var targetStream = File.Create(targetFile);
            resourceStream.CopyTo(targetStream);

            return targetFile;
        }).GetOrNull();
    }

    [PublicAPI]
    public static void ShowErrorNotification(string title, string message)
    {
        ShowNotification(title, message);
    }

    [PublicAPI]
    public static void ShowNotification(string title, string message, bool isError = true)
    {
        if (OperatingSystem.IsWindows())
            ShowWindowsToastNotification(title, message, isError);
        else if (OperatingSystem.IsLinux())
            ShowLinuxNotification(title, message, isError);
    }

    [SupportedOSPlatform("windows")]
    private static void ShowWindowsToastNotification(string title, string message, bool isError)
    {
        Task.Run(() =>
        {
            Try.Run(() =>
            {
                if (string.IsNullOrEmpty(ScriptPath) || !File.Exists(ScriptPath)) return;

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    ArgumentList =
                    {
                        "-NoProfile",
                        "-NonInteractive",
                        "-ExecutionPolicy",
                        "Bypass",
                        "-File",
                        ScriptPath,
                        "-AppId",
                        AppId,
                        "-Title",
                        title.Truncate(128, "...")!,
                        "-Message",
                        message.Truncate(1024, "...")!
                    },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (!string.IsNullOrEmpty(IconPath) && File.Exists(IconPath))
                {
                    psi.ArgumentList.Add("-IconUri");
                    psi.ArgumentList.Add(IconPath);
                }

                using var process = Process.Start(psi);
            });
        });
    }

    [SupportedOSPlatform("linux")]
    private static void ShowLinuxNotification(string title, string message, bool isError)
    {
        Task.Run(() =>
        {
            Try.Run(() =>
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "notify-send",
                    ArgumentList =
                    {
                        title,
                        message,
                        "-u",
                        isError ? "critical" : "normal",
                        "-a",
                        "VRCVideoCacher"
                    },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            });
        });
    }
}