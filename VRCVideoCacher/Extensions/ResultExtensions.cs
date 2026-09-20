using System.Diagnostics;
using System.Runtime.ExceptionServices;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
[StackTraceHidden]
public static class ResultExtensions
{
    extension<T>(Result<T> result)
    {
        public Result<T> OnSuccess([InstantHandle] Action<T> action)
        {
            if (result.IsSuccess)
                action(result.Value!);
            return result;
        }

        public Result<T> OnFailure([InstantHandle] Action<Exception> action)
        {
            if (!result.IsSuccess)
                action(result.Exception!);
            return result;
        }

        private void ThrowOnFailure()
        {
            if (result.Exception is not { } exception) return;
            ExceptionDispatchInfo.Throw(exception);
        }

        public TResult Match<TResult>([InstantHandle] Func<T, TResult> onSuccess,
            [InstantHandle] Func<Exception, TResult> onFailure) =>
            result.IsSuccess ? onSuccess(result.Value!) : onFailure(result.Exception!);

        public T? GetOrNull() => result.IsSuccess ? result.Value : default;

        public T GetOrElse([InstantHandle] Func<Exception, T> onFailure) =>
            result.IsSuccess ? result.Value! : onFailure(result.Exception!);

        public T GetOrThrow()
        {
            if (result.IsSuccess)
                return result.Value!;
            result.ThrowOnFailure();
            return default!;
        }

        public Result<TU> Select<TU>([InstantHandle] Func<T, TU> selector) => result.IsSuccess
            ? Result<TU>.Success(selector(result.Value!))
            : Result<TU>.Failure(result.Exception!);

        public Result<TU> SelectMany<TU>([InstantHandle] Func<T, Result<TU>> binder) =>
            result.IsSuccess ? binder(result.Value!) : Result<TU>.Failure(result.Exception!);

        public Result<TV> SelectMany<TU, TV>([InstantHandle] Func<T, Result<TU>> binder,
            [InstantHandle] Func<T, TU, TV> projector) => result.IsSuccess
            ? binder(result.Value!).Select(u => projector(result.Value!, u))
            : Result<TV>.Failure(result.Exception!);
    }

    extension<T>(Task<Result<T>> task)
    {
        public async Task<Result<T>> OnSuccess([InstantHandle(RequireAwait = true)] Func<T, Task> action)
        {
            var result = await task.ConfigureAwait(false);

            if (result.IsSuccess)
                await action(result.Value!).ConfigureAwait(false);

            return result;
        }

        public async Task<Result<T>> OnFailure([InstantHandle(RequireAwait = true)] Func<Exception, Task> action)
        {
            var result = await task.ConfigureAwait(false);

            if (!result.IsSuccess)
                await action(result.Exception!).ConfigureAwait(false);

            return result;
        }

        public async Task<TResult> Match<TResult>([InstantHandle(RequireAwait = true)] Func<T, Task<TResult>> onSuccess,
            [InstantHandle(RequireAwait = true)] Func<Exception, Task<TResult>> onFailure)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? await onSuccess(result.Value!).ConfigureAwait(false)
                : await onFailure(result.Exception!).ConfigureAwait(false);
        }

        public async Task<T?> GetOrNull()
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess ? result.Value : default;
        }

        public async Task<T> GetOrElse([InstantHandle(RequireAwait = true)] Func<Exception, Task<T>> onFailure)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? result.Value!
                : await onFailure(result.Exception!).ConfigureAwait(false);
        }

        public async Task<T> GetOrThrow()
        {
            var result = await task.ConfigureAwait(false);
            if (result.IsSuccess)
                return result.Value!;
            result.ThrowOnFailure();
            return default!;
        }

        public async Task<Result<TU>> Select<TU>([InstantHandle(RequireAwait = true)] Func<T, Task<TU>> selector)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? Result<TU>.Success(await selector(result.Value!).ConfigureAwait(false))
                : Result<TU>.Failure(result.Exception!);
        }

        public async Task<Result<TU>> SelectMany<TU>(
            [InstantHandle(RequireAwait = true)] Func<T, Task<Result<TU>>> binder)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? await binder(result.Value!).ConfigureAwait(false)
                : Result<TU>.Failure(result.Exception!);
        }

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