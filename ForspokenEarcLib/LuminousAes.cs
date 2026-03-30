using System.Security.Cryptography;

namespace ForspokenEarcLib;

/// <summary>
/// AES-128-CBC encryption used by Luminous Engine for archive file data.
/// Key is hardcoded. Format: [encrypted_data][IV 16b][padding 16b][flag 1b]
/// </summary>
public static class LuminousAes
{
    private static readonly byte[] Key = [156, 108, 93, 65, 21, 82, 63, 23, 90, 211, 248, 183, 117, 88, 30, 207];

    public static byte[] Encrypt(byte[] data)
    {
        int paddedSize = (data.Length + 15) & ~15;
        int encryptedSize = paddedSize + 33; // +16 IV + 16 padding + 1 flag

        var paddedData = new byte[paddedSize];
        Buffer.BlockCopy(data, 0, paddedData, 0, data.Length);

        using var aes = Aes.Create();
        aes.Key = Key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var encrypted = encryptor.TransformFinalBlock(paddedData, 0, paddedSize);

        var result = new byte[encryptedSize];
        Buffer.BlockCopy(encrypted, 0, result, 0, paddedSize);
        Buffer.BlockCopy(aes.IV, 0, result, paddedSize, 16);
        // 16 bytes of zeros at paddedSize+16 (already zero)
        result[paddedSize + 32] = 1; // flag: encrypted

        return result;
    }
}
