using System;
using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class StructInfoData : TypeInfoData
{
    private readonly List<FieldInfo> m_fieldInfos = new();

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Struct_{m_nameHash:x8}";
        }

        if (TypeInfo.Version > 2)
        {
            reader.ReadLong();
            reader.ReadLong();
            if (TypeInfo.Version > 3)
            {
                reader.ReadLong();
                reader.ReadLong();
                if (TypeInfo.Version > 4)
                {
                    reader.ReadLong();
                }
            }
        }

        var pDefaultValue = reader.ReadLong();
        var pFieldInfos = reader.ReadLong();

        reader.Position = pFieldInfos;
        for (var i = 0; i < m_fieldCount; i++)
        {
            m_fieldInfos.Add(new FieldInfo());
            m_fieldInfos[i].Read(reader, m_nameHash);
        }
    }

    public override void CreateType(StringBuilder sb)
    {
        if (m_name.Contains("::"))
        {
            // nested type
            sb.AppendLine($"public partial struct {m_name[..m_name.IndexOf("::", StringComparison.Ordinal)]}");
            sb.AppendLine("{");
        }

        base.CreateType(sb);

        sb.AppendLine($"public partial struct {CleanUpName()}");

        sb.AppendLine("{");

        m_fieldInfos.Sort();
        for (var i = 0; i < m_fieldInfos.Count; i++)
        {
            sb.AppendLine($"[{nameof(FieldIndexAttribute)}({i})]");
            m_fieldInfos[i].CreateField(sb);
        }

        sb.AppendLine("}");

        if (m_name.Contains("::"))
        {
            sb.AppendLine("}");
        }
    }
}