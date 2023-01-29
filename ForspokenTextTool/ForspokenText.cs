namespace ForspokenTextTool
{
    public class ForspokenText
    {
        public string Magic { get; set; }
        public UInt32 FileSize { get; set; }
        public UInt32 Constant1 { get; set; }
        public Int16 Unknown1 { get; set; }
        public byte[] Constant2 { get; set; }
        public Int32 NumberOfStrings { get; set; }
        public UInt64 Constant3 { get; set; }
        public UInt32 StringsStartOffset1 { get; set; }
        public UInt32 StringsStartOffset2 { get; set; }
        public byte[] Constant4 { get; set; }

        public List<TextEntry> Entries { get; set; } = new();
        public Dictionary<string, TextEntry> ResolvedEntries { get; set; } = new();
    }
}
