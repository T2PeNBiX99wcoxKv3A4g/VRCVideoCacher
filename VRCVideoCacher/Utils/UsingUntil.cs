using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public static class UsingUntil
{
    public static IDisposable Run<T>([InstantHandle] Func<T> usingFunc, [InstantHandle] Action<T> disposeAction)
    {
        var usingValue = usingFunc();
        return new UsingUntilScope<T>(usingValue, disposeAction);
    }

    public static IDisposable Run([InstantHandle] Action disposeAction) => new UsingUntilScopeWithoutUsing(disposeAction);

    public static async Task<IDisposable> RunAsync([InstantHandle(RequireAwait = true)] Func<Task> usingFunc,
        [InstantHandle] Action disposeAction)
    {
        await usingFunc().ConfigureAwait(false);
        return new UsingUntilScopeWithoutUsing(disposeAction);
    }

    public static async Task<IDisposable> RunAsync<T>([InstantHandle(RequireAwait = true)] Func<Task<T>> usingFunc,
        [InstantHandle] Action<T> disposeAction)
    {
        var usingValue = await usingFunc().ConfigureAwait(false);
        return new UsingUntilScope<T>(usingValue, disposeAction);
    }

    public sealed class UsingUntilScope<T>(T usingValue, Action<T> disposeAction) : IDisposable
    {
        public void Dispose()
        {
            disposeAction(usingValue);
        }
    }

    public sealed class UsingUntilScopeWithoutUsing(Action disposeAction) : IDisposable
    {
        public void Dispose()
        {
            disposeAction();
        }
    }
}