using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

/// <summary>
///     Every singleton that has been created in this process, in creation order. It is the
///     reflection-free alternative to scanning the assemblies: a service that wants to reach all
///     singletons walks <see cref="All" /> instead of keeping its own list.
/// </summary>
/// <remarks>
///     Types are the keys, so two services with the same name in different namespaces cannot collide,
///     and creating a second instance of a singleton is rejected.
/// </remarks>
[PublicAPI]
public static class SingletonRegistry
{
    private static readonly List<object> Instances = [];
    private static readonly HashSet<Type> Types = [];
    private static readonly Lock Gate = new();

    /// <summary>How many singletons have been created.</summary>
    public static int Count
    {
        get
        {
            lock (Gate)
                return Instances.Count;
        }
    }

    /// <summary>A snapshot of every created singleton, in creation order.</summary>
    public static IReadOnlyList<object> All
    {
        get
        {
            lock (Gate)
                return [.. Instances];
        }
    }

    /// <summary>Adds an instance, or throws when its type is already registered.</summary>
    /// <exception cref="InvalidOperationException">Another instance of this type exists.</exception>
    public static void Register(object instance)
    {
        var type = instance.GetType();

        lock (Gate)
        {
            if (!Types.Add(type))
                throw new InvalidOperationException(
                    $"{type.Name} is a singleton; use {type.Name}.Instance instead of creating another one.");

            Instances.Add(instance);
        }
    }

    /// <summary>The created instance of <typeparamref name="T" />, or null.</summary>
    public static T? Find<T>() where T : class
    {
        lock (Gate)
            return Instances.OfType<T>().FirstOrDefault();
    }

    /// <summary>The created instance of <typeparamref name="T" />; throws when it was never created.</summary>
    public static T Get<T>() where T : class =>
        Find<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} has not been created yet.");

    /// <summary>Every created singleton that is a <typeparamref name="T" />.</summary>
    public static IEnumerable<T> OfType<T>() => All.OfType<T>();
}
