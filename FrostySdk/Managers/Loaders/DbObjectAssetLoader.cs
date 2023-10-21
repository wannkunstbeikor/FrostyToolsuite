using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos.FileInfos;
using Frosty.Sdk.Managers.Loaders.Helpers;

namespace Frosty.Sdk.Managers.Loaders;

public class DbObjectAssetLoader : IAssetLoader
{
    public void Load()
    {
        foreach (var sbInfo in FileSystemManager.EnumerateSuperBundles())
        {
            var isPatched = true;
            var tocPath = FileSystemManager.ResolvePath(true, $"{sbInfo.Name}.toc");
            if (string.IsNullOrEmpty(tocPath))
            {
                isPatched = false;
                tocPath = FileSystemManager.ResolvePath(false, $"{sbInfo.Name}.toc");
                if (string.IsNullOrEmpty(tocPath))
                {
                    continue;
                }
            }

            var toc = DbObject.Deserialize(tocPath)?.AsDict();
            if (toc is null)
            {
                continue;
            }

            List<BundleInfo> bundles = new();
            Dictionary<int, BundleInfo> baseBundleDic = new();

            var superBundleId = AssetManager.AddSuperBundle(sbInfo.Name);

            if (!LoadToc(sbInfo.Name, superBundleId, toc, ref bundles, ref baseBundleDic, isPatched))
            {
                continue;
            }

            LoadSb(bundles, baseBundleDic, superBundleId);
        }
    }

    private bool LoadToc(string sbName, int superBundleId, DbObjectDict toc, ref List<BundleInfo> bundles,
        ref Dictionary<int, BundleInfo> baseBundleDic, bool isPatched)
    {
        // flag for if the assets are stored in cas files or in the superbundle directly
        var isCas = toc.AsBoolean("cas");
        // flag for das files (used in NFS Edge)
        var isDas = toc.AsBoolean("das");

        // process toc chunks
        if (toc.ContainsKey("chunks"))
        {
            var path = $"{(isPatched ? "native_patch" : "native_data")}/{sbName}.sb";

            foreach (var chunkObj in toc.AsList("chunks"))
            {
                var chunk = chunkObj.AsDict();

                var size = isCas ? ResourceManager.GetSize(chunk.AsSha1("sha1")) : chunk.AsLong("size");

                ChunkAssetEntry entry = new(chunk.AsGuid("id"), chunk.AsSha1("sha1", Sha1.Zero), size, 0, 0,
                    superBundleId);

                if (!isCas)
                {
                    entry.FileInfos.Add(new PathFileInfo(path, chunk.AsUInt("offset"), (uint)size, 0));
                }
                else
                {
                    var fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                    if (fileInfos is not null)
                    {
                        entry.FileInfos.UnionWith(fileInfos);
                    }
                }

                AssetManager.AddSuperBundleChunk(entry);
            }
        }

        var processBaseBundles = false;

        // process bundles
        if (toc.ContainsKey("bundles"))
        {
            // das TOC - stores bundles as a dict
            if (isDas)
            {
                var dasBundlesDict = toc.AsDict("bundles");

                var dasBundleNames = dasBundlesDict.AsList("names");
                var dasBundleOffsets = dasBundlesDict.AsList("offsets");
                var dasBundleSizes = dasBundlesDict.AsList("sizes");

                for (var bundleIter = 0; bundleIter < dasBundleNames.Count; bundleIter++)
                {
                    var name = dasBundleNames[bundleIter].AsString();
                    var offset = dasBundleOffsets[bundleIter].AsInt();
                    var size = dasBundleSizes[bundleIter].AsInt();

                    bundles.Add(new BundleInfo
                    {
                        Name = name,
                        SbName = sbName,
                        Offset = offset,
                        Size = size,
                        IsDelta = false,
                        IsPatch = isPatched
                    });
                }
            }
            // standard TOC, stores bundles as a list
            else
            {
                foreach (var bundleInfo in toc.AsList("bundles"))
                {
                    var bundleInfoDict = bundleInfo.AsDict();

                    var name = bundleInfoDict.AsString("id");

                    var isDelta = bundleInfoDict.AsBoolean("delta");
                    var isBase = bundleInfoDict.AsBoolean("base");

                    var offset = bundleInfoDict.AsLong("offset");
                    var size = bundleInfoDict.AsLong("size");

                    bundles.Add(new BundleInfo
                    {
                        Name = name,
                        SbName = sbName,
                        Offset = offset,
                        Size = size,
                        IsDelta = isDelta,
                        IsPatch = isPatched && !isBase,
                        IsNonCas = !isCas
                    });

                    if (isDelta)
                    {
                        processBaseBundles = true;
                    }
                }
            }
        }

        if (processBaseBundles)
        {
            var tocPath = FileSystemManager.ResolvePath($"native_data/{sbName}.toc");
            var baseToc = DbObject.Deserialize(tocPath)?.AsDict();
            if (baseToc == null)
            {
                return false;
            }

            isCas = baseToc.AsBoolean("cas");

            if (!baseToc.ContainsKey("bundles"))
            {
                return false;
            }

            foreach (var bundleInfo in baseToc.AsList("bundles"))
            {
                var name = bundleInfo.AsDict().AsString("id");

                var offset = bundleInfo.AsDict().AsLong("offset");
                var size = bundleInfo.AsDict().AsLong("size");

                baseBundleDic.Add(Utils.Utils.HashString(name, true),
                    new BundleInfo
                    {
                        Name = name,
                        SbName = sbName,
                        Offset = offset,
                        Size = size,
                        IsPatch = false,
                        IsNonCas = !isCas
                    });
            }
        }

        return true;
    }

