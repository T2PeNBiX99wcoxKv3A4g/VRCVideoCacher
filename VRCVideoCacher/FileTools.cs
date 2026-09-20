using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using Microsoft.Win32;
using ValveKeyValue;
using VRCVideoCacher.Extensions;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher;

public partial class FileTools : Singleton<FileTools>
{
    private readonly string? _ytdlPathVrc;
    private readonly string? _backupPathVrc;
    private readonly string? _ytdlPathReso;
    private readonly string? _backupPathReso;

    private static readonly ImmutableList<string> SteamPaths =
    [
        ".var/app/com.valvesoftware.Steam/data/Steam", ".steam/steam", ".steam/debian-installation", ".local/share/Steam"
    ];

    private static readonly ImmutableList<string> SteamRegistryPaths =
    [
        @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam"
    ];

    private const string ResoniteAppId = "2519830";
    private const string VrcAppId = "438100";

    public FileTools()
    {
        var resoPath = !string.IsNullOrEmpty(ConfigManager.Config.ResonitePath)
            ? ConfigManager.Config.ResonitePath
            : GetAppLibraryPath(ResoniteAppId, ConfigManager.Config.PatchResonite)
                ?.Select(path => Path.Join(path, "steamapps", "common", "Resonite")).Where(Path.Exists).First();
        if (!string.IsNullOrEmpty(resoPath))
        {
            _ytdlPathReso = Path.Join(resoPath, "RuntimeData", OperatingSystem.IsLinux() ? "yt-dlp_linux" : "yt-dlp.exe");
            _backupPathReso = $"{_ytdlPathReso}.bkp";
        }

        string? localLowPath;
        if (OperatingSystem.IsWindows())
            localLowPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low";
        else if (OperatingSystem.IsLinux())
        {
            var compatPath = GetCompatPath(VrcAppId);
            if (compatPath == null)
            {
                Log.Error("Unable to find VRChat compatdata path, VRChat patching will not work.");
                return;
            }

            localLowPath = Path.Join(compatPath, "pfx/drive_c/users/steamuser/AppData/LocalLow");
        }
        else
            throw new NotImplementedException("Unknown platform");

        var vrcPath = Path.Join(localLowPath, "VRChat", "VRChat", "Tools", "yt-dlp.exe");
        if (!File.Exists(vrcPath))
            Log.Warning("YT-DLP not found at expected VRChat path: {Path}", vrcPath);
        else
        {
            _ytdlPathVrc = vrcPath;
            _backupPathVrc = $"{vrcPath}.bkp";
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? GetSteamInstallPathWindows() => !OperatingSystem.IsWindows()
        ? null
        : SteamRegistryPaths.Select(registryPath => Registry.GetValue(registryPath, "InstallPath", null) as string)
            .FirstOrDefault(installPath => !string.IsNullOrWhiteSpace(installPath) && Directory.Exists(installPath));

    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private List<string>? GetAppLibraryPath(string appid, bool isEnabled)
    {
        string vdfPath;
        if (OperatingSystem.IsWindows())
        {
            var steamInstallPath = GetSteamInstallPathWindows();
            if (string.IsNullOrEmpty(steamInstallPath))
            {
                if (isEnabled)
                    Log.Error("GetAppLibraryPath: Unable to find Steam installation directory");
                else
                    Log.Warning("GetAppLibraryPath: Unable to find Steam installation directory");
                return null;
            }

            vdfPath = Path.Join(steamInstallPath, "steamapps", "libraryfolders.vdf");
        }
        else if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var vdfPaths = SteamPaths
                .Select(path => Path.Join(home, path, "steamapps", "libraryfolders.vdf"))
                .Where(Path.Exists).ToArray();

            if (vdfPaths.Length == 0)
            {
                if (isEnabled)
                    Log.Error("GetAppLibraryPath: Couldn't find libraryfolders.vdf!");
                else
                    Log.Warning("GetAppLibraryPath: Couldn't find libraryfolders.vdf!");
                return null;
            }

            vdfPath = vdfPaths.First();
        }
        else
        {
            Log.Error("GetAppLibraryPath: Unsupported operating system {OperatingSystem}",
                Environment.OSVersion.Platform);
            return null;
        }

        Log.Debug("GetAppLibraryPath: Using VDF path {VdfPath}", vdfPath);

        List<string> libraryPaths = [];
        var result = Try.Run(() =>
        {
            var stream = File.OpenRead(vdfPath);
            KVObject data = KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Deserialize(stream);
            foreach (var (_, folder) in data)
            {
                var apps = folder["apps"];
                if (apps.Any(app => app.Key == appid))
                    libraryPaths.Add(folder["path"].ToString(CultureInfo.InvariantCulture));
            }

            return true;
        }).GetOrElse((ex) =>
        {
            if (isEnabled)
                Log.Error("GetAppLibraryPath: Exception while reading libraryfolders.vdf: {Error}", ex.Message);
            else
                Log.Warning("GetAppLibraryPath: Exception while reading libraryfolders.vdf: {Error}", ex.Message);
            return false;
        });

        if (!result) return null;

        libraryPaths = [.. libraryPaths.Where(Path.Exists)];

        // ReSharper disable once InvertIf
        if (libraryPaths.Count == 0)
        {
            if (isEnabled)
                Log.Error("Failed to find library path for Steam app {AppId}.", appid);
            else
                Log.Warning("Failed to find library path for Steam app {AppId}.", appid);
            return null;
        }

        return libraryPaths;
    }

    [SupportedOSPlatform("linux")]
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    private string? GetCompatPath(string appid)
    {
        var libraryPaths = GetAppLibraryPath(appid, ConfigManager.Config.PatchVrChat);
        var paths = libraryPaths?.Select(path => Path.Join(path, "steamapps", "compatdata", appid))
            .Where(Path.Exists)
            .ToImmutableList();
        return paths?.Count > 0 ? paths.First() : null;
    }

    [PublicAPI]
    public string? LocateFile2(string filename)
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

    [PublicAPI]
    public void MarkFileExecutable2(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");

        if (OperatingSystem.IsWindows()) return;
        var mode = File.GetUnixFileMode(path);
        mode |= UnixFileMode.UserExecute;
        File.SetUnixFileMode(path, mode);
    }

    [PublicAPI]
    public void BackupAllYtdl2()
    {
        if (ConfigManager.Config.PatchVrChat)
        {
            Log.Information("Patching VRChat yt-dlp");
            if (!BackupAndReplaceYtdl(_ytdlPathVrc, _backupPathVrc, false))
                Log.Error("Can't find VRC data, it may not be installed. {Path}", _ytdlPathVrc);
        }

        // ReSharper disable once InvertIf
        if (ConfigManager.Config.PatchResonite)
        {
            Log.Information("Patching Resonite yt-dlp");
            if (!BackupAndReplaceYtdl(_ytdlPathReso, _backupPathReso, OperatingSystem.IsLinux()))
                Log.Warning("Can't find Resonite data, it may not be installed. {Path}", _ytdlPathReso);
        }
    }

    [PublicAPI]
    public void RestoreAllYtdl2()
    {
        RestoreYtdl(_ytdlPathVrc, _backupPathVrc);
        RestoreYtdl(_ytdlPathReso, _backupPathReso);
    }

    private bool BackupAndReplaceYtdl(string? ytdlPath, string? backupPath, bool useLinuxStub)
    {
        if (string.IsNullOrEmpty(ytdlPath) ||
            string.IsNullOrEmpty(backupPath) ||
            !Directory.Exists(Path.GetDirectoryName(ytdlPath)))
            return false;

        if (File.Exists(ytdlPath))
        {
            var hash = Program.ComputeBinaryContentHash(File.ReadAllBytes(ytdlPath));
            if (hash == Program.GetYtdlpHash(useLinuxStub))
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

        using var stream = Program.GetYtDlpStub(useLinuxStub);
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

    private void RestoreYtdl(string? ytdlPath, string? backupPath)
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