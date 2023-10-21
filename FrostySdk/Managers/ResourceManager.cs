using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.CatResources;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Managers.Infos.FileInfos;

namespace Frosty.Sdk.Managers;

public static class ResourceManager
{
    private static readonly Dictionary<Sha1, (List<CasFileInfo>, bool)> s_resourceEntries = new();

    private static readonly List<CatPatchEntry> s_patchEntries = new();

    private static readonly Dictionary<Sha1, uint> s_sizeMap = new();
    public static bool IsInitialized { get; private set; }

    public static void LoadInstallChunks()
    {
        foreach (var installChunkInfo in FileSystemManager.EnumerateInstallChunks())
        {
            LoadInstallChunk(installChunkInfo);
        }
    }

    private static void LoadInstallChunk(InstallChunkInfo info)
    {
        var deltaInfos = LoadEntries(info, true);
        var baseInfos = LoadEntries(info, false);

        if (deltaInfos is null)
        {
            return;
        }

        List<CasFileInfo>? baseFileInfos = null;
        foreach (var entry in s_patchEntries)
        {
            var containsBase = baseInfos?.TryGetValue(entry.BaseSha1, out baseFileInfos) == true;

            if (!deltaInfos.TryGetValue(entry.DeltaSha1, out var deltaFileInfos))
            {
                throw new Exception();
            }

            Debug.Assert(deltaFileInfos.Count == 1, "More than one Delta entry.");

            CasFileInfo? baseInfo = null;
            if (containsBase)
            {
                // no idea why there are sometimes more than one base entry
                Debug.Assert(baseFileInfos!.Count >= 1);
                baseInfo = baseFileInfos[0];
            }

            CasFileInfo fileInfo = new(baseInfo?.GetBase(), deltaFileInfos[0].GetBase());
            s_resourceEntries.TryAdd(entry.Sha1, (new List<CasFileInfo>(), false));
            s_resourceEntries[entry.Sha1].Item1.Add(fileInfo);
        }

        s_patchEntries.Clear();
    }

    private static Dictionary<Sha1, List<CasFileInfo>>? LoadEntries(InstallChunkInfo info, bool patch)
    {
        var filePath = FileSystemManager.ResolvePath(patch, $"{info.InstallBundle}/cas.cat");

        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        Dictionary<Sha1, List<CasFileInfo>> retVal = new();

        var installChunkIndex = FileSystemManager.GetInstallChunkIndex(info);

        using (CatStream stream = new(filePath))
        {
            for (var i = 0; i < stream.ResourceCount; i++)
            {
                var entry = stream.ReadResourceEntry();
                CasFileIdentifier casFileIdentifier = new(patch, installChunkIndex, entry.ArchiveIndex);

                CasFileInfo fileInfo = new(casFileIdentifier, entry.Offset, entry.Size, entry.LogicalOffset);

                s_resourceEntries.TryAdd(entry.Sha1, (new List<CasFileInfo>(), false));
                s_resourceEntries[entry.Sha1].Item1.Add(fileInfo);

                if (!s_sizeMap.TryAdd(entry.Sha1, entry.Size))
                {
                    s_sizeMap[entry.Sha1] = Math.Max(s_sizeMap[entry.Sha1], entry.Size);
                }

                retVal.TryAdd(entry.Sha1, new List<CasFileInfo>());
                retVal[entry.Sha1].Add(fileInfo);
            }

            for (var i = 0; i < stream.EncryptedCount; i++)
            {
                var entry = stream.ReadEncryptedEntry();
                CasFileIdentifier casFileIdentifier = new(patch, installChunkIndex, entry.ArchiveIndex);

                CasFileInfo fileInfo = new(casFileIdentifier, entry.Offset, entry.Size, entry.LogicalOffset,
                    entry.KeyId);

                s_resourceEntries.TryAdd(entry.Sha1, (new List<CasFileInfo>(), false));
                s_resourceEntries[entry.Sha1].Item1.Add(fileInfo);

                if (!s_sizeMap.TryAdd(entry.Sha1, entry.Size))
                {
                    s_sizeMap[entry.Sha1] = Math.Max(s_sizeMap[entry.Sha1], entry.Size);
                }

                retVal.TryAdd(entry.Sha1, new List<CasFileInfo>());
                retVal[entry.Sha1].Add(fileInfo);
            }

            for (var i = 0; i < stream.PatchCount; i++)
            {
                var entry = stream.ReadPatchEntry();
                s_patchEntries.Add(entry);
            }
        }

        return retVal;
    }

