using System;

namespace Frosty.Sdk.Ebx;

public class TypeRef
{
    private readonly Guid m_typeGuid;

    public TypeRef()
    {
        Name = string.Empty;
    }

    public TypeRef(string value)
    {
        Name = value;
    }

    public TypeRef(Guid guid)
    {
        m_typeGuid = guid;
        Name = TypeLibrary.GetType(guid)?.Name ?? m_typeGuid.ToString();
    }

    public string Name { get; }

    public Guid Guid => m_typeGuid;

    public Type GetReferencedType()
    {
        // should be a primitive type if the GUID is empty
        if (m_typeGuid == Guid.Empty)
        {
            var refType = TypeLibrary.GetType(Name);
            if (refType == null)
            {
                throw new Exception($"Could not find the type {Name}");
            }

            return refType;
        }
        else
        {
            var refType = TypeLibrary.GetType(m_typeGuid);
            if (refType == null)
            {
                throw new Exception($"Could not find the type {Name}");
            }

            return refType;
        }
    }

    public static implicit operator string(TypeRef value)
    {
        return value.m_typeGuid != Guid.Empty ? value.m_typeGuid.ToString().ToUpper() : value.Name;
    }

    public static implicit operator TypeRef(string value)
    {
        return new TypeRef(value);
    }

    public static implicit operator TypeRef(Guid guid)
    {
        return new TypeRef(guid);
    }

    public bool IsNull()
    {
        return string.IsNullOrEmpty(Name);
    }

    public override string ToString()
    {
        return $"TypeRef '{(IsNull() ? "(null)" : Name)}'";
    }
}