using System;
using System.IO;
using System.IO.Compression;

namespace DSR_TPUP
{
    class DCX
    {
        public string Type;
        public byte[] Decompressed;

        public DCX(byte[] compressed)
        {
            string magic = compressed.ReadString(0x0, 4);
            if (magic != "DCX")
                throw new FormatException("Invalid DCX magic characters: " + magic);

            Type = compressed.ReadString(0x28);
            switch (Type)
            {
                case "DFLT":
                    dfltDecompress(compressed);
                    break;

                default:
                    throw new NotImplementedException("Unsupported DCX decompression: " + Type);
            }
        }

        private void dfltDecompress(byte[] compressed)
        {
            uint decompressedSize = compressed.ReadUInt32(0x1C, true);
            uint compressedSize = compressed.ReadUInt32(0x20, true) - 2;
            uint start = compressed.ReadUInt32(0x14, true) + 0x22;

            Decompressed = new byte[decompressedSize];
            decompress(compressed, start, compressedSize,
                Decompressed, 0, decompressedSize);
        }

        private static void decompress(byte[] source, uint sourceStart, uint sourceSize, byte[] target, uint targetStart, uint targetSize)
        {
            MemoryStream sourceStream = new MemoryStream(source, (int)sourceStart, (int)sourceSize);
            DeflateStream deflateStream = new DeflateStream(sourceStream, CompressionMode.Decompress);
            MemoryStream targetStream = new MemoryStream(target, (int)targetStart, (int)targetSize);

            int b = deflateStream.ReadByte();
            while (b != -1)
            {
                targetStream.WriteByte((byte)b);
                b = deflateStream.ReadByte();
            }
        }

        public byte[] Compress()
        {
            byte[] result;
            switch (Type)
            {
                case "DFLT":
                    result = dfltCompress();
                    break;

                default:
                    throw new NotImplementedException("Unsupported DCX compression: " + Type);
            }
            return result;
        }

        private byte[] dfltCompress()
        {
            byte[] compressed = compress(Decompressed);
            byte[] result = new byte[0x4E + compressed.Length];

            result.WriteString(0x0, "DCX");
            result.WriteUInt32(0x4, 0x10000, true);
            result.WriteUInt32(0x8, 0x18, true);
            result.WriteUInt32(0xC, 0x24, true);
            result.WriteUInt32(0x10, 0x24, true);
            result.WriteUInt32(0x14, 0x2C, true);
            result.WriteString(0x18, "DCS");
            result.WriteUInt32(0x1C, (uint)Decompressed.Length, true);
            result.WriteUInt32(0x20, (uint)compressed.Length + 2, true);
            result.WriteString(0x24, "DCP");
            result.WriteString(0x28, "DFLT");
            result.WriteUInt32(0x2C, 0x20, true);
            result.WriteUInt32(0x30, 0x9000000, true);
            result.WriteUInt32(0x40, 0x10100, true);
            result.WriteString(0x44, "DCA");
            result.WriteUInt32(0x48, 0x8, true);
            result.WriteUInt32(0x4C, 0x78DA0000, true);
            Array.Copy(compressed, 0, result, 0x4E, compressed.Length);

            return result;
        }

        private static byte[] compress(byte[] decompressed)
        {
            MemoryStream compressedStream = new MemoryStream();
            DeflateStream deflateStream = new DeflateStream(compressedStream, CompressionMode.Compress, true);
            deflateStream.Write(decompressed, 0, decompressed.Length);
            deflateStream.Close();

            byte[] result = new byte[compressedStream.Length];
            compressedStream.Position = 0;
            compressedStream.Read(result, 0, (int)compressedStream.Length);
            return result;
        }
    }
}
