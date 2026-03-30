using ForspokenEarcLib;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

switch (command)
{
    case "list":
        return List(args);
    case "extract":
        return Extract(args);
    case "replace":
        return Replace(args);
    case "mod":
        return InstallMod(args);
    case "revert":
        return RevertMod(args);
    case "rewrite":
        return Rewrite(args);
    default:
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
}

static int List(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: ForspokenEarcConsole list <archive.earc> [filter]");
        return 1;
    }

    string earcPath = args[1];
    string? filter = args.Length >= 3 ? args[2] : null;

    using var archive = EarcArchive.Open(earcPath);

    var files = filter != null
        ? archive.FindByPattern(filter).ToList()
        : archive.Files;

    Console.WriteLine($"Archive: {Path.GetFileName(earcPath)} ({archive.Header.FileCount} files total)");
    if (filter != null) Console.WriteLine($"Filter: {filter}");
    Console.WriteLine();
    Console.WriteLine($"{"#",-6} {"Size",10} {"Stored",10} {"Flags",-14} URI");
    Console.WriteLine(new string('-', 100));

    foreach (var file in files)
    {
        var flagStr = FormatFlags(file.Flags);
        Console.WriteLine($"{file.Index,-6} {file.OriginalSize,10:N0} {file.ProcessedSize,10:N0} {flagStr,-14} {file.Uri}");
    }

    Console.WriteLine();
    Console.WriteLine($"Listed: {files.Count} files");
    return 0;
}

static int Extract(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: ForspokenEarcConsole extract <archive.earc> <uri-or-filter> [output-dir]");
        return 1;
    }

    string earcPath = args[1];
    string pattern = args[2];
    string outputDir = args.Length >= 4 ? args[3] : ".";

    Directory.CreateDirectory(outputDir);

    using var archive = EarcArchive.Open(earcPath);

    // Try exact URI match first, then pattern
    var matches = new List<EarcFileEntry>();
    var exact = archive.FindByUri(pattern);
    if (exact != null)
    {
        matches.Add(exact);
    }
    else
    {
        matches.AddRange(archive.FindByPattern(pattern));
    }

    if (matches.Count == 0)
    {
        Console.Error.WriteLine($"No files matching '{pattern}' found.");
        return 1;
    }

    foreach (var entry in matches)
    {
        var data = archive.Extract(entry);
        var filename = Path.GetFileName(entry.RelativePath);
        if (string.IsNullOrEmpty(filename))
            filename = Path.GetFileName(entry.Uri.Replace("://", "_").Replace("/", "_"));

        var outputPath = Path.Combine(outputDir, filename);
        File.WriteAllBytes(outputPath, data);
        Console.WriteLine($"  Extracted: {entry.Uri}");
        Console.WriteLine($"    -> {outputPath} ({data.Length:N0} bytes)");
    }

    Console.WriteLine($"\nExtracted {matches.Count} file(s).");
    return 0;
}

static int Replace(string[] args)
{
    if (args.Length < 4)
    {
        Console.Error.WriteLine("Usage: ForspokenEarcConsole replace <archive.earc> <uri> <replacement-file>");
        return 1;
    }

    string earcPath = args[1];
    string uri = args[2];
    string replacementPath = args[3];

    if (!File.Exists(replacementPath))
    {
        Console.Error.WriteLine($"Replacement file not found: {replacementPath}");
        return 1;
    }

    using var archive = EarcArchive.Open(earcPath);

    var entry = archive.FindByUri(uri);
    if (entry == null)
    {
        // Try partial match
        var matches = archive.FindByPattern("*" + uri).ToList();
        if (matches.Count == 1)
            entry = matches[0];
        else if (matches.Count > 1)
        {
            Console.Error.WriteLine($"Ambiguous match for '{uri}'. Candidates:");
            foreach (var m in matches)
                Console.Error.WriteLine($"  {m.Uri}");
            return 1;
        }
        else
        {
            Console.Error.WriteLine($"File not found in archive: {uri}");
            return 1;
        }
    }

    var newData = File.ReadAllBytes(replacementPath);

    Console.WriteLine($"Replacing: {entry.Uri}");
    Console.WriteLine($"  Original:    {entry.OriginalSize,10:N0} bytes (stored: {entry.ProcessedSize:N0})");
    Console.WriteLine($"  Replacement: {newData.Length,10:N0} bytes");
    Console.WriteLine($"  Flags: {FormatFlags(entry.Flags)}");

    archive.ReplaceFile(entry, newData);

    Console.WriteLine($"  New offset:  0x{entry.DataOffset:X}");
    Console.WriteLine($"  New stored:  {entry.ProcessedSize,10:N0} bytes");
    Console.WriteLine("Done.");
    return 0;
}

