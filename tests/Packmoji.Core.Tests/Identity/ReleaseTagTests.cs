using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Identity
{
    public sealed class ReleaseTagTests
    {
        [Theory]
        [InlineData("@thatplatypus/crypto", "1.0.0", "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz")]
        [InlineData("@thatplatypus/emoji_crypto", "0.4.1", "emoji_crypto-v0.4.1", "emoji_crypto-0.4.1.pmj.tar.gz")]
        [InlineData("@thatplatypus/crypto", "1.0.0-beta.1", "crypto-v1.0.0-beta.1", "crypto-1.0.0-beta.1.pmj.tar.gz")]
        public void A_tag_and_an_asset_are_made_from_the_name_and_the_version(string package, string version, string tag, string asset)
        {
            ReleaseTag.For(Sample.Name(package), Sample.Version(version)).ShouldBe(tag);
            AssetName.For(Sample.Name(package), Sample.Version(version)).ShouldBe(asset);
        }

        [Theory]
        [InlineData("crypto-v1.0.0", "crypto", "1.0.0")]
        [InlineData("emoji_crypto-v0.4.1", "emoji_crypto", "0.4.1")]
        [InlineData("crypto-v1.0.0-beta.1", "crypto", "1.0.0-beta.1")]
        [InlineData("v-v1.0.0-v", "v", "1.0.0-v")]
        public void A_tag_is_split_at_its_first_hyphen(string tag, string name, string version)
        {
            ReleaseTag.TryParse(tag, out var parsedName, out var parsedVersion).ShouldBeTrue();

            parsedName.ShouldBe(name);
            parsedVersion.ShouldBe(Sample.Version(version));
        }

        [Theory]
        [InlineData("")]
        [InlineData("v1.0.0")]
        [InlineData("crypto")]
        [InlineData("crypto-1.0.0")]
        [InlineData("crypto-v")]
        [InlineData("crypto-v1.0")]
        [InlineData("crypto-V1.0.0")]
        [InlineData("crypto-v1.0.0+build")]
        [InlineData("-v1.0.0")]
        [InlineData("Crypto-v1.0.0")]
        [InlineData("emoji-crypto-v1.0.0")]
        public void Anything_else_is_not_a_release_tag(string tag)
        {
            ReleaseTag.TryParse(tag, out var name, out var version).ShouldBeFalse();

            name.ShouldBeNull();
            version.ShouldBeNull();
        }
    }
}
