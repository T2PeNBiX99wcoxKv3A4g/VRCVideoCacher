using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public static class UsingUntil
{
    public static IDisposable Run([InstantHandle] Action disposeAction) => new UsingUntilScope(disposeAction);

    public static IAsyncDisposable RunAsync([InstantHandle] Func<ValueTask> disposeAction) => new UsingUntilAsyncScope(disposeAction);

    public sealed class UsingUntilScope(Action disposeAction) : IDisposable
    {
        public void Dispose() => disposeAction();
    }
    
    public sealed class UsingUntilAsyncScope(Func<ValueTask> disposeAction) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => disposeAction();
    }
}