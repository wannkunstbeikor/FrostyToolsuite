namespace Frosty.Sdk.Managers.Entries;

public class SuperBundleEntry
{
    public SuperBundleEntry(string inName)
    {
        Name = inName;
    }

    /// <summary>
    ///     The name of this <see cref="SuperBundleEntry" />.
    /// </summary>
    public string Name { get; }
}