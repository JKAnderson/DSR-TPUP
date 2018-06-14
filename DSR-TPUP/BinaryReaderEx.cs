using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DSR_TPUP
{
    class BinaryReaderEx
    {
        private static readonly Encoding ASCII = Encoding.ASCII;
        private static readonly Encoding ShiftJIS = Encoding.GetEncoding("shift-jis");
        private static readonly Encoding UTF16 = Encoding.Unicode;

        private MemoryStream ms;
        private BinaryReader br;
        public bool BigEndian = false;

        public BinaryReaderEx(byte[] input, bool bigEndian)
        {
            ms = new MemoryStream(input);
            br = new BinaryReader(ms);
            BigEndian = bigEndian;
        }

        private byte[] readEndian(int length)
        {
            byte[] bytes = br.ReadBytes(length);
            if (BigEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        public void Skip(int length)
        {
            ms.Seek(length, SeekOrigin.Current);
        }

        public byte ReadByte()
        {
            return br.ReadByte();
        }

        public byte[] ReadBytes(int length)
        {
            return br.ReadBytes(length);
        }

        public byte[] GetBytes(int offset, int length)
        {
            long pos = ms.Position;
            ms.Position = offset;
            byte[] result = ReadBytes(length);
            ms.Position = pos;
            return result;
        }

        public int ReadInt32()
        {
            return BitConverter.ToInt32(readEndian(4), 0);
        }

        private string readChars(Encoding encoding, int length)
        {
            byte[] bytes;
            if (length == 0)
            {
                List<byte> byteList = new List<byte>();
                byte b = ReadByte();
                while (b != 0)
                {
                    byteList.Add(b);
                    b = ReadByte();
                }
                bytes = byteList.ToArray();
            }
            else
            {
                bytes = ReadBytes(length);
            }
            return encoding.GetString(bytes);
        }

        public string ReadASCII(int length = 0)
        {
            return readChars(ASCII, length);
        }

        public string ReadShiftJIS(int length = 0)
        {
            return readChars(ShiftJIS, length);
        }

        public string GetShiftJIS(int offset)
        {
            long pos = ms.Position;
            ms.Position = offset;
            string result = ReadShiftJIS();
            ms.Position = pos;
            return result;
        }

        public void AssertByte(byte value)
        {
            byte b = ReadByte();
            if (b != value)
            {
                throw new InvalidDataException(string.Format(
                    "Read byte: 0x{0:X} | Expected byte: 0x{1:X}", b, value));
            }
        }

        public void AssertBytes(params byte[] values)
        {
            foreach (byte value in values)
            {
                byte b = ReadByte();
                if (b != value)
                {
                    throw new InvalidDataException(string.Format(
                        "Read byte: 0x{0:X} | Expected byte: 0x{1:X}", b, value));
                }
            }
        }

        public void AssertInt32(int value)
        {
            int i = ReadInt32();
            if (i != value)
            {
                throw new InvalidDataException(string.Format(
                    "Read int: 0x{0:X} | Expected int: 0x{1:X}", i, value));
            }
        }

        public void AssertASCII(string value, int length)
        {
            string s = ReadASCII(length);
            if (s != value)
            {
                throw new InvalidDataException(string.Format(
                    "Read string: {0} | Expected string: {1}", s, value));
            }
        }
    }
}
