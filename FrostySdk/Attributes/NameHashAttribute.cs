using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.All, Inherited = false)]
public class NameHashAttribute : Attribute
{
    public NameHashAttribute(uint inHash) { Hash = inHash; }
    public uint Hash { get; }
}