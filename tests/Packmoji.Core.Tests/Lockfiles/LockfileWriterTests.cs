using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Lockfiles
{
    public sealed class LockfileWriterTests
    {
        private static LockedPackage Package(string name, string version, string sha256, VerificationLevel verified, params string[] dependencies) => new(
            Sample.Name(name),
            Sample.Version(version),
            Sample.Repository("github.com/thatplatypus/grapevine"),
            Sample.ShaOf(sha256),
            verified,
            dependencies.Select(Pin).ToList());

        private static LockedDependency Pin(string pin)
        {
            var at = pin.LastIndexOf('@');
            return new LockedDependency(Sample.Name(pin[..at]), Sample.Version(pin[(at + 1)..]));
        }

        // The lockfile of Fixtures.Lockfile, built in code and with nothing in its canonical order.
        private static Lockfile Example() => new(
            new RootRequirements([new Dependency(Sample.Name("@thatplatypus/grapevine"), Sample.Requirement("0.3"))], []),
            [
                Package(
                    "@thatplatypus/grapevine", "0.3.0", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", VerificationLevel.Checksum,
                    "@thatplatypus/deflate@0.1.0", "@thatplatypus/crypto@1.0.0"),
                Package("@thatplatypus/deflate", "0.1.0", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", VerificationLevel.Attestation),
                Package("@thatplatypus/crypto", "1.0.0", "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", VerificationLevel.Attestation),
            ]);

        [Fact]
        public void A_lockfile_is_written_in_its_canonical_form_byte_for_byte() =>
            LockfileWriter.Write(Example()).ShouldBe(Fixtures.Lockfile);

        [Fact]
        public void A_lockfile_with_nothing_locked_is_written_with_its_empty_lists() =>
            LockfileWriter.Write(new Lockfile(new RootRequirements([], []), [])).ShouldBe(Fixtures.EmptyLockfile);

        [Fact]
        public void Reading_a_canonical_lockfile_and_writing_it_gives_the_same_bytes()
        {
            foreach (var fixture in new[] { Fixtures.Lockfile, Fixtures.EmptyLockfile })
            {
                LockfileWriter.Write(LockfileReader.Read(fixture).ShouldSucceed()).ShouldBe(fixture);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(20261009)]
        public void The_bytes_do_not_depend_on_the_order_anything_was_found_in(int seed)
        {
            var random = new Random(seed);
            var example = Example();

            var packages = example.Packages
                .Select(package =>
                {
                    var dependencies = package.Dependencies.ToArray();
                    random.Shuffle(dependencies);
                    return package with { Dependencies = dependencies };
                })
                .ToArray();
            random.Shuffle(packages);

            var requirements = new[]
            {
                new Dependency(Sample.Name("@thatplatypus/grapevine"), Sample.Requirement("0.3")),
                new Dependency(Sample.Name("@thatplatypus/crypto"), Sample.Requirement("1.0")),
                new Dependency(Sample.Name("@a-team/zebra"), Sample.Requirement("2.1")),
            };
            var inOrder = LockfileWriter.Write(new Lockfile(new RootRequirements(requirements, requirements), example.Packages));
            random.Shuffle(requirements);
            var shuffled = LockfileWriter.Write(new Lockfile(new RootRequirements(requirements, requirements.Reverse().ToArray()), packages));

            shuffled.ShouldBe(inOrder);
        }

        [Fact]
        public void A_root_requirement_is_written_as_the_manifest_spelled_it()
        {
            var lockfile = new Lockfile(
                new RootRequirements([new Dependency(Sample.Name("@thatplatypus/crypto"), Sample.Requirement("1.0.0"))], []),
                [Package("@thatplatypus/crypto", "1.0.0", new string('a', 64), VerificationLevel.Checksum)]);

            LockfileWriter.Write(lockfile).ShouldContain("\"@thatplatypus/crypto@1.0.0\"");
        }

        [Fact]
        public void What_is_written_holds_together_when_it_is_read()
        {
            var read = LockfileReader.Read(LockfileWriter.Write(Example())).ShouldSucceed();

            read.Packages.Select(package => package.Name.ToString())
                .ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/grapevine"]);
            LockfileWriter.Write(read).ShouldNotContain("\r");
        }
    }
}
