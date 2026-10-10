using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Packmoji.Core.Archives
{
    /// <summary>
    /// The gzip an archive's tar is in. It is written with stored blocks, which is to say not
    /// compressed at all: a compressor's output differs between versions and between processors, and
    /// the bytes of an archive are what a digest is taken of. Sources are small. What is read may be
    /// compressed by anything, so long as it is one whole gzip and nothing more.
    /// </summary>
    internal static class GzipFormat
    {
        private const int MaxStored = 65_535;
        private const int TrailerLength = 8;

        // Deflate, no flags, no time, no extra flags, and an operating system of "unknown": nothing
        // in the header says where or when it was written.
        private static readonly byte[] Header = [0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 255];

        private static readonly uint[] CrcTable = MakeCrcTable();

        public static byte[] Store(ReadOnlySpan<byte> data)
        {
            var blocks = Math.Max(1, (data.Length + MaxStored - 1) / MaxStored);
            var archive = new byte[Header.Length + (blocks * 5) + data.Length + TrailerLength];
            Header.CopyTo(archive, 0);
            var at = Header.Length;
            for (var block = 0; block < blocks; block++)
            {
                var chunk = data.Slice(block * MaxStored, Math.Min(MaxStored, data.Length - (block * MaxStored)));
                archive[at] = (byte)(block == blocks - 1 ? 1 : 0);
                BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(at + 1), (ushort)chunk.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(at + 3), (ushort)~chunk.Length);
                chunk.CopyTo(archive.AsSpan(at + 5));
                at += 5 + chunk.Length;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(at), Crc(data));
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(at + 4), (uint)data.Length);
            return archive;
        }

        /// <summary>What a gzip holds, or null and the reason when it is not one gzip of at most <paramref name="maxBytes"/> bytes.</summary>
        public static byte[]? Unpack(ReadOnlyMemory<byte> gzip, int maxBytes, out string? problem)
        {
            const string notGzip = "it is not a gzip file, or it is damaged";
            if (gzip.Length < Header.Length + TrailerLength || gzip.Span[0] != Header[0] || gzip.Span[1] != Header[1])
            {
                problem = notGzip;
                return null;
            }

            var bytes = MemoryMarshal.TryGetArray(gzip, out var segment) ? segment : new ArraySegment<byte>(gzip.ToArray());
            var unpacked = new MemoryStream();
            try
            {
                using var input = new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false);
                using var stream = new GZipStream(input, CompressionMode.Decompress);
                var buffer = new byte[81_920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (unpacked.Length + read > maxBytes)
                    {
                        problem = $"it unpacks to more than {maxBytes} bytes";
                        return null;
                    }

                    unpacked.Write(buffer, 0, read);
                }
            }
            catch (InvalidDataException)
            {
                problem = notGzip;
                return null;
            }

            // The check and the length a gzip ends with, held to what came out. A gzip that was cut
            // short, or that has something after it, or that is two in a row, does not end with them.
            var data = unpacked.ToArray();
            var trailer = gzip.Span[^TrailerLength..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(trailer) != Crc(data) || BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]) != (uint)data.Length)
            {
                problem = notGzip;
                return null;
            }

            problem = null;
            return data;
        }

        private static uint Crc(ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var value in data)
            {
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (var index = 0u; index < table.Length; index++)
            {
                var value = index;
                for (var bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
                }

                table[index] = value;
            }

            return table;
        }
    }
}
