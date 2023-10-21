using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Deobfuscators;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Infos;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers;

public static class FileSystemManager
{
    public static string BasePath = string.Empty;
    public static string CacheName = string.Empty;

    public static uint Base;
    public static uint Head;

    public static BundleFormat BundleFormat;

    public static GamePlatform GamePlatform = GamePlatform.Invalid;

    public static DbObjectDict? Manifest;

    private static readonly List<string> s_paths = new();

    private static readonly List<SuperBundleInfo> s_superBundles = new();
    private static readonly Dictionary<int, int> s_persistentIndexMap = new();
    private static readonly List<InstallChunkInfo> s_installChunks = new();
    private static readonly List<string> s_casFiles = new();

    private static Type? s_deobfuscator;
    private static readonly Dictionary<string, Block<byte>> s_memoryFs = new();
    public static bool IsInitialized { get; private set; }

    public static bool Initialize(string basePath)
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!ProfilesLibrary.IsInitialized)
        {
            return false;
        }

        if (!Directory.Exists(basePath))
        {
            return false;
        }

        BasePath = Path.GetFullPath(basePath);

        CacheName = $"Caches/{ProfilesLibrary.InternalName}";
        s_deobfuscator = ProfilesLibrary.FrostbiteVersion > "2014.4.11"
            ? typeof(SignatureDeobfuscator)
            : typeof(LegacyDeobfuscator);

        foreach (var source in ProfilesLibrary.Sources)
        {
            AddSource(source.Path, source.SubDirs);
        }

        if (!ProcessLayouts())
        {
            return false;
        }

        IsInitialized = true;
        return true;
    }

    /// <summary>
    ///     Resolves the path of a file inside the games data directories.
    /// </summary>
    /// <param name="filename">
    ///     <para>The relative path of the file prefixed with native_data or native_patch.</para>
    ///     If there is no prefix it will look through all data directories starting with the patch ones.
    /// </param>
    /// <returns>The full path to the file or an empty string if the file doesnt exist.</returns>
    public static string ResolvePath(string filename)
    {
        if (filename.StartsWith("native_patch/") && s_paths.Count == 1)
        {
            return string.Empty;
        }

        var startCount = 0;
        var endCount = s_paths.Count;

        if (filename.StartsWith("native_data/") && s_paths.Count > 1)
        {
            startCount = 1;
        }
        else if (filename.StartsWith("native_patch/"))
        {
            endCount = 1;
        }

        filename = filename.Replace("native_data/", string.Empty);
        filename = filename.Replace("native_patch/", string.Empty);
        filename = filename.Trim('/');

        for (var i = startCount; i < endCount; i++)
        {
            var path = Path.Combine(BasePath, s_paths[i], filename);
            if (File.Exists(path) || Directory.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    public static string ResolvePath(bool isPatch, string filename)
    {
        if (isPatch && s_paths.Count == 1)
        {
            return string.Empty;
        }

        var startCount = 0;
        var endCount = s_paths.Count;

        if (!isPatch && s_paths.Count > 1)
        {
            startCount = 1;
        }
        else if (isPatch)
        {
            endCount = 1;
        }

        for (var i = startCount; i < endCount; i++)
        {
            var path = Path.Combine(BasePath, s_paths[i], filename);
            if (File.Exists(path) || Directory.Exists(path))
            {
                return path;
            }
        }

        return string.Empty;
    }

    public static string GetFilePath(CasFileIdentifier casFileIdentifier)
    {
        var installChunkInfo = s_installChunks[s_persistentIndexMap[casFileIdentifier.InstallChunkIndex]];
        return
            $"{(casFileIdentifier.IsPatch ? "native_patch/" : "native_data/")}{installChunkInfo.InstallBundle}/cas_{casFileIdentifier.CasIndex:D2}.cas";
    }

    public static string GetFilePath(int casIndex)
    {
        return s_casFiles[casIndex];
    }

    public static string GetPatchPath()
    {
        return s_paths[0];
    }

    public static IDeobfuscator? CreateDeobfuscator()
    {
        return s_deobfuscator != null ? (IDeobfuscator?)Activator.CreateInstance(s_deobfuscator) : null;
    }

    public static IEnumerable<SuperBundleInfo> EnumerateSuperBundles()
    {
        foreach (var sbInfo in s_superBundles)
        {
            yield return sbInfo;
        }
    }

    public static IEnumerable<InstallChunkInfo> EnumerateInstallChunks()
    {
        foreach (var installChunkInfo in s_installChunks)
        {
            yield return installChunkInfo;
        }
    }

    public static InstallChunkInfo GetInstallChunkInfo(int index)
    {
        return s_installChunks[s_persistentIndexMap[index]];
    }

    public static InstallChunkInfo GetInstallChunkInfo(Guid id)
    {
        return s_installChunks.FirstOrDefault(ic => ic.Id == id) ?? throw new KeyNotFoundException();
    }

    public static int GetInstallChunkIndex(InstallChunkInfo info)
    {
        // TODO: works for now, since we only call this for the cat which doesnt have a persistent index, but we should create a dict for the reverse thing
        return s_installChunks.IndexOf(info);
    }

    public static bool HasFileInMemoryFs(string name)
    {
        return s_memoryFs.ContainsKey(name);
    }

    public static Block<byte> GetFileFromMemoryFs(string name)
    {
        return s_memoryFs[name];
    }

    private static void AddSource(string path, bool iterateSubPaths)
    {
        var fullPath = Path.Combine(BasePath, path);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        if (iterateSubPaths)
        {
            foreach (var subPath in Directory.EnumerateDirectories(fullPath, "*", SearchOption.AllDirectories))
            {
                if (subPath.Contains("Patch", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(subPath, "package.mft")))
                {
                    continue;
                }

                s_paths.Add(Path.Combine(Path.GetRelativePath(BasePath, subPath), "Data"));
            }
        }
        else
        {
            s_paths.Add(path);
        }
    }

    private static bool LoadInitFs(string name)
    {
        ParseGamePlatform(name.Remove(0, 7));

        var path = ResolvePath(name);
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var initFs = DbObject.Deserialize(path);

        if (initFs is null)
        {
            return false;
        }

        if (initFs.IsDict())
        {
            if (!initFs.AsDict().ContainsKey("encrypted"))
            {
                return false;
            }

            if (!KeyManager.HasKey("InitFsKey"))
            {
                return false;
            }

            using (BlockStream stream = new(new Block<byte>(initFs.AsDict().AsBlob("encrypted"))))
            {
                stream.Decrypt(KeyManager.GetKey("InitFsKey"), PaddingMode.PKCS7);

                initFs = DbObject.Deserialize(stream);

                if (initFs is null)
                {
                    return false;
                }
            }
        }

        foreach (var fileStub in initFs.AsList())
        {
            var file = fileStub.AsDict().AsDict("$file");
            var fileName = file.AsString("name");

            s_memoryFs.TryAdd(fileName, new Block<byte>(file.AsBlob("payload")));
        }

        return true;
    }

    private static void ParseGamePlatform(string platform)
    {
        if (GamePlatform != GamePlatform.Invalid)
        {
            return;
        }

        switch (platform)
        {
            case "Win32":
                GamePlatform = GamePlatform.Win32;
                break;
            case "Linux":
                GamePlatform = GamePlatform.Linux;
                break;
            case "Xenon":
                GamePlatform = GamePlatform.Xenon;
                break;
            case "Gen4a":
                GamePlatform = GamePlatform.Gen4a;
                break;
            case "Ps3":
                GamePlatform = GamePlatform.Ps3;
                break;
            case "Gen4b":
                GamePlatform = GamePlatform.Gen4b;
                break;
            case "Nx":
                GamePlatform = GamePlatform.Nx;
                break;
            default:
                throw new NotImplementedException($"GamePlatform not implemented: {platform}");
        }
    }

    private static bool ProcessLayouts()
    {
        var baseLayoutPath = ResolvePath(false, "layout.toc");
        var patchLayoutPath = ResolvePath(true, "layout.toc");

        if (string.IsNullOrEmpty(baseLayoutPath))
        {
            return false;
        }

        // Process base layout.toc
        var baseLayout = DbObject.Deserialize(baseLayoutPath)?.AsDict();

        if (baseLayout is null)
        {
            return false;
        }

        var kelvinPath = ResolvePath("kelvin.toc");
        if (!string.IsNullOrEmpty(kelvinPath))
        {
            BundleFormat = BundleFormat.Kelvin;
        }

        foreach (var superBundle in baseLayout.AsDict().AsList("superBundles"))
        {
            s_superBundles.Add(new SuperBundleInfo(superBundle.AsDict().AsString("name")));
        }

        if (!string.IsNullOrEmpty(patchLayoutPath))
        {
            // Process patch layout.toc
            var patchLayout = DbObject.Deserialize(patchLayoutPath)?.AsDict();

            if (patchLayout is null)
            {
                return false;
            }

            foreach (var superBundle in patchLayout.AsList("superBundles"))
            {
                // Merge super bundles
                var superBundleName = superBundle.AsDict().AsString("name");
                if (s_superBundles.FindIndex(si =>
                        si.Name.Equals(superBundleName, StringComparison.OrdinalIgnoreCase)) == -1)
                {
                    s_superBundles.Add(new SuperBundleInfo(superBundleName));
                }
            }

            Base = patchLayout.AsUInt("base");
            Head = patchLayout.AsUInt("head");

            if (!ProcessInstallChunks(patchLayout.AsDict("installManifest", null)))
            {
                return false;
            }

            Manifest = patchLayout.AsDict("manifest", null);
            if (Manifest is not null)
            {
                BundleFormat = BundleFormat.SuperBundleManifest;
            }

            if (!LoadInitFs(patchLayout.AsList("fs")[0].AsString()))
            {
                return false;
            }
        }
        else
        {
            Base = baseLayout.AsUInt("base");
            Head = baseLayout.AsUInt("head");

            if (!ProcessInstallChunks(baseLayout.AsDict("installManifest", null)))
            {
                return false;
            }


            Manifest = baseLayout.AsDict("manifest", null);
            if (Manifest is not null)
            {
                BundleFormat = BundleFormat.SuperBundleManifest;
            }

            if (!LoadInitFs(baseLayout.AsList("fs")[0].AsString()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ProcessInstallChunks(DbObjectDict? installManifest)
    {
        // Only if an install manifest exists
        if (installManifest is null)
        {
            // Older games dont have an InstallManifest, they have one InstallChunk in the data/patch folder
            InstallChunkInfo ci = new();
            foreach (var si in s_superBundles)
            {
                ci.SuperBundles.Add(si.Name);

                si.InstallChunks.Add(0, InstallChunkType.Default);
            }

            s_installChunks.Add(ci);
        }
        else
        {
            var platform = installManifest.AsString("platform");
            if (!string.IsNullOrEmpty(platform))
            {
                ParseGamePlatform(platform);
            }

            // check for platform, else we get it from the initFs
            foreach (var installChunk in installManifest.AsList("installChunks"))
            {
                if (installChunk.AsDict().AsBoolean("testDLC"))
                {
                    continue;
                }

                InstallChunkInfo info = new()
                {
                    Id = installChunk.AsDict().AsGuid("id"),
                    Name = installChunk.AsDict().AsString("name"),
                    InstallBundle = installChunk.AsDict().AsString("installBundle"),
                    AlwaysInstalled = installChunk.AsDict().AsBoolean("alwaysInstalled")
                };

                var index = installChunk.AsDict().AsInt("persistentIndex", s_installChunks.Count);
                s_persistentIndexMap.Add(index, s_installChunks.Count);
                s_installChunks.Add(info);

                foreach (var superBundle in installChunk.AsDict().AsList("superbundles"))
                {
                    info.SuperBundles.Add(superBundle.AsString());

                    var sb = s_superBundles.Find(si =>
                        si.Name.Equals(superBundle.AsString(), StringComparison.OrdinalIgnoreCase))!;
                    if (!sb.InstallChunks.ContainsKey(index))
                    {
                        sb.InstallChunks.Add(index, InstallChunkType.Default);
                    }
                    else
                    {
                        sb.InstallChunks[index] |= InstallChunkType.Default;
                    }
                }

                foreach (var requiredChunk in installChunk.AsDict().AsList("requiredChunks"))
                {
                    info.RequiredCatalogs.Add(requiredChunk.AsGuid());
                }

                if (installChunk.AsDict().ContainsKey("files"))
                {
                    foreach (var fileObj in installChunk.AsDict().AsList("files"))
                    {
                        var casId = fileObj.AsDict().AsInt("id");
                        while (s_casFiles.Count <= casId)
                        {
                            s_casFiles.Add(string.Empty);
                        }

                        var casPath = fileObj.AsDict().AsString("path").Trim('/');
                        casPath = casPath.Replace("native_data/Data", "native_data");
                        casPath = casPath.Replace("native_data/Patch", "native_patch");

                        s_casFiles[casId] = casPath;
                    }
                }

                if (installChunk.AsDict().ContainsKey("splitSuperbundles"))
                {
                    foreach (var superBundleContainer in installChunk.AsDict().AsList("splitSuperbundles"))
                    {
                        var superBundle = superBundleContainer.AsDict().AsString("superbundle");
                        info.SplitSuperBundles.Add(superBundle);

                        var sb = s_superBundles.Find(si =>
                            si.Name.Equals(superBundle, StringComparison.OrdinalIgnoreCase))!;
                        if (!sb.InstallChunks.ContainsKey(index))
                        {
                            sb.InstallChunks.Add(index, InstallChunkType.Split);
                        }
                        else
                        {
                            sb.InstallChunks[index] |= InstallChunkType.Split;
                        }
                    }
                }

                if (installChunk.AsDict().ContainsKey("splitTocs"))
                {
                    foreach (var superBundleContainer in installChunk.AsDict().AsList("splitTocs"))
                    {
                        var superBundle = superBundleContainer.AsDict().AsString("superbundle");
                        info.SplitSuperBundles.Add(superBundle);

                        var sb = s_superBundles.Find(si =>
                            si.Name.Equals(superBundle, StringComparison.OrdinalIgnoreCase))!;
                        if (!sb.InstallChunks.ContainsKey(index))
                        {
                            sb.InstallChunks.Add(index, InstallChunkType.Split);
                        }
                        else
                        {
                            sb.InstallChunks[index] |= InstallChunkType.Split;
                        }
                    }
                }
            }

            if (installManifest.ContainsKey("settings"))
            {
                BundleFormat = (BundleFormat)installManifest.AsDict("settings").AsLong("bundleFormat");
            }
        }

        return true;
    }
}