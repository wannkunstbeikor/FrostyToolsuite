using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;
using Frosty.Sdk.Managers.Loaders.Helpers;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Loaders;

public class Manifest2019AssetLoader : IAssetLoader
{
    public void Load()
    {
        foreach (var sbInfo in FileSystemManager.EnumerateSuperBundles())
        {
            foreach (var installChunk in sbInfo.InstallChunks)
            {
                if (installChunk.Value.HasFlag(InstallChunkType.Default))
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

                    List<BundleInfo> bundles = new();
                    var superBundleId = AssetManager.AddSuperBundle(sbInfo.Name);

                    if (!LoadToc(sbInfo.Name, superBundleId, tocPath, ref bundles, isPatched))
                    {
                        continue;
                    }

                    LoadSb(bundles, superBundleId);
                }

                if (installChunk.Value.HasFlag(InstallChunkType.Split))
                {
                    var installChunkInfo = FileSystemManager.GetInstallChunkInfo(installChunk.Key);

                    var sbName =
                        $"{installChunkInfo.InstallBundle}{sbInfo.Name[sbInfo.Name.IndexOf("/", StringComparison.Ordinal)..]}";

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

                    List<BundleInfo> bundles = new();
                    var superBundleId = AssetManager.AddSuperBundle(sbInfo.Name);

                    if (!LoadToc(sbName, superBundleId, tocPath, ref bundles, isPatched))
                    {
                        continue;
                    }

                    LoadSb(bundles, superBundleId);
                }
            }
        }
    }

    private bool LoadToc(string sbName, int superBundleId, string path, ref List<BundleInfo> bundles,
        bool isPatched)
    {
        using (var stream = BlockStream.FromFile(path, true))
        {
            var bundleHashMapOffset = stream.ReadUInt32(Endian.Big);
            var bundleDataOffset = stream.ReadUInt32(Endian.Big);
            var bundlesCount = stream.ReadInt32(Endian.Big);

            var chunkHashMapOffset = stream.ReadUInt32(Endian.Big);
            var chunkGuidOffset = stream.ReadUInt32(Endian.Big);
            var chunksCount = stream.ReadInt32(Endian.Big);

            // not used by any game rn, maybe crypto stuff
            stream.Position += sizeof(uint);
            stream.Position += sizeof(uint);

            var namesOffset = stream.ReadUInt32(Endian.Big);

            var chunkDataOffset = stream.ReadUInt32(Endian.Big);
            var dataCount = stream.ReadInt32(Endian.Big);

            var flags = (Flags)stream.ReadInt32(Endian.Big);

            if (flags.HasFlag(Flags.HasBaseBundles) || flags.HasFlag(Flags.HasBaseChunks))
            {
                var tocPath = FileSystemManager.ResolvePath(false, $"{sbName}.toc");
                LoadToc(sbName, superBundleId, tocPath, ref bundles, false);
            }

            uint namesCount = 0;
            uint tableCount = 0;
            var tableOffset = uint.MaxValue;
            HuffmanDecoder? huffmanDecoder = null;

            if (flags.HasFlag(Flags.HasCompressedNames))
            {
                huffmanDecoder = new HuffmanDecoder();
                namesCount = stream.ReadUInt32(Endian.Big);
                tableCount = stream.ReadUInt32(Endian.Big);
                tableOffset = stream.ReadUInt32(Endian.Big);
            }

            if (bundlesCount != 0)
            {
                if (flags.HasFlag(Flags.HasCompressedNames))
                {
                    stream.Position = namesOffset;
                    huffmanDecoder!.ReadEncodedData(stream, namesCount, Endian.Big);

                    stream.Position = tableOffset;
                    huffmanDecoder.ReadHuffmanTable(stream, tableCount, Endian.Big);
                }

                stream.Position = bundleHashMapOffset;
                stream.Position += sizeof(int) * bundlesCount;

                stream.Position = bundleDataOffset;

                for (var i = 0; i < bundlesCount; i++)
                {
                    var nameOffset = stream.ReadInt32(Endian.Big);
                    var bundleSize = stream.ReadUInt32(Endian.Big); // flag in first 2 bits: 0x40000000 inline sb
                    var bundleOffset = stream.ReadInt64(Endian.Big);

                    string name;

                    if (flags.HasFlag(Flags.HasCompressedNames))
                    {
                        name = huffmanDecoder!.ReadHuffmanEncodedString(nameOffset);
                    }
                    else
                    {
                        var curPos = stream.Position;
                        stream.Position = namesOffset + nameOffset;
                        name = stream.ReadNullTerminatedString();
                        stream.Position = curPos;
                    }

                    var idx = bundles.FindIndex(bbi => bbi.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (idx != -1)
                    {
                        bundles.RemoveAt(idx);
                    }

                    BundleInfo bi = new()
                    {
                        Name = name,
                        SbName = sbName,
                        Offset = bundleOffset,
                        Size = bundleSize,
                        IsPatch = isPatched
                    };

                    if (bundleSize != uint.MaxValue && bundleOffset != -1)
                    {
                        bundles.Add(bi);
                    }
                }

                huffmanDecoder?.Dispose();
            }

            if (chunksCount != 0)
            {
                stream.Position = chunkHashMapOffset;
                stream.Position += sizeof(int) * chunksCount;

                stream.Position = chunkDataOffset;
                Block<uint> chunkData = new(dataCount);
                stream.ReadExactly(chunkData.ToBlock<byte>());

                stream.Position = chunkGuidOffset;
                Span<byte> b = stackalloc byte[16];
                for (var i = 0; i < chunksCount; i++)
                {
                    stream.ReadExactly(b);
                    b.Reverse();

                    Guid guid = new(b);

                    // 0xFFFFFFFF remove chunk
                    var index = stream.ReadInt32(Endian.Big);

                    if (index != -1)
                    {
                        // im guessing the unknown offsets are connected to this
                        var flag = (byte)(index >> 24);

                        index &= 0x00FFFFFF;

                        CasFileIdentifier casFileIdentifier;
                        if (flag == 1)
                        {
                            casFileIdentifier =
                                CasFileIdentifier.FromFileIdentifier(
                                    BinaryPrimitives.ReverseEndianness(chunkData[index++]));
                        }
                        else if (flag == 0x80)
                        {
                            casFileIdentifier = CasFileIdentifier.FromFileIdentifier(
                                BinaryPrimitives.ReverseEndianness(chunkData[index++]),
                                BinaryPrimitives.ReverseEndianness(chunkData[index++]));
                        }
                        else
                        {
                            throw new NotImplementedException("Unknown file identifier flag.");
                        }

                        var offset = BinaryPrimitives.ReverseEndianness(chunkData[index++]);
                        var size = BinaryPrimitives.ReverseEndianness(chunkData[index]);

                        ChunkAssetEntry chunk = new(guid, Sha1.Zero, size, 0, 0, superBundleId);

                        chunk.FileInfos.Add(new CasFileInfo(casFileIdentifier, offset, size, 0));

                        AssetManager.AddSuperBundleChunk(chunk);
                    }
                }

                chunkData.Dispose();
            }

            return true;
        }
    }

    private void LoadSb(List<BundleInfo> bundles, int superBundleId)
    {
        var patchSbPath = string.Empty;
        var baseSbPath = string.Empty;
        BlockStream? patchStream = null;
        BlockStream? baseStream = null;

        foreach (var bundleInfo in bundles)
        {
            // get where the bundle is stored, either in toc or sb file
            byte flag;
            if ((bundleInfo.Size & 0xC0000000) == 0x40000000)
            {
                flag = 1;
            }
            else if ((bundleInfo.Size & 0xC0000000) == 0x80000000)
            {
                throw new NotImplementedException("unknown flag");
            }
            else
            {
                flag = 0;
            }

            // get correct stream for this bundle
            BlockStream stream;
            if (bundleInfo.IsPatch)
            {
                if (patchStream == null || patchSbPath != bundleInfo.SbName)
                {
                    patchSbPath = bundleInfo.SbName;
                    patchStream?.Dispose();

                    if (flag == 1)
                    {
                        patchStream = BlockStream.FromFile(
                            FileSystemManager.ResolvePath(true, $"{patchSbPath}.toc"), true);
                    }
                    else
                    {
                        patchStream = BlockStream.FromFile(
                            FileSystemManager.ResolvePath(true, $"{patchSbPath}.sb"), false);
                    }
                }

                stream = patchStream;
            }
            else
            {
                if (baseStream == null || baseSbPath != bundleInfo.SbName)
                {
                    baseSbPath = bundleInfo.SbName;
                    baseStream?.Dispose();

                    if (flag == 1)
                    {
                        baseStream = BlockStream.FromFile(
                            FileSystemManager.ResolvePath(false, $"{baseSbPath}.toc"), true);
                    }
                    else
                    {
                        baseStream = BlockStream.FromFile(
                            FileSystemManager.ResolvePath(false, $"{baseSbPath}.sb"), false);
                    }
                }

                stream = baseStream;
            }

            BinaryBundle bundle;

            stream.Position = bundleInfo.Offset;

            var bundleOffset = stream.ReadInt32(Endian.Big);
            var bundleSize = stream.ReadInt32(Endian.Big);
            var locationOffset = stream.ReadUInt32(Endian.Big);
            var totalCount = stream.ReadInt32(Endian.Big);
            var dataOffset = stream.ReadUInt32(Endian.Big);

            // not used by any game rn, again maybe crypto stuff
            stream.Position += sizeof(uint);
            stream.Position += sizeof(uint);
            // maybe count for the offsets above
            stream.Position += sizeof(int);

            // bundles can be stored in this file or in a separate cas file, then the first file info is for the bundle.
            // Seems to be related to the flag for in which file the sb is stored
            var inlineBundle = !(bundleOffset == 0 && bundleSize == 0);

            stream.Position = bundleInfo.Offset + locationOffset;

            Block<byte> flags = new(totalCount);
            stream.ReadExactly(flags);

            CasFileIdentifier casFileIdentifier = default;
            var z = 0;

            if (inlineBundle)
            {
                stream.Position = bundleInfo.Offset + bundleOffset;
                bundle = BinaryBundle.Deserialize(stream);
                stream.Position = bundleInfo.Offset + dataOffset;
            }
            else
            {
                stream.Position = bundleInfo.Offset + dataOffset;

                var fileFlag = flags[z++];
                if (fileFlag == 1)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big));
                }
                else if (fileFlag == 0x80)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big),
                        stream.ReadUInt32(Endian.Big));
                }

                var offset = stream.ReadUInt32(Endian.Big);
                var size = stream.ReadInt32(Endian.Big);

                var path = FileSystemManager.GetFilePath(casFileIdentifier);

                using (var casStream = BlockStream.FromFile(FileSystemManager.ResolvePath(path), offset, size))
                {
                    bundle = BinaryBundle.Deserialize(casStream);
                }
            }

            var bundleId = AssetManager.AddBundle(bundleInfo.Name, superBundleId);

            foreach (var ebx in bundle.EbxList)
            {
                var fileFlag = flags[z++];
                if (fileFlag == 1)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big));
                }
                else if (fileFlag == 0x80)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big),
                        stream.ReadUInt32(Endian.Big));
                }

                var offset = stream.ReadUInt32(Endian.Big);
                ebx.Size = stream.ReadUInt32(Endian.Big);

                ebx.FileInfos.Add(new CasFileInfo(casFileIdentifier, offset, (uint)ebx.Size, 0));

                AssetManager.AddEbx(ebx, bundleId);
            }

            foreach (var res in bundle.ResList)
            {
                var fileFlag = flags[z++];
                if (fileFlag == 1)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big));
                }
                else if (fileFlag == 0x80)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big),
                        stream.ReadUInt32(Endian.Big));
                }

                var offset = stream.ReadUInt32(Endian.Big);
                res.Size = stream.ReadUInt32(Endian.Big);

                res.FileInfos.Add(new CasFileInfo(casFileIdentifier, offset, (uint)res.Size, 0));

                AssetManager.AddRes(res, bundleId);
            }

            foreach (var chunk in bundle.ChunkList)
            {
                var fileFlag = flags[z++];
                if (fileFlag == 1)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big));
                }
                else if (fileFlag == 0x80)
                {
                    casFileIdentifier = CasFileIdentifier.FromFileIdentifier(stream.ReadUInt32(Endian.Big),
                        stream.ReadUInt32(Endian.Big));
                }

                var offset = stream.ReadUInt32(Endian.Big);
                chunk.Size = stream.ReadUInt32(Endian.Big);

                chunk.FileInfos.Add(new CasFileInfo(casFileIdentifier, offset, (uint)chunk.Size, chunk.LogicalOffset));

                AssetManager.AddChunk(chunk, bundleId);
            }

            flags.Dispose();
        }

        patchStream?.Dispose();
        baseStream?.Dispose();
    }

    [Flags]
    private enum Flags
    {
        HasBaseBundles = 1 << 0, // base toc has bundles that the patch doesnt have
        HasBaseChunks = 1 << 1, // base toc has chunks that the patch doesnt have
        HasCompressedNames = 1 << 2 // bundle names are huffman encoded
    }
}