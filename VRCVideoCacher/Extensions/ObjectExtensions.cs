using JetBrains.Annotations;

namespace VRCVideoCacher.Extensions;

[PublicAPI]
public static class ObjectExtensions
{
    extension<T>(T value)
    {
        public T Also(Action<T> action)
        {
            action(value);
            return value;
        }
    }
}