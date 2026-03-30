using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;

namespace ForspokenEarcLib;

/// <summary>
/// Installs replacement files into Forspoken using the EREP system.
/// Creates a mod archive in datas/mods/ and rewrites c000.earc with EREP mappings.
/// </summary>
public class ErepModInstaller
{
    // Use Flagrum's exact mod GUID for now
    private const string ModId = "e678cc0e-447d-4fe8-ad3b-2d0847568bd9";

    private readonly string _gameDataDir;
    private readonly string _c000Path;
    private readonly string _modDir;
    private readonly string _modArchivePath;

    public ErepModInstaller(string gameDataDirectory)
    {
        _gameDataDir = gameDataDirectory;
        _c000Path = Path.Combine(_gameDataDir, "c000.earc");
        _modDir = Path.Combine(_gameDataDir, "mods");
        _modArchivePath = Path.Combine(_modDir, $"{ModId}.earc");
    }

    /// <summary>
    /// Install replacement files.
    /// Each entry: (originalUri, replacementFilePath) e.g.
    /// ("data://asset/ui/font/lpathiaserif.uifn", "C:\path\to\modified.uifn")
    /// </summary>
    public void Install(IReadOnlyList<(string OriginalUri, string ReplacementFilePath)> replacements)
    {
        if (!File.Exists(_c000Path))
            throw new FileNotFoundException($"c000.earc not found: {_c000Path}");

        Directory.CreateDirectory(_modDir);

        // Step 1: Read existing c000.earc
        Console.WriteLine("Reading c000.earc...");
        using var c000 = EarcArchive.Open(_c000Path);

        // Step 2: Read existing EREP (if any)
        var erepEntry = c000.FindByUri("data://c000.erep");
        ErepData erep;
        byte[] existingErepData = [];

        if (erepEntry != null)
        {
            existingErepData = c000.Extract(erepEntry);
            erep = ErepData.FromBytes(existingErepData);
            Console.WriteLine($"  Existing EREP: {erep.Replacements.Count} mappings");
        }
        else
        {
            erep = new ErepData();
            Console.WriteLine("  No existing EREP (will create)");
        }

        // Step 3: Build mod archive with replacement files
        Console.WriteLine($"Building mod archive ({replacements.Count} files)...");
        var modWriter = new EarcWriter
        {
            BlockSize = 512,  // Mod archives use standard block size
            ChunkSize = 128,
        };

        foreach (var (originalUri, filePath) in replacements)
        {
            var data = File.ReadAllBytes(filePath);
            var fileName = Path.GetFileName(filePath);
            var ext = originalUri.Split('.').Last();
            var modUri = $"data://mods/{Guid.NewGuid()}.{ext}";

            // Preserve original relative path (Flagrum does this)
            var origRelPath = originalUri.Replace("data://", "");

            // Match original file flags: parambin = LZ4 compressed, fonts = raw
            EarcFileFlags fileFlags;
            byte[] fileData;
            uint origSize = (uint)data.Length;
            uint procSize;

            if (ext == "parambin")
            {
                // LZ4 compress like the original
                fileData = CompressLz4(data);
                procSize = (uint)fileData.Length;
                fileFlags = EarcFileFlags.Lz4Compression | EarcFileFlags.Compressed;
            }
            else
            {
                // Fonts and other assets: store raw, no flags
                fileData = data;
                procSize = (uint)data.Length;
                fileFlags = EarcFileFlags.None;
            }

            modWriter.AddFile(modUri, origRelPath, fileData, origSize, procSize, fileFlags);

            // Map original -> replacement
            var origHash = AssetHash.HashFileUri64(originalUri);
            var replHash = AssetHash.HashFileUri64(modUri);
            erep.Replacements[origHash] = replHash;

            // Update any existing chains
            foreach (var key in erep.Replacements
                .Where(kvp => kvp.Value == origHash)
                .Select(kvp => kvp.Key)
                .ToList())
            {
                erep.Replacements[key] = replHash;
            }

            Console.WriteLine($"  {fileName} -> {modUri}");
        }

        modWriter.WriteToFile(_modArchivePath);
        Console.WriteLine($"  Mod archive: {_modArchivePath}");

        // Step 4: Rewrite c000.earc with updated EREP + archive reference
        Console.WriteLine("Rewriting c000.earc...");
        var c000Writer = EarcWriter.FromExisting(c000);

        // Copy all existing files from c000.earc, preserving exact metadata
        foreach (var file in c000.Files)
        {
            var uri = file.Uri;

            if (uri == $"data://mods/{ModId}.ebex@")
                continue; // Skip old reference, we'll add a fresh one

            var data = c000.ExtractRaw(file);

            if (uri == "data://c000.erep")
            {
                // Replace EREP with updated version
                var newErepBytes = erep.ToBytes();
                c000Writer.AddFile(uri, file.RelativePath, newErepBytes,
                    (uint)newErepBytes.Length, (uint)newErepBytes.Length,
                    file.Flags, file.Id);
                Console.WriteLine($"  Updated EREP: {erep.Replacements.Count} mappings");
            }
            else
            {
                // Copy as-is, preserving original sizes, flags, and ID
                c000Writer.AddFile(uri, file.RelativePath, data,
                    file.OriginalSize, file.ProcessedSize,
                    file.Flags, file.Id);
            }
        }

        // Add mod archive reference (must use $archives/ prefix for game to find it)
        var refUri = $"data://mods/{ModId}.ebex@";
        var refRelPath = $"$archives/mods/{ModId}.earc";
        c000Writer.AddReference(refUri, refRelPath);
        Console.WriteLine($"  Added reference: {refUri} -> {refRelPath}");

        // Write new c000.earc (with correct hash)
        c000.Dispose(); // Release file handle
        c000Writer.WriteToFile(_c000Path);
        Console.WriteLine("  c000.earc rewritten.");

        Console.WriteLine($"\nDone! {replacements.Count} file(s) installed via EREP.");
    }

