using Packmoji.Core.Archives;
using Packmoji.Core.Direct;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Direct
{
    public sealed class ReleaseRuleTests
    {
        private const string Shared = "github.com/thatplatypus/grapevine";

        private static byte[] Archive(string manifest) =>
            PackageArchive.Write([Sample.File("packmoji.json", manifest), Sample.File("src/lib.🍇", "💭\n")]);

        private static ReleaseVerdict Judge(string name, string version, string repository, byte[] archive) =>
            ReleaseRule.Judge(Sample.Name(name), Sample.Version(version), Sample.Repository(repository), archive);

        [Fact]
        public void A_release_that_is_what_its_tag_says_is_taken_with_its_digest_and_what_it_needs()
        {
            var archive = Archive(Sample.ManifestJson("@thatplatypus/grapevine", "0.3.0", Shared, "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1"));

            var verdict = Judge("@thatplatypus/grapevine", "0.3.0", Shared, archive);

            verdict.Taken.ShouldBeTrue();
            verdict.Fault.ShouldBeNull();
            var published = verdict.Published.ShouldNotBeNull();
            published.Name.ShouldBe(Sample.Name("@thatplatypus/grapevine"));
            published.Version.ShouldBe(Sample.Version("0.3.0"));
            published.Status.ShouldBe(VersionStatus.Active);
            published.Source.ShouldBe(Sample.Repository(Shared));
            published.Sha256.ShouldBe(Sha256Digest.Of(archive));
            published.Verified.ShouldBe(VerificationLevel.Checksum);
            published.Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Requirement.Text}").Order(StringComparer.Ordinal)
                .ShouldBe(["@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1"]);
            verdict.Manifest.ShouldNotBeNull().Package.Name.ShouldBe(Sample.Name("@thatplatypus/grapevine"));
        }

        [Fact]
        public void A_package_in_a_repository_of_its_own_name_need_not_say_where_it_lives()
        {
            var archive = Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", "github.com/thatplatypus/crypto"));

            Judge("@thatplatypus/crypto", "1.0.0", "github.com/thatplatypus/crypto", archive).Taken.ShouldBeTrue();
        }

        [Fact]
        public void A_file_that_is_not_an_archive_is_refused_as_one()
        {
            var verdict = Judge("@thatplatypus/crypto", "1.0.0", Shared, [1, 2, 3]);

            verdict.Taken.ShouldBeFalse();
            verdict.Published.ShouldBeNull();
            verdict.Fault.ShouldBe(ReleaseFault.NotAnArchive);
            verdict.Reason.ShouldContain("gzip");
            verdict.Manifest.ShouldBeNull();
        }

        [Fact]
        public void An_archive_whose_manifest_cannot_be_read_is_refused_for_that()
        {
            var verdict = Judge("@thatplatypus/crypto", "1.0.0", Shared, Archive("{ \"package\": { \"name\": \"@thatplatypus/crypto\" } }"));

            verdict.Fault.ShouldBe(ReleaseFault.ManifestUnreadable);
            verdict.Reason.ShouldStartWith("its packmoji.json cannot be read: ");
            verdict.Manifest.ShouldBeNull();
        }

        [Fact]
        public void A_manifest_of_another_package_is_refused_and_says_whose_it_is()
        {
            var verdict = Judge("@thatplatypus/grapevine", "1.0.0", Shared, Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", Shared)));

            verdict.Fault.ShouldBe(ReleaseFault.OtherPackage);
            verdict.Reason.ShouldBe("the manifest in its archive is that of \"@thatplatypus/crypto\"");
            verdict.Manifest.ShouldNotBeNull().Package.Name.ShouldBe(Sample.Name("@thatplatypus/crypto"));
        }

        [Fact]
        public void A_manifest_of_another_version_is_refused_and_says_which()
        {
            var verdict = Judge("@thatplatypus/crypto", "1.1.0", Shared, Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", Shared)));

            verdict.Fault.ShouldBe(ReleaseFault.OtherVersion);
            verdict.Reason.ShouldBe("the manifest in its archive gives the version 1.0.0");
            verdict.Manifest.ShouldNotBeNull().Package.Version.ShouldBe(Sample.Version("1.0.0"));
        }

        [Fact]
        public void A_manifest_that_says_the_package_lives_elsewhere_is_refused_and_says_where()
        {
            var verdict = Judge("@thatplatypus/crypto", "1.0.0", Shared, Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", "github.com/thatplatypus/crypto")));

            verdict.Fault.ShouldBe(ReleaseFault.OtherRepository);
            verdict.Reason.ShouldBe("the manifest in its archive says that the package lives in github.com/thatplatypus/crypto, and a release counts only where its package says it lives");
            verdict.Manifest.ShouldNotBeNull().Repository.ShouldBe(Sample.Repository("github.com/thatplatypus/crypto"));
        }

        [Fact]
        public void The_name_is_held_before_the_version_and_the_version_before_the_place()
        {
            var elsewhere = Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", "github.com/thatplatypus/crypto"));

            Judge("@thatplatypus/grapevine", "2.0.0", Shared, elsewhere).Fault.ShouldBe(ReleaseFault.OtherPackage);
            Judge("@thatplatypus/crypto", "2.0.0", Shared, elsewhere).Fault.ShouldBe(ReleaseFault.OtherVersion);
        }
    }
}