    private void LoadSb(List<BundleInfo> bundles, Dictionary<int, BundleInfo> baseBundleDic, int superBundleId)
    {
        var patchSbPath = string.Empty;
        var baseSbPath = string.Empty;
        BlockStream? patchStream = null;
        BlockStream? baseStream = null;

        foreach (var bundleInfo in bundles)
        {
            // get correct stream for this bundle
            BlockStream stream;
            if (bundleInfo.IsPatch)
            {
                if (patchStream == null || patchSbPath != bundleInfo.SbName)
                {
                    patchSbPath = bundleInfo.SbName;
                    patchStream?.Dispose();
                    patchStream = BlockStream.FromFile(
                        FileSystemManager.ResolvePath(true, $"{patchSbPath}.sb"), false);
                }

                stream = patchStream;
            }
            else
            {
                if (baseStream == null || baseSbPath != bundleInfo.SbName)
                {
                    baseSbPath = bundleInfo.SbName;
                    baseStream?.Dispose();
                    baseStream = BlockStream.FromFile(
                        FileSystemManager.ResolvePath(false, $"{baseSbPath}.sb"), false);
                }

                stream = baseStream;
            }

            var bundleId = AssetManager.AddBundle(bundleInfo.Name, superBundleId);

            // load bundle from sb file
            if (bundleInfo.IsNonCas)
            {
                if (bundleInfo.IsDelta)
                {
                    var hash = Utils.Utils.HashString(bundleInfo.Name, true);
                    var hasBase = baseBundleDic.TryGetValue(hash, out var baseBundleInfo);
                    if (hasBase)
                    {
                        if (baseStream == null || baseSbPath != bundleInfo.SbName)
                        {
                            baseSbPath = bundleInfo.SbName;
                            baseStream?.Dispose();
                            baseStream = BlockStream.FromFile(
                                FileSystemManager.ResolvePath(false, $"{baseSbPath}.sb"), false);
                        }

                        baseStream!.Position = baseBundleInfo.Offset;
                    }

                    stream.Position = bundleInfo.Offset;

                    var bundle = DeserializeDeltaBundle(baseStream, stream);

                    // TODO: get asset refs from sb file similar to this (https://github.com/GreyDynamics/Frostbite3_Editor/blob/develop/src/tk/greydynamics/Resource/Frostbite3/Cas/NonCasBundle.java)
                    // or with a cache like before
                    // this is just so u can load those games for now
                    foreach (var ebx in bundle.EbxList)
                    {
                        AssetManager.AddEbx(ebx, bundleId);
                    }

                    foreach (var res in bundle.ResList)
                    {
                        AssetManager.AddRes(res, bundleId);
                    }

                    foreach (var chunk in bundle.ChunkList)
                    {
                        AssetManager.AddChunk(chunk, bundleId);
                    }
                }
                else
                {
                    stream.Position = bundleInfo.Offset;
                    var bundle = BinaryBundle.Deserialize(stream);

                    var path = $"{(bundleInfo.IsPatch ? "native_patch" : "native_data")}/{bundleInfo.Name}.sb";

                    // read data
                    foreach (var ebx in bundle.EbxList)
                    {
                        var offset = (uint)stream.Position;
                        ebx.Size = GetSize(stream, ebx.OriginalSize);
                        ebx.FileInfos.Add(new PathFileInfo(path, offset, (uint)ebx.Size, 0));

                        AssetManager.AddEbx(ebx, bundleId);
                    }

                    foreach (var res in bundle.ResList)
                    {
                        var offset = (uint)stream.Position;
                        res.Size = GetSize(stream, res.OriginalSize);
                        res.FileInfos.Add(new PathFileInfo(path, offset, (uint)res.Size, 0));

                        AssetManager.AddRes(res, bundleId);
                    }

                    foreach (var chunk in bundle.ChunkList)
                    {
                        var offset = (uint)stream.Position;
                        chunk.Size = GetSize(stream, (chunk.LogicalOffset & 0xFFFF) | chunk.LogicalSize);
                        chunk.FileInfos.Add(new PathFileInfo(path, offset, (uint)chunk.Size, chunk.LogicalOffset));

                        AssetManager.AddChunk(chunk, bundleId);
                    }

                    Debug.Assert(stream.Position == bundleInfo.Offset + bundleInfo.Size);
                }
            }
            else
            {
                stream.Position = bundleInfo.Offset;
                var bundle = DbObject.Deserialize(stream)!.AsDict();
                Debug.Assert(stream.Position == bundleInfo.Offset + bundleInfo.Size);

                var ebxList = bundle.AsList("ebx", null);
                var resList = bundle.AsList("res", null);
                var chunkList = bundle.AsList("chunks", null);

                for (var i = 0; i < ebxList?.Count; i++)
                {
                    var ebx = ebxList[i].AsDict();

                    EbxAssetEntry entry = new(ebx.AsString("name"), ebx.AsSha1("sha1"), ebx.AsLong("size"),
                        ebx.AsLong("originalSize"));

                    if (ebx.AsInt("casPatchType") == 2)
                    {
                        var baseSha1 = ebx.AsSha1("baseSha1");
                        var deltaSha1 = ebx.AsSha1("deltaSha1");

                        var fileInfos =
                            ResourceManager.GetPatchFileInfos(entry.Sha1, deltaSha1, baseSha1);

                        if (fileInfos is not null)
                        {
                            entry.FileInfos.UnionWith(fileInfos);
                        }
                    }
                    else
                    {
                        var fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                        if (fileInfos is not null)
                        {
                            entry.FileInfos.UnionWith(fileInfos);
                        }
                    }

                    AssetManager.AddEbx(entry, bundleId);
                }

                for (var i = 0; i < resList?.Count; i++)
                {
                    var res = resList[i].AsDict();

                    ResAssetEntry entry = new(res.AsString("name"), res.AsSha1("sha1"), res.AsLong("size"),
                        res.AsLong("originalSize"), res.AsULong("resRid"), res.AsUInt("resType"),
                        res.AsBlob("resMeta"));

                    if (res.AsInt("casPatchType") == 2)
                    {
                        var baseSha1 = res.AsSha1("baseSha1");
                        var deltaSha1 = res.AsSha1("deltaSha1");

                        var fileInfos =
                            ResourceManager.GetPatchFileInfos(entry.Sha1, deltaSha1, baseSha1);

                        if (fileInfos is not null)
                        {
                            entry.FileInfos.UnionWith(fileInfos);
                        }
                    }
                    else
                    {
                        var fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                        if (fileInfos is not null)
                        {
                            entry.FileInfos.UnionWith(fileInfos);
                        }
                    }

                    AssetManager.AddRes(entry, bundleId);
                }

                for (var i = 0; i < chunkList?.Count; i++)
                {
                    var chunk = chunkList[i].AsDict();

                    ChunkAssetEntry entry = new(chunk.AsGuid("id"), chunk.AsSha1("sha1"), chunk.AsLong("size"),
                        chunk.AsUInt("logicalOffset"), chunk.AsUInt("logicalSize"));

                    if (chunk.AsInt("casPatchType") == 2)
                    {
                        var baseSha1 = chunk.AsSha1("baseSha1");
                        var deltaSha1 = chunk.AsSha1("deltaSha1");

                        var fileInfos =
                            ResourceManager.GetPatchFileInfos(entry.Sha1, deltaSha1, baseSha1);

                        if (fileInfos is not null)
                        {
                            entry.FileInfos.UnionWith(fileInfos);
                        }
                    }
                    else
                    {
                        var fileInfos = ResourceManager.GetFileInfos(entry.Sha1);
                        if (fileInfos is not null)
                        {
                            entry.FileInfos.UnionWith(fileInfos);
                        }
                    }

                    AssetManager.AddChunk(entry, bundleId);
                }
            }
        }

        patchStream?.Dispose();
        baseStream?.Dispose();
    }

