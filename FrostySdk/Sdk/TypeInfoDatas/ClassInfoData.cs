﻿using System;
using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class ClassInfoData : TypeInfoData
{
    private readonly List<FieldInfo> m_fieldInfos = new();
    private readonly List<MethodInfo> m_methodInfos = new();

    private long p_superClass;

    public ClassInfo GetSuperClassInfo()
    {
        return (TypeInfo.TypeInfoMapping[p_superClass] as ClassInfo)!;
    }

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Class_{m_nameHash:x8}";
        }

        p_superClass = reader.ReadLong();
        if (TypeInfo.Version < 3)
        {
            reader.ReadLong();
        }

        var pFieldInfos = reader.ReadLong();
        var pMethodInfos = reader.ReadLong();

        if (pFieldInfos != 0)
        {
            reader.Position = pFieldInfos;
            for (var i = 0; i < m_fieldCount; i++)
            {
                m_fieldInfos.Add(new FieldInfo());
                m_fieldInfos[i].Read(reader, m_nameHash);
            }
        }

        if (TypeInfo.Version > 5)
        {
            if (pMethodInfos != 0)
            {
                reader.Position = pMethodInfos;
                bool read;
                do
                {
                    var methodInfo = new MethodInfo();
                    read = methodInfo.Read(reader);
                    if (read)
                    {
                        m_methodInfos.Add(methodInfo);
                    }
                } while (read);
            }
        }
    }

    public override void CreateType(StringBuilder sb)
    {
        if (m_name.Contains("::"))
        {
            // nested type
            sb.AppendLine($"public partial class {m_name[..m_name.IndexOf("::", StringComparison.Ordinal)]}");
            sb.AppendLine("{");
        }

        base.CreateType(sb);

        sb.Append($"public partial class {CleanUpName()}");

        var superClassFieldCount = 0;
        var superClass = GetSuperClassInfo();
        if (superClass.GetName() != CleanUpName())
        {
            superClassFieldCount = superClass.GetFieldCount();
            sb.Append($" : {superClass.GetName()}");
        }

        sb.AppendLine();

        sb.AppendLine("{");

        m_fieldInfos.Sort();
        for (var i = 0; i < m_fieldInfos.Count; i++)
        {
            sb.AppendLine($"[{nameof(FieldIndexAttribute)}({i + superClassFieldCount})]");
            m_fieldInfos[i].CreateField(sb);
        }

        // TODO: what to do with functions
        foreach (var method in m_methodInfos)
        {
            method.GetFunctionInfo().CreateType(sb);
        }

        sb.AppendLine("}");

        if (m_name.Contains("::"))
        {
            sb.AppendLine("}");
        }
    }

    public int GetFieldCount()
    {
        return m_fieldCount;
    }
}