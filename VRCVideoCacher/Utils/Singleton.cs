using Serilog;

namespace VRCVideoCacher.Utils;

public interface ISingleton
{
}

public abstract class Singleton<T> : ISingleton where T : Singleton<T>, new()
{
    private static readonly Lazy<T> InstanceInternal = new(() => new(), LazyThreadSafetyMode.ExecutionAndPublication);
    protected static readonly ILogger Log = Program.Logger.ForContext<T>();

    public static T Instance => InstanceInternal.Value;
}