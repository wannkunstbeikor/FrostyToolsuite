namespace Frosty.Sdk.Ebx;

public readonly struct ResourceRef
{
    public static ResourceRef Zero = new(0);
    private readonly ulong m_resourceId;

    public ResourceRef(ulong value)
    {
        m_resourceId = value;
    }

    public static implicit operator ulong(ResourceRef value)
    {
        return value.m_resourceId;
    }

    public static implicit operator ResourceRef(ulong value)
    {
        return new ResourceRef(value);
    }

    public override bool Equals(object? obj)
    {
        if (obj is ResourceRef a)
        {
            return Equals(a);
        }

        if (obj is ulong b)
        {
            return Equals(b);
        }

        return false;
    }

    public bool Equals(ResourceRef b)
    {
        return m_resourceId == b.m_resourceId;
    }

    public static bool operator ==(ResourceRef a, ResourceRef b)
    {
        return a.Equals(b);
    }

    public static bool operator !=(ResourceRef a, ResourceRef b)
    {
        return !a.Equals(b);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)2166136261;
            hash = (hash * 16777619) ^ m_resourceId.GetHashCode();
            return hash;
        }
    }

    public override string ToString()
    {
        return m_resourceId.ToString("X16");
    }
}