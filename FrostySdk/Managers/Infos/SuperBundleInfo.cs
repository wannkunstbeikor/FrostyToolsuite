using System.Collections.Generic;

namespace Frosty.Sdk.Managers.Infos;

public class SuperBundleInfo
{
    public SuperBundleInfo(string inName)
    {
        Name = inName;
        InstallChunks = new Dictionary<int, InstallChunkType>();
    }

    public string Name { get; set; }
    public Dictionary<int, InstallChunkType> InstallChunks { get; set; }
}