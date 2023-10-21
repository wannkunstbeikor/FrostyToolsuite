using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Assembly)]
public class SdkVersionAttribute : Attribute
{
    public SdkVersionAttribute(uint inHead)
    {
        Head = inHead;
    }

    public uint Head { get; }
}