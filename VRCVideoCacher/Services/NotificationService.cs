using System.Diagnostics;
using System.Runtime.InteropServices;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Services;

public static partial class NotificationService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIM_SETVERSION = 0x00000004;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_NONE = 0x00000000;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;
    private const uint NIIF_ERROR = 0x00000003;
    private const uint NIIF_LARGE_ICON = 0x00000020;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    public static void ShowErrorNotification(string title, string message)
    {
        ShowNotification(title, message, true);
    }

    public static void ShowNotification(string title, string message, bool isError = true)
    {
        if (OperatingSystem.IsWindows())
            ShowWindowsNotification(title, message, isError);
        else if (OperatingSystem.IsLinux())
            ShowLinuxNotification(title, message, isError);
    }

    private static void ShowWindowsNotification(string title, string message, bool isError)
    {
        Task.Run(() =>
        {
            Try.Run(() =>
            {
                var hWnd = CreateWindowExW(0, "STATIC", "VRCVideoCacher_Notification", 0, 0, 0, 0, 0, (IntPtr)(-3),
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (hWnd == IntPtr.Zero)
                    return;

                using (UsingUntil.Run(() => DestroyWindow(hWnd)))
                {
                    var nid = new NOTIFYICONDATA
                    {
                        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                        hWnd = hWnd,
                        uID = 2001,
                        uFlags = NIF_INFO | NIF_TIP,
                        szTip = "VRCVideoCacher",
                        szInfoTitle = Truncate(title, 63),
                        szInfo = Truncate(message, 255),
                        dwInfoFlags = (isError ? NIIF_ERROR : NIIF_INFO) | NIIF_LARGE_ICON,
                        uTimeoutOrVersion = NOTIFYICON_VERSION_4
                    };

                    Shell_NotifyIcon(NIM_ADD, ref nid);
                    Shell_NotifyIcon(NIM_SETVERSION, ref nid);

                    Thread.Sleep(10000);
                    Shell_NotifyIcon(NIM_DELETE, ref nid);
                }
            });
        });
    }

    private static void ShowLinuxNotification(string title, string message, bool isError)
    {
        Task.Run(() =>
        {
            Try.Run(() =>
            {
                Process.Start(new ProcessStartInfo
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