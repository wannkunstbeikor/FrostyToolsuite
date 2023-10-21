using Frosty.Sdk.IO;
using Frosty.Sdk.Sdk.TypeInfoDatas;

namespace Frosty.Sdk.Sdk.TypeInfos;

internal class ClassInfo : TypeInfo
{
    private long p_defaultInstance;

    private long p_superClass;

    public ClassInfo(ClassInfoData data)
        : base(data)
    {
    }

    public ClassInfo GetSuperClassInfo()
    {
        return (TypeInfoMapping[p_superClass] as ClassInfo)!;
    }

    public override void Read(MemoryReader reader)
    {
        base.Read(reader);

        p_superClass = reader.ReadLong();
        p_defaultInstance = reader.ReadLong();
    }

    public int GetFieldCount()
    {
        var fieldCount = (m_data as ClassInfoData)?.GetFieldCount() ?? 0;
        var superClass = GetSuperClassInfo();
        if (superClass != this)
        {
            fieldCount += superClass.GetFieldCount();
        }

        return fieldCount;
    }
}