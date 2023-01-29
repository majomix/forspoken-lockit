using System.Text;

namespace ForspokenTextTool
{
    internal class TextConverter
    {
        private ForspokenText _textFile;
        private Dictionary<int, Id2sEntry> _entries = new();

        public void LoadParamBinFile(string file)
        {
            using (var handle = File.Open(file, FileMode.Open))
            using (var reader = new ForspokenTextBinaryReader(handle))
            {
                _textFile = reader.ReadFile();
            }
        }

        public void WriteTextFile(string file)
        {
            var output1 = new List<string>();

            foreach (var entry in _textFile.Entries.Where(e => !string.IsNullOrWhiteSpace(e.Component3)).OrderBy(e => e.StringId))
            {
                output1.Add($"{entry.StringId}\t{entry.Component3.Replace("\r\n", "\\r\\n").Replace("\n", "\\n")}");
            }

            File.WriteAllLines(file, output1);
        }

        public void LoadId2sFile(string file)
        {
            using (var handle = File.Open(file, FileMode.Open))
            using (var reader = new ForspokenTextBinaryReader(handle))
            {
                var one = reader.ReadByte();
                var magic = reader.ReadUInt32();
                var fileSize = reader.ReadUInt32();
                var numberOfEntries = reader.ReadUInt32();

                _entries = new();

                for (var i = 0; i < numberOfEntries; i++)
                {
                    var entry = new Id2sEntry();

                    entry.InternalId = reader.ReadUInt16();
                    entry.Category = reader.ReadInt16();
                    entry.Id = reader.ReadNullTerminatedString();

                    if (entry.Category == 11264)
                    {
                        _entries[entry.InternalId] = entry;
                    }
                }
            }
        }

        public void ResolveStringIds()
        {
            foreach (var entry in _textFile.Entries)
            {
                if (_entries.TryGetValue(entry.InternalId, out var ids))
                {
                    entry.StringId = ids.Id;
                    _textFile.ResolvedEntries[entry.StringId] = entry;
                }
            }
        }

        public void LoadTextFile(string textFile)
        {
            var content = File.ReadAllLines(textFile, Encoding.UTF8);

            foreach (var line in content)
            {
                var split = line.Split('\t');
                var id = split[0];
                var value = split[1].Replace("\\r\\n", "\r\n").Replace("\\n", "\n");

                _textFile.ResolvedEntries[id].Component3 = value;
            }
        }

        public void WriteParamBinFile(string binFile)
        {
            using (var handle = File.Open(binFile, FileMode.Create))
            using (var writer = new ForspokenTextBinaryWriter(handle))
            {
                writer.Write(_textFile);
            }
        }
    }
}
