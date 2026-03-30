using System.Text;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace ForspokenEarcLib;

/// <summary>
/// Reads and modifies Forspoken EARC (Ebony Archive) files.
/// Supports listing, extracting, and replacing files within the archive.
/// </summary>
public class EarcArchive : IDisposable
{
    private readonly string _path;
    private FileStream? _stream;

    public EarcHeader Header { get; private set; } = null!;
    public List<EarcFileEntry> Files { get; private set; } = [];

    private EarcArchive(string path)
    {
        _path = path;
    }

    /// <summary>
    /// Open an EARC archive for reading and modification.
    /// </summary>
    public static EarcArchive Open(string path)
    {
        var archive = new EarcArchive(path);
        archive.Load();
        return archive;
    }

    private void Load()
    {
        _stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

        Header = EarcHeader.Read(reader);

        // Read file entries
        Files = new List<EarcFileEntry>((int)Header.FileCount);
        for (int i = 0; i < Header.FileCount; i++)
        {
            long headerOffset = Header.FileHeadersOffset + (long)i * EarcFileEntry.Size;
            _stream.Seek(headerOffset, SeekOrigin.Begin);
            var entry = EarcFileEntry.Read(reader, i, headerOffset);

            // Read URI
            _stream.Seek(entry.UriOffset, SeekOrigin.Begin);
            entry.Uri = ReadNullTerminatedString(reader);

            // Read relative path
            _stream.Seek(entry.RelativePathOffset, SeekOrigin.Begin);
            entry.RelativePath = ReadNullTerminatedString(reader);

            Files.Add(entry);
        }
    }

