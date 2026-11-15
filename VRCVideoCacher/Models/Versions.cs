using JetBrains.Annotations;
using Newtonsoft.Json;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Models;

[Singleton]
public partial class Versions
{
    private static readonly string VersionPath = Path.Join(Program.DataPath, "version.json");
    [PublicAPI] public readonly VersionJson CurrentVersion = new();

    private Versions()
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

    [PublicAPI]
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