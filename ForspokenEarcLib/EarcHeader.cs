using System.Buffers.Binary;

namespace ForspokenEarcLib;

/// <summary>
/// EARC (Ebony Archive) header — 64 bytes at offset 0.
/// Magic: "FARC" (0x46415243).
/// </summary>
public class EarcHeader
{
    public const int Size = 64;
    public const uint ExpectedMagic = 0x46415243; // "FARC"

    public uint Magic { get; set; }
    public uint Version { get; set; }
    public uint FileCount { get; set; }
    public uint BlockSize { get; set; }
    public uint FileHeadersOffset { get; set; }
    public uint UriListOffset { get; set; }
    public uint PathListOffset { get; set; }
    public uint DataOffset { get; set; }
    public int Flags { get; set; }
    public uint ChunkSize { get; set; }
    public ulong Hash { get; set; }

    public bool IsProtected => (Version & 0x80000000) != 0;

    public static EarcHeader Read(BinaryReader reader)
    {
        var header = new EarcHeader
        {
            Magic = reader.ReadUInt32(),
            Version = reader.ReadUInt32(),
            FileCount = reader.ReadUInt32(),
            BlockSize = reader.ReadUInt32(),
            FileHeadersOffset = reader.ReadUInt32(),
            UriListOffset = reader.ReadUInt32(),
            PathListOffset = reader.ReadUInt32(),
            DataOffset = reader.ReadUInt32(),
            Flags = reader.ReadInt32(),
            ChunkSize = reader.ReadUInt32(),
            Hash = reader.ReadUInt64(),
        };

        // Skip 16 bytes padding
        reader.ReadBytes(16);

        if (header.Magic != ExpectedMagic)
            throw new InvalidDataException($"Invalid EARC magic: 0x{header.Magic:X8}");

        return header;
    }
}