    public static void CLearInstallChunks()
    {
        s_resourceEntries.Clear();
        s_patchEntries.Clear();
    }

    public static bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!FileSystemManager.IsInitialized)
        {
            return false;
        }

        if (FileSystemManager.HasFileInMemoryFs("Dictionaries/ebx.dict"))
        {
            // load dictionary from memoryFs (used for decompressing ebx)
        }

        if (FileSystemManager.HasFileInMemoryFs("Scripts/CasEncrypt.yaml"))
        {
            // load CasEncrypt.yaml from memoryFs (used for decrypting data in cas files)
            using (TextReader stream =
                   new StreamReader(FileSystemManager.GetFileFromMemoryFs("Scripts/CasEncrypt.yaml").ToStream()))
            {
                byte[]? key = null;
                while (stream.Peek() != -1)
                {
                    var line = stream.ReadLine()!;
                    if (line.Contains("keyid:"))
                    {
                        var arr = line.Split(':');
                        KeyManager.AddKey(arr[1].Trim(), key!);
                    }
                    else if (line.Contains("key:"))
                    {
                        var arr = line.Split(':');
                        var keyStr = arr[1].Trim();

                        key = new byte[keyStr.Length / 2];
                        for (var i = 0; i < keyStr.Length / 2; i++)
                        {
                            key[i] = Convert.ToByte(keyStr.Substring(i * 2, 2), 16);
                        }
                    }
                }
            }
        }

        foreach (var libOodle in Directory.EnumerateFiles(FileSystemManager.BasePath, "oo2core_*"))
        {
            Directory.CreateDirectory("ThirdParty");

            var ext = Path.GetExtension(libOodle);
            var path = $"ThirdParty/oo2core{ext}";
            File.Delete(path);
            File.CreateSymbolicLink(path, libOodle);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && ext == ".dll")
            {
                const string oodleHack = "ThirdParty/oo2core.so";
                File.Delete(oodleHack);
                File.CreateSymbolicLink(oodleHack, "ThirdParty/liblinoodle.so");
            }

            break;
        }

        IsInitialized = true;
        return true;
    }

    public static long GetSize(Sha1 sha1)
    {
        return s_sizeMap.TryGetValue(sha1, out var size) ? size : -1;
    }

    public static IEnumerable<IFileInfo>? GetPatchFileInfos(Sha1 sha1, Sha1 deltaSha1, Sha1 baseSha1)
    {
        if (!s_resourceEntries.TryGetValue(sha1, out var fileInfos))
        {
            return null;
        }

        if (fileInfos.Item2)
        {
            return null;
        }

        var baseInfos = s_resourceEntries[baseSha1].Item1;
        var deltaInfos = s_resourceEntries[deltaSha1].Item1;

        for (var j = 0; j < baseInfos.Count; j++)
        {
            CasFileInfo fileInfo = new(deltaInfos[j].GetBase(), baseInfos[j].GetBase());

            s_resourceEntries.TryAdd(sha1, (new List<CasFileInfo>(), true));
            s_resourceEntries[sha1].Item1.Add(fileInfo);
        }

        return s_resourceEntries[sha1].Item1;
    }

    public static IEnumerable<IFileInfo>? GetFileInfos(Sha1 sha1)
    {
        if (!s_resourceEntries.TryGetValue(sha1, out var fileInfos))
        {
            return null;
        }

        if (fileInfos.Item2)
        {
            return null;
        }

        fileInfos.Item2 = true;
        return fileInfos.Item1;
    }
}