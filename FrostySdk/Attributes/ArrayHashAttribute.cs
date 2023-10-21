using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Delegate,
    Inherited = false)]
public class ArrayHashAttribute : Attribute
{
    public ArrayHashAttribute(uint inHash)
    {
        Hash = inHash;
    }

    public uint Hash { get; set; }
}