using System;
using Frosty.Sdk.IO.Ebx;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

public class BoxedValueRef
{
    public BoxedValueRef()
    {
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType)
    {
        Value = inValue;
        Type = inType;
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType, TypeFlags.TypeEnum inSubType)
    {
        Value = inValue;
        Type = inType;
        ArrayType = inSubType;
    }

    public BoxedValueRef(object? inValue, TypeFlags.TypeEnum inType, TypeFlags.TypeEnum inSubType,
        EbxFieldCategory inCategory)
    {
        Value = inValue;
        Type = inType;
        ArrayType = inSubType;
        Category = inCategory;
    }

    public object? Value { get; private set; }

    public TypeFlags.TypeEnum Type { get; }

    public TypeFlags.TypeEnum ArrayType { get; }

    public EbxFieldCategory Category { get; }

    public string TypeString
    {
        get
        {
            switch (Type)
            {
                case TypeFlags.TypeEnum.Array:
                    return EbxTypeToString(ArrayType, Value!.GetType().GenericTypeArguments[0]);
                case TypeFlags.TypeEnum.Enum:
                case TypeFlags.TypeEnum.Struct:
                    return Value!.GetType().Name;
                case TypeFlags.TypeEnum.CString:
                    return "CString";
                default:
                    return Type.ToString();
            }
        }
    }

    public void SetValue(object inValue)
    {
        Value = inValue;
    }

    public override string ToString()
    {
        if (Value is null)
        {
            return "BoxedValueRef '(null)'";
        }

        var s = "BoxedValueRef '";
        switch (Type)
        {
            case TypeFlags.TypeEnum.Array:
                s += $"Array<{EbxTypeToString(ArrayType, Value.GetType().GenericTypeArguments[0])}>";
                break;
            case TypeFlags.TypeEnum.Enum:
                s += $"{Value.GetType().Name}";
                break;
            case TypeFlags.TypeEnum.Struct:
                s += $"{Value.GetType().Name}";
                break;
            case TypeFlags.TypeEnum.CString:
                s += "CString";
                break;
            default:
                s += $"{Type}";
                break;
        }

        return $"{s}'";
    }

    private string EbxTypeToString(TypeFlags.TypeEnum typeToConvert, Type actualType)
    {
        switch (typeToConvert)
        {
            case TypeFlags.TypeEnum.Enum:
            case TypeFlags.TypeEnum.Struct:
                return actualType.Name;
            case TypeFlags.TypeEnum.CString:
                return "CString";
            default:
                return typeToConvert.ToString();
        }
    }
}