using System.Text;

namespace ForspokenTextTool
{
    public class ForspokenTextBinaryWriter : BinaryWriter
    {
        public ForspokenTextBinaryWriter(Stream stream)
        : base(stream) { }

        public void WriteNullTerminatedString(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            for (var i = 0; i < bytes.Length; i++)
            {
                var byteValue = bytes[i];
                if (byteValue == '{')
                {
                    var j = 0;
                    while (bytes[i + j++] != '}');

                    Write((byte)0x01);
                    Write((byte)(j - 2));
                    Write((byte)(bytes[++i] - '0'));
                    Write((byte)(bytes[++i] - '0'));
                    while (bytes[++i] != '}')
                    {
                        Write((byte)bytes[i]);
                    }
                }
                else
                {
                    Write(byteValue);
                }
            }
            Write((byte)0x00);
        }

        public void Write(ForspokenText textFile)
        {
            Write(Encoding.ASCII.GetBytes(textFile.Magic));
            Write(textFile.FileSize);
            Write(textFile.Constant1);
            Write(textFile.Unknown1);
            Write(textFile.Constant2);
            Write(textFile.NumberOfStrings);
            Write(textFile.Constant3);
            Write(textFile.StringsStartOffset1);
            Write(textFile.StringsStartOffset2);
            Write(textFile.Constant4);

            var entryDescriptorOffset = BaseStream.Position;
            WriteEntryDescriptorTable(textFile);
            WriteTextEntries(textFile);

            // rewrite offsets
            textFile.FileSize = (uint)BaseStream.Position;
            BaseStream.Seek(textFile.Magic.Length, SeekOrigin.Begin);
            Write(textFile.FileSize);

            BaseStream.Seek(entryDescriptorOffset, SeekOrigin.Begin);
            WriteEntryDescriptorTable(textFile);
        }

        private void WriteTextEntries(ForspokenText textFile)
        {
            var baseOffset = BaseStream.Position;
            Write((byte)0x0);
            for (var i = 0; i < textFile.NumberOfStrings; i++)
            {
                var entry = textFile.Entries[i];

                entry.Offset1 = (int)(BaseStream.Position - baseOffset);
                WriteNullTerminatedString(entry.Component1);

                entry.Offset2 = (int)(BaseStream.Position - baseOffset);
                WriteNullTerminatedString(entry.Component2);

                entry.Offset3 = (int)(BaseStream.Position - baseOffset);
                WriteNullTerminatedString(entry.Component3);
            }
        }

        private void WriteEntryDescriptorTable(ForspokenText textFile)
        {
            foreach (var entry in textFile.Entries)
            {
                Write(entry.InternalId);
                Write(entry.StringTypeConstant);
                Write(entry.StringNumber);
                Write(entry.Zero);
                Write(entry.One);
                Write(entry.Offset1);
                Write(entry.Offset2);
                Write(entry.Offset3);
            }
        }
    }
}
