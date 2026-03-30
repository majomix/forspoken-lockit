using System.Text;

namespace ForspokenEarcLib;

/// <summary>
/// FNV-1a hashing for Luminous Engine asset URIs.
/// Matches Flagrum's AssetId(string uri) constructor.
/// </summary>
public static class AssetHash
{
    private const ulong Seed64 = 1469598103934665603;
    private const ulong Prime64 = 1099511628211;

    // Flagrum's URI-to-extension map for special extensions
    private static readonly Dictionary<string, string> ExtensionMap = new()
    {
        { "ebex@", "earc" },
        { "ebex@.earcref", "earc" },
    };

    public static ulong Hash64(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        ulong hash = Seed64;
        foreach (var b in bytes)
            hash = (hash ^ b) * Prime64;
        return hash;
    }

    /// <summary>
    /// Infer extension from URI, matching Flagrum's UriHelper.InferExtensionFromUri.
    /// Takes everything after the FIRST dot in the URI, then maps through ExtensionMap.
    /// </summary>
    private static string InferExtension(string uri)
    {
        int dot = uri.IndexOf('.');
        if (dot < 0) return "";
        var extension = uri[(dot + 1)..];
        return ExtensionMap.GetValueOrDefault(extension, extension);
    }

    /// <summary>
    /// Compute a 64-bit asset ID from a URI.
    /// Lower 44 bits = URI hash, upper 20 bits = extension type hash.
    /// </summary>
    public static ulong HashFileUri64(string uri)
    {
        var extension = InferExtension(uri);
        var uriHash = Hash64(uri);
        var typeHash = Hash64(extension);
        return (typeHash << 44) | (uriHash & 0xFFFFFFFFFFF);
    }
}
