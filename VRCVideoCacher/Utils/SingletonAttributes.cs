using JetBrains.Annotations;

namespace VRCVideoCacher.Utils;

/// <summary>
/// Configures the generated static proxy members for a Singleton class.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SingletonStaticProxyAttribute : Attribute
{
    /// <summary>
    /// Suffix appended to instance member names when generating static proxy members. Default is empty.
    /// </summary>
    public string Suffix { get; set; } = "";

    /// <summary>
    /// Prefix prepended to instance member names when generating static proxy members. Default is empty.
    /// </summary>
    public string Prefix { get; set; } = "";

    /// <summary>
    /// Whether static proxy member generation is enabled for this singleton class. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Explicitly includes a non-public (or public) member to generate a static proxy,
/// optionally customizing the static proxy name or trimming the last character.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
    Inherited = false)]
public sealed class StaticIncludeAttribute : Attribute
{
    public string? Name { get; set; }

    /// <summary>
    /// When true, automatically removes the last character from the member's name for the static proxy.
    /// </summary>
    public bool TrimLast { get; set; }

    public StaticIncludeAttribute()
    {
    }

    public StaticIncludeAttribute(string name) => Name = name;
}

/// <summary>
/// Explicitly includes a member and automatically removes the last character from its name when generating the static proxy.
/// (e.g. TryUpdateShortcutPath2 -> TryUpdateShortcutPath)
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
    Inherited = false)]
public sealed class StaticTrimLastAttribute : Attribute
{
}

/// <summary>
/// Ignores generating a static proxy for this specific member.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event,
    Inherited = false)]
public sealed class StaticIgnoreAttribute : Attribute
{
}