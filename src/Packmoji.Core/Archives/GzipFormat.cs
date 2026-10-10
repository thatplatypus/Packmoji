using System.Buffers.Binary;

namespace Packmoji.Core.Archives
{
    /// <summary>
    /// The gzip an archive's tar is in. It is written with stored blocks, which is to say not
    /// compressed at all: a compressor's output differs between versions and between processors, and
    /// the bytes of an archive are what a digest is taken of. Sources are small.
    /// </summary>
    /// <remarks>
    /// What is read has to be the very bytes that would be written. A gzip can be made in endless
    /// ways that unpack to the same data, and can carry more after its end without ceasing to be one,
    /// so anything looser would give one set of files many archives, and let an archive hold what
    /// nobody who unpacks it would ever see. Nothing is inflated, so nothing can unpack to more than
    /// it is.
    /// </remarks>
    internal static class GzipFormat
    {
        private const int MaxStored = 65_535;
        private const int BlockHeaderLength = 5;
        private const int TrailerLength = 8;

        // Deflate, no flags, no time, no extra flags, and an operating system of "unknown": nothing
        // in the header says where or when it was written.
        private static readonly byte[] Header = [0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 255];

        private static readonly uint[] CrcTable = MakeCrcTable();

        /// <summary>How many bytes the gzip of this many bytes is.</summary>
        public static long SizeOf(long dataLength) => Header.Length + (Blocks(dataLength) * BlockHeaderLength) + dataLength + TrailerLength;

        public static byte[] Store(ReadOnlySpan<byte> data)
        {
            var blocks = (int)Blocks(data.Length);
            var archive = new byte[SizeOf(data.Length)];
            Header.CopyTo(archive, 0);
            var at = Header.Length;
            for (var block = 0; block < blocks; block++)
            {
                var chunk = data.Slice(block * MaxStored, Math.Min(MaxStored, data.Length - (block * MaxStored)));
                archive[at] = (byte)(block == blocks - 1 ? 1 : 0);
                BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(at + 1), (ushort)chunk.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(at + 3), (ushort)~chunk.Length);
                chunk.CopyTo(archive.AsSpan(at + BlockHeaderLength));
                at += BlockHeaderLength + chunk.Length;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(at), Crc(data));
            BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(at + 4), (uint)data.Length);
            return archive;
        }

        /// <summary>What a gzip holds, or null when it is not, byte for byte, the gzip that <see cref="Store"/> writes of it.</summary>
        public static byte[]? Unpack(ReadOnlySpan<byte> gzip)
        {
            // Every block but the last is full, so how long the whole is says how many blocks there
            // would be and how much they would hold. What is found at those places is taken out and
            // stored again: it is the archive only if that gives the same bytes.
            var blocks = Math.Max(1, (gzip.Length - Header.Length - TrailerLength + MaxStored + BlockHeaderLength - 1L) / (MaxStored + BlockHeaderLength));
            var length = gzip.Length - Header.Length - TrailerLength - (blocks * BlockHeaderLength);
            if (length < 0 || length > blocks * MaxStored)
            {
                return null;
            }

            var data = new byte[length];
            for (var block = 0; block < blocks; block++)
            {
                var from = Header.Length + (block * (long)(MaxStored + BlockHeaderLength)) + BlockHeaderLength;
                var chunk = (int)Math.Min(MaxStored, length - (block * (long)MaxStored));
                if (chunk < 0 || from + chunk > gzip.Length - TrailerLength)
                {
                    return null;
                }

                gzip.Slice((int)from, chunk).CopyTo(data.AsSpan(block * MaxStored));
            }

            return Store(data).AsSpan().SequenceEqual(gzip) ? data : null;
        }

        private static long Blocks(long dataLength) => Math.Max(1, (dataLength + MaxStored - 1) / MaxStored);

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
