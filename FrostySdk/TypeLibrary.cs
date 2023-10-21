using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Frosty.Sdk.Attributes;

namespace Frosty.Sdk;

public static class TypeLibrary
{
    private static readonly Dictionary<string, int> s_nameMapping = new();
    private static readonly Dictionary<uint, int> s_nameHashMapping = new();
    private static readonly Dictionary<Guid, int> s_guidMapping = new();
    private static Type[] s_types = Array.Empty<Type>();
    public static bool IsInitialized { get; private set; }

    public static bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        FileInfo fileInfo = new($"Sdk/{ProfilesLibrary.SdkFilename}.dll");
        if (!fileInfo.Exists)
        {
            return false;
        }

        var sdk = Assembly.LoadFile(fileInfo.FullName);

        s_types = sdk.GetTypes();

        for (var i = 0; i < s_types.Length; i++)
        {
            var type = s_types[i];
            var name = type.GetCustomAttribute<DisplayNameAttribute>()?.Name ?? type.Name;
            var nameHash = type.GetCustomAttribute<NameHashAttribute>()?.Hash ?? (uint)Utils.Utils.HashString(name);
            var guid = type.GetCustomAttribute<GuidAttribute>()?.Guid;

            s_nameMapping.Add(name, i);
            s_nameHashMapping.Add(nameHash, i);
            if (guid.HasValue)
            {
                s_guidMapping.Add(guid.Value, i);
            }
        }

        IsInitialized = true;
        return true;
    }

    public static Type? GetType(string name)
    {
        if (!s_nameMapping.TryGetValue(name, out var index))
        {
            return null;
        }

        return s_types[index];
    }

    public static Type? GetType(uint nameHash)
    {
        if (!s_nameHashMapping.TryGetValue(nameHash, out var index))
        {
            return null;
        }

        return s_types[index];
    }

    public static Type? GetType(Guid guid)
    {
        if (!s_guidMapping.TryGetValue(guid, out var index))
        {
            return null;
        }

        return s_types[index];
    }

    public static object? CreateObject(string name)
    {
        var type = GetType(name);
        return type == null ? null : Activator.CreateInstance(type);
    }

    public static object? CreateObject(uint nameHash)
    {
        var type = GetType(nameHash);
        return type == null ? null : Activator.CreateInstance(type);
    }

    public static object? CreateObject(Guid guid)
    {
        var type = GetType(guid);
        return type == null ? null : Activator.CreateInstance(type);
    }

    public static object? CreateObject(Type type)
    {
        return Activator.CreateInstance(type);
    }

    public static bool IsSubClassOf(object obj, string name)
    {
        var type = obj.GetType();

        return IsSubClassOf(type, name);
    }

    public static bool IsSubClassOf(Type type, string name)
    {
        var checkType = GetType(name);
        if (checkType == null)
        {
            return false;
        }

        return type.IsSubclassOf(checkType) || type == checkType;
    }

    public static bool IsSubClassOf(string type, string name)
    {
        var sourceType = GetType(type);

        return sourceType != null && IsSubClassOf(sourceType, name);
    }
}