using System.Buffers.Binary;

namespace ForspokenEarcLib;

/// <summary>
/// EREP (Ebony Replace) hash mapping table.
/// Binary format: sequential pairs of uint64 [original_hash, replacement_hash].
/// </summary>
public class ErepData
{
    public Dictionary<ulong, ulong> Replacements { get; } = new();

    public static ErepData FromBytes(ReadOnlySpan<byte> data)
    {
        var erep = new ErepData();
        for (int i = 0; i + 16 <= data.Length; i += 16)
        {
            var original = BinaryPrimitives.ReadUInt64LittleEndian(data[i..]);
            var replacement = BinaryPrimitives.ReadUInt64LittleEndian(data[(i + 8)..]);
            erep.Replacements[original] = replacement;
        }
        return erep;
    }

    public byte[] ToBytes()
    {
        var data = new byte[Replacements.Count * 16];
        int i = 0;
        foreach (var (original, replacement) in Replacements)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(i), original);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(i + 8), replacement);
            i += 16;
        }
        return data;
    }
}
