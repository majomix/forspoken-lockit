using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace ForspokenEarcLib;

/// <summary>
/// Writes EARC archives for Forspoken. Preserves original archive parameters.
/// </summary>
public class EarcWriter
{
    private const uint Magic = 0x46415243;
    private const uint HeaderSize = 64;
    private const uint FileEntrySize = 40;
    private const uint PointerSize = 8;

    // These can be set from the original archive or use defaults
    public uint Version { get; set; } = 0x00030014; // Forspoken
    public uint BlockSize { get; set; } = 16;
    public uint ChunkSize { get; set; } = 512;
    public uint Flags { get; set; } = 0;
    public ulong OriginalHash { get; set; } = 0;

    private readonly List<EarcWriteEntry> _files = [];

    /// <summary>
    /// Initialize writer with parameters copied from an existing archive.
    /// </summary>
    public static EarcWriter FromExisting(EarcArchive source)
    {
        return new EarcWriter
        {
            Version = source.Header.Version,
            BlockSize = source.Header.BlockSize > 0 ? source.Header.BlockSize : 16,
            ChunkSize = source.Header.ChunkSize > 0 ? source.Header.ChunkSize : 512,
            Flags = (uint)source.Header.Flags,
            OriginalHash = source.Header.Hash,
        };
    }

    public void AddFile(string uri, string relativePath, byte[] data,
        uint originalSize, uint processedSize, EarcFileFlags flags, ulong id = 0)
    {
        _files.Add(new EarcWriteEntry
        {
            Uri = uri,
            RelativePath = relativePath,
            RawData = data,
            OriginalSize = originalSize,
            ProcessedSize = processedSize > 0 ? processedSize : originalSize,
            Flags = flags,
            Id = id,
        });
    }

    /// <summary>
    /// Add a raw uncompressed file.
    /// </summary>
    public void AddRawFile(string uri, string relativePath, byte[] data, EarcFileFlags flags)
    {
        AddFile(uri, relativePath, data, (uint)data.Length, (uint)data.Length, flags);
    }

    /// <summary>
    /// Add a reference entry (e.g., data://mods/xxx.ebex@).
    /// </summary>
    public void AddReference(string uri, string relativePath)
    {
        AddFile(uri, relativePath, [], 0, 0,
            EarcFileFlags.Autoload | EarcFileFlags.Reference);
    }

