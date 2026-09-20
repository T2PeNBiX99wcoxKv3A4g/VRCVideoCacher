using JetBrains.Annotations;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class ObjectExtensions
{
    extension<T>(T value)
    {
        public TResult Let<TResult>([InstantHandle] Func<T, TResult> block) => block(value);

        public T Also([InstantHandle] Action<T> block)
        {
            block(value);
            return value;
        }
    }
}