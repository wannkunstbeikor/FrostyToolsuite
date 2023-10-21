using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Managers.Loaders;
using Frosty.Sdk.Profiles;

namespace Frosty.Sdk.IO;

public class BinaryBundle
{
    private BinaryBundle(DataStream stream)
    {
        // we use big endian for default
        var endian = Endian.Big;

        var size = stream.ReadUInt32(Endian.Big);

        var startPos = stream.Position;

        var magic = (Magic)(stream.ReadUInt32(endian) ^ GetSalt());

        // check what endian its written in
        if (!IsValidMagic(magic))
        {
            endian = Endian.Little;
            var value = (uint)magic ^ GetSalt();
            magic = (Magic)(BinaryPrimitives.ReverseEndianness(value) ^ GetSalt());

            if (!IsValidMagic(magic))
            {
                throw new InvalidDataException("magic");
            }
        }

        var containsSha1 = magic == Magic.Standard;

        var totalCount = stream.ReadUInt32(endian);
        var ebxCount = stream.ReadInt32(endian);
        var resCount = stream.ReadInt32(endian);
        var chunkCount = stream.ReadInt32(endian);
        var stringsOffset = stream.ReadUInt32(endian) + (magic == Magic.Encrypted ? 0 : startPos);
        stream.Position += sizeof(uint) + sizeof(int); // metaOffset + metaSize

        EbxList = new EbxAssetEntry[ebxCount];
        ResList = new ResAssetEntry[resCount];
        ChunkList = new ChunkAssetEntry[chunkCount];

        var sha1 = new Sha1[totalCount];

        // decrypt the data
        if (magic == Magic.Encrypted)
        {
            if (stream is not BlockStream blockStream)
            {
                throw new Exception();
            }

            blockStream.Decrypt(KeyManager.GetKey("BundleEncryptionKey"), (int)(size - 0x20), PaddingMode.None);
        }

        // read sha1s
        for (var i = 0; i < totalCount; i++)
        {
            sha1[i] = containsSha1 ? stream.ReadSha1() : Sha1.Zero;
        }

        var j = 0;
        for (var i = 0; i < ebxCount; i++, j++)
        {
            var nameOffset = stream.ReadUInt32(endian);
            var originalSize = stream.ReadUInt32(endian);

            var currentPos = stream.Position;
            stream.Position = stringsOffset + nameOffset;
            var name = stream.ReadNullTerminatedString();

            EbxList[i] = new EbxAssetEntry(name, sha1[j], -1, originalSize);

            stream.Position = currentPos;
        }

        var resTypeOffset = stream.Position + (resCount * 2 * sizeof(uint));
        var resMetaOffset = stream.Position + (resCount * 2 * sizeof(uint)) + (resCount * sizeof(uint));
        var resRidOffset = stream.Position + (resCount * 2 * sizeof(uint)) + (resCount * sizeof(uint)) +
                           (resCount * 0x10);
        for (var i = 0; i < resCount; i++, j++)
        {
            var nameOffset = stream.ReadUInt32(endian);
            var originalSize = stream.ReadUInt32(endian);

            var currentPos = stream.Position;
            stream.Position = stringsOffset + nameOffset;
            var name = stream.ReadNullTerminatedString();

            stream.Position = resTypeOffset + (i * sizeof(uint));
            var resType = stream.ReadUInt32();

            stream.Position = resMetaOffset + (i * 0x10);
            var resMeta = stream.ReadBytes(0x10);

            stream.Position = resRidOffset + (i * sizeof(ulong));
            var resRid = stream.ReadUInt64();

            ResList[i] = new ResAssetEntry(name, sha1[j], -1, originalSize, resRid, resType, resMeta);

            stream.Position = currentPos;
        }

        stream.Position = resRidOffset + (resCount * sizeof(ulong));
        for (var i = 0; i < chunkCount; i++, j++)
        {
            ChunkList[i] = new ChunkAssetEntry(stream.ReadGuid(endian), sha1[j], -1, stream.ReadUInt32(endian),
                stream.ReadUInt32(endian));
        }

        // we need to set the correct position, since the string table comes after the meta
        stream.Position = startPos + size;
    }

    public EbxAssetEntry[] EbxList { get; }
    public ResAssetEntry[] ResList { get; }
    public ChunkAssetEntry[] ChunkList { get; }

    /// <summary>
    ///     Dependent on the FB version, games use different salts.
    ///     If the game uses a version newer than 2017 it uses "pecn", else it uses "pecm".
    ///     <see cref="ProfileVersion.Battlefield5" /> is the only game that uses "arie".
    /// </summary>
    /// <returns>The salt, that the current game uses.</returns>
    private static uint GetSalt()
    {
        const uint pecm = 0x7065636D;
        const uint pecn = 0x7065636E;
        const uint arie = 0x61726965;

        if (ProfilesLibrary.IsLoaded(ProfileVersion.Battlefield5))
        {
            return arie;
        }

        if (ProfilesLibrary.FrostbiteVersion >= "2017")
        {
            return pecn;
        }

        return pecm;
    }

    /// <summary>
    ///     Only the games using the <see cref="KelvinAssetLoader" /> use a different Magic than <see cref="Magic.Standard" />.
    /// </summary>
    /// <returns>The magic the current game uses.</returns>
    private static Magic GetMagic()
    {
        switch (FileSystemManager.BundleFormat)
        {
            case BundleFormat.Kelvin:
                return Magic.Kelvin;
            default:
                return Magic.Standard;
        }
    }

    private static bool IsValidMagic(Magic magic)
    {
        return magic == Magic.Standard || magic == Magic.Kelvin || magic == Magic.Encrypted;
    }

    /// <summary>
    ///     Deserialize a binary stored bundle as <see cref="DbObject" />.
    /// </summary>
    /// <param name="stream">The <see cref="DataStream" /> from which the bundle should be Deserialized from.</param>
    /// <returns></returns>
    /// <exception cref="InvalidDataException">Is thrown when there is no valid <see cref="Magic" />.</exception>
    public static BinaryBundle Deserialize(DataStream stream)
    {
        return new BinaryBundle(stream);
    }

    private enum Magic : uint
    {
        Standard = 0xED1CEDB8,
        Kelvin = 0xC3889333,
        Encrypted = 0xC3E5D5C3
    }
}