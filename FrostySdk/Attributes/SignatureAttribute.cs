using System;

namespace Frosty.Sdk.Attributes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Delegate | AttributeTargets.Struct)]
public class SignatureAttribute : Attribute
{
    public SignatureAttribute(uint inSignature)
    {
        Signature = inSignature;
    }

    public uint Signature { get; }
}