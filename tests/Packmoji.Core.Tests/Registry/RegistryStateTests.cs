using Packmoji.Core.Registry;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;
using static Packmoji.Core.Tests.Registry.RegistrySample;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// A registry as it stands. Whatever it is made from, in whatever order, it is one thing: its
    /// packages and what was refused are in order, and a repository with nothing refused is not in it.
    /// </summary>
    public sealed class RegistryStateTests
    {
        private const string Crypto = "github.com/thatplatypus/crypto";

        private static readonly RegistryState State = new(
            [Package("@thatplatypus/grapevine", Version("0.1.0")), Package("@emojicode/sockets_more", Version("1.0.0")), Package("@thatplatypus/crypto", Version("0.1.0"))],
            [Refused(Grapevine, Refusal("deflate-v9.0.0")), Refused("github.com/thatplatypus/nothing"), Refused(Crypto, Refusal("crypto-v0.2.0"))]);

        [Fact]
        public void The_packages_are_in_order_of_name_whatever_order_they_were_given_in() =>
            State.Packages.Select(package => package.Name.ToString()).ShouldBe(["@emojicode/sockets_more", "@thatplatypus/crypto", "@thatplatypus/grapevine"]);

        [Fact]
        public void What_was_refused_is_in_order_of_repository_and_a_repository_with_nothing_refused_is_left_out() =>
            State.Refused.Select(file => file.Repository.ToString()).ShouldBe([Crypto, Grapevine]);

        [Fact]
        public void A_package_and_what_was_refused_in_a_repository_are_found_by_name_or_are_not_there()
        {
            State.Package(Sample.Name("@thatplatypus/crypto")).ShouldNotBeNull().Versions.ShouldHaveSingleItem();
            State.Package(Sample.Name("@thatplatypus/deflate")).ShouldBeNull();
            State.RefusedIn(Sample.Repository(Grapevine)).ShouldNotBeNull().Releases.ShouldHaveSingleItem().Tag.ShouldBe("deflate-v9.0.0");
            State.RefusedIn(Sample.Repository("github.com/thatplatypus/nothing")).ShouldBeNull();
        }

        [Fact]
        public void Its_files_are_each_at_the_path_its_name_gives_and_hold_what_the_writers_write()
        {
            var files = State.Files();

            files.Keys.ShouldBe(
            [
                "v1/packages/emojicode/sockets_more.json",
                "v1/packages/thatplatypus/crypto.json",
                "v1/packages/thatplatypus/grapevine.json",
                "v1/refused/thatplatypus/crypto.json",
                "v1/refused/thatplatypus/grapevine.json",
            ]);
            files["v1/packages/thatplatypus/crypto.json"].ShouldBe(RegistryPackageWriter.Write(State.Package(Sample.Name("@thatplatypus/crypto"))!));
            files["v1/refused/thatplatypus/grapevine.json"].ShouldBe(RefusedReleasesWriter.Write(State.RefusedIn(Sample.Repository(Grapevine))!));
        }

        [Fact]
        public void A_registry_that_holds_nothing_has_no_file()
        {
            RegistryState.Empty.Packages.ShouldBeEmpty();
            RegistryState.Empty.Refused.ShouldBeEmpty();
            RegistryState.Empty.Files().ShouldBeEmpty();
        }
    }
}
