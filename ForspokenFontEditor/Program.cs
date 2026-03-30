using ForspokenFontEditor;

if (args.Length < 1)
{
    Console.WriteLine("ForspokenFontEditor — UIFN font converter");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  ForspokenFontEditor <input.uifn>              Decode to OTF/TTF/TTC");
    Console.WriteLine("  ForspokenFontEditor <input.otf/ttf/ttc>       Encode to UIFN");
    Console.WriteLine("  ForspokenFontEditor <input> <output>           Explicit output path");
    Console.WriteLine("  ForspokenFontEditor --batch <directory>        Convert all UIFN files in directory");
    Console.WriteLine();
    Console.WriteLine("Direction is detected automatically from the input file extension.");
    return 1;
}

if (args[0] == "--batch" && args.Length >= 2)
{
    return BatchDecode(args[1]);
}

string inputPath = args[0];
if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"File not found: {inputPath}");
    return 1;
}

string ext = Path.GetExtension(inputPath).ToLowerInvariant();

if (ext == ".uifn")
{
    // UIFN -> Font
    byte[] uifnData = File.ReadAllBytes(inputPath);
    byte[] fontData = UifnConverter.Decode(uifnData);
    string fontExt = UifnConverter.DetectExtension(fontData);
    string outputPath = args.Length >= 2
        ? args[1]
        : Path.ChangeExtension(inputPath, fontExt);

    File.WriteAllBytes(outputPath, fontData);
    Console.WriteLine($"Decoded: {Path.GetFileName(inputPath)} -> {Path.GetFileName(outputPath)} ({fontData.Length:N0} bytes, {fontExt[1..].ToUpperInvariant()})");
}
else if (ext is ".otf" or ".ttf" or ".ttc")
{
    // Font -> UIFN
    byte[] fontData = File.ReadAllBytes(inputPath);
    byte[] uifnData = UifnConverter.Encode(fontData);
    string outputPath = args.Length >= 2
        ? args[1]
        : Path.ChangeExtension(inputPath, ".uifn");

    File.WriteAllBytes(outputPath, uifnData);
    Console.WriteLine($"Encoded: {Path.GetFileName(inputPath)} -> {Path.GetFileName(outputPath)} ({uifnData.Length:N0} bytes)");
}
else
{
    Console.Error.WriteLine($"Unknown extension '{ext}'. Expected .uifn, .otf, .ttf, or .ttc.");
    return 1;
}

return 0;

static int BatchDecode(string directory)
{
    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"Directory not found: {directory}");
        return 1;
    }

    var files = Directory.GetFiles(directory, "*.uifn");
    if (files.Length == 0)
    {
        Console.Error.WriteLine("No .uifn files found.");
        return 1;
    }

    Console.WriteLine($"Decoding {files.Length} UIFN files from {directory}");
    Console.WriteLine();

    int success = 0;
    foreach (var file in files.OrderBy(f => f))
    {
        try
        {
            byte[] uifnData = File.ReadAllBytes(file);
            byte[] fontData = UifnConverter.Decode(uifnData);
            string fontExt = UifnConverter.DetectExtension(fontData);
            string outputPath = Path.ChangeExtension(file, fontExt);
            File.WriteAllBytes(outputPath, fontData);
            Console.WriteLine($"  {Path.GetFileName(file),-40} -> {fontExt[1..].ToUpperInvariant()}  ({fontData.Length,10:N0} bytes)");
            success++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  {Path.GetFileName(file),-40} FAILED: {ex.Message}");
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Done: {success}/{files.Length} files decoded.");
    return success == files.Length ? 0 : 1;
}
