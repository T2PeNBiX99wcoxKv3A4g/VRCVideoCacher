using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

/// <summary>
///     Asks the generator to write the config plumbing of a partial view model: every
///     <c>[ObservableProperty]</c> whose name and type match a public field of
///     <see cref="Config" /> is loaded and saved automatically, and marks the view model as changed.
///     Properties that need a side effect or a different shape stay hand-written and are marked
///     <see cref="ConfigBindingIgnoreAttribute" />.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[PublicAPI]
public sealed class ConfigBindingsAttribute(Type config) : Attribute
{
    public Type Config { get; } = config;
}
