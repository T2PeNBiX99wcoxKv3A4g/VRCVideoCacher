using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
[StackTraceHidden]
public static class ResultExtensions
{
    extension<T>(Result<T> result)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T> OnSuccess([InstantHandle] Action<T> action)
        {
            if (result.IsSuccess)
                action(result.Value!);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T> OnFailure([InstantHandle] Action<Exception> action)
        {
            if (!result.IsSuccess)
                action(result.Exception!);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T> OnFinally([InstantHandle] Action action)
        {
            action();
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowOnFailure()
        {
            if (result.Exception is not { } exception) return;
            exception.Throw();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TResult Match<TResult>([InstantHandle] Func<T, TResult> onSuccess,
            [InstantHandle] Func<Exception, TResult> onFailure) =>
            result.IsSuccess ? onSuccess(result.Value!) : onFailure(result.Exception!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T? GetOrNull() => result.IsSuccess ? result.Value : default;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetOrElse([InstantHandle] Func<Exception, T> onFailure) =>
            result.IsSuccess ? result.Value! : onFailure(result.Exception!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetOrThrow()
        {
            if (result.IsSuccess)
                return result.Value!;
            result.ThrowOnFailure();
            return default!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TU> Select<TU>([InstantHandle] Func<T, TU> selector) => result.IsSuccess
            ? Result<TU>.Success(selector(result.Value!))
            : Result<TU>.Failure(result.Exception!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TU> SelectMany<TU>([InstantHandle] Func<T, Result<TU>> binder) =>
            result.IsSuccess ? binder(result.Value!) : Result<TU>.Failure(result.Exception!);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TV> SelectMany<TU, TV>([InstantHandle] Func<T, Result<TU>> binder,
            [InstantHandle] Func<T, TU, TV> projector) => result.IsSuccess
            ? binder(result.Value!).Select(u => projector(result.Value!, u))
            : Result<TV>.Failure(result.Exception!);
    }

    extension<T>(Task<Result<T>> task)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Result<T>> OnSuccess([InstantHandle(RequireAwait = true)] Func<T, Task> action)
        {
            var result = await task.ConfigureAwait(false);

            if (result.IsSuccess)
                await action(result.Value!).ConfigureAwait(false);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Result<T>> OnFailure([InstantHandle(RequireAwait = true)] Func<Exception, Task> action)
        {
            var result = await task.ConfigureAwait(false);

            if (!result.IsSuccess)
                await action(result.Exception!).ConfigureAwait(false);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Result<T>> OnFinally([InstantHandle] Func<Task> action)
        {
            var result = await task.ConfigureAwait(false);
            await action();
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<TResult> Match<TResult>([InstantHandle(RequireAwait = true)] Func<T, Task<TResult>> onSuccess,
            [InstantHandle(RequireAwait = true)] Func<Exception, Task<TResult>> onFailure)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? await onSuccess(result.Value!).ConfigureAwait(false)
                : await onFailure(result.Exception!).ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T?> GetOrNull()
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess ? result.Value : default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> GetOrElse([InstantHandle(RequireAwait = true)] Func<Exception, Task<T>> onFailure)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? result.Value!
                : await onFailure(result.Exception!).ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<T> GetOrThrow()
        {
            var result = await task.ConfigureAwait(false);
            if (result.IsSuccess)
                return result.Value!;
            result.ThrowOnFailure();
            return default!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Result<TU>> Select<TU>([InstantHandle(RequireAwait = true)] Func<T, Task<TU>> selector)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? Result<TU>.Success(await selector(result.Value!).ConfigureAwait(false))
                : Result<TU>.Failure(result.Exception!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Result<TU>> SelectMany<TU>(
            [InstantHandle(RequireAwait = true)] Func<T, Task<Result<TU>>> binder)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? await binder(result.Value!).ConfigureAwait(false)
                : Result<TU>.Failure(result.Exception!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public async Task<Result<TV>> SelectMany<TU, TV>(
            [InstantHandle(RequireAwait = true)] Func<T, Task<Result<TU>>> binder,
            [InstantHandle(RequireAwait = true)] Func<T, TU, Task<TV>> projector)
        {
            var result = await task.ConfigureAwait(false);

            if (!result.IsSuccess)
                return Result<TV>.Failure(result.Exception!);

            var inner = await binder(result.Value!).ConfigureAwait(false);
            return inner.IsSuccess
                ? Result<TV>.Success(await projector(result.Value!, inner.Value!).ConfigureAwait(false))
                : Result<TV>.Failure(inner.Exception!);
        }
    }
}