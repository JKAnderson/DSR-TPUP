using System;
using System.Collections.Generic;
using System.Text;

namespace DSR_TPUP
{
    class BND
    {
        private static Encoding shiftJIS = Encoding.GetEncoding("shift_jis");

        public string Type, Signature;
        public byte Format, Flag2, Flag3, Flag4;
        public byte[] Unknown;
        public List<BNDEntry> Files;

        public BND(byte[] bytes)
        {
            Type = bytes.ReadString(0x0, 4);
            switch (Type)
            {
                case "BND3":
                    bnd3Unpack(bytes);
                    break;

                default:
                    throw new NotImplementedException("Unknown BND format: " + Type);
            }
        }

        private void bnd3Unpack(byte[] bytes)
        {
            Signature = bytes.ReadString(0x4, 8);
            Format = bytes[0xC];
            if (Format != 0x74)
                throw new NotImplementedException("Unsupported BND3 format: " + Format);

            Flag2 = bytes[0xD];
            Flag3 = bytes[0xE];
            Flag4 = bytes[0xF];
            bool bigEndian = Flag2 == 1;

            uint fileCount = bytes.ReadUInt32(0x10, bigEndian);
            if (fileCount == 0)
                throw new NotSupportedException("Empty BND :(");

            Unknown = new byte[0xC];
            Array.Copy(bytes, 0x18, Unknown, 0, 0xC);

            uint entrySize = 0x18;
            uint entryStart = 0x18 + 0xC;

            Files = new List<BNDEntry>();
            for (int i = 0; i < fileCount; i++)
            {
                uint currentEntry = entryStart + (uint)i * entrySize;
                uint fileSize = bytes.ReadUInt32(currentEntry + 0x0, bigEndian);
                uint fileOffset = bytes.ReadUInt32(currentEntry + 0x4, bigEndian);
                uint fileID = bytes.ReadUInt32(currentEntry + 0x8, bigEndian);
                uint fileNameOffset = bytes.ReadUInt32(currentEntry + 0xC, bigEndian);

                BNDEntry entry = new BNDEntry
                {
                    Filename = bytes.ReadString(fileNameOffset, 0),
                    ID = fileID,
                    Bytes = new byte[fileSize]
                };
                Array.Copy(bytes, fileOffset, entry.Bytes, 0, fileSize);
                Files.Add(entry);
            }
        }

        public byte[] Repack()
        {
            uint headerSize = 0x20 + (uint)Files.Count * 0x18;
            uint totalNameSize = 0;
            uint totalFileSize = 0;
            for (int i = 0; i < Files.Count; i++)
            {
                BNDEntry entry = Files[i];
                totalNameSize += (uint)shiftJIS.GetByteCount(entry.Filename) + 1;
                totalFileSize += (uint)entry.Bytes.Length;
                if (i < Files.Count - 1 && entry.Bytes.Length % 0x10 > 0)
                    totalFileSize += 0x10 - (uint)entry.Bytes.Length % 0x10;
            }
            uint nameEnd = headerSize + totalNameSize;
            uint nameEndPadded = nameEnd;
            if (nameEndPadded % 0x10 > 0)
                nameEndPadded += 0x10 - nameEndPadded % 0x10;

            byte[] result = new byte[nameEndPadded + totalFileSize];
            result.WriteString(0x0, Type);
            result.WriteString(0x4, Signature);
            result[0xC] = Format;
            result[0xD] = Flag2;
            result[0xE] = Flag3;
            result[0xF] = Flag4;

            bool bigEndian = Flag2 == 1;
            result.WriteUInt32(0x10, (uint)Files.Count, bigEndian);
            result.WriteUInt32(0x14, nameEnd, bigEndian);

            uint currentFileOffset = nameEndPadded;
            uint currentNameOffset = headerSize;
            for (int i = 0; i < Files.Count; i++)
            {
                BNDEntry entry = Files[i];
                uint entryOffset = 0x20 + (uint)i * 0x18;
                result.WriteUInt32(entryOffset + 0x0, 0x40, bigEndian);
                result.WriteUInt32(entryOffset + 0x4, (uint)entry.Bytes.Length, bigEndian);
                result.WriteUInt32(entryOffset + 0x8, currentFileOffset, bigEndian);
                result.WriteUInt32(entryOffset + 0xC, entry.ID, bigEndian);
                result.WriteUInt32(entryOffset + 0x10, currentNameOffset, bigEndian);
                result.WriteUInt32(entryOffset + 0x14, (uint)entry.Bytes.Length, bigEndian);

                Array.Copy(entry.Bytes, 0, result, currentFileOffset, entry.Bytes.Length);
                currentFileOffset += (uint)entry.Bytes.Length;
                if (entry.Bytes.Length % 0x10 > 0)
                    currentFileOffset += 0x10 - (uint)entry.Bytes.Length % 0x10;

                byte[] encodedName = shiftJIS.GetBytes(entry.Filename);
                Array.Copy(encodedName, 0, result, currentNameOffset, encodedName.Length);
                currentNameOffset += (uint)encodedName.Length + 1;
            }

            return result;
        }
    }

    class BNDEntry
    {
        public string Filename;
        public uint ID;
        public byte[] Bytes;
    }
}
