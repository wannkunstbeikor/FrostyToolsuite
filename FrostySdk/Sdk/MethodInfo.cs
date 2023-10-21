using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfos;

namespace Frosty.Sdk.Sdk;

internal class MethodInfo
{
    private uint m_nameHash;
    private long p_functionInfo;
    private long p_unknown;

    public FunctionInfo GetFunctionInfo()
    {
        return (TypeInfo.TypeInfoMapping[p_functionInfo] as FunctionInfo)!;
    }

    public FunctionInfo GetFunctionInfo2()
    {
        return (TypeInfo.TypeInfoMapping[p_unknown] as FunctionInfo)!;
    }

    public bool Read(MemoryReader reader)
    {
        m_nameHash = reader.ReadUInt();

        if (m_nameHash == 0)
        {
            return false;
        }

        p_unknown = reader.ReadLong();
        p_functionInfo = reader.ReadLong();
        return true;
    }
}