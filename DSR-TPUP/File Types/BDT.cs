using System;
using System.Collections.Generic;

namespace DSR_TPUP
{
    class BDT
    {
        public List<BDTEntry> Files;
        private int flag;

        public static BDT Unpack(byte[] bhdBytes, byte[] bdtBytes)
        {
            return new BDT(bhdBytes, bdtBytes);
        }

        private BDT(byte[] bhdBytes, byte[] bdtBytes)
        {
            BHD bhd = new BHD(bhdBytes);
            flag = bhd.Flag;

            BinaryReaderEx br = new BinaryReaderEx(bdtBytes, false);
            br.AssertASCII("BDF307D7R6\0\0", 0xC);
            br.AssertInt32(0);

            Files = new List<BDTEntry>();
            for (int i = 0; i < bhd.Entries.Count; i++)
            {
                BHDEntry bhdEntry = bhd.Entries[i];
                string name = bhdEntry.Name;
                byte[] data = br.GetBytes(bhdEntry.Offset, bhdEntry.Size);

                BDTEntry bdtEntry = new BDTEntry
                {
                    Filename = name,
                    Bytes = data
                };
                Files.Add(bdtEntry);
            }
        }

        public (byte[], byte[]) Repack()
        {
            BinaryWriterEx bhw = new BinaryWriterEx(false);
            bhw.WriteASCII("BHF307D7R6\0\0");
            bhw.WriteInt32(flag);
            bhw.WriteInt32(Files.Count);
            bhw.WriteInt32(0);
            bhw.WriteInt32(0);
            bhw.WriteInt32(0);

            BinaryWriterEx bdw = new BinaryWriterEx(false);
            bdw.WriteASCII("BDF307D7R6\0\0");
            bdw.WriteInt32(0);

            for (int i = 0; i < Files.Count; i++)
            {
                BDTEntry file = Files[i];
                bhw.WriteInt32(0x40);
                bhw.WriteInt32(file.Bytes.Length);
                bhw.WriteInt32(bdw.Position);
                bhw.WriteInt32(i);
                bhw.ReserveInt32($"FileName{i}");
                bhw.WriteInt32(file.Bytes.Length);

                bdw.WriteBytes(file.Bytes);
                bdw.Pad(0x10);
            }

            for (int i = 0; i < Files.Count; i++)
            {
                BDTEntry file = Files[i];
                bhw.FillInt32($"FileName{i}", bhw.Position);
                bhw.WriteShiftJIS(file.Filename, true);
            }

            return (bhw.Finish(), bdw.Finish());
        }

        private class BHD
        {
            public List<BHDEntry> Entries;
            public int Flag;

            public BHD(byte[] bytes)
            {
                BinaryReaderEx br = new BinaryReaderEx(bytes, false);
                br.AssertASCII("BHF307D7R6\0\0", 0xC);
                Flag = br.ReadInt32();
                if (Flag != 0x54 && Flag != 0x74)
                    throw new NotSupportedException($"Unrecognized BHD flag: 0x{Flag:X}");

                int fileCount = br.ReadInt32();
                br.AssertInt32(0);
                br.AssertInt32(0);
                br.AssertInt32(0);

                Entries = new List<BHDEntry>();
                for (int i = 0; i < fileCount; i++)
                {
                    br.AssertInt32(0x40);
                    int fileSize = br.ReadInt32();
                    int fileOffset = br.ReadInt32();
                    br.AssertInt32(i);
                    int fileNameOffset = br.ReadInt32();
                    // Why is this here twice?
                    br.AssertInt32(fileSize);

                    string name = br.GetShiftJIS(fileNameOffset);
                    BHDEntry entry = new BHDEntry()
                    {
                        Name = name,
                        Offset = fileOffset,
                        Size = fileSize,
                    };
                    Entries.Add(entry);
                }
            }
        }

        private class BHDEntry
        {
            public string Name;
            public int Offset;
            public int Size;
        }
    }

    class BDTEntry
    {
        public string Filename;
        public byte[] Bytes;
    }
}
