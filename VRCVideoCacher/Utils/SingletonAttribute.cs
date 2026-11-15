using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

/// <summary>
///     Marks a partial class whose singleton members the generator writes: the <c>Instance</c>
///     property, the <see cref="SingletonRegistry" /> entry, a <c>Log</c> field and a private
///     constructor. The class keeps its own base class.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[PublicAPI]
public sealed class SingletonAttribute : Attribute;
