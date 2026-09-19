using JetBrains.Annotations;
using Serilog;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public interface ISingleton
{
}

[PublicAPI]
public abstract class Singleton<T> : ISingleton where T : Singleton<T>, new()
{
    protected readonly ILogger Log = Program.Logger.ForContext<T>();
    public static T? InstanceInternal { get; private set; } = new();
    public static T Instance { get; } = InstanceInternal!;
    public static bool IsInitialized => InstanceInternal != null;
}