using System.Runtime.Versioning;
using JetBrains.Annotations;
using ShellLink;

namespace VRCVideoCacher.Utils;

public partial class AutoStartShortcut : Singleton<AutoStartShortcut>
{
    private static readonly byte[] ShortcutSignatureBytes = [.. "L\0\0\0"u8]; // signature for ShellLinkHeader
    private const string ShortcutName = "VRCVideoCacher";
    private const string SteamShortcutExtension = ".url";
    private const string SteamGameUrl = "steam://rungameid/4296960";
    private const string ExeShortcutExtension = ".lnk";

    private const string ShortcutContent =
        $"[{{000214A0-0000-0000-C000-000000000046}}]\r\n[InternetShortcut]\r\nURL={SteamGameUrl}\r\n";

    private bool? _doesVrcxSupportSteamShortcut;

    [SupportedOSPlatform("windows")]
    [PublicAPI]
    public void TryUpdateShortcutPath2()
    {
        RemoveLegacyShortcut(true);

        var shortcut = GetOurShortcut();
        if (shortcut == null)
            return;

        if (ShouldUseSteamShortcut())
        {
            var currentContent = File.ReadAllText(shortcut);
            if (currentContent == ShortcutContent)
                return;

            Log.Information("Updating VRCX autostart shortcut URL...");
            File.WriteAllText(shortcut, ShortcutContent);
        }
        else
        {
            var info = Shortcut.ReadFromFile(shortcut);
            if (info.LinkTargetIDList.Path == Environment.ProcessPath &&
                info.StringData.WorkingDir == Path.GetDirectoryName(Environment.ProcessPath))
                return;

            Log.Information("Updating VRCX autostart shortcut path...");
            info.LinkTargetIDList.Path = Environment.ProcessPath;
            info.StringData.WorkingDir = Path.GetDirectoryName(Environment.ProcessPath);
            info.WriteToFile(shortcut);
        }
    }

    [SupportedOSPlatform("windows")]
    private bool StartupEnabled() => !string.IsNullOrEmpty(GetOurShortcut());

    [SupportedOSPlatform("windows")]
    [PublicAPI]
    public void CreateShortcut2()
    {
        if (StartupEnabled())
            return;

        RemoveLegacyShortcut(false);

        Log.Information("Adding VRCVideoCacher to VRCX autostart...");
        var path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX", "startup");
        var shortcutPath = Path.Join(path,
            $"{ShortcutName}{(_doesVrcxSupportSteamShortcut == true ? SteamShortcutExtension : ExeShortcutExtension)}");
        if (!Directory.Exists(path))
        {
            Log.Information("VRCX isn't installed");
            return;
        }

        if (ShouldUseSteamShortcut())
            File.WriteAllText(shortcutPath, ShortcutContent);
        else
        {
            var shortcut = new Shortcut
            {
                LinkTargetIDList = new()
                {
                    Path = Environment.ProcessPath
                },
                StringData = new()
                {
                    WorkingDir = Path.GetDirectoryName(Environment.ProcessPath)
                }
            };
            shortcut.WriteToFile(shortcutPath);
        }
    }

    [SupportedOSPlatform("windows")]
    private void RemoveLegacyShortcut(bool createIfAnyFound)
    {
        var shortcutPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX",
            "startup");
        if (!Directory.Exists(shortcutPath))
            return;

        var shortcuts = FindShortcutFiles(shortcutPath);

        var legacyExtension = ShouldUseSteamShortcut() ? ExeShortcutExtension : SteamShortcutExtension;
        var foundLegacy = false;
        foreach (var shortCut in shortcuts.Where(shortCut =>
                     shortCut.Contains(ShortcutName) &&
                     shortCut.EndsWith(legacyExtension, StringComparison.OrdinalIgnoreCase)))
        {
            foundLegacy = true;
            Log.Information("Removing alternate shortcut {ShortCut}", shortCut);
            File.Delete(shortCut);
        }

        if (createIfAnyFound && foundLegacy)
            CreateShortcut();
    }

    [SupportedOSPlatform("windows")]
    private string? GetOurShortcut()
    {
        var shortcutPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VRCX",
            "startup");
        if (!Directory.Exists(shortcutPath))
            return null;

        var shortcuts = FindShortcutFiles(shortcutPath);
        return shortcuts.FirstOrDefault(shortCut =>
            shortCut.Contains(ShortcutName) && shortCut.EndsWith(
                _doesVrcxSupportSteamShortcut == true ? SteamShortcutExtension : ExeShortcutExtension,
                StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> FindShortcutFiles(string folderPath)
    {
        var directoryInfo = new DirectoryInfo(folderPath);
        var files = directoryInfo.GetFiles();

        return
        [
            .. from file in files
            where file.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase) || IsShortcutFile(file.FullName)
            select file.FullName
        ];
    }

    private static bool IsShortcutFile(string filePath)
    {
        var headerBytes = new byte[4];
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fileStream.Length >= 4)
            fileStream.ReadExactly(headerBytes, 0, 4);

        return headerBytes.SequenceEqual(ShortcutSignatureBytes);
    }

    [SupportedOSPlatform("windows")]
    private bool ShouldUseSteamShortcut()
    {
#if STEAMRELEASE
        if (_doesVrcxSupportSteamShortcut.HasValue)
            return _doesVrcxSupportSteamShortcut.Value;
        if (TryGetVrcxVersion(out var version))
        {
            Log.Information("Detected VRCX version: {Version}", version);
            if (TryParseVrcxVersion(version, out var year, out var month, out var day))
                // Only don't use the steam shortcut if we know for certain that the VRCX version is older than 2026.3.14, which is when the url method was completed.
                // If we can't parse the version, or if it's newer than that, we'll just use the new method and assume it will work.
                if ((year, month, day) < (2026, 3, 14))
                    _doesVrcxSupportSteamShortcut = false;
        }

        _doesVrcxSupportSteamShortcut ??= true;
#else
        _doesVrcxSupportSteamShortcut ??= false;
#endif
        return _doesVrcxSupportSteamShortcut.Value;
    }
}