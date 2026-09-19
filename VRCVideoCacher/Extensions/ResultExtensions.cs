using System.Runtime.ExceptionServices;
using JetBrains.Annotations;
using VRCVideoCacher.Utils;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class ResultExtensions
{
    extension<T>(Result<T> result)
    {
        public Result<T> OnSuccess(Action<T> action)
        {
            if (result.IsSuccess)
                action(result.Value!);
            return result;
        }

        public Result<T> OnFailure(Action<ExceptionDispatchInfo> action)
        {
            if (!result.IsSuccess)
                action(result.ExceptionDispatchInfo!);
            return result;
        }

        public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<ExceptionDispatchInfo, TResult> onFailure) =>
            result.IsSuccess ? onSuccess(result.Value!) : onFailure(result.ExceptionDispatchInfo!);

        public T? GetOrNull() => result.IsSuccess ? result.Value : default;

        public T GetOrElse(Func<ExceptionDispatchInfo, T> onFailure) =>
            result.IsSuccess ? result.Value! : onFailure(result.ExceptionDispatchInfo!);

        public Result<TU> Select<TU>(Func<T, TU> selector) => result.IsSuccess
            ? Result<TU>.Success(selector(result.Value!))
            : Result<TU>.Failure(result.ExceptionDispatchInfo!);

        public Result<TU> SelectMany<TU>(Func<T, Result<TU>> binder) =>
            result.IsSuccess ? binder(result.Value!) : Result<TU>.Failure(result.ExceptionDispatchInfo!);

        public Result<TV> SelectMany<TU, TV>(Func<T, Result<TU>> binder, Func<T, TU, TV> projector) => result.IsSuccess
            ? binder(result.Value!).Select(u => projector(result.Value!, u))
            : Result<TV>.Failure(result.ExceptionDispatchInfo!);
    }

    extension<T>(Task<Result<T>> task)
    {
        public async Task<Result<T>> OnSuccess(Func<T, Task> action)
        {
            var result = await task.ConfigureAwait(false);

            if (result.IsSuccess)
                await action(result.Value!).ConfigureAwait(false);

            return result;
        }

        public async Task<Result<T>> OnFailure(Func<ExceptionDispatchInfo, Task> action)
        {
            var result = await task.ConfigureAwait(false);

            if (!result.IsSuccess)
                await action(result.ExceptionDispatchInfo!).ConfigureAwait(false);

            return result;
        }

        public async Task<TResult> Match<TResult>(Func<T, Task<TResult>> onSuccess,
            Func<ExceptionDispatchInfo, Task<TResult>> onFailure)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? await onSuccess(result.Value!).ConfigureAwait(false)
                : await onFailure(result.ExceptionDispatchInfo!).ConfigureAwait(false);
        }

        public async Task<T?> GetOrNull()
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess ? result.Value : default;
        }

        public async Task<T> GetOrElse(Func<ExceptionDispatchInfo, Task<T>> onFailure)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? result.Value!
                : await onFailure(result.ExceptionDispatchInfo!).ConfigureAwait(false);
        }

        public async Task<Result<TU>> Select<TU>(Func<T, TU> selector)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? Result<TU>.Success(selector(result.Value!))
                : Result<TU>.Failure(result.ExceptionDispatchInfo!);
        }

        public async Task<Result<TU>> SelectMany<TU>(Func<T, Task<Result<TU>>> binder)
        {
            var result = await task.ConfigureAwait(false);
            return result.IsSuccess
                ? await binder(result.Value!).ConfigureAwait(false)
                : Result<TU>.Failure(result.ExceptionDispatchInfo!);
        }

        public async Task<Result<TV>> SelectMany<TU, TV>(Func<T, Task<Result<TU>>> binder, Func<T, TU, TV> projector)
        {
            var result = await task.ConfigureAwait(false);

            if (!result.IsSuccess)
                return Result<TV>.Failure(result.ExceptionDispatchInfo!);

            var inner = await binder(result.Value!).ConfigureAwait(false);
            return inner.IsSuccess
                ? Result<TV>.Success(projector(result.Value!, inner.Value!))
                : Result<TV>.Failure(inner.ExceptionDispatchInfo!);
        }
    }
}