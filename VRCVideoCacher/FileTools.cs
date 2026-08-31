using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Serilog;
using ValveKeyValue;

namespace VRCVideoCacher;

public class FileTools
{
    private const string ResoniteAppId = "2519830";
    private const string VrcAppId = "438100";
    private static readonly ILogger Log = Program.Logger.ForContext<FileTools>();
    private static readonly string? YtdlPathVrc;
    private static readonly string? BackupPathVrc;
    private static readonly string? YtdlPathReso;
    private static readonly string? BackupPathReso;

    private static readonly ImmutableList<string> SteamPaths =
        [".var/app/com.valvesoftware.Steam/data/Steam", ".steam/steam", ".local/share/Steam"];

    static FileTools()
    {
        var resoPath = !string.IsNullOrEmpty(ConfigManager.Config.ResonitePath)
            ? ConfigManager.Config.ResonitePath
            : GetAppLibraryPath(ResoniteAppId)?.Select(path => Path.Join(path, "steamapps", "common", "Resonite"))
                .Where(Path.Exists).First();

        if (!string.IsNullOrEmpty(resoPath))
        {
            YtdlPathReso = OperatingSystem.IsLinux()
                ? $"{resoPath}/RuntimeData/yt-dlp_linux"
                : $@"{resoPath}\RuntimeData\yt-dlp.exe";
            BackupPathReso = $"{YtdlPathReso}.bkp";
        }

        string? localLowPath;
        if (OperatingSystem.IsWindows())
            localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
        else if (OperatingSystem.IsLinux())
        {
            var compatPath = GetCompatPath(VrcAppId) ?? throw new("Unable to find VRChat compat data");
            localLowPath = Path.Join(compatPath, "pfx/drive_c/users/steamuser/AppData/LocalLow");
        }
        else
            throw new NotImplementedException("Unknown platform");

        var vrcPath = Path.Join(localLowPath, "VRChat/VRChat/Tools/yt-dlp.exe");
        if (!File.Exists(vrcPath))
            Log.Warning("YT-DLP not found at expected VRChat path: {Path}", vrcPath);
        else
        {
            YtdlPathVrc = vrcPath;
            BackupPathVrc = $"{vrcPath}.bkp";
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetSteamInstallPathWindows()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        string?[] registryPaths =
        [
            @"HKEY_CURRENT_USER\Software\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
        ];

        return registryPaths.Select(registryPath => Registry.GetValue(registryPath ?? "", "InstallPath", null) as string)
            .FirstOrDefault(installPath => !string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath));
    }

    private static List<string>? GetAppLibraryPath(string appid)
    {
        string steamPath;
        if (OperatingSystem.IsWindows())
        {
            var steamInstallPath = GetSteamInstallPathWindows();
            if (string.IsNullOrEmpty(steamInstallPath))
            {
                Log.Error("GetAppLibraryPath: Unable to find Steam installation directory");
                return null;
            }

            steamPath = steamInstallPath;
        }
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var steamPaths = SteamPaths.Select(path => Path.Join(home, path)).Where(Path.Exists).ToArray();
            if (steamPaths.Length == 0)
            {
                Log.Error("GetAppLibraryPath: Steam folder doesn't exist!");
                return null;
            }

            steamPath = steamPaths.First();
        }
        else
        {
            Log.Error("GetAppLibraryPath: Unsupported operating system {OperatingSystem}",
                Environment.OSVersion.Platform);
            return null;
        }

        Log.Debug("GetAppLibraryPath: Using steam path {SteamPath}", steamPath);

        List<string> libraryPaths = [];
        try
        {
            var stream = File.OpenRead(Path.Join(steamPath, "steamapps", "libraryfolders.vdf"));
            KVObject data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            foreach (var (_, folder) in data)
            {
                // var label = folder["label"]?.ToString(CultureInfo.InvariantCulture);
                // var name = string.IsNullOrEmpty(label) ? folder.Name : label;
                // See https://github.com/ValveResourceFormat/ValveKeyValue/issues/30#issuecomment-1581924891
                var apps = folder["apps"];
                if (apps.Any(app => app.Key == appid))
                    libraryPaths.Add(folder["path"].ToString(CultureInfo.InvariantCulture));
            }
        }
        catch (Exception e)
        {
            Log.Error("GetAppLibraryPath: Exception while reading libraryfolders.vdf: {Error}", e.Message);
            return null;
        }

