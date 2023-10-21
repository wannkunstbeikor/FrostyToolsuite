using System;
using System.Diagnostics;
using System.IO;
using Frosty.Sdk.IO.Compression;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Profiles;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.IO;

public static class Cas
{
    public static Block<byte> DecompressData(DataStream inStream, int inOriginalSize)
    {
        Block<byte> outBuffer = new(inOriginalSize);
        while (inStream.Position < inStream.Length)
        {
            ReadBlock(inStream, outBuffer);
        }

        outBuffer.ResetShift();
        return outBuffer;
    }

    public static Block<byte> DecompressData(DataStream inDeltaStream, DataStream inBaseStream, int inOriginalSize)
    {
        Block<byte> outBuffer = new(inOriginalSize);
        while (inDeltaStream.Position < inDeltaStream.Length)
        {
            var packed = inDeltaStream.ReadUInt32(Endian.Big);
            var instructionType = (int)(packed & 0xF0000000) >> 28;

            switch (instructionType)
            {
                case 0:
                {
                    // read base blocks
                    var blockCount = (int)(packed & 0x0FFFFFFF);
                    while (blockCount-- > 0)
                    {
                        ReadBlock(inBaseStream, outBuffer);
                    }

                    break;
                }
                case 1:
                {
                    // make large fixes in base block
                    var blockCount = (int)(packed & 0x0FFFFFFF);

                    // read base block
                    using (var toPatch = ReadBlock(inBaseStream))
                    {
                        while (blockCount-- > 0)
                        {
                            var offset = inDeltaStream.ReadUInt16(Endian.Big);
                            var skipCount = inDeltaStream.ReadUInt16(Endian.Big);

                            // use base
                            var baseSize = offset - toPatch.ShiftAmount;
                            toPatch.CopyTo(outBuffer, baseSize);
                            toPatch.Shift(baseSize);
                            outBuffer.Shift(baseSize);

                            // use delta
                            ReadBlock(inDeltaStream, outBuffer);

                            // skip base
                            toPatch.Shift(skipCount);
                        }
                    }

                    break;
                }
                case 2:
                {
                    // make small fixes in base block
                    var deltaBlockSize = (int)(packed & 0x0FFFFFFF);
                    var curPos = inDeltaStream.Position;

                    var newBlockSize = inDeltaStream.ReadUInt16(Endian.Big) + 1;
                    var currentOffset = outBuffer.ShiftAmount;

                    // read base block
                    using (var toPatch = ReadBlock(inBaseStream))
                    {
                        while (inDeltaStream.Position < curPos + deltaBlockSize)
                        {
                            var offset = inDeltaStream.ReadUInt16(Endian.Big);
                            var skipCount = inDeltaStream.ReadByte();
                            var addCount = inDeltaStream.ReadByte();

                            // use base
                            var baseSize = offset - toPatch.ShiftAmount;
                            toPatch.CopyTo(outBuffer, baseSize);
                            toPatch.Shift(baseSize);
                            outBuffer.Shift(baseSize);

                            // skip base
                            toPatch.Shift(skipCount);

                            // add delta
                            inDeltaStream.ReadExactly(outBuffer.ToSpan(0, addCount));
                            outBuffer.Shift(addCount);
                        }
                    }

                    Debug.Assert(outBuffer.ShiftAmount - currentOffset == newBlockSize, "Fuck");

                    break;
                }
                case 3:
                {
                    // read delta blocks
                    var blockCount = (int)(packed & 0x0FFFFFFF);
                    while (blockCount-- > 0)
                    {
                        ReadBlock(inDeltaStream, outBuffer);
                    }

                    break;
                }
                case 4:
                {
                    // skip blocks
                    var blockCount = (int)(packed & 0x0FFFFFFF);
                    while (blockCount-- > 0)
                    {
                        ReadBlock(inBaseStream, null);
                    }

                    break;
                }
                default:
                    throw new InvalidDataException("block type");
            }
        }


        outBuffer.ResetShift();
        return outBuffer;
    }

