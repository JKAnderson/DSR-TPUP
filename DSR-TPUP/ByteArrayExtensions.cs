using System;
using System.Collections.Generic;
using System.Text;

namespace DSR_TPUP
{
    static class ByteArrayExtensions
    {
        private static byte[] endianize(byte[] source, uint offset, int length, bool bigEndian)
        {
            byte[] result = new byte[length];
            Array.Copy(source, offset, result, 0, length);
            if (bigEndian)
                Array.Reverse(result);
            return result;
        }

        public static uint ReadUInt32(this byte[] source, uint offset, bool bigEndian = false)
        {
            byte[] bytes = endianize(source, offset, 4, bigEndian);
            return BitConverter.ToUInt32(bytes, 0);
        }

        public static void WriteUInt32(this byte[] target, uint offset, uint value, bool bigEndian = false)
        {

            byte[] bytes = BitConverter.GetBytes(value);
            if (bigEndian)
                Array.Reverse(bytes);
            Array.Copy(bytes, 0, target, offset, 4);
        }

        public static string ReadString(this byte[] source, uint offset, int limit = 0, Encoding encoding = null)
        {
            if (encoding == null)
                encoding = Encoding.ASCII;

            List<byte> bytes = new List<byte>();
            uint i = offset;
            while (source[i] != 0 && (limit == 0 || i < offset + limit))
            {
                bytes.Add(source[i]);
                i++;
            }

            return encoding.GetString(bytes.ToArray());
        }

        public static void WriteString(this byte[] target, uint offset, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            Array.Copy(bytes, 0, target, offset, bytes.Length);
        }
    }
}