    public void WriteToFile(string path)
    {
        using var archive = new MemoryStream();

        // Sort exactly like Flagrum: OrderBy(bool) sorts false before true
        var sorted = _files
            .OrderBy(f => f.Flags.HasFlag(EarcFileFlags.Autoload))
            .ThenBy(f => f.Uri.EndsWith(".autoext"))
            .ThenBy(f => f.Flags.HasFlag(EarcFileFlags.Reference))
            .ThenBy(f => f.Id > 0 ? f.Id : AssetHash.HashFileUri64(f.Uri))
            .ToList();

        // Compute IDs
        foreach (var f in sorted)
        {
            if (f.Id == 0)
                f.Id = AssetHash.HashFileUri64(f.Uri);
        }

        // Layout: Header | FileHeaders | URIs | Paths | Data
        uint fhEnd = HeaderSize + (uint)sorted.Count * FileEntrySize;
        uint uriListOffset = Align(fhEnd, PointerSize);

        // Build URI list — write WITHOUT null terminator (alignment gap provides it)
        int currentUriOffset = 0;
        foreach (var f in sorted)
        {
            f.UriOffset = uriListOffset + (uint)currentUriOffset;
            var size = Encoding.UTF8.GetByteCount(f.Uri);
            currentUriOffset += (int)Align((uint)size, PointerSize);
        }

        uint pathListOffset = Align(uriListOffset + (uint)currentUriOffset, PointerSize);

        // Build path list
        int currentPathOffset = 0;
        foreach (var f in sorted)
        {
            f.PathOffset = pathListOffset + (uint)currentPathOffset;
            var size = Encoding.UTF8.GetByteCount(f.RelativePath);
            currentPathOffset += (int)Align((uint)size, PointerSize);
        }

        // Write URI strings
        archive.Seek(uriListOffset, SeekOrigin.Begin);
        foreach (var f in sorted)
        {
            archive.Seek(f.UriOffset, SeekOrigin.Begin);
            var bytes = Encoding.UTF8.GetBytes(f.Uri);
            archive.Write(bytes);
        }

        // Write path strings
        archive.Seek(pathListOffset, SeekOrigin.Begin);
        foreach (var f in sorted)
        {
            archive.Seek(f.PathOffset, SeekOrigin.Begin);
            var bytes = Encoding.UTF8.GetBytes(f.RelativePath);
            archive.Write(bytes);
        }

        uint dataOffset = Align((uint)archive.Position, BlockSize);

        // Write file data with zero padding. Hash computed up to endOfFile (before last padding).
        long endOfFile = 0;
        archive.Seek(dataOffset, SeekOrigin.Begin);
        for (int fi = 0; fi < sorted.Count; fi++)
        {
            var f = sorted[fi];
            f.DataOffset = (ulong)archive.Position;
            if (f.RawData.Length > 0)
                archive.Write(f.RawData);

            // endOfFile = position after last file's data, BEFORE its padding
            if (fi == sorted.Count - 1)
                endOfFile = archive.Position;

            // Pad to next block boundary (Flagrum's GetAlignment always advances)
            uint finalSize = Align(f.ProcessedSize, BlockSize);
            int paddingSize = (int)(finalSize - f.ProcessedSize);
            archive.Write(new byte[paddingSize]);
        }



        // Write file headers
        archive.Seek(HeaderSize, SeekOrigin.Begin);
        foreach (var f in sorted)
        {
            var entry = new byte[FileEntrySize];
            BinaryPrimitives.WriteUInt64LittleEndian(entry, f.Id);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(8), f.OriginalSize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(12), f.ProcessedSize);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(16), (int)f.Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(20), f.UriOffset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(24), f.DataOffset);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(32), f.PathOffset);
            // bytes 36-39: loctype=0, locale=0, key=0
            archive.Write(entry);
        }

        // Compute hash (endOfFile = after last file data, before its padding — matches Flagrum)
        ulong hash = CalculateHash(archive, endOfFile);

        // Write header
        archive.Seek(0, SeekOrigin.Begin);
        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)sorted.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), BlockSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), HeaderSize); // FileHeadersOffset
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), uriListOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), pathListOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), dataOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(32), Flags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36), ChunkSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(40), hash);
        // bytes 48-63: zero padding
        archive.Write(header);

        // Flush to disk
        archive.Seek(0, SeekOrigin.Begin);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        archive.CopyTo(fs);
    }

    private static ulong CalculateHash(MemoryStream stream, long dataSize)
    {
        const int CHUNK_SIZE = 0x800_0000; // 128 MiB

        long size = dataSize - HeaderSize;
        long aligned = (size + CHUNK_SIZE - 1) & ~((long)CHUNK_SIZE - 1);
        int blocks = (int)(aligned >> 27);

        var hashList = new byte[(blocks + 1) * 16];
        var chunkBuf = new byte[Math.Min(CHUNK_SIZE, size)];

        stream.Seek(HeaderSize, SeekOrigin.Begin);
        for (int i = 0; i < blocks; i++)
        {
            int length = (int)Math.Min(size - (long)i * CHUNK_SIZE, CHUNK_SIZE);
            stream.ReadExactly(chunkBuf, 0, length);
            MD5.HashData(chunkBuf.AsSpan(0, length)).CopyTo(hashList, i * 16);
        }

        int seedOff = blocks * 16;
        if (blocks > 8)
        {
            ulong seed = (ulong)size ^ 0x75757575_75757575UL;
            for (int i = 0; i < 4; i++)
            {
                seed ^= seed << 13;
                seed ^= seed >> 7;
                seed ^= seed << 17;
                BinaryPrimitives.WriteUInt32LittleEndian(hashList.AsSpan(seedOff + i * 4), (uint)seed);
            }
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(hashList.AsSpan(seedOff), 0xb16949df);
            BinaryPrimitives.WriteUInt32LittleEndian(hashList.AsSpan(seedOff + 4), 0x104098f5);
            BinaryPrimitives.WriteUInt32LittleEndian(hashList.AsSpan(seedOff + 8), 0x9eb9b68b);
            BinaryPrimitives.WriteUInt32LittleEndian(hashList.AsSpan(seedOff + 12), 0x3120f7cb);
        }

        var sha = SHA256.HashData(hashList);
        return BinaryPrimitives.ReadUInt64LittleEndian(sha)
             ^ BinaryPrimitives.ReadUInt64LittleEndian(sha.AsSpan(8))
             ^ BinaryPrimitives.ReadUInt64LittleEndian(sha.AsSpan(16))
             ^ BinaryPrimitives.ReadUInt64LittleEndian(sha.AsSpan(24));
    }

    private static void PadTo(MemoryStream stream, uint alignment)
    {
        long pos = stream.Position;
        long aligned = alignment + alignment * (pos / alignment);
        if (aligned > pos)
            stream.Write(new byte[aligned - pos]);
    }

    /// <summary>
    /// Flagrum-compatible alignment: always advances to the NEXT boundary, even if already aligned.
    /// Formula: blockSize + blockSize * (offset / blockSize)
    /// </summary>
    private static uint Align(uint value, uint alignment) =>
        alignment + alignment * (value / alignment);

    private class EarcWriteEntry
    {
        public string Uri { get; set; } = "";
        public string RelativePath { get; set; } = "";
        public byte[] RawData { get; set; } = [];
        public uint OriginalSize { get; set; }
        public uint ProcessedSize { get; set; }
        public EarcFileFlags Flags { get; set; }
        public ulong Id { get; set; }
        public uint UriOffset { get; set; }
        public uint PathOffset { get; set; }
        public ulong DataOffset { get; set; }
    }
}
