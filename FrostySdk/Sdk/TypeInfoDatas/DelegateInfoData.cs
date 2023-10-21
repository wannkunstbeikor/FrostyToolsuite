using System.Collections.Generic;
using System.Text;
using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk.TypeInfoDatas;

internal class DelegateInfoData : TypeInfoData
{
    private readonly List<ParameterInfo> m_parameterInfos = new();

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        if (ProfilesLibrary.HasStrippedTypeNames && string.IsNullOrEmpty(m_name))
        {
            m_name = $"Delegate_{m_nameHash:x8}";
        }

        var pParameterInfos = reader.ReadLong();

        reader.Position = pParameterInfos;
        for (var i = 0; i < m_fieldCount; i++)
        {
            m_parameterInfos.Add(new ParameterInfo());
            m_parameterInfos[i].Read(reader);
        }
    }

    public override void CreateType(StringBuilder sb)
    {
        base.CreateType(sb);

        var returnType = "void";

        StringBuilder inputParams = new();
        var hasReturn = false;

        foreach (var parameterInfo in m_parameterInfos)
        {
            var type = parameterInfo.GetTypeInfo();

            var typeName = type.GetName();

            if (type is ArrayInfo array)
            {
                typeName = $"List<{array.GetTypeInfo().GetName()}>";
            }

            switch (parameterInfo.GetParameterType())
            {
                case 0:
                    inputParams.Append($"{typeName} {parameterInfo.GetName()}, ");
                    break;
                case 1:
                    if (!hasReturn)
                    {
                        hasReturn = true;
                    }

                    returnType = typeName;
                    break;
                case 2:
                    inputParams.Append($"{typeName}* {parameterInfo.GetName()}, ");
                    break;
                case 3:
                    if (!hasReturn)
                    {
                        hasReturn = true;
                    }

                    returnType = $"{typeName}*";
                    break;
            }
        }

        if (inputParams.Length > 0)
        {
            inputParams.Remove(inputParams.Length - 2, 2);
        }

        sb.AppendLine($"public unsafe delegate {returnType} {CleanUpName()} ({inputParams});");
    }
}