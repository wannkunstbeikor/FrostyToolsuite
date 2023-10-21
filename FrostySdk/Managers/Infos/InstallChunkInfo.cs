using System;
using System.Collections.Generic;

namespace Frosty.Sdk.Managers.Infos;

public class InstallChunkInfo
{
    public readonly HashSet<Guid> RequiredCatalogs = new();
    public readonly HashSet<string> SplitSuperBundles = new();
    public readonly HashSet<string> SuperBundles = new();
    public bool AlwaysInstalled;
    public Guid Id;
    public string InstallBundle = string.Empty;
    public string Name = string.Empty;

    public bool RequiresInstallChunk(InstallChunkInfo b)
    {
        if (RequiredCatalogs.Count == 0)
        {
            return false;
        }

        if (RequiredCatalogs.Contains(b.Id))
        {
            return true;
        }

        foreach (var id in RequiredCatalogs)
        {
            var c = FileSystemManager.GetInstallChunkInfo(id);
            if (c.RequiresInstallChunk(b))
            {
                return true;
            }
        }

        return false;
    }
}