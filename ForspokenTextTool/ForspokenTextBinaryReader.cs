using System.Text;

namespace ForspokenTextTool
{
    public class ForspokenTextBinaryReader : BinaryReader
    {
        public ForspokenTextBinaryReader(Stream stream)
            : base(stream) { }

        public ForspokenText ReadFile()
        {
            var textFile = new ForspokenText();

            textFile.Magic = Encoding.ASCII.GetString(ReadBytes(12));
            textFile.FileSize = ReadUInt32();
            textFile.Constant1 = ReadUInt32();
            textFile.Unknown1 = ReadInt16();
            textFile.Constant2 = ReadBytes(30);
            textFile.NumberOfStrings = ReadInt32();
            textFile.Constant3 = ReadUInt64();
            textFile.StringsStartOffset1 = ReadUInt32();
            textFile.StringsStartOffset2 = ReadUInt32();
            textFile.Constant4 = ReadBytes(52);

            for (var i = 0; i < textFile.NumberOfStrings; i++)
            {
                var entry = ReadTextEntry();

                textFile.Entries.Add(entry);
            }

            var baseOffset = BaseStream.Position;
            for (var i = 0; i < textFile.NumberOfStrings; i++)
            {
                var entry = textFile.Entries[i];
                
                BaseStream.Seek(baseOffset + entry.Offset1, SeekOrigin.Begin);
                entry.Component1 = ReadNullTerminatedString();

                BaseStream.Seek(baseOffset + entry.Offset2, SeekOrigin.Begin);
                entry.Component2 = ReadNullTerminatedString();

                BaseStream.Seek(baseOffset + entry.Offset3, SeekOrigin.Begin);
                entry.Component3 = ReadNullTerminatedString();
            }

            return textFile;
        }

        private TextEntry ReadTextEntry()
        {
            var entry = new TextEntry();

            entry.InternalId = ReadUInt16();
            entry.StringTypeConstant = ReadInt16();
            entry.StringNumber = ReadInt32();
            entry.Zero = ReadInt32();
            entry.One = ReadInt32();
            entry.Offset1 = ReadInt32();
            entry.Offset2 = ReadInt32();
            entry.Offset3 = ReadInt32();

            return entry;
        }

        public string ReadNullTerminatedString()
        {
            List<byte> stringBytes = new List<byte>();
            int currentByte;

            while ((currentByte = ReadByte()) != 0x00)
            {
                if (currentByte == 0x1)
                {
                    stringBytes.Add((byte)'{');
                    var length = ReadByte();
                    var id1 = ReadByte();
                    var id2 = ReadByte();
                    stringBytes.Add((byte)(id1 + '0'));
                    stringBytes.Add((byte)(id2 + '0'));
                    stringBytes.AddRange(ReadBytes(length - 2));
                    stringBytes.Add((byte)'}');
                }
                else
                {
                    stringBytes.Add((byte)currentByte);
                }
            }

            return Encoding.UTF8.GetString(stringBytes.ToArray());
        }
    }
}
