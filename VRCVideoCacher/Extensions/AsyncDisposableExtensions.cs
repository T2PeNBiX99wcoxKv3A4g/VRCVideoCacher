using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class AsyncDisposableExtensions
{
    extension(IAsyncDisposable disposable)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async ValueTask<bool> TryDisposeAsync() => (await Try.Run(async () => await disposable.DisposeAsync())).IsSuccess;
    }
}