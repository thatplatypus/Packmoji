using System.Globalization;
using System.Text;

namespace Packmoji.Core.Archives
{
    /// <summary>
    /// The tar inside an archive, in the one form pmj writes: regular files only, in order of path,
    /// each with the same mode and no owner and no time, so that nothing of the machine that packed
    /// it is in the bytes. A path that is not short and plain ASCII goes in a pax record before its
    /// entry, which every tar reader of the last twenty years understands.
    /// </summary>
    internal static class TarFormat
    {
        private const int Block = 512;
        private const int NameLength = 100;
        private const int MaxPaxBytes = 4096;

        // What the name field holds when the path is in a record, and what the record's own entry is called.
        private const string PathIsInRecord = "PaxPath";
        private const string RecordName = "PaxHeader";

        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>The most bytes a tar of the most files of the most bytes can be.</summary>
        public static int MaxBytes => PackageArchive.MaxUnpackedBytes + (PackageArchive.MaxFiles * 6 * Block) + (2 * Block);

        /// <param name="files">In the order of <see cref="PathOrder"/>.</param>
        public static byte[] Write(IReadOnlyList<ArchiveFile> files)
        {
            var tar = new MemoryStream();
            var header = new byte[Block];
            foreach (var file in files)
            {
                var path = file.Path.Value;
                if (IsShortAndPlain(path))
                {
                    WriteHeader(tar, header, path, file.Content.Length, '0');
                }
                else
                {
                    var record = Record(path);
                    WriteHeader(tar, header, RecordName, record.Length, 'x');
                    WritePadded(tar, record);
                    WriteHeader(tar, header, PathIsInRecord, file.Content.Length, '0');
                }

                WritePadded(tar, file.Content.Span);
            }

            tar.Write(new byte[2 * Block]);
            return tar.ToArray();
        }

        /// <summary>The paths and contents a tar holds, or the reason it is not one that could be in an archive.</summary>
        public static bool TryParse(byte[] tar, out List<(string Path, ReadOnlyMemory<byte> Content)> entries, out string problem)
        {
            entries = [];
            problem = "";
            string? recorded = null;
            var at = 0;
            while (true)
            {
                if (at + Block > tar.Length)
                {
                    problem = "it ends before the two empty blocks that end a tar";
                    return false;
                }

                var header = tar.AsSpan(at, Block);
                if (header.IndexOfAnyExcept((byte)0) < 0)
                {
                    if (recorded is not null || at + (2 * Block) != tar.Length || tar.AsSpan(at + Block).IndexOfAnyExcept((byte)0) >= 0)
                    {
                        problem = "it does not end with two empty blocks and nothing after them";
                        return false;
                    }

                    return true;
                }

                if (!TryReadOctal(header.Slice(124, 12), out var size) || size > tar.Length - at - Block)
                {
                    problem = "an entry's length is not a number, or is longer than the archive";
                    return false;
                }

                var length = (int)size;
                var content = new ReadOnlyMemory<byte>(tar, at + Block, length);
                switch ((char)header[156])
                {
                    case 'x' when recorded is null && length <= MaxPaxBytes && TryReadPath(content.Span, out recorded):
                        break;
                    case 'x':
                        problem = "a record before an entry holds something other than the entry's path, which pmj pack never writes";
                        return false;
                    case '0':
                        var name = header[..NameLength];
                        var end = name.IndexOf((byte)0);
                        entries.Add((recorded ?? Encoding.ASCII.GetString(end < 0 ? name : name[..end]), content));
                        recorded = null;
                        break;
                    default:
                        problem = "it holds an entry that is not a file: a link, a directory or a device";
                        return false;
                }

                if (entries.Count > PackageArchive.MaxFiles)
                {
                    problem = $"it holds more than {PackageArchive.MaxFiles} files";
                    return false;
                }

                at += Block + Padded(length);
            }
        }

        private static bool IsShortAndPlain(string path) => path.Length <= NameLength && path.All(c => c is >= ' ' and <= '~');

        // A pax record is its own length in decimal, a space, key=value and a line feed, and the
        // length counts its own digits.
        private static byte[] Record(string path)
        {
            var rest = Encoding.UTF8.GetBytes($" path={path}\n");
            var length = rest.Length + 1;
            while (Digits(length) + rest.Length != length)
            {
                length = Digits(length) + rest.Length;
            }

            return [.. Encoding.ASCII.GetBytes(length.ToString(CultureInfo.InvariantCulture)), .. rest];
        }

        private static int Digits(int number) => number.ToString(CultureInfo.InvariantCulture).Length;

        private static bool TryReadPath(ReadOnlySpan<byte> record, out string? path)
        {
            path = null;
            var space = record.IndexOf((byte)' ');
            const string key = "path=";
            if (space <= 0 || record.Length < space + 1 + key.Length + 1 || record[^1] != (byte)'\n' || !record[(space + 1)..].StartsWith(Encoding.ASCII.GetBytes(key)))
            {
                return false;
            }

            try
            {
                path = StrictUtf8.GetString(record[(space + 1 + key.Length)..^1]);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            // Whether the record is spelled as pmj spells it, and holds nothing more, is settled by
            // writing it again: the archive as a whole has to come out the same.
            return !path.Contains('\n');
        }

        private static void WriteHeader(MemoryStream tar, byte[] header, string name, int size, char type)
        {
            Array.Clear(header);
            Encoding.ASCII.GetBytes(name, header);
            Put(header, 100, "0000644");
            Put(header, 108, "0000000");
            Put(header, 116, "0000000");
            Put(header, 124, Convert.ToString(size, 8).PadLeft(11, '0'));
            Put(header, 136, "00000000000");
            header[156] = (byte)type;
            Put(header, 257, "ustar");
            Put(header, 263, "00");

            // The checksum is the sum of the header's bytes with the checksum itself read as spaces.
            Array.Fill(header, (byte)' ', 148, 8);
            var sum = 0;
            foreach (var value in header)
            {
                sum += value;
            }

            Put(header, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
            tar.Write(header);
        }

        private static void Put(byte[] header, int at, string text) => Encoding.ASCII.GetBytes(text, 0, text.Length, header, at);

        private static void WritePadded(MemoryStream tar, ReadOnlySpan<byte> content)
        {
            tar.Write(content);
            tar.Write(new byte[Padded(content.Length) - content.Length]);
        }

        private static int Padded(int length) => (length + Block - 1) / Block * Block;

        private static bool TryReadOctal(ReadOnlySpan<byte> field, out long value)
        {
            value = 0;
            var digits = 0;
            foreach (var c in field)
            {
                if (c is 0 or (byte)' ')
                {
                    break;
                }

                if (c is < (byte)'0' or > (byte)'7')
                {
                    return false;
                }

                value = (value * 8) + (c - '0');
                digits++;
            }

            return digits > 0;
        }
    }
}
