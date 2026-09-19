using JetBrains.Annotations;
using Serilog;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public interface ISingleton
{
    Type ThisType { get; }
}

[PublicAPI]
public abstract class Singleton<T> : ISingleton where T : Singleton<T>, new()
{
    private static readonly Lazy<T> InstanceInternal = new(() => new(), LazyThreadSafetyMode.ExecutionAndPublication);
    protected readonly ILogger Log = Program.Logger.ForContext<T>();

    public Type ThisType { get; } = typeof(T);

    public static T Instance => InstanceInternal.Value;
}