using DesktopNotifications;
using DesktopNotifications.FreeDesktop;
using DesktopNotifications.Windows;
using JetBrains.Annotations;

namespace VRCVideoCacher.Services;

public static class NotificationService
{
    private static readonly INotificationManager Manager = OperatingSystem.IsWindows()
        ? new WindowsNotificationManager()
        : new FreeDesktopNotificationManager();

    [PublicAPI]
    public static void ShowNotification(string title, string message)
    {
        Manager.ShowNotification(new()
        {
            Title = title,
            Body = message
        });
    }

    [PublicAPI]
    public static void ShowNotification(Notification notification, DateTimeOffset? expirationTime = null)
    {
        Manager.ShowNotification(notification, expirationTime);
    }
}