    private BinaryBundle DeserializeDeltaBundle(DataStream? baseStream, DataStream deltaStream)
    {
        var magic = deltaStream.ReadUInt64();
        if (magic != 0x0000000001000000)
        {
            throw new InvalidDataException();
        }

        var bundleSize = deltaStream.ReadUInt32(Endian.Big);
        deltaStream.ReadUInt32(Endian.Big); // size of data after binary bundle

        var startOffset = deltaStream.Position;

        var patchedBundleSize = deltaStream.ReadInt32(Endian.Big);
        var baseBundleSize = baseStream?.ReadUInt32(Endian.Big) ?? 0;
        var baseBundleOffset = baseStream?.Position ?? -1;

        using (BlockStream stream = new(patchedBundleSize + 4))
        {
            stream.WriteInt32(patchedBundleSize, Endian.Big);

            while (deltaStream.Position < bundleSize + startOffset)
            {
                var packed = deltaStream.ReadUInt32(Endian.Big);
                var instructionType = (packed & 0xF0000000) >> 28;
                var blockData = (int)(packed & 0x0FFFFFFF);

                switch (instructionType)
                {
                    // read base block
                    case 0:
                        stream.Write(baseStream!.ReadBytes(blockData), 0, blockData);
                        break;
                    // skip base block
                    case 4:
                        baseStream!.Position += blockData;
                        break;
                    // read delta block
                    case 8:
                        stream.Write(deltaStream.ReadBytes(blockData), 0, blockData);
                        break;
                }
            }

            if (baseStream is not null)
            {
                baseStream.Position = baseBundleOffset + baseBundleSize;
            }

            stream.Position = 0;
            return BinaryBundle.Deserialize(stream);
        }
    }

    public static long GetSize(DataStream stream, long originalSize)
    {
        long size = 0;
        while (originalSize > 0)
        {
            ReadBlock(stream, ref originalSize, ref size);
        }

        return size;
    }

    private static void ReadBlock(DataStream stream, ref long originalSize, ref long size)
    {
        var packed = stream.ReadUInt64(Endian.Big);

        var decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        var compressionType = (byte)((packed >> 24) & 0x7F);
        var bufferSize = (int)(packed & 0x000FFFFF);

        originalSize -= decompressedSize;

        if (compressionType == 0)
        {
            bufferSize = decompressedSize;
        }

        size += bufferSize + 8;
        stream.Position += bufferSize;
    }
}