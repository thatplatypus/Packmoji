using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Diagnostics
{
    /// <summary>
    /// Reading a file builds a tree many times the file's size, so a file that could not be a real
    /// manifest or lockfile is refused by its length, before any of it is read.
    /// </summary>
    public sealed class FileSizeLimitTests
    {
        // JSON allows spaces after its closing brace, so a real file can be padded to any length.
        private static string Padded(string text, int bytes) => text + new string(' ', bytes - Encoding.UTF8.GetByteCount(text));

        private static Diagnostic ShouldBeTooLarge<T>(ReadResult<T> result, string file) where T : class
        {
            result.Succeeded.ShouldBeFalse();
            result.Value.ShouldBeNull();
            result.OmittedDiagnostics.ShouldBe(0);
            var diagnostic = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.FileTooLarge);
            diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
            diagnostic.Location.ShouldBe(new SourceLocation(file, 1, 1));
            diagnostic.Message.ShouldContain($"\"{file}\"");
            return diagnostic;
        }

        [Fact]
        public void A_manifest_may_be_one_mebibyte_and_a_lockfile_four()
        {
            ManifestReader.MaxBytes.ShouldBe(1_048_576);
            LockfileReader.MaxBytes.ShouldBe(4_194_304);
        }

        [Fact]
        public void A_manifest_of_exactly_the_limit_is_read()
        {
            var text = Padded(Fixtures.FullManifest, ManifestReader.MaxBytes);

            Encoding.UTF8.GetByteCount(text).ShouldBe(ManifestReader.MaxBytes);
            ManifestReader.Read(text).ShouldSucceed();
            ManifestReader.Read(Encoding.UTF8.GetBytes(text)).ShouldSucceed();
        }

        [Fact]
        public void A_manifest_of_one_byte_more_is_refused_and_nothing_else_is_said_of_it()
        {
            var text = Padded(Fixtures.FullManifest, ManifestReader.MaxBytes + 1);

            var fromText = ShouldBeTooLarge(ManifestReader.Read(text), ManifestReader.FileName);
            var fromBytes = ShouldBeTooLarge(ManifestReader.Read(Encoding.UTF8.GetBytes(text), "other/packmoji.json"), "other/packmoji.json");

            fromText.Reason.ShouldContain("1048576 bytes");
            fromText.Fix.ShouldContain("the file you meant");
            fromBytes.Reason.ShouldBe(fromText.Reason);
        }

        [Fact]
        public void A_lockfile_of_exactly_the_limit_is_read()
        {
            var text = Padded(Fixtures.Lockfile, LockfileReader.MaxBytes);

            Encoding.UTF8.GetByteCount(text).ShouldBe(LockfileReader.MaxBytes);
            LockfileReader.Read(text).ShouldSucceed();
            LockfileReader.Read(Encoding.UTF8.GetBytes(text)).ShouldSucceed();
        }

        [Fact]
        public void A_lockfile_of_one_byte_more_is_refused_and_the_thing_to_do_is_to_write_it_again()
        {
            var text = Padded(Fixtures.Lockfile, LockfileReader.MaxBytes + 1);

            var fromText = ShouldBeTooLarge(LockfileReader.Read(text), LockfileReader.FileName);
            var fromBytes = ShouldBeTooLarge(LockfileReader.Read(Encoding.UTF8.GetBytes(text), "other/packmoji.lock"), "other/packmoji.lock");

            fromText.Reason.ShouldContain("4194304 bytes");
            fromText.Fix.ShouldBe(LockfileReader.RegenerateFix);
            fromBytes.Fix.ShouldBe(LockfileReader.RegenerateFix);
        }

        [Fact]
        public void The_limit_counts_bytes_and_not_characters()
        {
            // 🍇 is four bytes in UTF-8 and two characters in a string.
            var grapes = string.Concat(Enumerable.Repeat("🍇", ManifestReader.MaxBytes / 4 + 1));

            grapes.Length.ShouldBeLessThan(ManifestReader.MaxBytes);
            ShouldBeTooLarge(ManifestReader.Read(grapes), ManifestReader.FileName);
        }

        [Fact]
        public void A_file_that_is_too_large_is_not_read_at_all()
        {
            // A million braces is nesting no reader accepts, and a file that is not JSON: neither is said.
            var braces = new string('{', LockfileReader.MaxBytes + 1);

            ShouldBeTooLarge(ManifestReader.Read(braces), ManifestReader.FileName);
            ShouldBeTooLarge(LockfileReader.Read(braces), LockfileReader.FileName);
        }
    }
}
