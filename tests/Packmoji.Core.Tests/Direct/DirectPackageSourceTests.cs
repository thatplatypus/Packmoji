using System.Formats.Tar;
using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Direct
{
    /// <summary>
    /// Without a registry, a package version is a release of a repository its scope owns, and pmj has
    /// to work out which repository. These hold it to looking in the right places in the right order,
    /// and to taking a release only when the release says it is what was looked for.
    /// </summary>
    public sealed class DirectPackageSourceTests
    {
        private const string Shared = "github.com/thatplatypus/grapevine";

        private static readonly Manifest Outsider = Project.Named("@someone/app", [], []);

        // Grapevine as it is: three packages in one repository, two of them not in a repository of their own name.
        private static FakeReleaseHost Grapevine() => new FakeReleaseHost()
            .Release(Shared, "@thatplatypus/crypto", "1.0.0")
            .Release(Shared, "@thatplatypus/deflate", "0.1.0")
            .Release(Shared, "@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

        private static ValueTask<PublishedVersion?> Find(DirectPackageSource source, string name, string version) =>
            source.FindAsync(Sample.Name(name), Sample.Version(version), TestContext.Current.CancellationToken);

        private static async Task<Diagnostic> ShouldBeRefused(DirectPackageSource source, string name, string version)
        {
            var thrown = await Should.ThrowAsync<PackageSourceException>(async () => await Find(source, name, version));
            thrown.Diagnostic.ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.ReleaseInvalid);
            return thrown.Diagnostic;
        }

        [Fact]
        public async Task A_package_in_a_repository_of_its_own_name_is_found_there_and_kept()
        {
            var host = new FakeReleaseHost().Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0", "@thatplatypus/bytes@0.2");
            var store = new MemoryAssetStore();
            var source = new DirectPackageSource(host, store, Outsider, null);

            var published = (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();

            var archive = host.Archive("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            published.Name.ShouldBe(Sample.Name("@thatplatypus/crypto"));
            published.Version.ShouldBe(Sample.Version("1.0.0"));
            published.Status.ShouldBe(VersionStatus.Active);
            published.Verified.ShouldBe(VerificationLevel.Checksum);
            published.Source.ShouldBe(Sample.Repository("github.com/thatplatypus/crypto"));
            published.Sha256.ShouldBe(Sha256Digest.Of(archive));
            published.Dependencies.ShouldBe([Sample.Asks("@thatplatypus/bytes@0.2")]);
            store.Kept.ShouldBe([Sha256Digest.Of(archive).Hex]);
            host.Downloads.ShouldBe(["github.com/thatplatypus/crypto crypto-v1.0.0"]);
        }

        [Fact]
        public async Task A_version_with_no_release_is_not_there_and_where_pmj_looked_is_kept()
        {
            var host = new FakeReleaseHost().Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            (await Find(source, "@thatplatypus/crypto", "1.0.1")).ShouldBeNull();

            source.LookedIn(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.1")).ShouldBe([Sample.Repository("github.com/thatplatypus/crypto")]);
        }

        [Fact]
        public async Task A_package_beside_the_project_is_found_in_the_projects_own_repository()
        {
            // Grapevine's own manifest, resolving what it needs from the repository it is in.
            var project = Project.Named("@thatplatypus/grapevine", [], []);
            var host = Grapevine();
            var source = new DirectPackageSource(host, new MemoryAssetStore(), project, null);

            var crypto = (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();

            crypto.Source.ShouldBe(Sample.Repository(Shared));
            host.Downloads.ShouldBe(["github.com/thatplatypus/crypto crypto-v1.0.0", "github.com/thatplatypus/grapevine crypto-v1.0.0"]);
        }

        [Fact]
        public async Task A_package_beside_one_that_was_found_is_found_there_too()
        {
            var host = Grapevine();
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            var known = source.KnownRepositories;

            (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldBeNull();
            (await Find(source, "@thatplatypus/grapevine", "0.3.0")).ShouldNotBeNull();
            var crypto = (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();

            crypto.Source.ShouldBe(Sample.Repository(Shared));
            source.KnownRepositories.ShouldBe(known + 1);
        }

        [Fact]
        public async Task The_repository_of_another_owner_is_never_looked_in()
        {
            var host = Grapevine().Release("github.com/someone/tools", "@someone/tools", "1.0.0");
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            (await Find(source, "@someone/tools", "1.0.0")).ShouldNotBeNull();
            (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldBeNull();

            host.Downloads.ShouldNotContain(download => download.StartsWith("github.com/someone/") && download.Contains("crypto"));
            source.LookedIn(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0")).ShouldBe([Sample.Repository("github.com/thatplatypus/crypto")]);
        }

        [Fact]
        public async Task Where_the_lockfile_says_a_package_lives_is_looked_in_first()
        {
            var host = Grapevine().Release(Shared, "@thatplatypus/crypto", "1.1.0");
            var locked = new Lockfile(
                new RootRequirements([], []),
                [new LockedPackage(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), Sample.Repository(Shared), Sample.Sha('a'), VerificationLevel.Checksum, [])]);
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, locked);

            (await Find(source, "@thatplatypus/crypto", "1.1.0")).ShouldNotBeNull();

            host.Downloads.ShouldBe(["github.com/thatplatypus/grapevine crypto-v1.1.0"]);
        }

        [Fact]
        public async Task What_the_lockfile_pins_and_the_cache_holds_is_not_downloaded_again()
        {
            var host = Grapevine();
            var store = new MemoryAssetStore();
            var first = (await Find(new DirectPackageSource(host, store, Project.Named("@thatplatypus/grapevine", [], []), null), "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();
            var locked = new Lockfile(
                new RootRequirements([], []),
                [new LockedPackage(first.Name, first.Version, first.Source, first.Sha256, first.Verified, [])]);
            host.Downloads.Clear();

            var again = (await Find(new DirectPackageSource(host, store, Outsider, locked), "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();

            host.Downloads.ShouldBeEmpty();
            again.Sha256.ShouldBe(first.Sha256);
            again.Source.ShouldBe(first.Source);
        }

        [Fact]
        public async Task A_package_that_pmj_was_told_the_place_of_is_found_there()
        {
            var host = Grapevine();
            var told = new Dictionary<Packmoji.Core.Identity.PackageName, Packmoji.Core.Identity.RepositoryRef> { [Sample.Name("@thatplatypus/crypto")] = Sample.Repository(Shared) };
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null, told);

            (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull().Source.ShouldBe(Sample.Repository(Shared));

            host.Downloads.ShouldBe(["github.com/thatplatypus/grapevine crypto-v1.0.0"]);
        }

        [Fact]
        public async Task Where_the_lockfile_says_a_package_lives_comes_before_where_pmj_was_told()
        {
            const string elsewhere = "github.com/thatplatypus/elsewhere";
            var host = Grapevine().Release(elsewhere, "@thatplatypus/crypto", "1.0.0");
            var locked = new Lockfile(
                new RootRequirements([], []),
                [new LockedPackage(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), Sample.Repository(Shared), Sha256Digest.Of(host.Archive(Shared, "@thatplatypus/crypto", "1.0.0")), VerificationLevel.Checksum, [])]);
            var told = new Dictionary<Packmoji.Core.Identity.PackageName, Packmoji.Core.Identity.RepositoryRef> { [Sample.Name("@thatplatypus/crypto")] = Sample.Repository(elsewhere) };
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, locked, told);

            (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull().Source.ShouldBe(Sample.Repository(Shared));

            host.Downloads.ShouldBe(["github.com/thatplatypus/grapevine crypto-v1.0.0"]);
        }

        [Fact]
        public async Task A_repository_that_the_packages_scope_does_not_own_is_never_asked_whoever_names_it()
        {
            // The release is there, and is everything a release has to be but for whose repository it is in.
            const string theirs = "github.com/someone/grapevine";
            var host = new FakeReleaseHost().ReleaseWith(theirs, "@thatplatypus/crypto", "1.0.0", Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", "github.com/thatplatypus/crypto"));
            var locked = new Lockfile(
                new RootRequirements([], []),
                [new LockedPackage(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), Sample.Repository(theirs), Sample.Sha('a'), VerificationLevel.Checksum, [])]);
            var told = new Dictionary<Packmoji.Core.Identity.PackageName, Packmoji.Core.Identity.RepositoryRef> { [Sample.Name("@thatplatypus/crypto")] = Sample.Repository(theirs) };

            (await Find(new DirectPackageSource(host, new MemoryAssetStore(), Outsider, locked), "@thatplatypus/crypto", "1.0.0")).ShouldBeNull();
            (await Find(new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null, told), "@thatplatypus/crypto", "1.0.0")).ShouldBeNull();

            host.Downloads.ShouldBe(["github.com/thatplatypus/crypto crypto-v1.0.0", "github.com/thatplatypus/crypto crypto-v1.0.0"]);
        }

        [Fact]
        public async Task What_the_cache_holds_under_a_digest_is_used_only_if_it_is_those_bytes()
        {
            var host = Grapevine();
            var archive = host.Archive(Shared, "@thatplatypus/crypto", "1.0.0");
            var locked = new Lockfile(
                new RootRequirements([], []),
                [new LockedPackage(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), Sample.Repository(Shared), Sha256Digest.Of(archive), VerificationLevel.Checksum, [])]);
            var store = new MemoryAssetStore();

            // Another package's archive, kept where this one's should be: a cache that was spoiled.
            await store.KeepAsync(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), Sha256Digest.Of(archive), host.Archive(Shared, "@thatplatypus/deflate", "0.1.0"), TestContext.Current.CancellationToken);

            var found = (await Find(new DirectPackageSource(host, store, Outsider, locked), "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();

            found.Name.ShouldBe(Sample.Name("@thatplatypus/crypto"));
            found.Sha256.ShouldBe(Sha256Digest.Of(archive));
            host.Downloads.ShouldBe(["github.com/thatplatypus/grapevine crypto-v1.0.0"]);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_release_that_is_not_the_bytes_the_lockfile_holds_is_refused_unread_and_is_not_kept(bool anArchive)
        {
            // The lockfile holds the digest of what crypto 1.0.0 was. What stands there now is
            // another package's archive, or nothing that is an archive at all.
            var host = Grapevine();
            var was = Sha256Digest.Of(host.Archive(Shared, "@thatplatypus/crypto", "1.0.0"));
            var now = anArchive ? host.Archive(Shared, "@thatplatypus/deflate", "0.1.0") : Encoding.UTF8.GetBytes("no archive");
            host.Upload(Shared, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", now);
            var locked = new Lockfile(
                new RootRequirements([], []),
                [new LockedPackage(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), Sample.Repository(Shared), was, VerificationLevel.Checksum, [])]);
            var store = new MemoryAssetStore();

            var thrown = await Should.ThrowAsync<PackageSourceException>(async () => await Find(new DirectPackageSource(host, store, Outsider, locked), "@thatplatypus/crypto", "1.0.0"));

            var diagnostic = thrown.Diagnostic.ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.LockMismatch);
            diagnostic.Message.ShouldBe("packmoji.lock does not agree with what is published for \"@thatplatypus/crypto\" 1.0.0.");
            diagnostic.Reason.ShouldContain($"the lockfile has the digest {was} and the release in {Shared} has {Sha256Digest.Of(now)}");
            diagnostic.Fix.ShouldNotContain("delete");
            store.Kept.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_repository_that_pmj_was_told_to_look_in_as_well_is_looked_in_for_every_package_of_its_owner_and_for_no_other()
        {
            var host = Grapevine().Release("github.com/someone/tools", "@someone/tools", "1.0.0");
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null, alsoLookIn: [Sample.Repository(Shared)]);

            (await Find(source, "@thatplatypus/deflate", "0.1.0")).ShouldNotBeNull().Source.ShouldBe(Sample.Repository(Shared));
            (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull().Source.ShouldBe(Sample.Repository(Shared));
            (await Find(source, "@someone/tools", "1.0.0")).ShouldNotBeNull();
            (await Find(source, "@someone/other", "1.0.0")).ShouldBeNull();

            // A repository of its own name comes first, and the one that was named after it.
            host.Downloads.Take(2).ShouldBe(["github.com/thatplatypus/deflate deflate-v0.1.0", "github.com/thatplatypus/grapevine deflate-v0.1.0"]);
            host.Downloads.ShouldNotContain(download => download.StartsWith("github.com/thatplatypus/", StringComparison.Ordinal) && download.Contains("tools", StringComparison.Ordinal));
            host.Downloads.ShouldNotContain(download => download.StartsWith("github.com/thatplatypus/", StringComparison.Ordinal) && download.Contains("other", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Each_version_is_downloaded_once_however_often_it_is_asked_for()
        {
            var host = new FakeReleaseHost().Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            var first = await Find(source, "@thatplatypus/crypto", "1.0.0");
            var second = await Find(source, "@thatplatypus/crypto", "1.0.0");
            await Find(source, "@thatplatypus/crypto", "9.9.9");
            await Find(source, "@thatplatypus/crypto", "9.9.9");

            second.ShouldBeSameAs(first);
            host.Downloads.Count.ShouldBe(2);
        }

        [Fact]
        public async Task A_release_whose_manifest_names_another_package_is_refused()
        {
            var host = new FakeReleaseHost().Upload(
                "github.com/thatplatypus/crypto", "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz",
                new FakeReleaseHost().ReleaseWith("github.com/thatplatypus/crypto", "@thatplatypus/deflate", "1.0.0", Sample.ManifestJson("@thatplatypus/deflate", "1.0.0", "github.com/thatplatypus/deflate"))
                    .Archive("github.com/thatplatypus/crypto", "@thatplatypus/deflate", "1.0.0"));
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            var diagnostic = await ShouldBeRefused(source, "@thatplatypus/crypto", "1.0.0");

            diagnostic.Message.ShouldBe("The release crypto-v1.0.0 in github.com/thatplatypus/crypto is not \"@thatplatypus/crypto\" 1.0.0.");
            diagnostic.Reason.ShouldContain("\"@thatplatypus/deflate\"");
        }

        [Fact]
        public async Task A_release_whose_manifest_gives_another_version_is_refused()
        {
            var host = new FakeReleaseHost().ReleaseWith("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0", Sample.ManifestJson("@thatplatypus/crypto", "1.0.1", "github.com/thatplatypus/crypto"));
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            (await ShouldBeRefused(source, "@thatplatypus/crypto", "1.0.0")).Reason.ShouldContain("1.0.1");
        }

        [Fact]
        public async Task A_release_whose_manifest_says_the_package_lives_elsewhere_is_refused()
        {
            // The manifest gives no repository, so it says github.com/thatplatypus/crypto, and this is another.
            var host = new FakeReleaseHost().ReleaseWith(Shared, "@thatplatypus/crypto", "1.0.0", Sample.ManifestJson("@thatplatypus/crypto", "1.0.0", "github.com/thatplatypus/crypto"));
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Project.Named("@thatplatypus/grapevine", [], []), null);

            var diagnostic = await ShouldBeRefused(source, "@thatplatypus/crypto", "1.0.0");

            diagnostic.Reason.ShouldContain("github.com/thatplatypus/crypto");
            diagnostic.Fix.ShouldContain("\"repository\"");
        }

        [Fact]
        public async Task A_release_whose_archive_pmj_did_not_write_is_refused_and_nothing_is_kept()
        {
            using var tar = new MemoryStream();
            using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
            {
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "packmoji.json") { DataStream = new MemoryStream(Encoding.UTF8.GetBytes("{}")) });
            }

            var store = new MemoryAssetStore();
            var host = new FakeReleaseHost().Upload("github.com/thatplatypus/crypto", "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", RawTar.Gzip(tar.ToArray()));
            var source = new DirectPackageSource(host, store, Outsider, null);

            (await ShouldBeRefused(source, "@thatplatypus/crypto", "1.0.0")).Reason.ShouldContain("pmj pack");

            store.Kept.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_release_whose_manifest_cannot_be_read_is_refused_with_what_is_wrong_with_it()
        {
            var host = new FakeReleaseHost().ReleaseWith("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0", "{ \"package\": { \"name\": \"Crypto\" } }");
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            (await ShouldBeRefused(source, "@thatplatypus/crypto", "1.0.0")).Reason.ShouldContain("packmoji.json");
        }

        [Fact]
        public async Task A_host_that_cannot_be_reached_is_not_taken_for_a_release_that_is_not_there()
        {
            var host = Grapevine();
            host.Down = true;
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Outsider, null);

            var thrown = await Should.ThrowAsync<PackageSourceException>(async () => await Find(source, "@thatplatypus/grapevine", "0.3.0"));

            thrown.Diagnostic.Code.ShouldBe(DiagnosticCodes.GitHubUnreachable);
        }

        [Fact]
        public async Task The_versions_of_a_package_are_listed_from_the_first_place_that_has_any()
        {
            var host = Grapevine().Release(Shared, "@thatplatypus/crypto", "1.2.0").Release(Shared, "@thatplatypus/crypto", "2.0.0-rc.1");
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Project.Named("@thatplatypus/grapevine", [], []), null);

            var listed = (await source.ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken)).ShouldNotBeNull();

            listed.Repository.ShouldBe(Sample.Repository(Shared));
            listed.Versions.Select(version => version.ToString()).ShouldBe(["1.0.0", "1.2.0", "2.0.0-rc.1"]);
            host.Listings.ShouldBe(["github.com/thatplatypus/crypto", "github.com/thatplatypus/grapevine"]);
        }

        [Fact]
        public async Task A_repository_is_asked_for_its_releases_once_however_many_of_its_packages_are_listed()
        {
            var host = Grapevine();
            var source = new DirectPackageSource(host, new MemoryAssetStore(), Project.Named("@thatplatypus/grapevine", [], []), null);

            foreach (var name in new[] { "@thatplatypus/grapevine", "@thatplatypus/crypto", "@thatplatypus/deflate", "@thatplatypus/crypto" })
            {
                (await source.ListVersionsAsync(Sample.Name(name), TestContext.Current.CancellationToken)).ShouldNotBeNull();
            }

            // The API answers sixty times an hour to someone it does not know, so nothing is asked twice.
            host.Listings.ShouldBe(["github.com/thatplatypus/grapevine", "github.com/thatplatypus/crypto", "github.com/thatplatypus/deflate"]);
        }

        [Fact]
        public async Task A_package_with_no_release_anywhere_pmj_looks_has_no_versions()
        {
            var source = new DirectPackageSource(Grapevine(), new MemoryAssetStore(), Outsider, null);

            (await source.ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken)).ShouldBeNull();

            source.LookedIn(Sample.Name("@thatplatypus/crypto")).ShouldBe([Sample.Repository("github.com/thatplatypus/crypto")]);
        }
    }
}
