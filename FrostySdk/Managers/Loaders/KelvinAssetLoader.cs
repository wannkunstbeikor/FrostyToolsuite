using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers.Loaders;

public class KelvinAssetLoader : IAssetLoader
{
    public void Load()
    {
        foreach (var sbInfo in FileSystemManager.EnumerateSuperBundles())
        {
            foreach (var installChunk in sbInfo.InstallChunks)
            {
                if (installChunk.Value.HasFlag(InstallChunkType.Default))
                {
                    var tocPath = FileSystemManager.ResolvePath(true, $"{sbInfo.Name}.toc");
                    if (string.IsNullOrEmpty(tocPath))
                    {
                        tocPath = FileSystemManager.ResolvePath(false, $"{sbInfo.Name}.toc");
                        if (string.IsNullOrEmpty(tocPath))
                        {
                            continue;
                        }
                    }

                    var superBundleId = AssetManager.AddSuperBundle(sbInfo.Name);

                    LoadToc(superBundleId, tocPath);
                }

                if (installChunk.Value.HasFlag(InstallChunkType.Split))
                {
                    var installChunkInfo = FileSystemManager.GetInstallChunkInfo(installChunk.Key);

                    var sbName =
                        $"{installChunkInfo.InstallBundle}{sbInfo.Name[sbInfo.Name.IndexOf("/", StringComparison.Ordinal)..]}";

                    var tocPath = FileSystemManager.ResolvePath(true, $"{sbName}.toc");
                    if (string.IsNullOrEmpty(tocPath))
                    {
                        tocPath = FileSystemManager.ResolvePath(false, $"{sbName}.toc");
                        if (string.IsNullOrEmpty(tocPath))
                        {
                            continue;
                        }
                    }

                    var superBundleId = AssetManager.AddSuperBundle(sbInfo.Name);

                    LoadToc(superBundleId, tocPath);
                }
            }
        }
    }

