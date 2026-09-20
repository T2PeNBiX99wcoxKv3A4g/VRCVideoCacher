using System.Diagnostics;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class ProcessExtensions
{
    extension(Process process)
    {
        public bool TryDispose() => Try.Run(process.Dispose).IsSuccess;
    }
}