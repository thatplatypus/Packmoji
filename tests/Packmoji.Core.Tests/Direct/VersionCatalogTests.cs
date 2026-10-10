using Packmoji.Core.Direct;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Direct
{
    public sealed class VersionCatalogTests
    {
        private static ReleaseInfo Release(string tag, params string[] assets) => new(tag, tag.Contains("-beta") || tag.Contains("-rc"), assets);

        private static readonly ReleaseInfo[] Releases =
        [
            Release("crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz"),
            Release("crypto-v1.2.0", "crypto-1.2.0.pmj.tar.gz", "notes.txt"),
            Release("crypto-v1.10.0", "crypto-1.10.0.pmj.tar.gz"),
            Release("crypto-v2.0.0-rc.1", "crypto-2.0.0-rc.1.pmj.tar.gz"),
            Release("crypto-v0.9.0", "crypto-0.9.0.pmj.tar.gz"),
            Release("deflate-v3.0.0", "deflate-3.0.0.pmj.tar.gz"),
            Release("crypto-v1.3.0", "crypto-1.3.0.zip"),
            Release("crypto-v1.4.0"),
            Release("v1.5.0", "crypto-1.5.0.pmj.tar.gz"),
            Release("crypto-v1.6", "crypto-1.6.pmj.tar.gz"),
            Release("cryptography-v9.0.0", "cryptography-9.0.0.pmj.tar.gz"),
        ];

        private static string[] Versions(string name) => VersionCatalog.Of(Sample.Name(name), Releases).Select(version => version.ToString()).ToArray();

        [Fact]
        public void A_packages_versions_are_the_releases_with_its_tag_and_its_asset_lowest_first()
        {
            Versions("@thatplatypus/crypto").ShouldBe(["0.9.0", "1.0.0", "1.2.0", "1.10.0", "2.0.0-rc.1"]);
            Versions("@thatplatypus/deflate").ShouldBe(["3.0.0"]);
            Versions("@thatplatypus/grapevine").ShouldBeEmpty();
        }

        [Fact]
        public void The_latest_is_the_highest_that_is_not_a_pre_release()
        {
            VersionCatalog.Latest(VersionCatalog.Of(Sample.Name("@thatplatypus/crypto"), Releases)).ShouldBe(Sample.Version("1.10.0"));
            VersionCatalog.Latest([Sample.Version("1.0.0-beta.1"), Sample.Version("1.0.0-rc.1")]).ShouldBeNull();
            VersionCatalog.Latest([]).ShouldBeNull();
        }

        [Fact]
        public void The_latest_on_a_requirements_line_is_above_its_minimum_and_never_on_another_line()
        {
            var versions = VersionCatalog.Of(Sample.Name("@thatplatypus/crypto"), Releases);

            VersionCatalog.LatestOnLine(versions, Sample.Requirement("1.0")).ShouldBe(Sample.Version("1.10.0"));
            VersionCatalog.LatestOnLine(versions, Sample.Requirement("1.2")).ShouldBe(Sample.Version("1.10.0"));
            VersionCatalog.LatestOnLine(versions, Sample.Requirement("1.10")).ShouldBeNull();
            VersionCatalog.LatestOnLine(versions, Sample.Requirement("0.9")).ShouldBeNull();
            VersionCatalog.LatestOnLine(versions, Sample.Requirement("0.8")).ShouldBeNull();
            VersionCatalog.LatestOnLine(versions, Sample.Requirement("3.0")).ShouldBeNull();
        }
    }
}
