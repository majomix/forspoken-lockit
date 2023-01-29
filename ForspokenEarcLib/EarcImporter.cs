using K4os.Compression.LZ4.Streams;
using System.IO;

namespace ForspokenEarcLib
{
    public class EarcImporter
    {
        public void ImportTextFile(int fileSizeOffset, int fileOffsetOffset, string textPath, string earcPath)
        {
            byte[] compressed;

            using (var source = File.OpenRead(textPath))
            using (var stream = new MemoryStream())
            using (var target = LZ4Stream.Encode(stream))
            {
                source.CopyTo(target);
                target.Close();
                compressed = stream.ToArray();
            }

            var fileSize = compressed.Length;

            using (var fileStream = File.Open(earcPath, FileMode.Open))
            using (var writer = new BinaryWriter(fileStream))
            {
                writer.BaseStream.Seek(0, SeekOrigin.End);
                var fileOffset = writer.BaseStream.Position;

                writer.Write(compressed);
                var padding = 512 - (compressed.Length % 512);
                writer.Write(new byte[padding]);

                writer.BaseStream.Seek(fileSizeOffset, SeekOrigin.Begin);
                writer.Write(fileSize);

                writer.BaseStream.Seek(fileOffsetOffset, SeekOrigin.Begin);
                writer.Write(fileOffset);
            }
        }
    }
}
