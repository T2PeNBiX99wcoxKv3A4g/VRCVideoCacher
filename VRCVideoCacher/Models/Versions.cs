using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Models;

public partial class Versions : Singleton<Versions>
{
    private static readonly string VersionPath = Path.Join(Program.DataPath, "version.json");
    public readonly VersionJson CurrentVersion = new();

    public Versions()
    {
        if (File.Exists(VersionPath))
            try
            {
                CurrentVersion = JsonConvert.DeserializeObject<VersionJson>(File.ReadAllText(VersionPath)) ??
                                 new VersionJson();
                return;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse version file, it may be corrupted. Recreating...");
            }

        Save();
    }

    public void Save()
    {
        File.WriteAllText(VersionPath, JsonConvert.SerializeObject(CurrentVersion, Formatting.Indented));
    }
}

public class VersionJson
{
    public string Ytdlp { get; set; } = string.Empty;
    public string Ffmpeg { get; set; } = string.Empty;
    public string Deno { get; set; } = string.Empty;
    public string BgUtil { get; set; } = string.Empty;
}