    private void LoadToc(int superBundleId, string tocPath)
    {
        using (var stream = BlockStream.FromFile(tocPath, true))
        {
            var magic = stream.ReadUInt32();
            var bundlesOffset = stream.ReadUInt32();
            var chunksOffset = stream.ReadUInt32();

            if (magic == 0xC3E5D5C3)
            {
                stream.Decrypt(KeyManager.GetKey("BundleEncryptionKey"), PaddingMode.None);
            }

            if (bundlesOffset != 0xFFFFFFFF)
            {
                stream.Position = bundlesOffset;

                var bundleCount = stream.ReadInt32();

                // bundle hashmap
                stream.Position += sizeof(int) * bundleCount;

                for (var i = 0; i < bundleCount; i++)
                {
                    var bundleOffset = stream.ReadUInt32();

                    var curPos = stream.Position;

                    stream.Position = bundleOffset;

                    var name = ReadString(stream, stream.ReadInt32());

                    List<FileIdentifier> files = new();
                    while (true)
                    {
                        var file = stream.ReadInt32();
                        var fileOffset = stream.ReadUInt32();
                        var fileSize = stream.ReadUInt32();

                        files.Add(new FileIdentifier(file & 0x7FFFFFFF, fileOffset, fileSize));
                        if ((file & 0x80000000) == 0)
                        {
                            break;
                        }
                    }

                    stream.Position = curPos;

                    var index = 0;
                    var resourceInfo = files[index];

                    var dataStream = BlockStream.FromFile(
                        FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                        resourceInfo.Offset, (int)resourceInfo.Size);

                    var bundle = BinaryBundle.Deserialize(dataStream);

                    var bundleId = AssetManager.AddBundle(name, superBundleId);

                    foreach (var ebx in bundle.EbxList)
                    {
                        if (dataStream.Position == resourceInfo.Size)
                        {
                            dataStream.Dispose();
                            resourceInfo = files[++index];
                            dataStream = BlockStream.FromFile(
                                FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                                resourceInfo.Offset, (int)resourceInfo.Size);
                        }

                        var offset = (uint)dataStream.Position;
                        ebx.Size = DbObjectAssetLoader.GetSize(dataStream, ebx.OriginalSize);

                        ebx.FileInfos.Add(new PathFileInfo(FileSystemManager.GetFilePath(resourceInfo.FileIndex),
                            resourceInfo.Offset + offset, (uint)ebx.Size, 0));

                        AssetManager.AddEbx(ebx, bundleId);
                    }

                    foreach (var res in bundle.ResList)
                    {
                        if (dataStream.Position == resourceInfo.Size)
                        {
                            dataStream.Dispose();
                            resourceInfo = files[++index];
                            dataStream = BlockStream.FromFile(
                                FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                                resourceInfo.Offset, (int)resourceInfo.Size);
                        }

                        var offset = (uint)dataStream.Position;
                        res.Size = DbObjectAssetLoader.GetSize(dataStream, res.OriginalSize);

                        res.FileInfos.Add(new PathFileInfo(FileSystemManager.GetFilePath(resourceInfo.FileIndex),
                            resourceInfo.Offset + offset, (uint)res.Size, 0));

                        AssetManager.AddRes(res, bundleId);
                    }

                    foreach (var chunk in bundle.ChunkList)
                    {
                        if (dataStream.Position == resourceInfo.Size)
                        {
                            dataStream.Dispose();
                            resourceInfo = files[++index];
                            dataStream = BlockStream.FromFile(
                                FileSystemManager.ResolvePath(FileSystemManager.GetFilePath(resourceInfo.FileIndex)),
                                resourceInfo.Offset, (int)resourceInfo.Size);
                        }

                        var offset = (uint)dataStream.Position;
                        chunk.Size = DbObjectAssetLoader.GetSize(dataStream,
                            (chunk.LogicalOffset & 0xFFFF) | chunk.LogicalSize);

                        chunk.FileInfos.Add(new PathFileInfo(FileSystemManager.GetFilePath(resourceInfo.FileIndex),
                            resourceInfo.Offset + offset, (uint)chunk.Size, chunk.LogicalOffset));

                        AssetManager.AddChunk(chunk, bundleId);
                    }

                    dataStream.Dispose();
                }
            }

            if (chunksOffset != 0xFFFFFFFF)
            {
                stream.Position = chunksOffset;
                var chunksCount = stream.ReadInt32();

                // hashmap
                stream.Position += sizeof(int) * chunksCount;

                for (var i = 0; i < chunksCount; i++)
                {
                    var offset = stream.ReadInt32();

                    var pos = stream.Position;
                    stream.Position = offset;

                    var guid = stream.ReadGuid();
                    var fileIndex = stream.ReadInt32();
                    var dataOffset = stream.ReadUInt32();
                    var dataSize = stream.ReadUInt32();

                    ChunkAssetEntry chunk = new(guid, Sha1.Zero, dataSize, 0, 0, superBundleId);

                    chunk.FileInfos.Add(new PathFileInfo(FileSystemManager.GetFilePath(fileIndex), dataOffset, dataSize,
                        0));

                    AssetManager.AddSuperBundleChunk(chunk);

                    stream.Position = pos;
                }
            }
        }
    }

    private string ReadString(DataStream reader, int offset)
    {
        var curPos = reader.Position;
        StringBuilder sb = new();

        do
        {
            reader.Position = offset - 1;
            var tmp = reader.ReadNullTerminatedString();
            offset = reader.ReadInt32();

            sb.Append(tmp);
        } while (offset != 0);

        reader.Position = curPos;
        return new string(sb.ToString().Reverse().ToArray());
    }

    private readonly struct FileIdentifier
    {
        public readonly int FileIndex;
        public readonly uint Offset;
        public readonly uint Size;

        public FileIdentifier(int inFileIndex, uint inOffset, uint inSize)
        {
            FileIndex = inFileIndex;
            Offset = inOffset;
            Size = inSize;
        }
    }
}