namespace ForspokenFontEditor;

/// <summary>
/// Xorshift128 byte permutation used by the Luminous Engine to obfuscate UIFN payloads.
/// Seeded from the data length using Mersenne Twister-style initialization (constant 0x6C078965).
/// </summary>
public static class XorshiftShuffle
{
    private const uint MtConstant = 1812433253; // 0x6C078965

    /// <summary>
    /// Unshuffle (decode): reverse Fisher-Yates permutation. Used when reading UIFN files.
    /// </summary>
    public static void Unshuffle(Span<byte> data)
    {
        var perm = GeneratePermutation(data.Length);

        for (int i = data.Length - 1; i > 0; i--)
            (data[i], data[perm[i]]) = (data[perm[i]], data[i]);
    }

    /// <summary>
    /// Shuffle (encode): forward Fisher-Yates permutation. Used when writing UIFN files.
    /// </summary>
    public static void Shuffle(Span<byte> data)
    {
        var perm = GeneratePermutation(data.Length);

        for (int i = 1; i < data.Length; i++)
            (data[i], data[perm[i]]) = (data[perm[i]], data[i]);
    }

    private static int[] GeneratePermutation(int size)
    {
        uint s0 = (uint)size;
        uint s1 = unchecked(MtConstant * (s0 ^ (s0 >> 30)) + 1);
        uint s2 = unchecked(MtConstant * (s1 ^ (s1 >> 30)) + 2);
        uint s3 = unchecked(MtConstant * (s2 ^ (s2 >> 30)) + 3);
        uint s4 = unchecked(MtConstant * (s3 ^ (s3 >> 30)) + 4);

        uint v4 = s1, v5 = s2, v6 = s3, v7 = s4;
        var perm = new int[size];
        uint uSize = (uint)size;

        for (int i = 0; i < size; i++)
        {
            uint t = v4 ^ (v4 << 11);
            v4 = v5;
            v5 = v6;
            v6 = v7;
            v7 = v7 ^ t ^ (t >> 8) ^ (v7 >> 19);
            perm[i] = (int)(v7 % uSize);
        }

        return perm;
    }
}
