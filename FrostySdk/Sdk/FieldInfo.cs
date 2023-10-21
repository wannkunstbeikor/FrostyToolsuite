using System;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk;

internal class FieldInfo : IComparable
{
    private TypeFlags m_flags;

    private string m_name = string.Empty;
    private uint m_nameHash;
    private ushort m_offset;
    private long p_typeInfo;

    public int CompareTo(object? obj)
    {
        return m_offset.CompareTo((obj as FieldInfo)!.m_offset);
    }

    public string GetName()
    {
        return m_name;
    }

    public TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeInfoMapping[p_typeInfo];
    }

    public int GetEnumValue()
    {
        return (int)p_typeInfo;
    }

    public void Read(MemoryReader reader, uint classHash)
    {
        if (!ProfilesLibrary.HasStrippedTypeNames)
        {
            m_name = reader.ReadNullTerminatedString();
        }

        if (TypeInfo.Version > 4)
        {
            m_nameHash = reader.ReadUInt();
        }

        m_flags = reader.ReadUShort();
        m_offset = reader.ReadUShort();

        p_typeInfo = reader.ReadLong();

        if (ProfilesLibrary.HasStrippedTypeNames)
        {
            if (Strings.FieldHashes.ContainsKey(classHash) && Strings.FieldHashes[classHash].ContainsKey(m_nameHash))
            {
                m_name = Strings.FieldHashes[classHash][m_nameHash];
            }
            else if (Strings.StringHashes.TryGetValue(m_nameHash, out var hash))
            {
                m_name = hash;
            }
            else
            {
                m_name = $"Field_{m_nameHash:x8}";
            }
        }
    }

    public void CreateField(StringBuilder sb)
    {
        var type = GetTypeInfo();
        var typeName = type.GetName();
        var flags = type.GetFlags();
        var isClass = false;

        if (type is ClassInfo)
        {
            typeName = "PointerRef";
            isClass = true;
        }

        if (type is ArrayInfo arrayInfo)
        {
            type = arrayInfo.GetTypeInfo();
            typeName = type.GetName();
            if (type is ClassInfo)
            {
                typeName = "PointerRef";
                isClass = true;
            }

            typeName = $"List<{typeName}>";
            sb.AppendLine($"[{nameof(EbxArrayMetaAttribute)}({(ushort)type.GetFlags()})]");
        }

        sb.AppendLine(
            $"[{nameof(EbxFieldMetaAttribute)}({(ushort)flags}, {m_offset}, {(isClass ? $"typeof({type.GetName()})" : "null")})]");
        if (m_nameHash != 0)
        {
            sb.AppendLine($"[{nameof(NameHashAttribute)}({m_nameHash})]");
        }

        sb.AppendLine($"private {typeName} _{m_name};");
    }
}