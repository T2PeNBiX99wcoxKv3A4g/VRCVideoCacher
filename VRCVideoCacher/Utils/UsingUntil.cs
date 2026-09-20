using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public static class UsingUntil
{
    public static UsingUntilScope<T> Run<T>([InstantHandle] Func<T> usingFunc, [InstantHandle] Action<T> disposeAction)
    {
        var usingValue = usingFunc();
        return new(usingValue, disposeAction);
    }

    public sealed class UsingUntilScope<T>(T usingValue, Action<T> disposeAction) : IDisposable
    {
        public void Dispose()
        {
            disposeAction(usingValue);
        }
    }
}