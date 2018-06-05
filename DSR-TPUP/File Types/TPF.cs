using System;
using System.Collections.Generic;
using System.Text;

namespace DSR_TPUP
{
    class TPF
    {
        private static Encoding shiftJIS = Encoding.GetEncoding("shift_jis");

        public List<TPFEntry> Files;

        public TPF(byte[] bytes)
        {
            bool bigEndian = bytes.ReadUInt32(0x8) >= 0x1000000;
            uint flags = bytes.ReadUInt32(0xC, bigEndian);
            switch (flags)
            {
                case 0x20300:
                    dsUnpack(bytes, bigEndian);
                    break;

                default:
                    throw new NotImplementedException("Unsupported TPF flags: " + flags);
            }
        }

        private void dsUnpack(byte[] bytes, bool bigEndian)
        {
            uint fileCount = bytes.ReadUInt32(0x8, bigEndian);
            Files = new List<TPFEntry>();
            for (uint i = 0; i < fileCount; i++)
            {
                uint fileOffset = bytes.ReadUInt32(0x10 + i * 0x14, bigEndian);
                uint size = bytes.ReadUInt32(0x14 + i * 0x14, bigEndian);
                uint flags1 = bytes.ReadUInt32(0x18 + i * 0x14, bigEndian);
                uint nameOffset = bytes.ReadUInt32(0x1C + i * 0x14, bigEndian);
                uint flags2 = bytes.ReadUInt32(0x20 + i * 0x14, bigEndian);
                string filename = bytes.ReadString(nameOffset, 0, shiftJIS);
                byte[] file = new byte[size];
                Array.Copy(bytes, fileOffset, file, 0, size);

                TPFEntry entry = new TPFEntry
                {
                    Name = filename,
                    Flags1 = flags1,
                    Flags2 = flags2,
                    Bytes = file
                };
                Files.Add(entry);
            }
        }

        public byte[] Repack()
        {
            uint totalHeaderSize = 0x10 + (uint)Files.Count * 0x14;
            uint nameEnd = totalHeaderSize;
            uint totalFileSize = 0;
            uint totalFileSizePadded = 0;

            foreach (TPFEntry entry in Files)
            {
                byte[] encodedName = shiftJIS.GetBytes(entry.Name);
                nameEnd += (uint)encodedName.Length + 1;

                uint fileSize = (uint)entry.Bytes.Length;
                totalFileSize += fileSize;

                uint fileSizePadded = fileSize;
                if (fileSizePadded % 0x10 > 0)
                    fileSizePadded += 0x10 - fileSizePadded % 0x10;
                totalFileSizePadded += fileSizePadded;
            }

            if (nameEnd % 0x10 > 0)
                nameEnd += 0x10 - nameEnd % 0x10;

            byte[] result = new byte[nameEnd + totalFileSizePadded];
            result.WriteString(0x0, "TPF");
            result.WriteUInt32(0x4, totalFileSize);
            result.WriteUInt32(0x8, (uint)Files.Count);
            result.WriteUInt32(0xC, 0x20300);

            uint currentNameOffset = totalHeaderSize;
            uint currentFileOffset = nameEnd;
            for (int i = 0; i < Files.Count; i++)
            {
                TPFEntry entry = Files[i];
                uint headerOffset = (uint)i * 0x14;
                byte[] encodedName = shiftJIS.GetBytes(entry.Name);
                uint fileSize = (uint)entry.Bytes.Length;
                uint fileSizePadded = fileSize;
                if (fileSizePadded % 0x10 > 0)
                    fileSizePadded += 0x10 - fileSizePadded % 0x10;

                result.WriteUInt32(headerOffset + 0x10, currentFileOffset);
                result.WriteUInt32(headerOffset + 0x14, fileSize);
                result.WriteUInt32(headerOffset + 0x18, entry.Flags1);
                result.WriteUInt32(headerOffset + 0x1C, currentNameOffset);
                result.WriteUInt32(headerOffset + 0x20, entry.Flags2);

                Array.Copy(entry.Bytes, 0, result, currentFileOffset, fileSize);
                currentFileOffset += fileSizePadded;

                Array.Copy(encodedName, 0, result, currentNameOffset, encodedName.Length);
                currentNameOffset += (uint)encodedName.Length + 1;
            }

            return result;
        }
    }

    public class TPFEntry
    {
        public string Name;
        public uint Flags1;
        public uint Flags2;
        public byte[] Bytes;
    }
}
