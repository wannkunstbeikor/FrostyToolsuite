using System;

namespace Frosty.Sdk.Attributes;

/// <summary>
///     Specifies the guid for the class, used when looking up type refs
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Enum |
    AttributeTargets.Delegate | AttributeTargets.Method | AttributeTargets.Field, Inherited = false)]
public class GuidAttribute : Attribute
{
    public GuidAttribute(string inGuid)
    {
        Guid = Guid.Parse(inGuid);
    }

    public Guid Guid { get; set; }
}