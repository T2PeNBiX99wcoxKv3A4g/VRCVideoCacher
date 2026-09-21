using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;
using Microsoft.Win32;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public static class NotificationService
{
    private const string AppId = "VRCVideoCacher";
    private static bool _isAumidRegistered;

    [SupportedOSPlatform("windows")]
    private static void EnsureAppUserModelIdRegistered()
    {
        if (_isAumidRegistered)
            return;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"SOFTWARE\Classes\AppUserModelId\{AppId}");
            if (key != null)
            {
                key.SetValue("DisplayName", "VRCVideoCacher");
                if (!string.IsNullOrEmpty(Environment.ProcessPath))
                    key.SetValue("IconUri", Environment.ProcessPath);
            }

            _isAumidRegistered = true;
        }
        catch
        {
            // Ignore registry errors
        }
    }

    public static void ShowErrorNotification(string title, string message)
    {
        ShowNotification(title, message, true);
    }

    public static void ShowNotification(string title, string message, bool isError = true)
    {
        if (OperatingSystem.IsWindows())
            ShowWindowsToastNotification(title, message, isError);
        else if (OperatingSystem.IsLinux())
            ShowLinuxNotification(title, message, isError);
    }

    private static void ShowWindowsToastNotification(string title, string message, bool isError)
    {
        Task.Run(() =>
        {
            Try.Run(() =>
            {
                EnsureAppUserModelIdRegistered();

                var escapedTitle = EscapeXml(Truncate(title, 128));
                var escapedMessage = EscapeXml(Truncate(message, 1024));

                var script = $@"
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null

$xml = New-Object Windows.Data.Xml.Dom.XmlDocument
$template = @""
<toast>
    <visual>
        <binding template=""""ToastGeneric"""">
            <text>{escapedTitle}</text>
            <text>{escapedMessage}</text>
        </binding>
    </visual>
</toast>
""@
$xml.LoadXml($template)
$toast = New-Object Windows.UI.Notifications.ToastNotification $xml
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier(""{AppId}"").Show($toast)
";

                var bytes = Encoding.Unicode.GetBytes(script);
                var encoded = Convert.ToBase64String(bytes);

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
            });
        });
    }

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

    private static string EscapeXml(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return SecurityElement.Escape(text) ?? string.Empty;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
    }
}