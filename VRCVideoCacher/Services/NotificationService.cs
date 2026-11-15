using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using Avalonia.Platform;
using Avalonia.Threading;
using JetBrains.Annotations;
using Microsoft.Win32;
using Serilog;
using Swan;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;
using OperatingSystem = System.OperatingSystem;
#if WINDOWS_DESKTOP
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
#endif

namespace VRCVideoCacher.Services;

public static class NotificationService
{
    private const string AppId = "VRCVideoCacher";
    private static readonly ILogger Log = Program.Logger.ForContext(typeof(NotificationService));
    private static readonly string? IconPath;
#if WINDOWS_DESKTOP
    // Windows raises dismissal/failure on its own schedule; keeping the COM object alive until then
    // prevents a short-lived toast from being collected before it is shown.
    private static readonly List<ToastNotification> LiveToasts = [];
    private static readonly Lock ToastGate = new();
#endif

    static NotificationService()
    {
        if (!OperatingSystem.IsWindows()) return;
        IconPath = EnsureAssetExtracted("icon.ico", "avares://VRCVideoCacher/Assets/icon.ico");
#if WINDOWS_DESKTOP
        EnsureAppIdRegistered();
#endif
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

#if WINDOWS_DESKTOP
    /// <summary>
    /// Unpackaged desktop apps need an AppUserModelID entry before Windows will accept a toast from
    /// them. This is the same registration the old PowerShell helper performed.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void EnsureAppIdRegistered()
    {
        Try.Run(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\AppUserModelId\{AppId}");
            if (key == null) return;

            key.SetValue("DisplayName", "VRCVideoCacher");

            var icon = !string.IsNullOrEmpty(IconPath) && File.Exists(IconPath)
                ? IconPath
                : Environment.ProcessPath;
            if (!string.IsNullOrEmpty(icon))
                key.SetValue("IconUri", icon);
        }).OnFailure(ex => Log.Debug(ex, "Could not register the toast AppUserModelId"));
    }
#endif

    [PublicAPI]
    public static void ShowErrorNotification(string title, string message) => ShowNotification(title, message);

    [PublicAPI]
    public static void ShowNotification(string title, string message, bool isError = true)
    {
#if WINDOWS_DESKTOP
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            ShowWindowsToastNotification(title, message);
            return;
        }
#endif
        if (OperatingSystem.IsLinux())
            ShowLinuxNotification(title, message, isError);
    }

#if WINDOWS_DESKTOP
    [SupportedOSPlatform("windows10.0.10240")]
    private static void ShowWindowsToastNotification(string title, string message)
    {
        // WinRT toasts are COM objects; running them on the Avalonia UI thread (STA) keeps repeat
        // notifications reliable. A unique tag/group also stops Windows from coalescing a later toast
        // into the previous one when the text is identical.
        var tag = Guid.NewGuid().ToString("N")[..16];

        Dispatcher.UIThread.Post(() =>
        {
            Try.Run(() =>
            {
                var escapedTitle = SecurityElement.Escape(title.Truncate(128, "...") ?? string.Empty) ?? string.Empty;
                var escapedMessage = SecurityElement.Escape(message.Truncate(1024, "...") ?? string.Empty)
                                     ?? string.Empty;

                var xml = $"""
                           <toast>
                               <visual>
                                   <binding template="ToastGeneric">
                                       <text>{escapedTitle}</text>
                                       <text>{escapedMessage}</text>
                                   </binding>
                               </visual>
                           </toast>
                           """;

                var document = new XmlDocument();
                document.LoadXml(xml);
                var toast = new ToastNotification(document)
                {
                    Tag = tag,
                    Group = "VRCVideoCacher"
                };
                toast.Dismissed += (_, _) => ForgetToast(toast);
                toast.Failed += (_, _) => ForgetToast(toast);

                lock (ToastGate)
                {
                    LiveToasts.Add(toast);
                    if (LiveToasts.Count > 16)
                        LiveToasts.RemoveAt(0);
                }

                ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
            }).OnFailure(ex => Log.Warning(ex, "Failed to show Windows toast notification"));
        });
    }

    [SupportedOSPlatform("windows10.0.10240")]
    private static void ForgetToast(ToastNotification toast)
    {
        lock (ToastGate)
            LiveToasts.Remove(toast);
    }
#endif

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