        libraryPaths = [.. libraryPaths.Where(Path.Exists)];

        // ReSharper disable once InvertIf
        if (libraryPaths.Count == 0)
        {
            Log.Error("Failed to find library path for Steam app {AppId}.", appid);
            return null;
        }

        return libraryPaths;
    }

    [SupportedOSPlatform("linux")]
    private static string? GetCompatPath(string appid)
    {
        var libraryPaths = GetAppLibraryPath(appid);
        var paths = libraryPaths?.Select(path => Path.Join(path, $"steamapps/compatdata/{appid}"))
            .Where(Path.Exists)
            .ToImmutableList();
        return paths?.Count > 0 ? paths.First() : null;
    }

    public static string? LocateFile(string filename)
    {
        var systemPath = Environment.GetEnvironmentVariable("PATH");
        if (systemPath == null) return null;

        var systemPaths = systemPath.Split(Path.PathSeparator);

        var paths = systemPaths
            .Select(path => Path.Join(path, filename))
            .Where(Path.Exists)
            .ToImmutableList();
        return paths.Count > 0 ? paths.First() : null;
    }

    public static void MarkFileExecutable(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(path);
        mode |= UnixFileMode.UserExecute;
        File.SetUnixFileMode(path, mode);
    }

    public static void BackupAllYtdl()
    {
        if (ConfigManager.Config.PatchVrChat)
            if (!BackupAndReplaceYtdl(YtdlPathVrc, BackupPathVrc, false))
                Log.Error("Can't find VRC data, it may not be installed. {Path}", YtdlPathVrc);
        // ReSharper disable once InvertIf
        if (ConfigManager.Config.PatchResonite)
            if (!BackupAndReplaceYtdl(YtdlPathReso, BackupPathReso, OperatingSystem.IsLinux()))
                Log.Warning("Can't find Resonite data, it may not be installed. {Path}", YtdlPathVrc);
    }

    public static void RestoreAllYtdl()
    {
        RestoreYtdl(YtdlPathVrc, BackupPathVrc);
        RestoreYtdl(YtdlPathReso, BackupPathReso);
    }

    private static bool BackupAndReplaceYtdl(string? ytdlPath, string? backupPath, bool linux)
    {
        if (string.IsNullOrEmpty(ytdlPath) ||
            string.IsNullOrEmpty(backupPath) ||
            !Directory.Exists(Path.GetDirectoryName(ytdlPath)))
            return false;

        if (File.Exists(ytdlPath))
        {
            var hash = Program.ComputeBinaryContentHash(File.ReadAllBytes(ytdlPath));
            if (hash == Program.GetYtDlpHash(linux))
            {
                Log.Information("YT-DLP is already patched.");
                return true;
            }

            if (File.Exists(backupPath))
            {
                File.SetAttributes(backupPath, FileAttributes.Normal);
                File.Delete(backupPath);
            }

            File.Move(ytdlPath, backupPath);
            Log.Information("Backed up YT-DLP.");
        }

        using var stream = Program.GetYtDlpStub(linux);
        using var fileStream = File.Create(ytdlPath);
        stream.CopyTo(fileStream);
        fileStream.Close();
        var attr = File.GetAttributes(ytdlPath);
        attr |= FileAttributes.ReadOnly;
        File.SetAttributes(ytdlPath, attr);
        MarkFileExecutable(ytdlPath);
        Log.Information("Patched YT-DLP.");
        return true;
    }

    private static void RestoreYtdl(string? ytdlPath, string? backupPath)
    {
        if (string.IsNullOrEmpty(ytdlPath) ||
            string.IsNullOrEmpty(backupPath) ||
            !File.Exists(backupPath))
            return;

        Log.Information("Restoring yt-dlp...");
        if (File.Exists(ytdlPath))
        {
            File.SetAttributes(ytdlPath, FileAttributes.Normal);
            File.Delete(ytdlPath);
        }

        File.Move(backupPath, ytdlPath);
        var attr = File.GetAttributes(ytdlPath);
        attr &= ~FileAttributes.ReadOnly;
        File.SetAttributes(ytdlPath, attr);
        Log.Information("Restored YT-DLP.");
    }
}