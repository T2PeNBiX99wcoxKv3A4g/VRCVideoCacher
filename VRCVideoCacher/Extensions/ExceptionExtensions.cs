using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using JetBrains.Annotations;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class ExceptionExtensions
{
    extension(Exception exception)
    {
        [DoesNotReturn]
        [StackTraceHidden]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Throw()
        {
            ExceptionDispatchInfo.Throw(exception);
        }
    }
}