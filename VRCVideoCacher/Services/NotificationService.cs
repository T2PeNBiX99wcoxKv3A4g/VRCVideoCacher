using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia.Platform;
using JetBrains.Annotations;
using Microsoft.Win32;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public static class NotificationService
{
    private const string AppId = "VRCVideoCacher";
    private static bool _isAumidRegistered;
    private static string? _scriptPath;
    private static readonly object ScriptLock = new();

    [SupportedOSPlatform("windows")]
    private static void EnsureAppUserModelIdRegistered()
    {
        if (_isAumidRegistered)
            return;

        Try.Run(() =>
        {
            // ReSharper disable once CanReplaceCastWithVariableType
            using var key = (RegistryKey?)Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\AppUserModelId\{AppId}");
            if (key == null) return;

            key.SetValue("DisplayName", "VRCVideoCacher");
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                key.SetValue("IconUri", Environment.ProcessPath);

            _isAumidRegistered = true;
        });
    }

    [SupportedOSPlatform("windows")]
    private static string? EnsureScriptExtracted()
    {
        if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath))
            return _scriptPath;

        lock (ScriptLock)
        {
            if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath))
                return _scriptPath;

            try
            {
                var targetDir = !string.IsNullOrEmpty(Program.UtilsPath)
                    ? Program.UtilsPath
                    : Path.Combine(Path.GetTempPath(), "VRCVideoCacher");

                Directory.CreateDirectory(targetDir);
                var targetFile = Path.Combine(targetDir, "ToastNotification.ps1");

                using var resourceStream = AssetLoader.Open(new Uri("avares://VRCVideoCacher/Assets/ToastNotification.ps1"));
                using var fileStream = File.Create(targetFile);
                resourceStream.CopyTo(fileStream);

                _scriptPath = targetFile;
                return _scriptPath;
            }
            catch
            {
                return null;
            }
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
                EnsureAppUserModelIdRegistered();

                var scriptPath = EnsureScriptExtracted();
                if (string.IsNullOrEmpty(scriptPath) || !File.Exists(scriptPath))
                    return;

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    ArgumentList =
                    {
                        "-NoProfile",
                        "-NonInteractive",
                        "-ExecutionPolicy", "Bypass",
                        "-File", scriptPath,
                        "-AppId", AppId,
                        "-Title", Truncate(title, 128),
                        "-Message", Truncate(message, 1024)
                    },
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

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