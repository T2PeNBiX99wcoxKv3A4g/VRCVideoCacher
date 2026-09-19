using Serilog;

namespace VRCVideoCacher.Utils;

public interface ILog
{
    public ILogger Log { get; }
}