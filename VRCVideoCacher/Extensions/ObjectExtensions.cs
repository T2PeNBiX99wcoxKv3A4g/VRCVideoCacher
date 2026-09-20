using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
[StackTraceHidden]
public static class ObjectExtensions
{
    extension<T>(T value)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Let<TResult>([InstantHandle] Func<T, TResult> block) => block(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task<TResult> Let<TResult>([InstantHandle] Func<T, Task<TResult>> block) => block(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Also([InstantHandle] Action<T> block)
        {
            block(value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> Also([InstantHandle] Func<T, Task> block)
        {
            await block(value);
            return value;
        }
    }
}