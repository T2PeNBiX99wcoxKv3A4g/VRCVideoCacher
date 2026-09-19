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
    /// Suffix appended to instance member names when generating static proxy members. Default is "S".
    /// </summary>
    public string Suffix { get; set; } = "S";

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
/// Customizes the static proxy name for a specific member, or overrides the default naming rule.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event, Inherited = false)]
public sealed class StaticMemberAttribute : Attribute
{
    public string? Name { get; set; }

    public StaticMemberAttribute()
    {
    }

    public StaticMemberAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Explicitly includes a non-public (or public) member to generate a static proxy.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event, Inherited = false)]
public sealed class StaticIncludeAttribute : Attribute
{
    public string? Name { get; set; }

    public StaticIncludeAttribute()
    {
    }

    public StaticIncludeAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Ignores generating a static proxy for this specific member.
/// </summary>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event, Inherited = false)]
public sealed class StaticIgnoreAttribute : Attribute
{
}
