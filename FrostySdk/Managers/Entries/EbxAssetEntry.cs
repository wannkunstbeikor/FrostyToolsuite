using System;
using System.Collections.Generic;
using Frosty.Sdk.Profiles;

namespace Frosty.Sdk.Managers.Entries;

public class EbxAssetEntry : AssetEntry
{
    /// <summary>
    ///     <see cref="Guid" />s of the <see cref="EbxAssetEntry" />s this <see cref="EbxAssetEntry" /> depends on.
    /// </summary>
    public readonly HashSet<Guid> DependentAssets = new();

    /// <summary>
    ///     The <see cref="Guid" /> of this <see cref="EbxAssetEntry" />.
    /// </summary>
    public Guid Guid;

    public EbxAssetEntry(string inName, Sha1 inSha1, long inSize, long inOriginalSize)
        : base(inSha1, inSize, inOriginalSize)
    {
        base.Name = inName;
    }

    public override string Name
    {
        get
        {
            // TODO: @techdebt find better method to move blueprint bundles to sub-folder, this will most likely break writing.
            if (ProfilesLibrary.IsLoaded(ProfileVersion.Battlefield2042) &&
                (base.Name.StartsWith("cd_") || (base.Name.StartsWith("md_") && !base.Name.Contains("win32/"))))
            {
                return $"win32/{base.Name}";
            }

            return base.Name;
        }
    }

    public override string AssetType => "ebx";

    public IEnumerable<Guid> EnumerateDependencies()
    {
        foreach (var guid in DependentAssets)
        {
            yield return guid;
        }
    }
}