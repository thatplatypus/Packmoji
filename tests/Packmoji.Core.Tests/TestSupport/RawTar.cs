using System.IO.Compression;
using System.Text;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// Makes tar archives a field at a time, so that a test can make one that pmj would never write:
    /// a link, a path that climbs out, a mode that sets a user, an entry out of order.
    /// </summary>
    internal static class RawTar
    {
        public static byte[] Header(string name, long size, char type = '0', string mode = "0000644", string owner = "0000000", string time = "00000000000", string link = "", string user = "")
        {
            var block = new byte[512];
            Put(block, 0, name);
            Put(block, 100, mode);
            Put(block, 108, owner);
            Put(block, 116, owner);
            Put(block, 124, Convert.ToString(size, 8).PadLeft(11, '0'));
            Put(block, 136, time);
            block[156] = (byte)type;
            Put(block, 157, link);
            Put(block, 257, "ustar");
            Put(block, 263, "00");
            Put(block, 265, user);
            WriteChecksum(block);
            return block;
        }

        /// <summary>A header and its content, padded to whole blocks.</summary>
        public static byte[] Entry(string name, string content = "", char type = '0', string mode = "0000644", string owner = "0000000", string time = "00000000000", string link = "", string user = "")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var padded = new byte[(bytes.Length + 511) / 512 * 512];
            bytes.CopyTo(padded, 0);
            return [.. Header(name, bytes.Length, type, mode, owner, time, link, user), .. padded];
        }

        /// <summary>The record that says where the next entry's path is, when it is not in the entry's own header.</summary>
        public static byte[] PaxPath(string path, string more = "")
        {
            var record = PaxRecord("path", path) + more;
            return Entry("PaxHeader", record, type: 'x');
        }

        public static string PaxRecord(string key, string value)
        {
            var rest = $" {key}={value}\n";
            var bytes = Encoding.UTF8.GetByteCount(rest);
            var length = bytes + 1;
            while (length.ToString().Length + bytes != length)
            {
                length = length.ToString().Length + bytes;
            }

            return length + rest;
        }

        public static byte[] End() => new byte[1024];

        /// <summary>The parts one after another, in a gzip that compresses, as any other tool writes one.</summary>
        public static byte[] Gzip(params byte[][] parts)
        {
            using var archive = new MemoryStream();
            using (var gzip = new GZipStream(archive, CompressionLevel.Optimal))
            {
                foreach (var part in parts)
                {
                    gzip.Write(part);
                }
            }

            return archive.ToArray();
        }

        public static void WriteChecksum(byte[] block)
        {
            Array.Fill(block, (byte)' ', 148, 8);
            var sum = block.Sum(b => (int)b);
            Put(block, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
        }

        private static void Put(byte[] block, int offset, string text) => Encoding.UTF8.GetBytes(text).CopyTo(block, offset);
    }
}
