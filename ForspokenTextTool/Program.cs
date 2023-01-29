using ForspokenTextTool;
using NDesk.Options;

var export = true;
string compactDir = string.Empty;
string ids2s = string.Empty;
string parambin = string.Empty;
string parambinout = string.Empty;
string txtFile = string.Empty;

var options = new OptionSet()
    .Add("import", value => export = false)
    .Add("id2s=", value => ids2s = value)
    .Add("parambin=", value => parambin = value)
    .Add("parambinout=", value => parambinout = value)
    .Add("compact=", value => compactDir = value)
    .Add("txt=", value => txtFile = value);

options.Parse(Environment.GetCommandLineArgs());

var converter = new TextConverter();

converter.LoadId2sFile(ids2s);
converter.LoadParamBinFile(parambin);
converter.ResolveStringIds();

if (compactDir != string.Empty)
{
    //converter.CompactLanguages(compactDir);
}
else if (export)
{
    converter.WriteTextFile(txtFile);
}
else
{
    converter.LoadTextFile(txtFile);
    converter.WriteParamBinFile(parambinout);
}