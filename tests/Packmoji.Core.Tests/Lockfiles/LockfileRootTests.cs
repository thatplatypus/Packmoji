using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Lockfiles
{
    /// <summary>
    /// A manifest names a package once, so a lockfile's record of what the manifest asked for does too.
    /// One that names a package twice was not written from a manifest, and has no one order to be
    /// written in.
    /// </summary>
    public sealed class LockfileRootTests
    {
        private const string Entry = """
            {
              "name": "@thatplatypus/crypto",
              "version": "1.0.0",
              "source": "github.com/thatplatypus/grapevine",
              "releaseTag": "crypto-v1.0.0",
              "asset": "crypto-1.0.0.pmj.tar.gz",
              "sha256": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
              "verified": "checksum",
              "dependencies": []
            }
            """;

        private static string Lock(string dependencies, string devDependencies) =>
            "{\n  \"version\": 1,\n  \"root\": {\n    \"dependencies\": [" + dependencies + "],\n    \"devDependencies\": [" + devDependencies + "]\n  },\n  \"packages\": [" + Entry + "]\n}";

        [Fact]
        public void A_package_asked_for_twice_under_two_spellings_is_refused_at_the_second()
        {
            var marked = Marked.From(Lock("\"@thatplatypus/crypto@1.0\", §\"@thatplatypus/crypto@1.0.0\"", ""));

            var diagnostic = LockfileReader.Read(marked.Text).ShouldFailAt(marked, DiagnosticCodes.DependencyDuplicate, LockfileReader.FileName);

            diagnostic.Message.ShouldContain("\"@thatplatypus/crypto\"");
            diagnostic.Fix.ShouldBe(LockfileReader.RegenerateFix);
        }

        [Fact]
        public void A_package_asked_for_in_both_lists_is_refused_at_the_second()
        {
            var marked = Marked.From(Lock("\"@thatplatypus/crypto@1.0\"", "§\"@thatplatypus/crypto@1.0\""));

            LockfileReader.Read(marked.Text).ShouldFailAt(marked, DiagnosticCodes.DependencyDuplicate, LockfileReader.FileName);
        }

        [Fact]
        public void A_package_asked_for_once_reads()
        {
            LockfileReader.Read(Lock("\"@thatplatypus/crypto@1.0\"", "")).ShouldSucceed();
            LockfileReader.Read(Lock("", "\"@thatplatypus/crypto@1.0\"")).ShouldSucceed();
        }

        [Fact]
        public void The_bytes_written_do_not_depend_on_order_even_for_a_model_that_names_a_package_twice()
        {
            var crypto = Sample.Name("@thatplatypus/crypto");
            var loose = new Dependency(crypto, Sample.Requirement("1.0"));
            var exact = new Dependency(crypto, Sample.Requirement("1.0.0"));
            var newer = new Dependency(crypto, Sample.Requirement("1.2"));

            var one = LockfileWriter.Write(new Lockfile(new RootRequirements([loose, exact, newer], [newer, loose]), []));
            var other = LockfileWriter.Write(new Lockfile(new RootRequirements([newer, exact, loose], [loose, newer]), []));

            other.ShouldBe(one);
        }

        [Fact]
        public void Nor_for_a_model_that_holds_a_package_at_two_versions()
        {
            LockedPackage Crypto(string version, params string[] pins) => new(
                Sample.Name("@thatplatypus/crypto"),
                Sample.Version(version),
                Sample.Repository("github.com/thatplatypus/grapevine"),
                Sample.Sha('a'),
                VerificationLevel.Checksum,
                pins.Select(pin => new LockedDependency(Sample.Name("@thatplatypus/deflate"), Sample.Version(pin))).ToList());

            var root = new RootRequirements([], []);
            var one = LockfileWriter.Write(new Lockfile(root, [Crypto("1.0.0", "0.1.0", "0.2.0"), Crypto("1.1.0")]));
            var other = LockfileWriter.Write(new Lockfile(root, [Crypto("1.1.0"), Crypto("1.0.0", "0.2.0", "0.1.0")]));

            other.ShouldBe(one);
        }
    }
}
