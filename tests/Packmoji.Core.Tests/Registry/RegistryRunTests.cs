using System.Text;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// One run of what keeps a registry: the releases of the listed repositories are looked at, and
    /// the registry's next state is made. What is held here is what a run takes, what it refuses and
    /// why, what it never changes, and that it looks at nothing twice.
    /// </summary>
    public sealed class RegistryRunTests
    {
        private const string G = ListedReleases.Grapevine;
        private const string Elsewhere = "github.com/thatplatypus/crypto";

        private static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 17, 40, TimeSpan.Zero);
        private static readonly DateTimeOffset Later = new(2026, 10, 12, 8, 17, 3, TimeSpan.Zero);

        private static readonly PackageName Crypto = Sample.Name("@thatplatypus/crypto");

        private static ListedReleases ThreePackages() => new ListedReleases()
            .Package(G, "@thatplatypus/crypto", "0.1.0")
            .Package(G, "@thatplatypus/deflate", "0.1.0")
            .Package(G, "@thatplatypus/grapevine", "0.1.0", "@thatplatypus/crypto@0.1", "@thatplatypus/deflate@0.1");

        private static Task<RegistryRunResult> Run(ListedReleases host, RegistryState? before = null, DateTimeOffset? now = null, RegistryLimits? limits = null) =>
            RunOn(host, [G], before, now, limits);

        private static Task<RegistryRunResult> RunOn(ListedReleases host, string[] repositories, RegistryState? before = null, DateTimeOffset? now = null, RegistryLimits? limits = null) =>
            RegistryRun.RunAsync(before ?? RegistryState.Empty, repositories.Select(Sample.Repository).ToList(), host, now ?? Now, limits ?? RegistryLimits.Default, TestContext.Current.CancellationToken);

        private static IEnumerable<string> Said(RegistryRunResult result) => result.Notes.Select(note => note.Text);

        private static RegistryVersion Only(RegistryState state, string name) =>
            state.Package(Sample.Name(name)).ShouldNotBeNull().Versions.ShouldHaveSingleItem();

        private static async Task<RefusedRelease> ShouldRefuse(ListedReleases host, string tag, string code, string why, RegistryState? before = null)
        {
            var result = await Run(host, before);

            var refused = result.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Find(tag).ShouldNotBeNull();
            refused.Code.ShouldBe(code);
            refused.Why.ShouldBe(why);
            refused.Seen.ShouldBe(Now);
            RefusedReleasesReader.IsCode(refused.Code).ShouldBeTrue();
            RefusedReleasesReader.IsFitToShow(refused.Why).ShouldBeTrue();
            result.Notes.ShouldContain(note => note.Kind == RegistryNoteKind.Refused && note.Text == $"refused {tag} in {G}: {why}");
            return refused;
        }

        [Fact]
        public async Task The_releases_of_a_listed_repository_become_the_versions_of_their_packages()
        {
            var host = ThreePackages();
            host.Change(G, "deflate-v0.1.0", release => release.IsImmutable = true);

            var result = await Run(host);

            result.After.Packages.Select(package => package.Name.ToString()).ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/grapevine"]);
            result.After.Refused.ShouldBeEmpty();

            var archive = host.Find(G, "grapevine-v0.1.0").Bytes;
            var grapevine = Only(result.After, "@thatplatypus/grapevine");
            grapevine.Version.ShouldBe(Sample.Version("0.1.0"));
            grapevine.Status.ShouldBe(VersionStatus.Active);
            grapevine.Source.ShouldBe(Sample.Repository(G));
            grapevine.Sha256.ShouldBe(Sha256Digest.Of(archive));
            grapevine.Size.ShouldBe(archive.Length);
            grapevine.Verified.ShouldBe(VerificationLevel.Checksum);
            grapevine.IsImmutable.ShouldBeFalse();
            grapevine.Published.ShouldBe(ListedReleases.PublishedAt);
            grapevine.Indexed.ShouldBe(Now);
            grapevine.Quarantined.ShouldBeNull();
            grapevine.Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Requirement.Text}").Order(StringComparer.Ordinal)
                .ShouldBe(["@thatplatypus/crypto@0.1", "@thatplatypus/deflate@0.1"]);
            Only(result.After, "@thatplatypus/deflate").IsImmutable.ShouldBeTrue();

            Said(result).ShouldBe(
            [
                $"added @thatplatypus/crypto 0.1.0 from {G}",
                $"added @thatplatypus/deflate 0.1.0 from {G}",
                $"added @thatplatypus/grapevine 0.1.0 from {G}",
            ]);
            result.Notes.ShouldAllBe(note => note.Kind == RegistryNoteKind.Added);
        }

        [Fact]
        public async Task A_second_run_finds_nothing_new_and_downloads_nothing_and_changes_nothing()
        {
            var host = ThreePackages();
            var first = await Run(host);
            host.Downloads.Clear();

            var second = await Run(host, first.After, Later);

            host.Downloads.ShouldBeEmpty();
            second.Notes.ShouldBeEmpty();
            second.After.Files().ShouldBe(first.After.Files());
        }

        [Fact]
        public async Task The_order_of_the_list_and_of_what_GitHub_answers_changes_nothing()
        {
            ListedReleases Host() => ThreePackages().Package(Elsewhere, "@thatplatypus/crypto", "1.0.0").Release(G, "deflate-v9.0.0", "deflate-9.0.0.pmj.tar.gz", [1, 2, 3]);
            var backwards = Host();
            backwards.Backwards = true;

            var one = await RunOn(Host(), [G, Elsewhere]);
            var other = await RunOn(backwards, [Elsewhere, G]);

            other.After.Files().ShouldBe(one.After.Files());
            Said(other).ShouldBe(Said(one));
        }

        [Fact]
        public async Task A_version_is_indexed_from_whichever_repository_its_package_says_it_lives_in()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "0.1.0").Package(Elsewhere, "@thatplatypus/crypto", "1.0.0");

            var result = await RunOn(host, [Elsewhere, G]);

            result.After.Package(Crypto).ShouldNotBeNull().Versions.Select(version => $"{version.Version} {version.Source}").ShouldBe([$"0.1.0 {G}", $"1.0.0 {Elsewhere}"]);
        }

        [Fact]
        public async Task A_release_that_is_no_claim_to_be_a_package_is_passed_over_and_nothing_of_it_is_downloaded()
        {
            var archive = ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", G));
            var host = new ListedReleases()
                .Release(G, "v1.0.0", "crypto-1.0.0.pmj.tar.gz", archive)
                .Release(G, "crypto-1.0.0", "crypto-1.0.0.pmj.tar.gz", archive)
                .Release(G, "crypto-v1.0", "crypto-1.0.pmj.tar.gz", archive)
                .Release(G, "crypto-v1.0.0", "crypto-1.0.0.zip", archive)
                .Release(G, "crypto-v1.1.0", "crypto-1.1.0.pmj.tar.gz", archive)
                .Release(G, "crypto-v1.2.0", "crypto-1.2.0.pmj.tar.gz", archive)
                .Release(G, "crypto-v1.3.0", "crypto-1.3.0.pmj.tar.gz", archive)
                .Change(G, "crypto-v1.1.0", release => release.IsUploaded = false)
                .Change(G, "crypto-v1.2.0", release => release.SaysDigest = false)
                .Change(G, "crypto-v1.3.0", release => release.Published = null);

            var result = await Run(host);

            result.After.Packages.ShouldBeEmpty();
            result.After.Refused.ShouldBeEmpty();
            result.Notes.ShouldBeEmpty();
            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_that_is_not_an_archive_is_refused_and_recorded_with_the_digest_GitHub_lists()
        {
            var host = new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", [1, 2, 3]);

            var refused = await ShouldRefuse(host, "crypto-v1.0.0", DiagnosticCodes.ArchiveInvalid, "its file is not an archive as pmj pack writes one");

            refused.Sha256.ShouldBe(Sha256Digest.Of([1, 2, 3]));
        }

        [Fact]
        public async Task A_manifest_that_cannot_be_read_is_refused_and_nothing_it_holds_is_repeated()
        {
            var host = new ListedReleases().PackageWith(G, "@thatplatypus/crypto", "1.0.0", "{ \"package\": { \"name\": \"\\u001b[31m::error::@evil/thing\\n\" } }");

            var refused = await ShouldRefuse(host, "crypto-v1.0.0", DiagnosticCodes.ReleaseInvalid, "the packmoji.json in its archive cannot be read as a manifest");

            refused.Why.ShouldNotContain("evil");
        }

        [Fact]
        public async Task A_manifest_of_another_package_is_refused_and_says_whose_it_is() =>
            await ShouldRefuse(
                new ListedReleases().Release(G, "grapevine-v1.0.0", "grapevine-1.0.0.pmj.tar.gz", ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", G))),
                "grapevine-v1.0.0",
                DiagnosticCodes.ReleaseInvalid,
                "the manifest in its archive is that of @thatplatypus/crypto");

        [Fact]
        public async Task A_manifest_of_another_version_is_refused_and_says_which() =>
            await ShouldRefuse(
                new ListedReleases().Release(G, "crypto-v1.1.0", "crypto-1.1.0.pmj.tar.gz", ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", G))),
                "crypto-v1.1.0",
                DiagnosticCodes.ReleaseInvalid,
                "the manifest in its archive gives the version 1.0.0");

        [Fact]
        public async Task A_manifest_that_says_the_package_lives_elsewhere_is_refused_and_says_where() =>
            await ShouldRefuse(
                new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", Elsewhere))),
                "crypto-v1.0.0",
                DiagnosticCodes.ReleaseInvalid,
                $"the manifest in its archive says that the package lives in {Elsewhere}");

        // The scope a release claims is the owner of the repository it is in, and never what its manifest says.
        [Fact]
        public async Task A_package_of_one_owner_released_in_the_repository_of_another_is_not_that_package()
        {
            var host = new ListedReleases().Release("github.com/someone/grapevine", "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", G)));

            var result = await RunOn(host, ["github.com/someone/grapevine"]);

            result.After.Packages.ShouldBeEmpty();
            result.After.RefusedIn(Sample.Repository("github.com/someone/grapevine")).ShouldNotBeNull().Releases.ShouldHaveSingleItem().Why.ShouldBe("the manifest in its archive is that of @thatplatypus/crypto");
        }

        [Fact]
        public async Task A_file_the_list_says_is_over_the_limit_is_refused_and_is_not_downloaded()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0").Change(G, "crypto-v1.0.0", release => release.ListedSize = PackageArchive.MaxBytes + 1L);

            await ShouldRefuse(host, "crypto-v1.0.0", DiagnosticCodes.ArchiveInvalid, "its file is larger than 16777216 bytes");

            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_of_exactly_the_limit_by_the_list_is_downloaded()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0").Change(G, "crypto-v1.0.0", release => release.ListedSize = PackageArchive.MaxBytes);

            (await Run(host)).After.Package(Crypto).ShouldNotBeNull();
        }

        [Fact]
        public async Task The_size_that_is_recorded_is_the_files_own_and_not_what_the_list_said()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0").Change(G, "crypto-v1.0.0", release => release.ListedSize = 7);

            var result = await Run(host);

            Only(result.After, "@thatplatypus/crypto").Size.ShouldBe(host.Find(G, "crypto-v1.0.0").Bytes.Length);
        }

        // A file of the registry may be so large and no larger, and its own reader refuses one that is.
        // So a version that would make its package's file too large is not recorded: a registry that
        // could not be read again would stop every run after it, for every package.
        [Fact]
        public async Task A_version_whose_record_would_make_its_packages_file_too_large_is_refused_and_what_was_recorded_stays()
        {
            var host = new ListedReleases()
                .Package(G, "@thatplatypus/crypto", "1.0.0")
                .Package(G, "@thatplatypus/crypto", "1.1.0", "@thatplatypus/deflate@0.1", "@thatplatypus/grapevine@0.1")
                .Package(G, "@thatplatypus/deflate", "0.1.0");
            var one = Encoding.UTF8.GetByteCount(RegistryPackageWriter.Write((await Run(new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0"))).After.Package(Crypto)!));
            var limits = RegistryLimits.Default with { MaxFileBytes = one + 100 };

            var result = await Run(host, limits: limits);

            result.After.Package(Crypto).ShouldNotBeNull().Versions.Select(version => version.Version.ToString()).ShouldBe(["1.0.0"]);
            result.After.Package(Sample.Name("@thatplatypus/deflate")).ShouldNotBeNull();
            var refused = result.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Releases.ShouldHaveSingleItem();
            refused.Tag.ShouldBe("crypto-v1.1.0");
            refused.Code.ShouldBe(DiagnosticCodes.ReleaseInvalid);
            refused.Why.ShouldBe("what would be recorded of it is more than the file of its package may hold");
            foreach (var text in result.After.Files().Values)
            {
                Encoding.UTF8.GetByteCount(text).ShouldBeLessThanOrEqualTo(limits.MaxFileBytes);
            }
        }

        [Fact]
        public async Task A_packages_file_of_exactly_the_most_it_may_be_is_written()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0");
            var one = Encoding.UTF8.GetByteCount(RegistryPackageWriter.Write((await Run(host)).After.Package(Crypto)!));

            (await Run(host, limits: RegistryLimits.Default with { MaxFileBytes = one })).After.Package(Crypto).ShouldNotBeNull();
            (await Run(host, limits: RegistryLimits.Default with { MaxFileBytes = one - 1 })).After.Package(Crypto).ShouldBeNull();
        }

        [Fact]
        public void A_file_of_the_registry_is_written_no_larger_than_its_reader_reads() =>
            RegistryLimits.Default.MaxFileBytes.ShouldBe(RegistryPackageReader.MaxBytes);

        [Fact]
        public async Task A_file_that_proves_to_be_over_the_limit_when_it_is_downloaded_is_refused_as_one_the_list_said_was()
        {
            var host = new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", new byte[PackageArchive.MaxBytes + 1]).Change(G, "crypto-v1.0.0", release => release.ListedSize = 10);

            await ShouldRefuse(host, "crypto-v1.0.0", DiagnosticCodes.ArchiveInvalid, "its file is larger than 16777216 bytes");
        }

        [Fact]
        public async Task A_file_whose_bytes_are_not_the_digest_GitHub_lists_is_refused_under_the_digest_that_was_listed()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0").Change(G, "crypto-v1.0.0", release => release.ListedDigest = Sample.ShaOf(RegistryFixture.OtherSha));

            var refused = await ShouldRefuse(host, "crypto-v1.0.0", DiagnosticCodes.ReleaseInvalid, "what GitHub serves for it does not have the digest that GitHub lists for it");

            refused.Sha256.ShouldBe(Sample.ShaOf(RegistryFixture.OtherSha));
        }

        [Fact]
        public async Task A_name_that_no_package_may_have_is_refused_and_its_file_is_not_downloaded()
        {
            var host = new ListedReleases().Release(G, "json-v1.0.0", "json-1.0.0.pmj.tar.gz", [1, 2, 3]);

            await ShouldRefuse(host, "json-v1.0.0", DiagnosticCodes.NameReserved, "the name json is one of the compiler's own packages");

            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_version_that_is_in_the_registry_from_one_repository_is_refused_from_another_and_is_not_downloaded()
        {
            var first = await RunOn(new ListedReleases().Package(Elsewhere, "@thatplatypus/crypto", "1.0.0"), [Elsewhere]);
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0");

            await ShouldRefuse(host, "crypto-v1.0.0", DiagnosticCodes.ReleaseInvalid, $"this version is already in the registry, from {Elsewhere}", first.After);

            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_release_that_was_refused_is_not_looked_at_again_while_it_holds_the_same_file()
        {
            var host = new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", [1, 2, 3]);
            var first = await Run(host);
            host.Downloads.Clear();

            var second = await Run(host, first.After, Later);

            host.Downloads.ShouldBeEmpty();
            second.Notes.ShouldBeEmpty();
            second.After.Files().ShouldBe(first.After.Files());
            second.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Releases.ShouldHaveSingleItem().Seen.ShouldBe(Now);
        }

        [Fact]
        public async Task A_refused_release_whose_file_is_replaced_by_a_good_one_is_taken_and_is_no_longer_refused()
        {
            var host = new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", [1, 2, 3]);
            var first = await Run(host);
            host.Change(G, "crypto-v1.0.0", release => release.Bytes = ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", G)));

            var second = await Run(host, first.After, Later);

            Only(second.After, "@thatplatypus/crypto").Indexed.ShouldBe(Later);
            second.After.Refused.ShouldBeEmpty();
            Said(second).ShouldBe([$"added @thatplatypus/crypto 1.0.0 from {G}"]);
        }

        [Fact]
        public async Task A_refused_release_whose_file_is_replaced_by_another_bad_one_is_refused_anew()
        {
            var host = new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", [1, 2, 3]);
            var first = await Run(host);
            host.Change(G, "crypto-v1.0.0", release => release.Bytes = [4, 5, 6]);

            var second = await Run(host, first.After, Later);

            var refused = second.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Releases.ShouldHaveSingleItem();
            refused.Sha256.ShouldBe(Sha256Digest.Of([4, 5, 6]));
            refused.Seen.ShouldBe(Later);
        }

        [Fact]
        public async Task A_refused_release_that_is_deleted_is_no_longer_refused_and_a_file_with_nothing_left_in_it_is_gone()
        {
            var host = new ListedReleases().Release(G, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", [1, 2, 3]).Release(G, "deflate-v1.0.0", "deflate-1.0.0.pmj.tar.gz", [1, 2, 3]);
            var first = await Run(host);

            var second = await Run(host.Delete(G, "crypto-v1.0.0"), first.After, Later);
            var third = await Run(host.Delete(G, "deflate-v1.0.0"), second.After, Later);

            second.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Releases.ShouldHaveSingleItem().Tag.ShouldBe("deflate-v1.0.0");
            Said(second).ShouldBe([$"no longer refused: crypto-v1.0.0 is gone from {G}"]);
            third.After.RefusedIn(Sample.Repository(G)).ShouldBeNull();
            third.After.Refused.ShouldBeEmpty();
            third.After.Files().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_version_whose_file_GitHub_lists_with_the_recorded_digest_is_not_downloaded()
        {
            var host = ThreePackages();
            var first = await Run(host);
            host.Downloads.Clear();

            await Run(host, first.After, Later);

            host.Listings.ShouldBe([G, G]);
            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_version_whose_bytes_have_changed_is_quarantined_and_nothing_else_recorded_of_it_changes()
        {
            var host = ThreePackages();
            var first = await Run(host);
            var changed = ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/crypto", "0.1.0", G, "@thatplatypus/deflate@0.1"));
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = changed);
            host.Downloads.Clear();

            var second = await Run(host, first.After, Later);

            var before = Only(first.After, "@thatplatypus/crypto");
            var after = Only(second.After, "@thatplatypus/crypto");
            after.Status.ShouldBe(VersionStatus.Quarantined);
            after.Quarantined.ShouldBe(new RegistryQuarantine(Later, Sha256Digest.Of(changed)));
            after.SameButForStatus(before).ShouldBeTrue();
            after.Sha256.ShouldBe(before.Sha256);
            after.Dependencies.ShouldBeEmpty();
            host.Downloads.ShouldBe([$"{G} crypto-v0.1.0"]);
            Said(second).ShouldBe([$"quarantined @thatplatypus/crypto 0.1.0: its file in {G} is no longer the bytes that were recorded"]);
            second.Notes.ShouldHaveSingleItem().Kind.ShouldBe(RegistryNoteKind.Quarantined);
            Only(second.After, "@thatplatypus/deflate").Status.ShouldBe(VersionStatus.Active);
        }

        // Such a file is not downloaded whole, so what its bytes are is GitHub's word. That they are
        // not the recorded ones needs nobody's word: nothing recorded is that large.
        [Fact]
        public async Task A_version_whose_file_is_replaced_by_one_too_large_to_be_an_archive_is_quarantined_with_the_digest_GitHub_lists()
        {
            var host = ThreePackages();
            var first = await Run(host);
            var huge = new byte[PackageArchive.MaxBytes + 1];
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = huge);

            var second = await Run(host, first.After, Later);

            var after = Only(second.After, "@thatplatypus/crypto");
            after.Status.ShouldBe(VersionStatus.Quarantined);
            after.Quarantined.ShouldBe(new RegistryQuarantine(Later, Sha256Digest.Of(huge)));
            after.SameButForStatus(Only(first.After, "@thatplatypus/crypto")).ShouldBeTrue();
        }

        [Fact]
        public async Task Looking_again_at_versions_whose_digest_has_changed_is_held_to_the_runs_limit_too()
        {
            var host = ThreePackages();
            var first = await Run(host);
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = [1, 2, 3]);
            host.Change(G, "deflate-v0.1.0", release => release.Bytes = [4, 5, 6]);
            host.Change(G, "grapevine-v0.1.0", release => release.Bytes = [7, 8, 9]);
            host.Downloads.Clear();
            var limits = new RegistryLimits(MaxDownloads: 2, MaxBytes: long.MaxValue);

            var second = await Run(host, first.After, Later, limits);
            var third = await Run(host, second.After, Later.AddHours(1), limits);

            second.After.Packages.Select(package => package.Versions[0].Status).ShouldBe([VersionStatus.Quarantined, VersionStatus.Quarantined, VersionStatus.Active]);
            Said(second).ShouldContain($"left for the next run: grapevine-v0.1.0 in {G}");
            third.After.Packages.ShouldAllBe(package => package.Versions[0].Status == VersionStatus.Quarantined);
            host.Downloads.Count.ShouldBe(3);
        }

        [Fact]
        public async Task A_run_counts_the_bytes_it_was_given_and_not_only_the_bytes_the_list_promised()
        {
            var host = ThreePackages();
            var crypto = host.Find(G, "crypto-v0.1.0").Bytes.Length;
            host.Change(G, "crypto-v0.1.0", release => release.ListedSize = 1);
            host.Change(G, "deflate-v0.1.0", release => release.ListedSize = 1);

            var result = await Run(host, limits: new RegistryLimits(MaxDownloads: 64, MaxBytes: crypto));

            result.After.Packages.Select(package => package.Name.ToString()).ShouldBe(["@thatplatypus/crypto"]);
            host.Downloads.ShouldBe([$"{G} crypto-v0.1.0"]);
        }

        [Fact]
        public async Task What_is_refused_without_a_download_is_refused_once_and_said_once()
        {
            var first = await RunOn(new ListedReleases().Package(Elsewhere, "@thatplatypus/crypto", "1.0.0"), [Elsewhere]);
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0").Release(G, "json-v1.0.0", "json-1.0.0.pmj.tar.gz", [1, 2, 3]);
            var second = await Run(host, first.After);

            var third = await Run(host, second.After, Later);

            second.Notes.Count.ShouldBe(2);
            third.Notes.ShouldBeEmpty();
            third.After.Files().ShouldBe(second.After.Files());
            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_version_is_not_quarantined_on_the_word_of_the_list_when_its_bytes_are_what_they_were()
        {
            var host = ThreePackages();
            var first = await Run(host);
            host.Change(G, "crypto-v0.1.0", release => release.ListedDigest = Sample.ShaOf(RegistryFixture.OtherSha));
            host.Downloads.Clear();

            var second = await Run(host, first.After, Later);

            host.Downloads.ShouldBe([$"{G} crypto-v0.1.0"]);
            second.After.Files().ShouldBe(first.After.Files());
            second.Notes.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_quarantined_version_stays_as_it_is_and_is_not_downloaded_again_even_when_its_first_bytes_come_back()
        {
            var host = ThreePackages();
            var good = host.Find(G, "crypto-v0.1.0").Bytes;
            var first = await Run(host);
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = [1, 2, 3]);
            var second = await Run(host, first.After, Later);
            host.Downloads.Clear();

            var third = await Run(host, second.After, Later.AddHours(1));
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = good);
            var fourth = await Run(host, third.After, Later.AddHours(2));

            host.Downloads.ShouldBeEmpty();
            third.Notes.ShouldBeEmpty();
            fourth.Notes.ShouldBeEmpty();
            fourth.After.Files().ShouldBe(second.After.Files());
            Only(fourth.After, "@thatplatypus/crypto").Quarantined.ShouldBe(new RegistryQuarantine(Later, Sha256Digest.Of([1, 2, 3])));
        }

        [Fact]
        public async Task A_yanked_version_whose_bytes_have_changed_is_quarantined_too()
        {
            var host = ThreePackages();
            var first = await Run(host);
            var yanked = new RegistryState(
                first.After.Packages.Select(package => package.Name == Crypto ? package with { Versions = [package.Versions[0] with { Status = VersionStatus.Yanked }] } : package),
                first.After.Refused);
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = [1, 2, 3]);

            var second = await Run(host, yanked, Later);

            Only(second.After, "@thatplatypus/crypto").Status.ShouldBe(VersionStatus.Quarantined);
        }

        [Fact]
        public async Task A_status_that_a_person_gave_a_version_is_kept()
        {
            var host = ThreePackages();
            var first = await Run(host);
            var yanked = new RegistryState(
                first.After.Packages.Select(package => package.Name == Crypto ? package with { Versions = [package.Versions[0] with { Status = VersionStatus.Yanked }] } : package),
                first.After.Refused);

            var second = await Run(host, yanked, Later);

            Only(second.After, "@thatplatypus/crypto").Status.ShouldBe(VersionStatus.Yanked);
            second.After.Files().ShouldBe(yanked.Files());
        }

        [Fact]
        public async Task A_version_whose_release_is_gone_stays_as_it_is_and_the_run_says_so()
        {
            var host = ThreePackages();
            var first = await Run(host);
            host.Delete(G, "crypto-v0.1.0");

            var second = await Run(host, first.After, Later);

            second.After.Files().ShouldBe(first.After.Files());
            Said(second).ShouldBe([$"gone: the release of @thatplatypus/crypto 0.1.0 is no longer in {G}"]);
            second.Notes.ShouldHaveSingleItem().Kind.ShouldBe(RegistryNoteKind.Gone);
        }

        [Fact]
        public async Task A_version_whose_file_is_gone_by_the_time_it_is_asked_for_stays_as_it_is()
        {
            var host = ThreePackages();
            var first = await Run(host);
            host.Change(G, "crypto-v0.1.0", release =>
            {
                release.ListedDigest = Sample.ShaOf(RegistryFixture.OtherSha);
                release.GoneWhenAsked = true;
            });

            var second = await Run(host, first.After, Later);

            second.After.Files().ShouldBe(first.After.Files());
        }

        [Fact]
        public async Task A_new_file_that_is_gone_by_the_time_it_is_asked_for_is_no_claim()
        {
            var host = new ListedReleases().Package(G, "@thatplatypus/crypto", "1.0.0").Change(G, "crypto-v1.0.0", release => release.GoneWhenAsked = true);

            var result = await Run(host);

            result.After.Files().ShouldBeEmpty();
            result.Notes.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_repository_that_cannot_be_listed_is_left_as_it_was_and_the_others_are_still_done()
        {
            var host = ThreePackages().Release(G, "deflate-v9.0.0", "deflate-9.0.0.pmj.tar.gz", [1, 2, 3]);
            var first = await Run(host);
            host.Package(Elsewhere, "@thatplatypus/crypto", "1.0.0").Package(G, "@thatplatypus/crypto", "0.2.0");
            host.Unlistable.Add(G);

            var second = await RunOn(host, [G, Elsewhere], first.After, Later);

            second.After.Package(Crypto).ShouldNotBeNull().Versions.Select(version => version.Version.ToString()).ShouldBe(["0.1.0", "1.0.0"]);
            second.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Releases.ShouldHaveSingleItem().Tag.ShouldBe("deflate-v9.0.0");
            Said(second).ShouldBe([$"added @thatplatypus/crypto 1.0.0 from {Elsewhere}", $"could not list the releases of {G} (github.unreachable)"]);
            second.Notes[1].Kind.ShouldBe(RegistryNoteKind.Unreachable);
        }

        [Fact]
        public async Task A_repository_that_is_taken_off_the_list_keeps_its_versions_and_is_asked_nothing()
        {
            var host = ThreePackages().Release(G, "deflate-v9.0.0", "deflate-9.0.0.pmj.tar.gz", [1, 2, 3]).Package(Elsewhere, "@thatplatypus/crypto", "1.0.0");
            var first = await RunOn(host, [G, Elsewhere]);
            host.Listings.Clear();
            host.Change(G, "crypto-v0.1.0", release => release.Bytes = [1, 2, 3]);

            var second = await RunOn(host, [Elsewhere], first.After, Later);

            host.Listings.ShouldBe([Elsewhere]);
            second.After.Packages.Select(package => package.Name.ToString()).ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/grapevine"]);
            second.After.Package(Crypto).ShouldNotBeNull().Versions.ShouldAllBe(version => version.Status == VersionStatus.Active);
            second.After.Refused.ShouldBeEmpty();
            second.Notes.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_run_downloads_no_more_files_than_its_limit_and_the_next_run_takes_up_where_it_stopped()
        {
            var host = ThreePackages();
            var limits = new RegistryLimits(MaxDownloads: 2, MaxBytes: long.MaxValue);

            var first = await Run(host, limits: limits);
            var second = await Run(host, first.After, Later, limits);

            first.After.Packages.Select(package => package.Name.ToString()).ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate"]);
            Said(first).ShouldBe(
            [
                $"added @thatplatypus/crypto 0.1.0 from {G}",
                $"added @thatplatypus/deflate 0.1.0 from {G}",
                $"left for the next run: grapevine-v0.1.0 in {G}",
            ]);
            first.Notes[2].Kind.ShouldBe(RegistryNoteKind.Left);
            second.After.Packages.Count.ShouldBe(3);
            host.Downloads.Count.ShouldBe(3);
        }

        // Otherwise the repository that comes first by name, with enough new files at every run,
        // would keep every repository after it waiting for ever, and anyone who is listed could be that one.
        [Fact]
        public async Task The_files_of_one_repository_do_not_keep_anothers_waiting_however_many_they_are()
        {
            const string many = "github.com/aaa/many";
            var host = new ListedReleases();
            foreach (var name in new[] { "one", "two", "three", "four", "five" })
            {
                host.Package(many, $"@aaa/{name}", "1.0.0");
            }

            host.Package(G, "@thatplatypus/crypto", "0.1.0");
            var limits = new RegistryLimits(MaxDownloads: 3, MaxBytes: long.MaxValue);

            var first = await RunOn(host, [many, G], limits: limits);
            var second = await RunOn(host, [many, G], first.After, Later, limits);

            first.After.Package(Crypto).ShouldNotBeNull();
            Said(first).ShouldBe(
            [
                $"added @aaa/five 1.0.0 from {many}",
                $"added @thatplatypus/crypto 0.1.0 from {G}",
                $"added @aaa/four 1.0.0 from {many}",
                $"left for the next run: one-v1.0.0 in {many}",
                $"left for the next run: three-v1.0.0 in {many}",
                $"left for the next run: two-v1.0.0 in {many}",
            ]);
            Said(second).ShouldBe([$"added @aaa/one 1.0.0 from {many}", $"added @aaa/three 1.0.0 from {many}", $"added @aaa/two 1.0.0 from {many}"]);
            host.Downloads.Count.ShouldBe(6);
        }

        [Fact]
        public async Task A_turn_is_one_file_downloaded_and_what_costs_no_download_ends_no_turn()
        {
            const string alpha = "github.com/thatplatypus/alpha";
            const string beta = "github.com/thatplatypus/beta";
            var host = new ListedReleases()
                .Package(alpha, "@thatplatypus/crypto", "0.1.0")
                .Package(alpha, "@thatplatypus/deflate", "0.1.0")
                .Package(beta, "@thatplatypus/grapevine", "0.1.0");
            var first = await RunOn(host, [alpha, beta]);
            host.Release(alpha, "json-v1.0.0", "json-1.0.0.pmj.tar.gz", [1, 2, 3])
                .Package(alpha, "@thatplatypus/sockets_more", "1.0.0")
                .Package(alpha, "@thatplatypus/words", "1.0.0")
                .Package(beta, "@thatplatypus/zebra", "1.0.0");
            host.Downloads.Clear();

            var second = await RunOn(host, [alpha, beta], first.After, Later);

            // Of alpha's first turn: two versions it knows by their digests and a name that is refused
            // unseen, none of which is a download, and then the one file that is.
            Said(second).ShouldBe(
            [
                $"refused json-v1.0.0 in {alpha}: the name json is one of the compiler's own packages",
                $"added @thatplatypus/sockets_more 1.0.0 from {alpha}",
                $"added @thatplatypus/zebra 1.0.0 from {beta}",
                $"added @thatplatypus/words 1.0.0 from {alpha}",
            ]);
            host.Downloads.Count.ShouldBe(3);
        }

        [Fact]
        public async Task With_nothing_left_of_the_limit_what_needs_no_file_is_still_done_in_every_repository()
        {
            const string alpha = "github.com/thatplatypus/alpha";
            const string beta = "github.com/thatplatypus/beta";
            var host = new ListedReleases()
                .Package(alpha, "@thatplatypus/crypto", "0.1.0")
                .Package(alpha, "@thatplatypus/deflate", "0.1.0")
                .Release(alpha, "json-v1.0.0", "json-1.0.0.pmj.tar.gz", [1, 2, 3])
                .Package(beta, "@thatplatypus/grapevine", "0.1.0")
                .Release(beta, "testtube-v1.0.0", "testtube-1.0.0.pmj.tar.gz", [1, 2, 3])
                .Package(beta, "@thatplatypus/zebra", "1.0.0");

            var result = await RunOn(host, [alpha, beta], limits: new RegistryLimits(MaxDownloads: 1, MaxBytes: long.MaxValue));

            Said(result).ShouldBe(
            [
                $"added @thatplatypus/crypto 0.1.0 from {alpha}",
                $"left for the next run: grapevine-v0.1.0 in {beta}",
                $"refused testtube-v1.0.0 in {beta}: the name testtube is one of the compiler's own packages",
                $"left for the next run: zebra-v1.0.0 in {beta}",
                $"left for the next run: deflate-v0.1.0 in {alpha}",
                $"refused json-v1.0.0 in {alpha}: the name json is one of the compiler's own packages",
            ]);
            result.After.Refused.Select(file => file.Repository.ToString()).ShouldBe([alpha, beta]);
        }

        [Fact]
        public async Task What_is_gone_from_a_repository_is_said_when_all_of_its_releases_have_been_looked_at()
        {
            const string alpha = "github.com/thatplatypus/alpha";
            const string beta = "github.com/thatplatypus/beta";
            var host = new ListedReleases()
                .Package(alpha, "@thatplatypus/crypto", "0.1.0")
                .Package(beta, "@thatplatypus/grapevine", "0.1.0");
            var first = await RunOn(host, [alpha, beta]);
            host.Delete(alpha, "crypto-v0.1.0")
                .Package(alpha, "@thatplatypus/deflate", "0.1.0")
                .Package(alpha, "@thatplatypus/words", "1.0.0")
                .Package(beta, "@thatplatypus/zebra", "1.0.0");

            var second = await RunOn(host, [alpha, beta], first.After, Later);

            Said(second).ShouldBe(
            [
                $"added @thatplatypus/deflate 0.1.0 from {alpha}",
                $"added @thatplatypus/zebra 1.0.0 from {beta}",
                $"added @thatplatypus/words 1.0.0 from {alpha}",
                $"gone: the release of @thatplatypus/crypto 0.1.0 is no longer in {alpha}",
            ]);
        }

        [Fact]
        public async Task A_run_downloads_no_more_bytes_than_its_limit_by_what_the_list_says_each_file_is()
        {
            var host = ThreePackages();
            var crypto = host.Find(G, "crypto-v0.1.0").Bytes.Length;
            var deflate = host.Find(G, "deflate-v0.1.0").Bytes.Length;

            var result = await Run(host, limits: new RegistryLimits(MaxDownloads: 64, MaxBytes: crypto + deflate));

            result.After.Packages.Select(package => package.Name.ToString()).ShouldBe(["@thatplatypus/crypto", "@thatplatypus/deflate"]);
            host.Downloads.Count.ShouldBe(2);
        }

        [Fact]
        public void A_run_downloads_at_most_64_files_and_256_mebibytes_when_it_is_told_nothing()
        {
            RegistryLimits.Default.MaxDownloads.ShouldBe(64);
            RegistryLimits.Default.MaxBytes.ShouldBe(256L * 1024 * 1024);
        }

        [Fact]
        public async Task A_file_that_cannot_be_downloaded_is_left_for_the_next_run_and_nothing_is_recorded_of_it()
        {
            var host = ThreePackages();
            host.DownloadsFail = true;

            var result = await Run(host);

            result.After.Files().ShouldBeEmpty();
            Said(result).ShouldBe(
            [
                $"could not download crypto-v0.1.0 from {G} (github.unreachable)",
                $"could not download deflate-v0.1.0 from {G} (github.unreachable)",
                $"could not download grapevine-v0.1.0 from {G} (github.unreachable)",
            ]);
            result.Notes.ShouldAllBe(note => note.Kind == RegistryNoteKind.Unreachable);
        }

        [Fact]
        public async Task The_moment_of_a_run_is_recorded_in_UTC_to_the_second()
        {
            var moment = new DateTimeOffset(2026, 10, 11, 17, 17, 40, 987, TimeSpan.FromHours(2));

            var result = await Run(ThreePackages().Release(G, "deflate-v9.0.0", "deflate-9.0.0.pmj.tar.gz", [1, 2, 3]), now: moment);

            Only(result.After, "@thatplatypus/crypto").Indexed.ShouldBe(Now);
            Only(result.After, "@thatplatypus/crypto").Indexed.Offset.ShouldBe(TimeSpan.Zero);
            result.After.RefusedIn(Sample.Repository(G)).ShouldNotBeNull().Releases.ShouldHaveSingleItem().Seen.ShouldBe(Now);
        }

        [Fact]
        public async Task Every_file_of_the_state_a_run_makes_reads_back_as_a_file_of_the_registry()
        {
            var result = await Run(ThreePackages().Release(G, "deflate-v9.0.0", "deflate-9.0.0.pmj.tar.gz", [1, 2, 3]));

            var files = result.After.Files();

            files.Keys.Order(StringComparer.Ordinal).ShouldBe(
            [
                "v1/packages/thatplatypus/crypto.json",
                "v1/packages/thatplatypus/deflate.json",
                "v1/packages/thatplatypus/grapevine.json",
                "v1/refused/thatplatypus/grapevine.json",
            ]);
            foreach (var (path, text) in files)
            {
                if (RegistryPaths.TryParsePackage(path, out var name))
                {
                    RegistryPackageWriter.Write(RegistryPackageReader.Read(System.Text.Encoding.UTF8.GetBytes(text), name, path).ShouldSucceed()).ShouldBe(text);
                }
                else
                {
                    RegistryPaths.TryParseRefused(path, out var repository).ShouldBeTrue();
                    RefusedReleasesWriter.Write(RefusedReleasesReader.Read(System.Text.Encoding.UTF8.GetBytes(text), repository!, path).ShouldSucceed()).ShouldBe(text);
                }
            }
        }
    }
}
