using System;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class EbxArrayMetaAttribute : Attribute
{
    public EbxArrayMetaAttribute(ushort flags)
    {
        Flags = flags;
    }

    public TypeFlags Flags { get; set; }
}