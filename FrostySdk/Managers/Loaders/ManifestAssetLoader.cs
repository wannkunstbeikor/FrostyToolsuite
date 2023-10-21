using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers.Loaders;

public class ManifestAssetLoader : IAssetLoader
{
    public void Load()
    {
        // This format has all SuperBundles stripped
        // all of the bundles and chunks of all SuperBundles are put into the manifest
        // afaik u cant reconstruct the SuperBundles, so this might make things a bit ugly
        // They also seem to have catalog files which entries are not used, but they still make a sanity check for the offsets and indices in the file

        AssetManager.AddSuperBundle("Manifest");

        var manifest = FileSystemManager.Manifest!;

        var file = CasFileIdentifier.FromFileIdentifierV2(manifest.AsUInt("file"));

        var path = FileSystemManager.GetFilePath(file);

        using (var stream = BlockStream.FromFile(FileSystemManager.ResolvePath(path), manifest.AsUInt("offset"),
                   manifest.AsInt("size")))
        {
            var resourceInfoCount = stream.ReadUInt32();
            var bundleCount = stream.ReadUInt32();
            var chunkCount = stream.ReadUInt32();

            var files = new (CasFileIdentifier, uint, long)[resourceInfoCount];

            // resource infos
            for (var i = 0; i < resourceInfoCount; i++)
            {
                files[i] = (CasFileIdentifier.FromFileIdentifierV2(stream.ReadUInt32()), stream.ReadUInt32(),
                    (uint)stream.ReadInt64());
            }

            // bundles
            for (var i = 0; i < bundleCount; i++)
            {
                var nameHash = stream.ReadInt32();
                var startIndex = stream.ReadInt32();
                var resourceCount = stream.ReadInt32();

                // unknown, always 0
                stream.ReadInt32();
                stream.ReadInt32();

                var resourceInfo = files[startIndex];
                BinaryBundle bundle;
                using (var bundleStream = BlockStream.FromFile(FileSystemManager.ResolvePath(
                               FileSystemManager.GetFilePath(resourceInfo.Item1)), resourceInfo.Item2,
                           (int)resourceInfo.Item3))
                {
                    bundle = BinaryBundle.Deserialize(bundleStream);
                }

                if (!ProfilesLibrary.SharedBundles.TryGetValue(nameHash, out var name))
                {
                    // we get the name while processing the ebx, since blueprint/sublevel bundles always have an ebx asset with the same name
                    name = nameHash.ToString("x8");
                }

                var bundleId = AssetManager.AddBundle(name, 0);

                foreach (var ebx in bundle.EbxList)
                {
                    var fileInfos = ResourceManager.GetFileInfos(ebx.Sha1);
                    if (fileInfos is not null)
                    {
                        ebx.FileInfos.UnionWith(fileInfos);
                    }

                    AssetManager.AddEbx(ebx, bundleId);
                }

                foreach (var res in bundle.ResList)
                {
                    var fileInfos = ResourceManager.GetFileInfos(res.Sha1);
                    if (fileInfos is not null)
                    {
                        res.FileInfos.UnionWith(fileInfos);
                    }

                    AssetManager.AddRes(res, bundleId);
                }

                foreach (var chunk in bundle.ChunkList)
                {
                    var fileInfos = ResourceManager.GetFileInfos(chunk.Sha1);
                    if (fileInfos is not null)
                    {
                        chunk.FileInfos.UnionWith(fileInfos);
                    }

                    AssetManager.AddChunk(chunk, bundleId);
                }
            }

            // chunks
            for (var i = 0; i < chunkCount; i++)
            {
                var chunkId = stream.ReadGuid();
                var resourceInfo = files[stream.ReadInt32()];

                ChunkAssetEntry entry = new(chunkId, Sha1.Zero, resourceInfo.Item3, 0, 0, 0);

                entry.FileInfos.Add(
                    new CasFileInfo(resourceInfo.Item1, resourceInfo.Item2, (uint)resourceInfo.Item3, 0));

                AssetManager.AddSuperBundleChunk(entry);
            }
        }
    }
}