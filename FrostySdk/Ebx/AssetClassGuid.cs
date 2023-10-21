using System;

namespace Frosty.Sdk.Ebx;

public readonly struct AssetClassGuid
{
    public Guid ExportedGuid => m_exportedGuid;
    public int InternalId { get; }

    public bool IsExported { get; }

    private readonly Guid m_exportedGuid;

    public AssetClassGuid(Guid inGuid, int inId)
    {
        m_exportedGuid = inGuid;
        InternalId = inId;
        IsExported = inGuid != Guid.Empty;
    }

    public AssetClassGuid(int inId)
    {
        m_exportedGuid = Guid.Empty;
        InternalId = inId;
        IsExported = false;
    }

    public static bool operator ==(AssetClassGuid a, object b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(AssetClassGuid a, object b)
    {
        return !a.Equals(b);
    }

    public override bool Equals(object? obj)
    {
        switch (obj)
        {
            case null:
                return false;
            case AssetClassGuid reference:
                return IsExported == reference.IsExported && m_exportedGuid == reference.m_exportedGuid &&
                       InternalId == reference.InternalId;
            case Guid guid:
                return IsExported && guid == m_exportedGuid;
            case int id:
                return InternalId == id;
            default:
                return false;
        }
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)2166136261;
            hash = (hash * 16777619) ^ m_exportedGuid.GetHashCode();
            hash = (hash * 16777619) ^ InternalId.GetHashCode();
            hash = (hash * 16777619) ^ IsExported.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        return IsExported ? m_exportedGuid.ToString() : $"00000000-0000-0000-0000-{InternalId:x12}";
    }
}