namespace VRCVideoCacher.Utils;

/// <summary>Keeps a property out of the generated config bindings: it is handled by hand instead.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigBindingIgnoreAttribute : Attribute;
