namespace ForspokenFontEditor;

/// <summary>
/// Bidirectional converter between UIFN (Luminous Engine font container) and standard font files.
/// </summary>
public static class UifnConverter
{
    /// <summary>
    /// Decode a UIFN file to a standard font (OTF/TTF/TTC).
    /// </summary>
    public static byte[] Decode(byte[] uifnData)
    {
        var header = SedbHeader.Read(uifnData);
        var payload = new byte[uifnData.Length - header.Offset];
        Array.Copy(uifnData, header.Offset, payload, 0, payload.Length);

        XorshiftShuffle.Unshuffle(payload);
        return LzssCodec.Decompress(payload);
    }

    /// <summary>
    /// Encode a standard font file (OTF/TTF/TTC) into UIFN format.
    /// </summary>
    public static byte[] Encode(byte[] fontData)
    {
        var compressed = LzssCodec.Compress(fontData);
        var payload = new byte[compressed.Length];
        Array.Copy(compressed, payload, compressed.Length);

        XorshiftShuffle.Shuffle(payload);

        var header = new SedbHeader();
        ulong totalSize = (ulong)(SedbHeader.Size + payload.Length);
        var headerBytes = header.Write(totalSize);

        var result = new byte[totalSize];
        Array.Copy(headerBytes, result, SedbHeader.Size);
        Array.Copy(payload, 0, result, SedbHeader.Size, payload.Length);
        return result;
    }

    /// <summary>
    /// Detect the font format from decompressed data.
    /// </summary>
    public static string DetectExtension(ReadOnlySpan<byte> fontData)
    {
        if (fontData.Length >= 4)
        {
            // "OTTO" = OpenType/CFF
            if (fontData[0] == 'O' && fontData[1] == 'T' && fontData[2] == 'T' && fontData[3] == 'O')
                return ".otf";

            // "ttcf" = TrueType Collection
            if (fontData[0] == 't' && fontData[1] == 't' && fontData[2] == 'c' && fontData[3] == 'f')
                return ".ttc";

            // 00 01 00 00 = TrueType
            if (fontData[0] == 0x00 && fontData[1] == 0x01 && fontData[2] == 0x00 && fontData[3] == 0x00)
                return ".ttf";
        }

        return ".bin";
    }
}
