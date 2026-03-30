using System.Buffers.Binary;

namespace ForspokenFontEditor;

/// <summary>
/// LZSS compression/decompression as used by the Luminous Engine.
/// 12-bit sliding window (4096 bytes), 4-bit match length (+3), 8-bit flag bytes.
/// </summary>
public static class LzssCodec
{
    private const int WindowSize = 4096;   // 12-bit
    private const int WindowMask = 0xFFF;
    private const int MinMatch = 3;
    private const int MaxMatch = 18;       // 4-bit + 3
    private const int RingOffset = 18;     // constant offset in ring buffer addressing

    /// <summary>
    /// Decompress LZSS data. Input starts with uint32 compressed_size + uint32 decompressed_size.
    /// </summary>
    public static byte[] Decompress(ReadOnlySpan<byte> src)
    {
        uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(src);
        uint decompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(src[4..]);

        var output = new byte[decompressedSize];
        int pos = 8;
        int end = (int)compressedSize;
        int outPos = 0;
        int flags = 0;
        int flagBits = 0;

        while (pos < end && outPos < (int)decompressedSize)
        {
            if (flagBits == 0)
            {
                flags = src[pos++];
                flagBits = 8;
                if (pos >= end) break;
            }

            int byte1 = src[pos++];

            if ((flags & 1) != 0)
            {
                // Literal byte
                output[outPos++] = (byte)byte1;
            }
            else
            {
                // Back-reference
                if (pos >= end) break;
                int byte2 = src[pos++];

                int matchLen = (byte2 & 0x0F) + MinMatch;
                int combined = byte1 | ((byte2 & 0xF0) << 4);
                int ringOff = ((outPos & 0xFFFF) - combined - RingOffset) & WindowMask;
                int srcPos = outPos - ringOff;

                for (int k = 0; k < matchLen && outPos < (int)decompressedSize; k++, outPos++)
                {
                    int srcIdx = srcPos + k;
                    output[outPos] = (srcIdx >= 0 && srcIdx < outPos) ? output[srcIdx] : (byte)0;
                }
            }

            flags >>= 1;
            flagBits--;
        }

        return output;
    }

    /// <summary>
    /// Compress data using LZSS. Output starts with uint32 compressed_size + uint32 decompressed_size.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        int dataLen = data.Length;
        // Worst case: 8-byte header + every byte is a literal with a flag byte per 8 literals
        var buffer = new byte[8 + dataLen + (dataLen / 8) + 16];
        int outPos = 8; // skip header, fill later
        int inPos = 0;

        while (inPos < dataLen)
        {
            int flagPos = outPos++;
            byte flagByte = 0;

            for (int bit = 0; bit < 8 && inPos < dataLen; bit++)
            {
                // Try to find a match in the sliding window
                int bestLen = 0;
                int bestRingPos = 0;

                if (inPos >= 1) // need at least 1 byte of history
                {
                    int windowStart = Math.Max(0, inPos - WindowSize + 1);
                    int maxLen = Math.Min(MaxMatch, dataLen - inPos);

                    for (int candidate = windowStart; candidate < inPos; candidate++)
                    {
                        int len = 0;
                        while (len < maxLen && data[candidate + len] == data[inPos + len])
                        {
                            len++;
                            if (candidate + len >= inPos && len < maxLen)
                            {
                                // Allow overlapping match (copy from already written output)
                                // For compression we just keep matching since decompressor handles it
                            }
                        }

                        if (len >= MinMatch && len > bestLen)
                        {
                            bestLen = len;
                            // Compute the ring buffer position that the decompressor expects
                            int ringOff = inPos - candidate;
                            int combined = ((inPos & 0xFFFF) - ringOff - RingOffset) & WindowMask;
                            // Verify roundtrip: decompressor computes ringOff = ((outPos & 0xFFFF) - combined - 18) & 0xFFF
                            int checkRingOff = ((inPos & 0xFFFF) - combined - RingOffset) & WindowMask;
                            if (checkRingOff == ringOff)
                            {
                                bestRingPos = combined;
                                if (bestLen == MaxMatch) break;
                            }
                        }
                    }
                }

                if (bestLen >= MinMatch)
                {
                    // Back-reference: flag bit = 0
                    int byte1 = bestRingPos & 0xFF;
                    int byte2 = ((bestRingPos >> 4) & 0xF0) | ((bestLen - MinMatch) & 0x0F);
                    buffer[outPos++] = (byte)byte1;
                    buffer[outPos++] = (byte)byte2;
                    inPos += bestLen;
                }
                else
                {
                    // Literal: flag bit = 1
                    flagByte |= (byte)(1 << bit);
                    buffer[outPos++] = data[inPos++];
                }
            }

            buffer[flagPos] = flagByte;
        }

        // Write header
        uint compressedSize = (uint)outPos;
        uint decompressedSize = (uint)dataLen;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0), compressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), decompressedSize);

        return buffer[..outPos];
    }
}