    private static unsafe void ReadBlock(DataStream inStream, Block<byte>? outBuffer)
    {
        var packed = inStream.ReadUInt64(Endian.Big);

        var flags = (byte)(packed >> 56);
        var decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        var compressionType = (CompressionType)(packed >> 24);
        Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
        var bufferSize = (int)(packed & 0x000FFFFF);

        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            bufferSize = decompressedSize;
        }

        Block<byte> compressedBuffer;
        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None && outBuffer is not null)
        {
            compressedBuffer = new Block<byte>(outBuffer.Ptr, bufferSize);
            compressedBuffer.MarkMemoryAsFragile();
        }
        else
        {
            compressedBuffer = new Block<byte>(bufferSize);
        }

        if (outBuffer is not null)
        {
            Decompress(inStream, compressedBuffer, compressionType, flags, bufferSize, outBuffer);
        }

        compressedBuffer.Dispose();
        outBuffer?.Shift(decompressedSize);
    }

    private static Block<byte> ReadBlock(DataStream inStream)
    {
        var packed = inStream.ReadUInt64(Endian.Big);

        var flags = (byte)(packed >> 56);
        var decompressedSize = (int)((packed >> 32) & 0x00FFFFFF);
        var compressionType = (CompressionType)(packed >> 24);
        Debug.Assert(((packed >> 20) & 0xF) == 7, "Invalid cas data");
        var bufferSize = (int)(packed & 0x000FFFFF);

        Block<byte> outBuffer = new(decompressedSize);

        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            bufferSize = decompressedSize;
        }

        Block<byte> compressedBuffer;
        if ((compressionType & ~CompressionType.Obfuscated) == CompressionType.None)
        {
            compressedBuffer = outBuffer;
        }
        else
        {
            compressedBuffer = new Block<byte>(bufferSize);
        }

        Decompress(inStream, compressedBuffer, compressionType, flags, bufferSize, outBuffer);

        // dispose compressed buffer, unless there wasn't a compressed buffer
        if ((compressionType & ~CompressionType.Obfuscated) != CompressionType.None)
        {
            compressedBuffer.Dispose();
        }

        return outBuffer;
    }

    private static void Decompress(DataStream inStream, Block<byte> inCompressedBuffer,
        CompressionType inCompressionType, byte inFlags, int inBufferSize, Block<byte> outBuffer)
    {
        inStream.ReadExactly(inCompressedBuffer);

        if (inCompressionType.HasFlag(CompressionType.Obfuscated))
        {
            // this probably only exist in FIFA 19
            // should be fine to check for it here, since i doubt they will ever have 128 different compression types
            // currently they are at 25
            if (!ProfilesLibrary.IsLoaded(ProfileVersion.Fifa19))
            {
                throw new Exception("obfuscation");
            }

            var key = KeyManager.GetKey("CasObfuscationKey");
            for (var i = 0; i < inBufferSize; i++)
            {
                inCompressedBuffer[i] ^= key[i & 0x3FFF];
            }
        }

        // TODO: compression methods https://github.com/users/CadeEvs/projects/1?pane=issue&itemId=25477869
        switch (inCompressionType & ~CompressionType.Obfuscated)
        {
            case CompressionType.None:
                // we already read the data into the outBuffer so nothing to do
                break;
            case CompressionType.ZLib:
                //ZLib.Decompress(compressedBuffer, ref outBuffer);
                break;
            case CompressionType.ZStd:
                //ZStd.Decompress(compressedBuffer, ref outBuffer, flags != 0 ? CompressionFlags.ZStdUseDicts : CompressionFlags.None);
                break;
            case CompressionType.LZ4:
                //LZ4.Decompress(compressedBuffer, ref outBuffer);
                break;
            case CompressionType.OodleKraken:
                //Oodle.Decompress(compressedBuffer, ref outBuffer, CompressionFlags.OodleKraken);
                break;
            case CompressionType.OodleSelkie:
                //Oodle.Decompress(compressedBuffer, ref outBuffer, CompressionFlags.OodleSelkie);
                break;
            case CompressionType.OodleLeviathan:
                //Oodle.Decompress(compressedBuffer, ref outBuffer, CompressionFlags.OodleLeviathan);
                break;
            default:
                throw new NotImplementedException($"Compression type: {inCompressionType}");
        }
    }
}