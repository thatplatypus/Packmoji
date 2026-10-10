using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Archives
{
    /// <summary>
    /// An archive comes from someone else's repository and is unpacked onto this machine. What pmj
    /// pack writes is the only thing that has to be read, so everything else is refused: a link, a
    /// path that climbs out, a file that unpacks to fill a disk, and every tar that merely differs.
    /// </summary>
    public sealed class PackageArchiveRefusalTests
    {
        private const string Manifest = "{}";

        private static Diagnostic ShouldBeRefused(byte[] archive)
        {
            var result = PackageArchive.Read(archive);

            result.Succeeded.ShouldBeFalse();
            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.ArchiveInvalid);
            return diagnostic;
        }

        private static byte[] Unpacked(byte[] archive)
        {
            using var unpacked = new MemoryStream();
            using (var gzip = new GZipStream(new MemoryStream(archive), CompressionMode.Decompress))
            {
                gzip.CopyTo(unpacked);
            }

            return unpacked.ToArray();
        }

        [Fact]
        public void A_canonical_tar_in_a_gzip_that_another_tool_compressed_is_read()
        {
            var tar = Unpacked(PackageArchive.Write(Sample.SmallPackage()));

            PackageArchive.Read(RawTar.Gzip(tar)).ShouldSucceed().Count.ShouldBe(2);
        }

        [Fact]
        public void The_tar_this_file_writes_by_hand_is_the_tar_pmj_writes()
        {
            // So that the refusals below are of the one thing each changes, and not of the hand that wrote them.
            var byHand = RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest), RawTar.Entry("src/lib.emojic", "x"), RawTar.End());

            PackageArchive.Read(byHand).ShouldSucceed().Select(file => file.Path.Value).ShouldBe(["packmoji.json", "src/lib.emojic"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("this is not an archive")]
        public void What_is_not_a_gzip_is_refused(string text)
        {
            ShouldBeRefused(Encoding.UTF8.GetBytes(text)).Reason.ShouldContain("gzip");
        }

        [Fact]
        public void A_gzip_whose_data_is_damaged_is_refused()
        {
            var archive = PackageArchive.Write(Sample.SmallPackage());

            // The byte after the header says what kind of block follows, and no block is of this kind.
            archive[10] = 0x07;

            ShouldBeRefused(archive).Reason.ShouldContain("gzip");
        }

        [Fact]
        public void A_gzip_that_does_not_end_with_the_check_and_the_length_of_what_it_holds_is_refused()
        {
            var archive = PackageArchive.Write(Sample.SmallPackage());
            var wrongCheck = (byte[])archive.Clone();
            wrongCheck[^8] ^= 1;
            var wrongLength = (byte[])archive.Clone();
            wrongLength[^4] ^= 1;

            ShouldBeRefused(wrongCheck).Reason.ShouldContain("gzip");
            ShouldBeRefused(wrongLength).Reason.ShouldContain("gzip");
        }

        [Fact]
        public void A_gzip_with_anything_after_it_and_two_gzips_in_a_row_are_refused()
        {
            var archive = PackageArchive.Write(Sample.SmallPackage());

            ShouldBeRefused([.. archive, 0]).Reason.ShouldContain("gzip");
            ShouldBeRefused([.. archive, .. Encoding.UTF8.GetBytes("and more")]).Reason.ShouldContain("gzip");
            ShouldBeRefused([.. archive, .. archive]).Reason.ShouldContain("gzip");
        }

        [Fact]
        public void One_file_more_than_the_limit_is_refused_before_an_archive_is_written()
        {
            var manifest = Sample.File("packmoji.json", Manifest);
            var others = Enumerable.Range(0, PackageArchive.MaxFiles).Select(number => Sample.File($"f{number:D5}")).ToList();

            PackageArchive.Check([manifest, .. others.Take(PackageArchive.MaxFiles - 1)]).ShouldBeEmpty();
            PackageArchive.Check([manifest, .. others]).ShouldHaveSingleItem().ShouldBe($"it holds more than {PackageArchive.MaxFiles} files");
            Should.Throw<ArgumentException>(() => PackageArchive.Write([manifest, .. others])).Message.ShouldContain($"more than {PackageArchive.MaxFiles} files");
        }

        [Fact]
        public void One_byte_more_than_the_limit_is_refused_before_an_archive_is_written()
        {
            var manifest = Sample.File("packmoji.json", Manifest);
            ArchiveFile Rest(int more) => new(Sample.Path("big.bin"), new byte[PackageArchive.MaxUnpackedBytes - manifest.Content.Length + more]);

            PackageArchive.Check([manifest, Rest(0)]).ShouldBeEmpty();
            PackageArchive.Check([manifest, Rest(1)]).ShouldHaveSingleItem().ShouldBe($"its files come to more than {PackageArchive.MaxUnpackedBytes} bytes");
        }

        [Fact]
        public void An_archive_over_the_limit_is_refused_by_its_length()
        {
            ShouldBeRefused(new byte[PackageArchive.MaxBytes + 1]).Reason.ShouldContain($"{PackageArchive.MaxBytes} bytes");
        }

        [Fact]
        public void A_small_archive_that_unpacks_to_fill_a_disk_is_refused_before_it_has()
        {
            // A hundred megabytes of zeros is a hundred kilobytes when it is compressed.
            var bomb = RawTar.Gzip(RawTar.Header("zeros.bin", 100L * 1024 * 1024), new byte[100 * 1024 * 1024], RawTar.End());

            bomb.Length.ShouldBeLessThan(1024 * 1024);
            ShouldBeRefused(bomb).Reason.ShouldContain("unpacks to more than");
        }

        [Fact]
        public void A_tar_that_another_tool_wrote_is_refused_though_it_holds_the_same_files()
        {
            using var tar = new MemoryStream();
            using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "packmoji.json") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(Manifest)) });
            }

            ShouldBeRefused(RawTar.Gzip(tar.ToArray())).Reason.ShouldContain("pmj pack");
        }

        [Theory]
        [InlineData('1')] // a hard link
        [InlineData('2')] // a symbolic link
        [InlineData('3')] // a character device
        [InlineData('5')] // a directory
        [InlineData('6')] // a pipe
        [InlineData('L')] // GNU's entry for a long name
        public void An_entry_that_is_not_a_file_is_refused(char type)
        {
            var archive = RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest), RawTar.Entry("src/evil", "", type, link: "/etc/passwd"), RawTar.End());

            ShouldBeRefused(archive).Reason.ShouldContain("not a file");
        }

        [Theory]
        [InlineData("../climbs-out.txt")]
        [InlineData("src/../../climbs-out.txt")]
        [InlineData("/etc/passwd")]
        [InlineData("src\\windows.txt")]
        [InlineData("C:/windows.txt")]
        [InlineData("src//double.txt")]
        [InlineData("./dot.txt")]
        [InlineData("-option.txt")]
        public void A_path_that_a_manifest_could_not_name_is_refused(string path)
        {
            var archive = RawTar.Gzip(RawTar.Entry(path, "x"), RawTar.Entry("packmoji.json", Manifest), RawTar.End());

            ShouldBeRefused(archive).Reason.ShouldContain("path");
        }

        [Fact]
        public void A_file_that_would_run_or_would_set_a_user_is_refused()
        {
            ShouldBeRefused(RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest, mode: "0000755"), RawTar.End())).Reason.ShouldContain("pmj pack");
            ShouldBeRefused(RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest, mode: "0004755"), RawTar.End())).Reason.ShouldContain("pmj pack");
        }

        [Fact]
        public void An_owner_a_time_or_a_user_name_is_refused()
        {
            ShouldBeRefused(RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest, owner: "0001750"), RawTar.End()));
            ShouldBeRefused(RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest, time: "14712345670"), RawTar.End()));
            ShouldBeRefused(RawTar.Gzip(RawTar.Entry("packmoji.json", Manifest, user: "root"), RawTar.End()));
        }

        [Fact]
        public void Files_out_of_order_are_refused()
        {
            var archive = RawTar.Gzip(RawTar.Entry("src/lib.emojic", "x"), RawTar.Entry("packmoji.json", Manifest), RawTar.End());

            ShouldBeRefused(archive).Reason.ShouldContain("pmj pack");
        }

        [Theory]
        [InlineData("src/lib.emojic", "src/lib.emojic", "twice")]
        [InlineData("README.md", "readme.md", "but for case")]
        [InlineData("src", "src/lib.emojic", "both a file and a directory")]
        [InlineData("SRC/a.emojic", "src", "both a file and a directory")]
        public void Two_paths_that_one_disk_could_not_hold_are_refused(string one, string other, string why)
        {
            var entries = new[] { one, other, "packmoji.json" }.Order(StringComparer.Ordinal).Select(path => RawTar.Entry(path, path == "packmoji.json" ? Manifest : "x"));

            ShouldBeRefused(RawTar.Gzip([.. entries, RawTar.End()])).Reason.ShouldContain(why);
        }

        [Theory]
        [InlineData("CON")]
        [InlineData("src/nul.txt")]
        [InlineData("aux.emojic")]
        [InlineData("src/Com1/file.txt")]
        [InlineData("lpt9.tar.gz")]
        public void A_name_that_is_a_device_on_windows_is_refused(string path)
        {
            var entries = new[] { path, "packmoji.json" }.Order(StringComparer.Ordinal).Select(name => RawTar.Entry(name, name == "packmoji.json" ? Manifest : "x"));

            ShouldBeRefused(RawTar.Gzip([.. entries, RawTar.End()])).Reason.ShouldContain("Windows");
        }

        [Theory]
        [InlineData("console.txt")]
        [InlineData("src/com10.txt")]
        [InlineData("null")]
        [InlineData("auxiliary/con-text.emojic")]
        public void A_name_that_only_begins_like_a_device_is_fine(string path)
        {
            var files = new[] { Sample.File("packmoji.json", Manifest), Sample.File(path, "x") };

            PackageArchive.Read(PackageArchive.Write(files)).ShouldSucceed();
        }

        [Fact]
        public void An_archive_with_no_manifest_is_refused()
        {
            ShouldBeRefused(RawTar.Gzip(RawTar.Entry("src/lib.emojic", "x"), RawTar.End())).Reason.ShouldContain("packmoji.json");
        }

        [Fact]
        public void More_files_than_the_limit_are_refused()
        {
            var entries = Enumerable.Range(0, PackageArchive.MaxFiles + 1).Select(number => RawTar.Entry($"f{number:D5}"));

            ShouldBeRefused(RawTar.Gzip([.. entries, RawTar.Entry("packmoji.json", Manifest), RawTar.End()])).Reason.ShouldContain($"{PackageArchive.MaxFiles} files");
        }

        [Fact]
        public void Something_after_the_end_or_no_end_at_all_is_refused()
        {
            var entry = RawTar.Entry("packmoji.json", Manifest);

            ShouldBeRefused(RawTar.Gzip(entry));
            ShouldBeRefused(RawTar.Gzip(entry, new byte[512]));
            ShouldBeRefused(RawTar.Gzip(entry, RawTar.End(), new byte[512]));
            ShouldBeRefused(RawTar.Gzip(entry, RawTar.End(), Encoding.UTF8.GetBytes("more")));
        }

        [Fact]
        public void A_length_that_is_not_a_number_or_is_longer_than_the_archive_is_refused()
        {
            var garbled = RawTar.Header("packmoji.json", 2);
            Encoding.ASCII.GetBytes("12x45678901").CopyTo(garbled, 124);
            RawTar.WriteChecksum(garbled);

            ShouldBeRefused(RawTar.Gzip(garbled, new byte[512], RawTar.End()));
            ShouldBeRefused(RawTar.Gzip(RawTar.Header("packmoji.json", 8L * 1024 * 1024 * 1024 - 1), new byte[512], RawTar.End()));
        }

        [Fact]
        public void A_record_before_an_entry_may_hold_its_path_and_nothing_else()
        {
            var manifest = RawTar.Entry("packmoji.json", Manifest);
            var named = RawTar.Entry("PaxPath", "x");

            // The path of a file whose name is not plain ASCII has to be given this way, and is.
            PackageArchive.Read(RawTar.Gzip(manifest, RawTar.PaxPath("src/lib.🍇"), named, RawTar.End())).ShouldSucceed();

            ShouldBeRefused(RawTar.Gzip(manifest, RawTar.PaxPath("src/lib.🍇", RawTar.PaxRecord("mtime", "1")), named, RawTar.End()));
            ShouldBeRefused(RawTar.Gzip(manifest, RawTar.PaxPath("src/lib.🍇", RawTar.PaxRecord("linkpath", "/etc/passwd")), named, RawTar.End()));
            ShouldBeRefused(RawTar.Gzip(manifest, RawTar.PaxPath("src/plain.txt"), named, RawTar.End())).Reason.ShouldContain("pmj pack");
            ShouldBeRefused(RawTar.Gzip(manifest, RawTar.PaxPath("src/a.🍇"), RawTar.PaxPath("src/b.🍇"), named, RawTar.End()));
            ShouldBeRefused(RawTar.Gzip(manifest, RawTar.PaxPath("src/lib.🍇"), RawTar.End()));
        }

        [Fact]
        public void However_an_archive_is_cut_short_it_is_refused_and_nothing_is_thrown()
        {
            var tar = Unpacked(PackageArchive.Write([Sample.File("packmoji.json", Manifest), Sample.File("src/lib.🍇", new string('x', 700))]));

            for (var length = 0; length < tar.Length; length += 37)
            {
                PackageArchive.Read(RawTar.Gzip(tar[..length])).Succeeded.ShouldBeFalse($"cut to {length} bytes");
            }

            var whole = PackageArchive.Write(Sample.SmallPackage());
            for (var length = 0; length < whole.Length; length += 53)
            {
                PackageArchive.Read(whole[..length]).Succeeded.ShouldBeFalse($"the gzip cut to {length} bytes");
            }
        }

        [Fact]
        public void Whatever_one_byte_of_an_archive_is_changed_to_it_is_refused_or_read_as_another_package_and_nothing_is_thrown()
        {
            var tar = Unpacked(PackageArchive.Write([Sample.File("packmoji.json", Manifest), Sample.File("src/lib.🍇", "🌍 🐇 👋 🍇 🍉")]));

            for (var at = 0; at < tar.Length; at++)
            {
                var changed = (byte[])tar.Clone();
                changed[at] ^= 0x41;

                var result = PackageArchive.Read(RawTar.Gzip(changed));

                // What is read is canonical: packing it again gives the very bytes that were read.
                if (result.Succeeded)
                {
                    Unpacked(PackageArchive.Write(result.Value)).ShouldBe(changed, $"byte {at}");
                }
            }
        }

        [Fact]
        public void Files_that_cannot_be_an_archive_are_said_to_be_so_before_anything_is_written()
        {
            ArchiveFile[] collide = [Sample.File("packmoji.json", Manifest), Sample.File("README.md"), Sample.File("Readme.md")];
            ArchiveFile[] device = [Sample.File("packmoji.json", Manifest), Sample.File("src/nul.🍇")];
            ArchiveFile[] noManifest = [Sample.File("src/lib.🍇")];

            PackageArchive.Check(collide).ShouldHaveSingleItem().ShouldContain("but for case");
            PackageArchive.Check(device).ShouldHaveSingleItem().ShouldContain("Windows");
            PackageArchive.Check(noManifest).ShouldHaveSingleItem().ShouldContain("packmoji.json");
            PackageArchive.Check(Sample.SmallPackage()).ShouldBeEmpty();
            Should.Throw<ArgumentException>(() => PackageArchive.Write(collide));
        }
    }
}
