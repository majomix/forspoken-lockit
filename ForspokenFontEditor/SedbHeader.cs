using System.Buffers.Binary;

namespace ForspokenFontEditor;

/// <summary>
/// SEDB (Section Data Binary) container header — 48 bytes.
/// </summary>
public class SedbHeader
{
    public const int Size = 48;
    public const uint SedbMagic = 0x42444553; // "SEDB" LE
    public const uint UifnSubtype = 0x6E666975; // "uifn" LE

    public uint Magic { get; set; } = SedbMagic;
    public uint Subtype { get; set; } = UifnSubtype;
    public uint Version { get; set; } = 1;
    public byte EndianType { get; set; }
    public byte AlignmentBits { get; set; }
    public ushort Offset { get; set; } = Size;
    public ulong FileSize { get; set; }
    public ulong DateTime { get; set; }
    public byte[] ResourceId { get; set; } = new byte[16];

    public static SedbHeader Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
            throw new InvalidDataException("Data too short for SEDB header.");

        var header = new SedbHeader
        {
            Magic = BinaryPrimitives.ReadUInt32LittleEndian(data),
            Subtype = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
            Version = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
            EndianType = data[12],
            AlignmentBits = data[13],
            Offset = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]),
            FileSize = BinaryPrimitives.ReadUInt64LittleEndian(data[16..]),
            DateTime = BinaryPrimitives.ReadUInt64LittleEndian(data[24..]),
        };

        data.Slice(32, 16).CopyTo(header.ResourceId);

        if (header.Magic != SedbMagic)
            throw new InvalidDataException($"Invalid SEDB magic: 0x{header.Magic:X8}");
        if (header.Subtype != UifnSubtype)
            throw new InvalidDataException($"Invalid SEDB subtype: 0x{header.Subtype:X8} (expected 'uifn')");

        return header;
    }

    public byte[] Write(ulong fileSize)
    {
        FileSize = fileSize;
        var data = new byte[Size];
        BinaryPrimitives.WriteUInt32LittleEndian(data, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), Subtype);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), Version);
        data[12] = EndianType;
        data[13] = AlignmentBits;
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), Offset);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(16), FileSize);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(24), DateTime);
        ResourceId.CopyTo(data.AsSpan(32));
        return data;
    }
}