static string FormatFlags(EarcFileFlags flags)
{
    var parts = new List<string>();
    if (flags.HasFlag(EarcFileFlags.Lz4Compression)) parts.Add("lz4");
    else if (flags.HasFlag(EarcFileFlags.Compressed)) parts.Add("zlib");
    if (flags.HasFlag(EarcFileFlags.Encrypted)) parts.Add("aes");
    if (flags.HasFlag(EarcFileFlags.Autoload)) parts.Add("auto");
    if (parts.Count == 0) parts.Add("raw");
    return string.Join("|", parts);
}

static int InstallMod(string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: ForspokenEarcConsole mod <game-datas-dir> <uri1>=<file1> [<uri2>=<file2> ...]");
        return 1;
    }

    var gameDataDir = args[1];
    var replacements = new List<(string OriginalUri, string ReplacementFilePath)>();

    for (int i = 2; i < args.Length; i++)
    {
        var parts = args[i].Split('=', 2);
        if (parts.Length != 2)
        {
            Console.Error.WriteLine($"Invalid mapping: {args[i]} (expected uri=filepath)");
            return 1;
        }
        if (!File.Exists(parts[1]))
        {
            Console.Error.WriteLine($"File not found: {parts[1]}");
            return 1;
        }
        replacements.Add((parts[0], parts[1]));
    }

    var installer = new ErepModInstaller(gameDataDir);
    installer.Install(replacements);
    return 0;
}

static int RevertMod(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: ForspokenEarcConsole revert <game-datas-dir>");
        return 1;
    }

    var installer = new ErepModInstaller(args[1]);
    installer.Revert();
    return 0;
}

static int Rewrite(string[] args)
{
    if (args.Length < 2) { Console.Error.WriteLine("Usage: rewrite <archive.earc> [add-ref|add-erep]"); return 1; }
    var mode = args.Length >= 3 ? args[2] : "none";

    using var archive = EarcArchive.Open(args[1]);
    var writer = EarcWriter.FromExisting(archive);

    foreach (var file in archive.Files)
    {
        var data = archive.ExtractRaw(file);

        if (file.Uri == "data://c000.erep" && mode == "add-erep")
        {
            var erep = ErepData.FromBytes(archive.Extract(file));
            var origHash = AssetHash.HashFileUri64("data://param/bin/text_us_46a51283.parambin");
            var modHash = AssetHash.HashFileUri64("data://mods/forspoken-localization/test.parambin");
            erep.Replacements[origHash] = modHash;
            var newData = erep.ToBytes();
            writer.AddFile(file.Uri, file.RelativePath, newData,
                (uint)newData.Length, (uint)newData.Length, file.Flags, file.Id);
            Console.WriteLine($"  Added EREP mapping ({erep.Replacements.Count} total)");
            continue;
        }

        writer.AddFile(file.Uri, file.RelativePath, data,
            file.OriginalSize, file.ProcessedSize, file.Flags, file.Id);
    }

    if (mode == "add-ref")
    {
        writer.AddReference("data://mods/forspoken-localization.ebex@",
            "$archives/mods/forspoken-localization.earc");
        Console.WriteLine("  Added mod reference");
    }

    archive.Dispose();
    writer.WriteToFile(args[1]);
    Console.WriteLine($"Rewritten ({mode}): {args[1]}");
    return 0;
}

static void PrintUsage()
{
    Console.WriteLine("ForspokenEarcConsole — EARC archive tool for Forspoken");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  list    <archive.earc> [filter]                  List files");
    Console.WriteLine("  extract <archive.earc> <uri-or-filter> [outdir]  Extract file(s)");
    Console.WriteLine("  replace <archive.earc> <uri> <replacement-file>  Direct EARC replace (no hash update)");
    Console.WriteLine("  mod     <datas-dir> <uri>=<file> [...]           Install via EREP (safe, reversible)");
    Console.WriteLine("  revert  <datas-dir>                              Remove localization mod");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  ForspokenEarcConsole list datas\\c000_002.earc *.uifn");
    Console.WriteLine("  ForspokenEarcConsole extract datas\\c000_002.earc *.uifn ./out");
    Console.WriteLine("  ForspokenEarcConsole mod datas data://asset/ui/font/lpathiaserif.uifn=font.uifn");
    Console.WriteLine("  ForspokenEarcConsole revert datas");
}
