using System;
using System.Collections.Generic;
using System.Text;

namespace DSR_TPUP
{
    class BHD
    {
        private static Encoding shiftJIS = Encoding.GetEncoding("shift_jis");

        public List<BHDEntry> Files;
        public uint Flag;

        public BHD(byte[] bytes)
        {
            string format = bytes.ReadString(0x0);
            if (format != "BHF307D7R6")
                throw new NotSupportedException("Unrecognized BHD format: " + format);

            Flag = bytes.ReadUInt32(0xC);
            if (Flag != 0x74 && Flag != 0x54)
                throw new NotSupportedException("Unrecognized BHD flag: 0x" + Flag.ToString("X"));

            uint count = bytes.ReadUInt32(0x10);
            Files = new List<BHDEntry>();
            for (int i = 0; i < count; i++)
            {
                uint offset = 0x20 + (uint)i * 0x18;
                uint separator = bytes.ReadUInt32(offset + 0x0);
                uint fileSize = bytes.ReadUInt32(offset + 0x4);
                uint fileOffset = bytes.ReadUInt32(offset + 0x8);
                uint fileID = bytes.ReadUInt32(offset + 0xC);
                uint fileNameOffset = bytes.ReadUInt32(offset + 0x10);
                uint fileSizeDummy = bytes.ReadUInt32(offset + 0x14);

                if (fileSize != fileSizeDummy)
                    throw new FormatException("Unmatched BHD filesizes");
                if (separator != 0x40)
                    throw new FormatException("Unknown BHD separator: 0x" + separator.ToString("X"));

                BHDEntry entry = new BHDEntry()
                {
                    Filename = bytes.ReadString(fileNameOffset, 0, shiftJIS),
                    Offset = fileOffset,
                    Size = fileSize
                };
                Files.Add(entry);
            }
        }

        public (byte[], byte[]) Repack(BDT bdt)
        {
            uint count = (uint)bdt.Files.Count;
            uint totalHeaderSize = 0x20 + count * 0x18;
            uint totalDataSize = 0x10;
            foreach (BDTEntry file in bdt.Files)
            {
                totalHeaderSize += (uint)shiftJIS.GetByteCount(file.Filename) + 1;
                totalDataSize += (uint)file.Bytes.Length;
                if (totalDataSize % 0x10 > 0)
                    totalDataSize += 0x10 - totalDataSize % 0x10;
            }

            byte[] bhdResult = new byte[totalHeaderSize];
            bhdResult.WriteString(0x0, "BHF307D7R6");
            bhdResult.WriteUInt32(0xC, Flag);
            bhdResult.WriteUInt32(0x10, count);

            byte[] bdtResult = new byte[totalDataSize];
            bdtResult.WriteString(0x0, "BDF307D7R6");

            uint currentNameOffset = 0x20 + count * 0x18;
            uint currentFileOffset = 0x10;
            Files = new List<BHDEntry>();
            for (int i = 0; i < count; i++)
            {
                BDTEntry file = bdt.Files[i];
                uint headerOffset = 0x20 + (uint)i * 0x18;
                bhdResult.WriteUInt32(headerOffset + 0x0, 0x40);
                bhdResult.WriteUInt32(headerOffset + 0x4, (uint)file.Bytes.Length);
                bhdResult.WriteUInt32(headerOffset + 0x8, currentFileOffset);
                bhdResult.WriteUInt32(headerOffset + 0xC, (uint)i);
                bhdResult.WriteUInt32(headerOffset + 0x10, currentNameOffset);
                bhdResult.WriteUInt32(headerOffset + 0x14, (uint)file.Bytes.Length);

                BHDEntry bhdEntry = new BHDEntry()
                {
                    Filename = file.Filename,
                    Offset = currentFileOffset,
                    Size = (uint)file.Bytes.Length
                };
                Files.Add(bhdEntry);

                byte[] encodedName = shiftJIS.GetBytes(file.Filename);
                Array.Copy(encodedName, 0, bhdResult, currentNameOffset, encodedName.Length);
                currentNameOffset += (uint)encodedName.Length + 1;

                Array.Copy(file.Bytes, 0, bdtResult, currentFileOffset, file.Bytes.Length);
                currentFileOffset += (uint)file.Bytes.Length;
                if (currentFileOffset % 0x10 > 0)
                    currentFileOffset += 0x10 - currentFileOffset % 0x10;
            }

            return (bhdResult, bdtResult);
        }
    }

    class BHDEntry
    {
        public string Filename;
        public uint Offset;
        public uint Size;
    }
}
