using JetBrains.Annotations;
using Serilog;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class LogExtensions
{
    extension(ILog ilog)
    {
        public ILogger Logger => ilog.Log;
    }
}