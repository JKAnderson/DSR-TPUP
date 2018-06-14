using System.Collections.Generic;

namespace DSR_TPUP
{
    class TPF
    {
        public List<TPFEntry> Files;

        public static TPF Unpack(byte[] bytes)
        {
            return new TPF(bytes);
        }

        private TPF(byte[] bytes)
        {
            BinaryReaderEx br = new BinaryReaderEx(bytes, false);
            br.AssertASCII("TPF\0", 4);
            int totalFileSize = br.ReadInt32();
            int fileCount = br.ReadInt32();
            // Only DS1 support
            br.AssertInt32(0x20300);

            Files = new List<TPFEntry>();
            for (int i = 0; i < fileCount; i++)
            {
                int fileOffset = br.ReadInt32();
                int fileSize = br.ReadInt32();
                int flags1 = br.ReadInt32();
                int nameOffset = br.ReadInt32();
                int flags2 = br.ReadInt32();

                byte[] fileData = br.GetBytes(fileOffset, fileSize);
                string fileName = br.GetShiftJIS(nameOffset);

                TPFEntry entry = new TPFEntry
                {
                    Name = fileName,
                    Flags1 = flags1,
                    Flags2 = flags2,
                    Bytes = fileData,
                };
                Files.Add(entry);
            }
        }

        public byte[] Repack()
        {
            BinaryWriterEx bw = new BinaryWriterEx(false);
            bw.WriteASCII("TPF\0");
            bw.ReserveInt32("DataSize");
            bw.WriteInt32(Files.Count);
            bw.WriteInt32(0x20300);

            for (int i = 0; i < Files.Count; i++)
            {
                TPFEntry entry = Files[i];
                bw.ReserveInt32($"FileData{i}");
                bw.WriteInt32(entry.Bytes.Length);
                bw.WriteInt32(entry.Flags1);
                bw.ReserveInt32($"FileName{i}");
                bw.WriteInt32(entry.Flags2);
            }

            for (int i = 0; i < Files.Count; i++)
            {
                TPFEntry entry = Files[i];
                bw.FillInt32($"FileName{i}", bw.Position);
                bw.WriteShiftJIS(entry.Name, true);
            }
            bw.Pad(0x10);

            int dataStart = bw.Position;
            for (int i = 0; i < Files.Count; i++)
            {
                TPFEntry entry = Files[i];
                bw.FillInt32($"FileData{i}", bw.Position);
                bw.WriteBytes(entry.Bytes);
                bw.Pad(0x10);
            }
            bw.FillInt32("DataSize", bw.Position - dataStart);

            return bw.Finish();
        }
    }

    public class TPFEntry
    {
        public string Name;
        public int Flags1;
        public int Flags2;
        public byte[] Bytes;
    }
}
