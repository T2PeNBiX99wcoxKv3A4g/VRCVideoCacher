using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using ShellLink;

namespace VRCVideoCacher.Utils;

public class AutoStartShortcut : Singleton<AutoStartShortcut>
{
    private static readonly byte[] ShortcutSignatureBytes = [.. "L\0\0\0"u8]; // signature for ShellLinkHeader
    private const string ShortcutName = "VRCVideoCacher";
    private const string SteamShortcutExtension = ".url";
    private const string SteamGameUrl = "steam://rungameid/4296960";
    private const string ExeShortcutExtension = ".lnk";

    private const string ShortcutContent =
        $"[{{000214A0-0000-0000-C000-000000000046}}]\r\n[InternetShortcut]\r\nURL={SteamGameUrl}\r\n";

    private static readonly ImmutableList<string> RegistryPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private bool? _doesVrcxSupportSteamShortcut;

    [SupportedOSPlatform("windows")]
    public void TryUpdateShortcutPath()
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

    private bool StartupEnabled() => !string.IsNullOrEmpty(GetOurShortcut());

    [SupportedOSPlatform("windows")]
    public void CreateShortcut()
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
                if (year <= 2026 && month <= 3 && day < 14)
                    _doesVrcxSupportSteamShortcut = false;
        }

        _doesVrcxSupportSteamShortcut ??= true;
#else
        _doesVrcxSupportSteamShortcut ??= false;
#endif
        return _doesVrcxSupportSteamShortcut.Value;
    }

    private bool TryParseVrcxVersion(string? version, out int year, out int month, out int day)
    {
        year = 0;
        month = 0;
        day = 0;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        try
        {
            if (version.Contains('T'))
            {
                var dateEnd = version.IndexOf('T');
                if (dateEnd > 0)
                {
                    var datePart = version.Substring(0, dateEnd);
                    var parts = datePart.Split('-');
                    if (parts.Length == 3 &&
                        int.TryParse(parts[0], out year) &&
                        int.TryParse(parts[1], out month) &&
                        int.TryParse(parts[2], out day))
                        return true;
                }
            }
            else if (version.Contains('.'))
            {
                var parts = version.Split('.');
                if (parts.Length >= 3 &&
                    int.TryParse(parts[0], out year) &&
                    int.TryParse(parts[1], out month) &&
                    int.TryParse(parts[2], out day))
                    return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing VRCX version: {Version}", version);
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private bool TryGetVrcxVersion(out string? version)
    {
        version = null;

        try
        {
            // Check Windows registry for installed applications
            foreach (var regPath in RegistryPaths)
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key == null)
                    continue;
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    var displayName = subKey?.GetValue("DisplayName") as string;
                    if (subKey == null || displayName == null ||
                        !displayName.Contains("VRCX", StringComparison.OrdinalIgnoreCase)) continue;
                    var installLocation = subKey.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        if (TryGetVrcxVersionFromFile(installLocation, out version))
                            return true;
                    }
                    else
                    {
                        // Try DisplayIcon as fallback
                        var displayIcon = subKey.GetValue("DisplayIcon") as string;
                        displayIcon = displayIcon?.Trim('"');
                        if (string.IsNullOrWhiteSpace(displayIcon) ||
                            !displayIcon.EndsWith("VRCX.ico", StringComparison.OrdinalIgnoreCase)) continue;
                        if (TryGetVrcxVersionFromFile(Path.GetDirectoryName(displayIcon), out version)) return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error searching registry for VRCX");
        }

        Process[]? processes = null;
        try
        {
            processes = Process.GetProcessesByName("VRCX");

            foreach (var proc in processes)
                try
                {
                    var vrcxPath = proc?.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(vrcxPath)) continue;
                    if (TryGetVrcxVersionFromFile(Path.GetDirectoryName(vrcxPath), out version)) return true;
                    break;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error accessing process module for VRCX");
                }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error searching for VRCX processes");
        }
        finally
        {
            if (processes != null)
                foreach (var proc in processes)
                    proc.Dispose();
        }

        return false;
    }

    private bool TryGetVrcxVersionFromFile(string? directory, out string? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        var filePath = Path.Join(directory, "Version");
        try
        {
            if (File.Exists(filePath))
            {
                var versionText = File.ReadAllText(filePath).Trim();
                if (!string.IsNullOrWhiteSpace(versionText))
                {
                    version = versionText;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error getting VRCX version from file: {FilePath}", filePath);
        }

        return false;
    }
}