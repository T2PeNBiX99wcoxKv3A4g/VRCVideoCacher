using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class ProcessExtensions
{
    extension(Process process)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDispose() => Try.Run(process.Dispose).IsSuccess;
    }
}