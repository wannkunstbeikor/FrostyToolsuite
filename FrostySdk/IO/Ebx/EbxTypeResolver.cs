namespace Frosty.Sdk.IO.Ebx;

internal class EbxTypeResolver
{
    private readonly EbxFieldDescriptor[] m_fieldDescriptors;
    private readonly EbxTypeDescriptor[] m_typeDescriptors;

    internal EbxTypeResolver(EbxTypeDescriptor[] inTypeDescriptors, EbxFieldDescriptor[] inFieldDescriptors)
    {
        EbxSharedTypeDescriptors.Initialize();
        m_typeDescriptors = inTypeDescriptors;
        m_fieldDescriptors = inFieldDescriptors;
    }

    public EbxTypeDescriptor ResolveType(int index)
    {
        var typeDescriptor = m_typeDescriptors[index];
        if (typeDescriptor.IsSharedTypeDescriptorKey())
        {
            return EbxSharedTypeDescriptors.GetTypeDescriptor(typeDescriptor.ToKey());
        }

        return typeDescriptor;
    }

    public EbxTypeDescriptor ResolveType(EbxTypeDescriptor typeDescriptor, int index)
    {
        if (typeDescriptor.IsSharedTypeDescriptorKey())
        {
            return EbxSharedTypeDescriptors.GetTypeDescriptor(typeDescriptor.ToKey(), (short)index);
        }

        return m_typeDescriptors[index];
    }

    public virtual EbxFieldDescriptor ResolveField(int index)
    {
        if (m_fieldDescriptors.Length == 0)
        {
            return EbxSharedTypeDescriptors.GetFieldDescriptor(index);
        }

        return m_fieldDescriptors[index];
    }
}