using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Frosty.Sdk.Managers;

namespace FrostyEditor.Models;

public partial class FolderTreeNodeModel : ObservableObject
{
    public HashSet<AssetModel> Assets = new();

    private readonly ObservableCollection<FolderTreeNodeModel> m_children = new();
    private readonly Dictionary<int, int> m_childrenMap = new();

    [ObservableProperty] private bool m_hasChildren;

    [ObservableProperty] private bool m_isExpanded;

    [ObservableProperty] private string m_name;

    public FolderTreeNodeModel(string inName)
    {
        Name = inName;
    }

    public IReadOnlyList<FolderTreeNodeModel> Children => m_children;

    public static FolderTreeNodeModel Create()
    {
        FolderTreeNodeModel root = new("ROOT") { IsExpanded = true };

        foreach (var entry in AssetManager.EnumerateEbxAssetEntries())
        {
            AssetModel asset = new(entry);

            var path = entry.Name;

            var folders = path.Split('/');

            var current = root;
            for (var i = 0; i < folders.Length - 1; i++)
            {
                FolderTreeNodeModel folder;
                var name = folders[i];
                var hash = Frosty.Sdk.Utils.Utils.HashString(name, true);
                if (!current.m_childrenMap.ContainsKey(hash))
                {
                    current.m_childrenMap.Add(hash, current.m_children.Count);
                    current.m_children.Add(folder = new FolderTreeNodeModel(name));
                    current.HasChildren = true;
                }
                else
                {
                    folder = current.m_children[current.m_childrenMap[hash]];
                }

                current = folder;
            }

            current.Assets.Add(asset);
        }

        return root;
    }

    public bool Equals(FolderTreeNodeModel other)
    {
        return Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj)
    {
        if (obj is FolderTreeNodeModel b)
        {
            return Equals(b);
        }

        return false;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }

    public static Comparison<FolderTreeNodeModel?> SortAscending<T>(Func<FolderTreeNodeModel, T> selector)
    {
        return (x, y) =>
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            return Comparer<T>.Default.Compare(selector(x), selector(y));
        };
    }

    public static Comparison<FolderTreeNodeModel?> SortDescending<T>(Func<FolderTreeNodeModel, T> selector)
    {
        return (x, y) =>
        {
            if (x is null && y is null)
            {
                return 0;
            }

            if (x is null)
            {
                return 1;
            }

            if (y is null)
            {
                return -1;
            }

            return Comparer<T>.Default.Compare(selector(y), selector(x));
        };
    }
}