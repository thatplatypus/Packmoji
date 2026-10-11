using Packmoji.Core.Registry;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// Where each file of a registry is. A path is made from a name and a name is read back from a
    /// path, and a path that is not one of the two kinds is the path of nothing.
    /// </summary>
    public sealed class RegistryPathsTests
    {
        [Fact]
        public void A_packages_file_is_under_its_scope_and_named_for_it() =>
            RegistryPaths.Package(Sample.Name("@thatplatypus/emoji_crypto")).ShouldBe("v1/packages/thatplatypus/emoji_crypto.json");

        [Fact]
        public void The_file_of_what_was_refused_in_a_repository_is_under_its_owner_and_named_for_it() =>
            RegistryPaths.Refused(Sample.Repository("github.com/that-platypus/grape.vine_2")).ShouldBe("v1/refused/that-platypus/grape.vine_2.json");

        [Theory]
        [InlineData("@thatplatypus/crypto")]
        [InlineData("@a/b")]
        [InlineData("@that-platypus/emoji_crypto2")]
        public void A_packages_path_gives_its_name_back(string name)
        {
            RegistryPaths.TryParsePackage(RegistryPaths.Package(Sample.Name(name)), out var read).ShouldBeTrue();

            read.ShouldBe(Sample.Name(name));
        }

        [Theory]
        [InlineData("github.com/thatplatypus/grapevine")]
        [InlineData("github.com/that-platypus/grape.vine_2")]
        [InlineData("github.com/a/b.json")]
        public void The_path_of_what_was_refused_gives_the_repository_back(string repository)
        {
            RegistryPaths.TryParseRefused(RegistryPaths.Refused(Sample.Repository(repository)), out var read).ShouldBeTrue();

            read.ShouldBe(Sample.Repository(repository));
        }

        [Theory]
        [InlineData("v1/packages/thatplatypus/crypto")]
        [InlineData("v1/packages/thatplatypus/crypto.JSON")]
        [InlineData("v1/packages/thatplatypus/crypto.json.bak")]
        [InlineData("v1/packages/thatplatypus/Crypto.json")]
        [InlineData("v1/packages/thatplatypus/emoji-crypto.json")]
        [InlineData("v1/packages/thatplatypus/json.json")]
        [InlineData("v1/packages/That/crypto.json")]
        [InlineData("v1/packages/crypto.json")]
        [InlineData("v1/packages/thatplatypus/more/crypto.json")]
        [InlineData("v1/packages/thatplatypus/.json")]
        [InlineData("v1/packages//crypto.json")]
        [InlineData("v1/refused/thatplatypus/crypto.json")]
        [InlineData("v2/packages/thatplatypus/crypto.json")]
        [InlineData("packages/thatplatypus/crypto.json")]
        [InlineData("/v1/packages/thatplatypus/crypto.json")]
        [InlineData("v1/packages/thatplatypus/crypto.json/")]
        [InlineData("v1\\packages\\thatplatypus\\crypto.json")]
        [InlineData("v1/packages/../packages/thatplatypus/crypto.json")]
        [InlineData("")]
        public void A_path_that_is_not_a_packages_is_the_path_of_no_package(string path) =>
            RegistryPaths.TryParsePackage(path, out _).ShouldBeFalse();

        [Theory]
        [InlineData("v1/refused/thatplatypus/grapevine")]
        [InlineData("v1/refused/thatplatypus/Grapevine.json")]
        [InlineData("v1/refused/thatplatypus/grapevine.git.json")]
        [InlineData("v1/refused/thatplatypus/...json")]
        [InlineData("v1/refused/thatplatypus/..json")]
        [InlineData("v1/refused/-platypus/grapevine.json")]
        [InlineData("v1/refused/grapevine.json")]
        [InlineData("v1/refused/thatplatypus/more/grapevine.json")]
        [InlineData("v1/packages/thatplatypus/grapevine.json")]
        [InlineData("v1/refused/thatplatypus/grape vine.json")]
        [InlineData("")]
        public void A_path_that_is_not_one_of_what_was_refused_is_the_path_of_no_repository(string path) =>
            RegistryPaths.TryParseRefused(path, out _).ShouldBeFalse();
    }
}
