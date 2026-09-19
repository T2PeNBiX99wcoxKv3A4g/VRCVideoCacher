using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

[PublicAPI]
public readonly struct Result<T>
{
    public T? Value { get; }
    public Exception? Exception { get; }
    public bool IsSuccess => Exception is null;

    private Result(T value)
    {
        Value = value;
        Exception = null;
    }

    private Result(Exception exceptionDispatchInfo)
    {
        Value = default;
        Exception = exceptionDispatchInfo;
    }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Exception exception) => new(exception);

    public void Deconstruct(out bool isSuccess, out T? value, out Exception? exception)
    {
        isSuccess = IsSuccess;
        value = Value;
        exception = Exception;
    }

    public static bool operator true(Result<T> result) => result.IsSuccess;

    public static bool operator false(Result<T> result) => !result.IsSuccess;

    public override string ToString() =>
        IsSuccess ? $"Success({Value})" : $"Failure({Exception})";
}