    /// <summary>
    /// Revert: remove localization mod.
    /// </summary>
    public void Revert()
    {
        if (!File.Exists(_c000Path))
            throw new FileNotFoundException($"c000.earc not found: {_c000Path}");

        Console.WriteLine("Reading c000.earc...");
        using var c000 = EarcArchive.Open(_c000Path);

        var c000Writer = EarcWriter.FromExisting(c000);

        foreach (var file in c000.Files)
        {
            var uri = file.Uri;

            // Skip our mod reference
            if (uri == $"data://mods/{ModId}.ebex@")
                continue;

            var data = c000.ExtractRaw(file);

            if (uri == "data://c000.erep" && File.Exists(_modArchivePath))
            {
                // Remove our mappings from EREP
                var erep = ErepData.FromBytes(c000.Extract(file));

                // Find hashes that belong to our mod
                using var modArchive = EarcArchive.Open(_modArchivePath);
                var modHashes = new HashSet<ulong>(modArchive.Files.Select(f => f.Id));

                foreach (var key in erep.Replacements
                    .Where(kvp => modHashes.Contains(kvp.Value))
                    .Select(kvp => kvp.Key)
                    .ToList())
                {
                    erep.Replacements.Remove(key);
                }

                modArchive.Dispose();
                data = erep.ToBytes();
                c000Writer.AddFile(uri, file.RelativePath, data,
                    (uint)data.Length, (uint)data.Length, file.Flags, file.Id);
                Console.WriteLine($"  Cleaned EREP: {erep.Replacements.Count} mappings remaining");
                continue;
            }

            c000Writer.AddFile(uri, file.RelativePath, data,
                file.OriginalSize, file.ProcessedSize, file.Flags, file.Id);
        }

        c000.Dispose();
        c000Writer.WriteToFile(_c000Path);

        if (File.Exists(_modArchivePath))
        {
            File.Delete(_modArchivePath);
            Console.WriteLine("  Mod archive deleted.");
        }

        Console.WriteLine("Localization mod reverted.");
    }

    private static byte[] CompressLz4(byte[] data)
    {
        using var output = new MemoryStream();
        using (var lz4 = LZ4Stream.Encode(output, LZ4Level.L12_MAX))
            lz4.Write(data);
        return output.ToArray();
    }
}
