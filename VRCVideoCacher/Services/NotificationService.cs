using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Avalonia.Platform;
using JetBrains.Annotations;
using Microsoft.Win32;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public static class NotificationService
{
    private const string AppId = "VRCVideoCacher";
    private static bool _isAumidRegistered;
    private static string? _registeredIconPath;
    private static string? _scriptPath;
    private static string? _iconPath;
    private static readonly Lock ResourceLock = new();

    [SupportedOSPlatform("windows")]
    private static void EnsureAppUserModelIdRegistered(string? iconPath)
    {
        if (_isAumidRegistered && _registeredIconPath == iconPath)
            return;

        Try.Run(() =>
        {
            // ReSharper disable once CanReplaceCastWithVariableType
            using var key = (RegistryKey?)Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\AppUserModelId\{AppId}");
            if (key == null) return;

            key.SetValue("DisplayName", "VRCVideoCacher");
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                key.SetValue("IconUri", iconPath);
            else if (!string.IsNullOrEmpty(Environment.ProcessPath))
                key.SetValue("IconUri", Environment.ProcessPath);

            _registeredIconPath = iconPath;
            _isAumidRegistered = true;
        });
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

    [SupportedOSPlatform("windows")]
    private static (string? ScriptPath, string? IconPath) EnsureWindowsAssets()
    {
        if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath) &&
            !string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
        {
            return (_scriptPath, _iconPath);
        }

        lock (ResourceLock)
        {
            if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath) &&
                !string.IsNullOrEmpty(_iconPath) && File.Exists(_iconPath))
            {
                return (_scriptPath, _iconPath);
            }

            _scriptPath = EnsureAssetExtracted("ToastNotification.ps1", "avares://VRCVideoCacher/Assets/ToastNotification.ps1");
            _iconPath = EnsureAssetExtracted("icon.ico", "avares://VRCVideoCacher/Assets/icon.ico");

            return (_scriptPath, _iconPath);
        }
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
                var (scriptPath, iconPath) = EnsureWindowsAssets();
                if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
                    return;

                EnsureAppUserModelIdRegistered(iconPath);

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
                        scriptPath,
                        "-AppId",
                        AppId,
                        "-Title",
                        Truncate(title, 128),
                        "-Message",
                        Truncate(message, 1024)
                    },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    psi.ArgumentList.Add("-IconUri");
                    psi.ArgumentList.Add(iconPath);
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

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}