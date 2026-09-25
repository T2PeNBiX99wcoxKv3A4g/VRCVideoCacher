using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class DisposableExtensions
{
    extension(IDisposable disposable)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDispose() => Try.Run(disposable.Dispose).IsSuccess;
    }
}