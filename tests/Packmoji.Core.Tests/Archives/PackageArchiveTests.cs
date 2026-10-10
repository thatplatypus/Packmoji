using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using CsCheck;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Archives
{
    /// <summary>
    /// A package's archive is the thing a digest is taken of, so the same files have to give the same
    /// bytes on every machine and in every later pmj. These hold the writer to that, and hold what it
    /// writes to being a tar in a gzip that other tools read.
    /// </summary>
    public sealed class PackageArchiveTests
    {
        private static byte[] Gunzip(byte[] archive)
        {
            using var unpacked = new MemoryStream();
            using (var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress))
            {
                gzip.CopyTo(unpacked);
            }

            return unpacked.ToArray();
        }

        // What .NET's own tar reader, which shares no code with pmj's, makes of an archive.
        private static List<(string Name, string Text, TarEntry Entry)> ReadWithTheBaseLibrary(byte[] archive)
        {
            var entries = new List<(string Name, string Text, TarEntry Entry)>();
            using var reader = new TarReader(new MemoryStream(Gunzip(archive)));
            while (reader.GetNextEntry(copyData: true) is { } entry)
            {
                using var text = new StreamReader(entry.DataStream ?? Stream.Null, Encoding.UTF8);
                entries.Add((entry.Name, text.ReadToEnd(), entry));
            }

            return entries;
        }

        [Fact]
        public void The_same_files_give_the_same_bytes_and_they_are_these()
        {
            var archive = PackageArchive.Write(Sample.SmallPackage());

            // Written down once, from a run. If this changes, every digest ever locked would change with it.
            Sha256Digest.Of(archive).Hex.ShouldBe("83000c1bc9db2f94db2a74fc744bcfbd18470f57b7b37ea78dae2e4335c146e7");
            PackageArchive.Write(Sample.SmallPackage()).ShouldBe(archive);
        }

        [Fact]
        public void The_order_the_files_are_given_in_does_not_matter()
        {
            ArchiveFile[] files = [Sample.File("packmoji.json", "{}"), Sample.File("src/b.🍇", "b"), Sample.File("src/a.🍇", "a"), Sample.File("README.md", "read me")];

            var one = PackageArchive.Write(files);
            var other = PackageArchive.Write(files.Reverse().ToArray());

            other.ShouldBe(one);
        }

        [Fact]
        public void What_is_written_is_a_gzip_with_no_name_and_no_time_in_it()
        {
            var archive = PackageArchive.Write(Sample.SmallPackage());

            // Magic, deflate, no flags, no time, no extra flags, and an operating system of "unknown".
            archive.Take(10).ShouldBe(new byte[] { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 255 });
        }

        [Fact]
        public void Another_tar_reader_finds_the_files_in_order_of_path_with_nothing_of_the_machine_that_packed_them()
        {
            ArchiveFile[] files =
            [
                Sample.File("src/lib.🍇", "library"),
                Sample.File("packmoji.json", "{ }"),
                Sample.File("README.md", "read me"),
                Sample.File("native/include/net.h", "header"),
            ];

            var entries = ReadWithTheBaseLibrary(PackageArchive.Write(files));

            entries.Select(entry => (entry.Name, entry.Text)).ShouldBe(
            [
                ("README.md", "read me"),
                ("native/include/net.h", "header"),
                ("packmoji.json", "{ }"),
                ("src/lib.🍇", "library"),
            ]);
            foreach (var (_, _, entry) in entries)
            {
                entry.EntryType.ShouldBe(TarEntryType.RegularFile);
                entry.Mode.ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
                entry.Uid.ShouldBe(0);
                entry.Gid.ShouldBe(0);
                entry.ModificationTime.ShouldBe(DateTimeOffset.UnixEpoch);
            }
        }

        [Fact]
        public void A_path_of_more_than_a_hundred_bytes_is_read_whole_by_another_reader()
        {
            var deep = string.Join("/", Enumerable.Repeat("a-directory-with-a-long-name", 6)) + "/file.txt";
            ArchiveFile[] files = [Sample.File("packmoji.json", "{}"), Sample.File(deep, "deep")];

            var entries = ReadWithTheBaseLibrary(PackageArchive.Write(files));

            Encoding.UTF8.GetByteCount(deep).ShouldBeGreaterThan(100);
            entries.Select(entry => entry.Name).ShouldBe([deep, "packmoji.json"]);
        }

        [Fact]
        public void A_path_of_exactly_a_hundred_bytes_is_in_its_header_and_one_of_a_byte_more_is_in_a_record_before_it()
        {
            // Both sort before the manifest, so each is the first thing in its tar.
            var fits = new string('a', 100);
            var over = new string('a', 101);
            const int typeAt = 156;

            var fitting = Gunzip(PackageArchive.Write([Sample.File("packmoji.json", "{}"), Sample.File(fits, "fits")]));
            var spilling = Gunzip(PackageArchive.Write([Sample.File("packmoji.json", "{}"), Sample.File(over, "over")]));

            Encoding.ASCII.GetString(fitting, 0, 100).ShouldBe(fits);
            fitting[typeAt].ShouldBe((byte)'0');
            spilling[typeAt].ShouldBe((byte)'x');
            spilling.Length.ShouldBe(fitting.Length + 1024);
            ReadWithTheBaseLibrary(PackageArchive.Write([Sample.File("packmoji.json", "{}"), Sample.File(fits, "fits")])).Select(entry => (entry.Name, entry.Text)).ShouldBe([(fits, "fits"), ("packmoji.json", "{}")]);
            ReadWithTheBaseLibrary(PackageArchive.Write([Sample.File("packmoji.json", "{}"), Sample.File(over, "over")])).Select(entry => (entry.Name, entry.Text)).ShouldBe([(over, "over"), ("packmoji.json", "{}")]);
            PackageArchive.Read(PackageArchive.Write([Sample.File("packmoji.json", "{}"), Sample.File(over, "over")])).ShouldSucceed().Select(file => file.Path.Value).ShouldBe([over, "packmoji.json"]);
        }

        [Fact]
        public void An_archive_larger_than_one_block_of_a_gzip_is_cut_into_blocks_of_the_largest_size_and_is_these_bytes()
        {
            // Two hundred thousand bytes that no two machines would disagree about.
            var large = new byte[200_000];
            for (var at = 0; at < large.Length; at++)
            {
                large[at] = (byte)((at * 31) % 251);
            }

            var archive = PackageArchive.Write([Sample.File("packmoji.json", "{}"), new ArchiveFile(Sample.Path("src/large.bin"), large)]);

            // After the header of ten bytes, each block says whether it is the last, then its length twice over.
            const int first = 10;
            const int second = first + 5 + 65_535;
            archive[first].ShouldBe((byte)0);
            BitConverter.ToUInt16(archive, first + 1).ShouldBe((ushort)65_535);
            BitConverter.ToUInt16(archive, first + 3).ShouldBe((ushort)0);
            archive[second].ShouldBe((byte)0);
            BitConverter.ToUInt16(archive, second + 1).ShouldBe((ushort)65_535);

            // Written down once, from a run, as the digest of the small package is.
            Sha256Digest.Of(archive).Hex.ShouldBe("9922437474a5bb7ed0ace77f3967f5fe846335d53cb29502b841984415f4a029");
            ReadWithTheBaseLibrary(archive).Select(entry => entry.Name).ShouldBe(["packmoji.json", "src/large.bin"]);
            PackageArchive.Read(archive).ShouldSucceed()[1].Content.ToArray().ShouldBe(large);
        }

        [Fact]
        public void An_empty_file_and_one_of_exactly_one_block_are_written_and_read()
        {
            ArchiveFile[] files = [Sample.File("packmoji.json", "{}"), Sample.File("empty.txt"), Sample.File("block.txt", new string('x', 512))];

            var read = PackageArchive.Read(PackageArchive.Write(files)).ShouldSucceed();

            read.Select(file => (file.Path.Value, file.Content.Length)).ShouldBe([("block.txt", 512), ("empty.txt", 0), ("packmoji.json", 2)]);
        }

        [Fact]
        public void What_is_read_back_is_what_was_packed()
        {
            var files = Sample.SmallPackage();

            var read = PackageArchive.Read(PackageArchive.Write(files)).ShouldSucceed();

            read.Select(file => file.Path.Value).ShouldBe(["packmoji.json", "src/lib.🍇"]);
            Encoding.UTF8.GetString(read[0].Content.Span).ShouldBe(Fixtures.MinimalManifest);
            Encoding.UTF8.GetString(read[1].Content.Span).ShouldBe("🌍 🐇 👋 🍇 🍉\n");
        }

        [Fact]
        public void Files_are_in_order_of_code_point_and_not_of_how_dot_net_holds_a_string()
        {
            // U+FF21 comes before 🍇 by code point and after it by UTF-16 unit, which is how .NET sorts.
            var fullwidth = char.ConvertFromUtf32(0xFF21) + ".txt";
            ArchiveFile[] files = [Sample.File("packmoji.json", "{}"), Sample.File("🍇.txt"), Sample.File(fullwidth)];

            var read = PackageArchive.Read(PackageArchive.Write(files)).ShouldSucceed();

            read.Select(file => file.Path.Value).ShouldBe(["packmoji.json", fullwidth, "🍇.txt"]);
        }

        // Paths from a few names that include an emoji, a space and a long one, in up to three directories.
        private static readonly Gen<ArchiveFile[]> Trees =
            Gen.Select(
                Gen.OneOfConst("a", "b", "src", "a b", "long-" + new string('n', 60), "🍇", "native").Array[0, 3],
                Gen.OneOfConst("lib.🍇", "main.emojic", "x.cpp", "notes.txt", "data.bin", "🧪.🍇"),
                Gen.Byte.Array[0, 2000],
                (directories, name, content) => (Path: string.Join("/", directories.Append(name)), Content: content))
            .Array[0, 12]
            .Select(files => files
                .GroupBy(file => file.Path.ToUpperInvariant())
                .Select(same => new ArchiveFile(Sample.Path(same.First().Path), same.First().Content))
                .Append(Sample.File("packmoji.json", "{}"))
                .GroupBy(file => file.Path.Value.ToUpperInvariant())
                .Select(same => same.First())
                .ToArray())
            .Where(files => PackageArchive.Check(files).Count == 0);

        [Fact]
        public void Whatever_the_tree_it_packs_to_the_same_bytes_in_any_order_and_reads_back_as_it_was()
        {
            Trees.Sample(
                files =>
                {
                    var archive = PackageArchive.Write(files);
                    var shuffled = files.OrderBy(file => Sha256Digest.Of(Encoding.UTF8.GetBytes(file.Path.Value)).Hex, StringComparer.Ordinal).ToArray();

                    PackageArchive.Write(shuffled).ShouldBe(archive);
                    var read = PackageArchive.Read(archive).ShouldSucceed();
                    read.Count.ShouldBe(files.Length);
                    foreach (var file in files)
                    {
                        read.Single(found => found.Path == file.Path).Content.ToArray().ShouldBe(file.Content.ToArray());
                    }

                    ReadWithTheBaseLibrary(archive).Select(entry => entry.Name).ShouldBe(read.Select(file => file.Path.Value));
                },
                iter: 300);
        }
    }
}
