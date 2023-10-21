using Frosty.Sdk.Attributes;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

[EbxTypeMeta(TypeFlags.TypeEnum.Class)]
public partial class DataContainer
{
}

public partial class Asset : DataContainer
{
    [EbxFieldMeta(TypeFlags.TypeEnum.CString)]
    private CString _Name;
}

public partial class DataContainerAsset : DataContainer
{
    private CString _Name;
}