    /// <summary>
    /// Find a file entry by URI (e.g., "data://asset/ui/font/FOT-CezannePro-M.uifn").
    /// Case-insensitive search.
    /// </summary>
    public EarcFileEntry? FindByUri(string uri)
    {
        return Files.FirstOrDefault(f =>
            f.Uri.Equals(uri, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Find all file entries matching a pattern (e.g., "*.uifn", "*.parambin").
    /// Searches both URI and relative path.
    /// </summary>
    public IEnumerable<EarcFileEntry> FindByPattern(string pattern)
    {
        var trimmed = pattern.Trim('*');
        bool startsWild = pattern.StartsWith('*');
        bool endsWild = pattern.EndsWith('*');

        return Files.Where(f =>
        {
            var text = f.Uri + "|" + f.RelativePath;
            if (startsWild && endsWild)
                return text.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
            if (startsWild)
                return f.Uri.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       f.RelativePath.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase);
            if (endsWild)
                return f.Uri.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
                       f.RelativePath.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase);
            return f.Uri.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                   f.RelativePath.Equals(trimmed, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Extract raw (processed) data for a file entry, without decompression.
    /// </summary>
    public byte[] ExtractRaw(EarcFileEntry entry)
    {
        EnsureOpen();
        _stream!.Seek((long)entry.DataOffset, SeekOrigin.Begin);
        var data = new byte[entry.ProcessedSize];
        _stream.ReadExactly(data);
        return data;
    }

    /// <summary>
    /// Extract and decompress a file entry.
    /// Handles uncompressed, LZ4, and zlib-chunked compression.
    /// </summary>
    public byte[] Extract(EarcFileEntry entry)
    {
        var raw = ExtractRaw(entry);

        if (entry.IsUncompressed)
            return raw;

        if (entry.IsLz4)
            return DecompressLz4(raw, entry.OriginalSize);

        if (entry.IsCompressed)
            return DecompressZlibChunked(raw, entry.OriginalSize, entry.Key);

        return raw;
    }

    /// <summary>
    /// Replace a file in the archive. Appends new data at the end and updates the file entry header.
    /// For LZ4-compressed files, the replacement data is re-compressed.
    /// For uncompressed files (like .uifn), the data is written as-is.
    /// </summary>
    public void ReplaceFile(EarcFileEntry entry, byte[] newData)
    {
        byte[] dataToWrite;
        uint newOriginalSize = (uint)newData.Length;
        uint newProcessedSize;
        var newFlags = entry.Flags;

        if (entry.IsLz4)
        {
            dataToWrite = CompressLz4(newData);
            newProcessedSize = (uint)dataToWrite.Length;
        }
        else if (entry.IsCompressed)
        {
            // For zlib-chunked files, store uncompressed to avoid complexity.
            // The game should handle uncompressed files.
            dataToWrite = newData;
            newProcessedSize = (uint)newData.Length;
            newFlags &= ~EarcFileFlags.Compressed;
            newFlags &= ~(EarcFileFlags)256;
        }
        else
        {
            dataToWrite = newData;
            newProcessedSize = (uint)newData.Length;
        }

        // Close the read stream so we can open for writing
        _stream?.Dispose();
        _stream = null;

        using var writeStream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Append data at end of file
        writeStream.Seek(0, SeekOrigin.End);

        // Align to block size
        long currentPos = writeStream.Position;
        long blockSize = Header.BlockSize > 0 ? Header.BlockSize : 512;
        long alignedPos = (currentPos + blockSize - 1) / blockSize * blockSize;
        if (alignedPos > currentPos)
            writeStream.Write(new byte[alignedPos - currentPos]);

        long newDataOffset = writeStream.Position;
        writeStream.Write(dataToWrite);

        // Pad to block size
        long endPos = writeStream.Position;
        long paddedEnd = (endPos + blockSize - 1) / blockSize * blockSize;
        if (paddedEnd > endPos)
            writeStream.Write(new byte[paddedEnd - endPos]);

        // Update the file entry header in place
        writeStream.Seek(entry.HeaderOffset, SeekOrigin.Begin);
        using var writer = new BinaryWriter(writeStream, Encoding.UTF8, leaveOpen: true);

        writer.Write(entry.Id);
        writer.Write(newOriginalSize);
        writer.Write(newProcessedSize);
        writer.Write((int)newFlags);
        writer.Write(entry.UriOffset);
        writer.Write((ulong)newDataOffset);
        writer.Write(entry.RelativePathOffset);
        writer.Write(entry.LocalizationType);
        writer.Write(entry.Locale);
        writer.Write(entry.Key);

        // Update in-memory state
        entry.OriginalSize = newOriginalSize;
        entry.ProcessedSize = newProcessedSize;
        entry.DataOffset = (ulong)newDataOffset;
        entry.Flags = newFlags;

        // Skip hash recalculation for now — testing whether game checks it
        // RecalculateHash(writeStream);
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
    }

    /// <summary>
    /// Recalculate the Forspoken archive hash and write it to the header.
    /// Algorithm: MD5 each 8MB chunk, append xorshift64 seed, SHA256 the lot, XOR the result.
    /// </summary>
    private static void RecalculateHash(FileStream stream)
    {
        const int ChunkSize = 0x800_0000; // 8 MiB
        const ulong XorConst = 0x75757575_75757575UL;
        const uint StaticSeed0 = 0xb16949df;
        const uint StaticSeed1 = 0x104098f5;
        const uint StaticSeed2 = 0x9eb9b68b;
        const uint StaticSeed3 = 0x3120f7cb;

        long archiveSize = stream.Length;
        long dataSize = archiveSize - EarcHeader.Size;
        int blockCount = (int)((dataSize + ChunkSize - 1) / ChunkSize);

        // 16 bytes per MD5 hash + 16 bytes for seed
        var hashList = new byte[(blockCount + 1) * 16];
        var chunkBuffer = new byte[ChunkSize];

        // Step 1: MD5 hash each 8MB chunk
        stream.Seek(EarcHeader.Size, SeekOrigin.Begin);
        for (int i = 0; i < blockCount; i++)
        {
            long offset = (long)i * ChunkSize;
            int length = (int)Math.Min(dataSize - offset, ChunkSize);
            stream.ReadExactly(chunkBuffer, 0, length);

            byte[] md5 = System.Security.Cryptography.MD5.HashData(
                chunkBuffer.AsSpan(0, length));
            md5.CopyTo(hashList, i * 16);
        }

        // Step 2: Generate seed (last 16 bytes of hashList)
        int seedOffset = blockCount * 16;
        if (blockCount > 8)
        {
            ulong seed = (ulong)dataSize ^ XorConst;
            for (int i = 0; i < 4; i++)
            {
                seed ^= seed << 13;
                seed ^= seed >> 7;
                seed ^= seed << 17;
                BitConverter.TryWriteBytes(hashList.AsSpan(seedOffset + i * 4), (uint)seed);
            }
        }
        else
        {
            BitConverter.TryWriteBytes(hashList.AsSpan(seedOffset), StaticSeed0);
            BitConverter.TryWriteBytes(hashList.AsSpan(seedOffset + 4), StaticSeed1);
            BitConverter.TryWriteBytes(hashList.AsSpan(seedOffset + 8), StaticSeed2);
            BitConverter.TryWriteBytes(hashList.AsSpan(seedOffset + 12), StaticSeed3);
        }

        // Step 3: SHA256 and XOR the four 64-bit words
        byte[] sha = System.Security.Cryptography.SHA256.HashData(hashList);
        ulong hash = BitConverter.ToUInt64(sha, 0)
                   ^ BitConverter.ToUInt64(sha, 8)
                   ^ BitConverter.ToUInt64(sha, 16)
                   ^ BitConverter.ToUInt64(sha, 24);

        // Write hash to header at offset 0x28
        stream.Seek(0x28, SeekOrigin.Begin);
        stream.Write(BitConverter.GetBytes(hash));
        stream.Flush();
    }

    private void EnsureOpen()
    {
        if (_stream == null || !_stream.CanRead)
            throw new ObjectDisposedException(nameof(EarcArchive));
    }

    private static byte[] DecompressLz4(byte[] data, uint originalSize)
    {
        using var source = new MemoryStream(data);
        using var lz4Stream = LZ4Stream.Decode(source);
        using var output = new MemoryStream((int)originalSize);
        lz4Stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] CompressLz4(byte[] data)
    {
        using var output = new MemoryStream();
        using (var lz4Stream = LZ4Stream.Encode(output, LZ4Level.L12_MAX))
        {
            lz4Stream.Write(data);
        }
        return output.ToArray();
    }

    private static byte[] DecompressZlibChunked(byte[] data, uint originalSize, ushort key)
    {
        const int chunkSize = 128 * 1024;
        int chunks = (int)((originalSize + chunkSize - 1) / chunkSize);

        using var input = new MemoryStream(data);
        using var output = new MemoryStream((int)originalSize);

        for (int i = 0; i < chunks; i++)
        {
            // Skip alignment padding (except first chunk)
            if (i > 0)
            {
                int padding = (int)(4 - (input.Position % 4));
                if (padding < 4) input.Seek(padding, SeekOrigin.Current);
            }

            var sizeBuffer = new byte[4];
            input.ReadExactly(sizeBuffer);
            uint compressedSize = BitConverter.ToUInt32(sizeBuffer);

            input.ReadExactly(sizeBuffer);
            uint decompressedSize = BitConverter.ToUInt32(sizeBuffer);

            // Key decryption for first chunk
            if (i == 0 && key > 0)
            {
                ulong partialKey = (ulong)key * 1103515245 + 12345;
                ulong finalKey = partialKey * 1103515245 + 12345;
                compressedSize ^= (uint)(finalKey >> 32);
                decompressedSize ^= (uint)finalKey;
            }

            var compressed = new byte[compressedSize];
            input.ReadExactly(compressed);

            using var zlibInput = new MemoryStream(compressed);
            using var zlibStream = new System.IO.Compression.ZLibStream(
                zlibInput, System.IO.Compression.CompressionMode.Decompress);

            var decompressed = new byte[decompressedSize];
            zlibStream.ReadExactly(decompressed);
            output.Write(decompressed);
        }

        return output.ToArray();
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        while (true)
        {
            byte b = reader.ReadByte();
            if (b == 0) break;
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
