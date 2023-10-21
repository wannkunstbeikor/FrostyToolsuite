using Frosty.Sdk.IO;

namespace Frosty.Sdk.Sdk;

internal class ParameterInfo
{
    private string m_name = string.Empty;
    private byte m_type;
    private long p_defaultValue;
    private long p_typeInfo;

    public string GetName()
    {
        return m_name;
    }

    public TypeInfo GetTypeInfo()
    {
        return TypeInfo.TypeInfoMapping[p_typeInfo];
    }

    public byte GetParameterType()
    {
        return m_type;
    }

    public void Read(MemoryReader reader)
    {
        m_name = reader.ReadNullTerminatedString();
        p_typeInfo = reader.ReadLong();
        m_type = reader.ReadByte();
        p_defaultValue = reader.ReadLong();
    }

    public void ProcessDefaultValue()
    {
    }
}