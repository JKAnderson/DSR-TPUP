using System;
using System.Collections.Generic;

namespace DSR_TPUP
{
    class BDT
    {
        public List<BDTEntry> Files;

        public BDT(byte[] bytes, BHD header)
        {
            string format = bytes.ReadString(0x0);
            if (format != "BDF307D7R6")
                throw new NotSupportedException("Unrecognized BDT format: " + format);

            Files = new List<BDTEntry>();
            for (int i = 0; i < header.Files.Count; i++)
            {
                BHDEntry bhdEntry = header.Files[i];
                BDTEntry bdtEntry = new BDTEntry
                {
                    Filename = bhdEntry.Filename,
                    Bytes = new byte[bhdEntry.Size]
                };
                Array.Copy(bytes, bhdEntry.Offset, bdtEntry.Bytes, 0, bhdEntry.Size);
                Files.Add(bdtEntry);
            }
        }
    }

    class BDTEntry
    {
        public string Filename;
        public byte[] Bytes;
    }
}
