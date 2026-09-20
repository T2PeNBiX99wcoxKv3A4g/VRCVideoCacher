using System.Diagnostics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

[PublicAPI]
[StackTraceHidden]
public static class Try
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<T> Run<T>([InstantHandle] Func<T> func)
    {
        try
        {
            return Result<T>.Success(func());
        }
        catch (Exception e)
        {
            return Result<T>.Failure(e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<Unit> Run([InstantHandle] Action action)
    {
        try
        {
            action();
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception e)
        {
            return Result<Unit>.Failure(e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<Result<T>> Run<T>([InstantHandle] Func<Task<T>> func)
    {
        try
        {
            return Result<T>.Success(await func().ConfigureAwait(false));
        }
        catch (Exception e)
        {
            return Result<T>.Failure(e);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async Task<Result<Unit>> Run([InstantHandle] Func<Task> func)
    {
        try
        {
            await func().ConfigureAwait(false);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (Exception e)
        {
            return Result<Unit>.Failure(e);
        }
    }
}

public readonly struct Unit : IEquatable<Unit>
{
    public static readonly Unit Value = new();

    public bool Equals(Unit other) => true;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
    public override string ToString() => "Unit";

    public static bool operator ==(Unit left, Unit right) => left.Equals(right);
    public static bool operator !=(Unit left, Unit right) => !(left == right);
}