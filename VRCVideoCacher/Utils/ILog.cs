using JetBrains.Annotations;
using Serilog;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public interface ILog<T>
{
    protected ILogger Log { get; }
}