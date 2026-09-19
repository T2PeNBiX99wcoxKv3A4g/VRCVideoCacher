using System.Runtime.ExceptionServices;
using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public readonly struct Result<T>
{
    public T? Value { get; }
    public ExceptionDispatchInfo? ExceptionDispatchInfo { get; }
    public bool IsSuccess => ExceptionDispatchInfo is null;

    private Result(T value)
    {
        Value = value;
        ExceptionDispatchInfo = null;
    }

    private Result(ExceptionDispatchInfo exceptionDispatchInfo)
    {
        Value = default;
        ExceptionDispatchInfo = exceptionDispatchInfo;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Exception exception) => new(ExceptionDispatchInfo.Capture(exception));
    public static Result<T> Failure(ExceptionDispatchInfo exceptionDispatchInfo) => new(exceptionDispatchInfo);

    public void Deconstruct(out bool isSuccess, out T? value, out ExceptionDispatchInfo? exception)
    {
        isSuccess = IsSuccess;
        value = Value;
        exception = ExceptionDispatchInfo;
    }

    public static bool operator true(Result<T> result) => result.IsSuccess;

    public static bool operator false(Result<T> result) => !result.IsSuccess;

    public override string ToString() =>
        IsSuccess ? $"Success({Value})" : $"Failure({ExceptionDispatchInfo?.SourceException})";
}