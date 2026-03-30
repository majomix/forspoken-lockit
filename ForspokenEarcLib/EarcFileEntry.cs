namespace ForspokenEarcLib;

/// <summary>
/// EARC file entry — 40 bytes per file in the file header table.
/// </summary>
public class EarcFileEntry
{
    public const int Size = 40;

    public ulong Id { get; set; }
    public uint OriginalSize { get; set; }
    public uint ProcessedSize { get; set; }
    public EarcFileFlags Flags { get; set; }
    public uint UriOffset { get; set; }
    public ulong DataOffset { get; set; }
    public uint RelativePathOffset { get; set; }
    public byte LocalizationType { get; set; }
    public byte Locale { get; set; }
    public ushort Key { get; set; }

    public string Uri { get; set; } = "";
    public string RelativePath { get; set; } = "";

    /// <summary>
    /// Index of this entry in the file header table.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Absolute offset of this entry's header in the archive file.
    /// </summary>
    public long HeaderOffset { get; set; }

    public bool IsCompressed => Flags.HasFlag(EarcFileFlags.Compressed);
    public bool IsLz4 => Flags.HasFlag(EarcFileFlags.Lz4Compression);
    public bool IsEncrypted => Flags.HasFlag(EarcFileFlags.Encrypted);
    public bool IsUncompressed => !IsCompressed && !IsEncrypted;

    public static EarcFileEntry Read(BinaryReader reader, int index, long headerOffset)
    {
        var entry = new EarcFileEntry
        {
            Index = index,
            HeaderOffset = headerOffset,
            Id = reader.ReadUInt64(),
            OriginalSize = reader.ReadUInt32(),
            ProcessedSize = reader.ReadUInt32(),
            Flags = (EarcFileFlags)reader.ReadInt32(),
            UriOffset = reader.ReadUInt32(),
            DataOffset = reader.ReadUInt64(),
            RelativePathOffset = reader.ReadUInt32(),
            LocalizationType = reader.ReadByte(),
            Locale = reader.ReadByte(),
            Key = reader.ReadUInt16(),
        };

        // Flag 0x100 implies Compressed
        if (((int)entry.Flags & 256) > 0)
            entry.Flags |= EarcFileFlags.Compressed;

        return entry;
    }
}

[Flags]
public enum EarcFileFlags
{
    None = 0,
    Autoload = 1,
    Compressed = 2,           // Zlib chunk compression
    Reference = 4,
    NoEarc = 8,
    Patched = 16,
    PatchedDeleted = 32,
    Encrypted = 64,           // AES-128
    MaskProtected = 128,
    Lz4Compression = 0x50000000,  // LZ4 frame compression
}
