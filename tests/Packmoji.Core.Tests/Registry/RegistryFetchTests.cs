using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// Fetching what a registry said to use, and answering for what a lockfile holds from the cache.
    /// A registry says which bytes a version is, and the bytes are held to that before they are kept:
    /// a registry that is wrong, or that someone has changed, can then name a digest and no more.
    /// </summary>
    public sealed class RegistryFetchTests
    {
        private const string G = ListedReleases.Grapevine;

        private static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 17, 40, TimeSpan.Zero);
        private static readonly PackageName Grapevine = Sample.Name("@thatplatypus/grapevine");

        private static ListedReleases Released() => new ListedReleases()
            .Package(G, "@thatplatypus/crypto", "1.0.0")
            .Package(G, "@thatplatypus/deflate", "0.1.0")
            .Package(G, "@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

        private static async Task<RegistryState> IndexedAsync(ListedReleases host)
        {
            var run = await RegistryRun.RunAsync(RegistryState.Empty, [Sample.Repository(G)], host, Now, RegistryLimits.Default, TestContext.Current.CancellationToken);
            host.Downloads.Clear();
            host.Listings.Clear();
            return run.After;
        }

        private static async Task<PublishedVersion> RecordedAsync(RegistryState state, string name, string version) =>
            (await new RegistryPackageSource(new MemoryRegistryFiles().Hold(state)).FindAsync(Sample.Name(name), Sample.Version(version), TestContext.Current.CancellationToken)).ShouldNotBeNull();

        private static async Task<Diagnostic> ShouldStop(Func<Task> act, string code)
        {
            var thrown = await Should.ThrowAsync<PackageSourceException>(act);
            thrown.Diagnostic.ShouldBeComplete().Code.ShouldBe(code);
            return thrown.Diagnostic;
        }

        [Fact]
        public async Task What_the_cache_does_not_hold_is_downloaded_from_where_the_registry_says_and_kept_under_its_digest()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var store = new MemoryAssetStore();

            await RegistryFetch.FetchAsync(recorded, host, store, TestContext.Current.CancellationToken);

            host.Downloads.ShouldBe([$"{G} grapevine-v0.3.0"]);
            host.Listings.ShouldBeEmpty();
            store.Kept.ShouldBe([Sha256Digest.Of(host.Find(G, "grapevine-v0.3.0").Bytes).Hex]);
        }

        [Fact]
        public async Task What_the_cache_holds_is_not_downloaded_again()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var store = new MemoryAssetStore();
            await RegistryFetch.FetchAsync(recorded, host, store, TestContext.Current.CancellationToken);
            host.Downloads.Clear();

            await RegistryFetch.FetchAsync(recorded, host, store, TestContext.Current.CancellationToken);

            host.Downloads.ShouldBeEmpty();
        }

        // The bytes are the version's, wherever they came from. What the registry says the version
        // needs is still only what the registry says, until it has been held to them: with a full
        // cache nothing is downloaded, and a record that lies would otherwise never be found out.
        [Fact]
        public async Task What_the_cache_holds_is_held_to_the_record_as_what_is_downloaded_is()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var store = new MemoryAssetStore();
            await RegistryFetch.FetchAsync(recorded, host, store, TestContext.Current.CancellationToken);
            host.Downloads.Clear();
            var lied = recorded with { Dependencies = [new Dependency(Sample.Name("@thatplatypus/crypto"), Sample.Requirement("1.0"))] };

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(lied, host, store, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Reason.ShouldBe("the registry says it needs @thatplatypus/crypto@1.0, and the manifest in its archive says @thatplatypus/crypto@1.0, @thatplatypus/deflate@0.1");
            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task Bytes_in_the_cache_that_are_another_package_than_the_record_names_are_refused_though_their_digest_is_the_records()
        {
            var host = Released();
            var crypto = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/crypto", "1.0.0");
            var store = new MemoryAssetStore();
            await RegistryFetch.FetchAsync(crypto, host, store, TestContext.Current.CancellationToken);
            host.Downloads.Clear();
            var lied = crypto with { Name = Grapevine, Version = Sample.Version("9.0.0") };

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(lied, host, store, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Reason.ShouldBe("the manifest in its archive is that of \"@thatplatypus/crypto\"");
            host.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task What_the_cache_holds_under_the_digest_and_is_not_those_bytes_is_downloaded_again()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var store = new MemoryAssetStore();
            await store.KeepAsync(recorded.Name, recorded.Version, recorded.Sha256, "spoiled"u8.ToArray(), TestContext.Current.CancellationToken);

            await RegistryFetch.FetchAsync(recorded, host, store, TestContext.Current.CancellationToken);

            host.Downloads.ShouldBe([$"{G} grapevine-v0.3.0"]);
            var kept = await store.FindAsync(recorded.Name, recorded.Version, recorded.Sha256, TestContext.Current.CancellationToken);
            Sha256Digest.Of(kept.ShouldNotBeNull().Span).ShouldBe(recorded.Sha256);
        }

        [Fact]
        public async Task The_order_a_manifest_names_what_it_needs_in_is_no_difference_from_the_record()
        {
            var host = Released().Package(G, "@thatplatypus/grapevine", "0.4.0", "@thatplatypus/deflate@0.1", "@thatplatypus/crypto@1.0");
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.4.0");
            var store = new MemoryAssetStore();

            await RegistryFetch.FetchAsync(recorded with { Dependencies = recorded.Dependencies.Reverse().ToList() }, host, store, TestContext.Current.CancellationToken);
            await RegistryFetch.FetchAsync(recorded, host, new MemoryAssetStore(), TestContext.Current.CancellationToken);

            store.Kept.ShouldHaveSingleItem();
        }

        [Fact]
        public async Task A_record_that_names_what_a_version_needs_and_asks_another_version_of_it_is_refused()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var lied = recorded with
            {
                Dependencies = [new Dependency(Sample.Name("@thatplatypus/crypto"), Sample.Requirement("1.2")), new Dependency(Sample.Name("@thatplatypus/deflate"), Sample.Requirement("0.1"))],
            };
            var store = new MemoryAssetStore();

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(lied, host, store, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Reason.ShouldBe("the registry says it needs @thatplatypus/crypto@1.2, @thatplatypus/deflate@0.1, and the manifest in its archive says @thatplatypus/crypto@1.0, @thatplatypus/deflate@0.1");
            store.Kept.ShouldBeEmpty();
        }

        [Fact]
        public async Task Bytes_that_are_not_the_ones_the_registry_recorded_are_refused_and_are_not_kept()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var changed = ListedReleases.Archive(Sample.ManifestJson("@thatplatypus/grapevine", "0.3.0", G));
            host.Change(G, "grapevine-v0.3.0", release => release.Bytes = changed);
            var store = new MemoryAssetStore();

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(recorded, host, store, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Message.ShouldBe("What GitHub serves for \"@thatplatypus/grapevine\" 0.3.0 is not what the registry recorded.");
            diagnostic.Reason.ShouldContain(recorded.Sha256.Hex);
            diagnostic.Reason.ShouldContain(Sha256Digest.Of(changed).Hex);
            diagnostic.Reason.ShouldContain($"grapevine-v0.3.0 in {G}");
            store.Kept.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_release_that_is_no_longer_there_is_said_to_be_gone_and_is_not_taken_for_a_network_that_is_down()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            host.Delete(G, "grapevine-v0.3.0");

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(recorded, host, new MemoryAssetStore(), TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Reason.ShouldBe($"the release grapevine-v0.3.0 in {G}, which the registry recorded, is no longer there");
        }

        // The digest decides the bytes. What a registry says a version needs was read from those same
        // bytes, so a record that says otherwise was not made from them.
        [Fact]
        public async Task A_record_that_says_a_version_needs_other_than_its_own_manifest_says_is_refused_and_nothing_is_kept()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            var lied = recorded with { Dependencies = [new Dependency(Sample.Name("@thatplatypus/crypto"), Sample.Requirement("1.0"))] };
            var store = new MemoryAssetStore();

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(lied, host, store, TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Reason.ShouldBe("the registry says it needs @thatplatypus/crypto@1.0, and the manifest in its archive says @thatplatypus/crypto@1.0, @thatplatypus/deflate@0.1");
            store.Kept.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_record_of_bytes_that_are_not_that_version_of_that_package_is_refused()
        {
            var host = Released();
            var crypto = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/crypto", "1.0.0");
            var lied = crypto with { Name = Grapevine, Version = Sample.Version("9.0.0") };
            host.Release(G, "grapevine-v9.0.0", "grapevine-9.0.0.pmj.tar.gz", host.Find(G, "crypto-v1.0.0").Bytes);

            var diagnostic = await ShouldStop(() => RegistryFetch.FetchAsync(lied, host, new MemoryAssetStore(), TestContext.Current.CancellationToken), DiagnosticCodes.RegistryMismatch);

            diagnostic.Reason.ShouldBe("the manifest in its archive is that of \"@thatplatypus/crypto\"");
        }

        [Fact]
        public async Task A_GitHub_that_cannot_be_reached_is_said_to_be_that()
        {
            var host = Released();
            var recorded = await RecordedAsync(await IndexedAsync(host), "@thatplatypus/grapevine", "0.3.0");
            host.DownloadsFail = true;

            await ShouldStop(() => RegistryFetch.FetchAsync(recorded, host, new MemoryAssetStore(), TestContext.Current.CancellationToken), DiagnosticCodes.GitHubUnreachable);
        }

        private static async Task<(ListedReleases Host, MemoryRegistryFiles Files, Lockfile Lockfile, MemoryAssetStore Store)> LockedAsync()
        {
            var host = Released();
            var files = new MemoryRegistryFiles().Hold(await IndexedAsync(host));
            var manifest = Project.Named("@someone/app", ["@thatplatypus/grapevine@0.3"], []);
            var graph = (await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldSucceed();
            var store = new MemoryAssetStore();
            foreach (var package in graph.Packages)
            {
                await RegistryFetch.FetchAsync(package.Published, host, store, TestContext.Current.CancellationToken);
            }

            files.Asked.Clear();
            host.Downloads.Clear();
            return (host, files, graph.ToLockfile(manifest), store);
        }

        [Fact]
        public async Task What_a_lockfile_holds_and_the_cache_has_is_answered_for_with_nothing_asked_of_anyone()
        {
            var (host, files, lockfile, store) = await LockedAsync();

            var check = await LockCheck.CheckAsync(lockfile, new LockedFromCache(lockfile, store, new RegistryPackageSource(files)), TestContext.Current.CancellationToken);

            check.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
            files.Asked.ShouldBeEmpty();
            host.Downloads.ShouldBeEmpty();
            host.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task What_a_lockfile_holds_and_the_cache_does_not_have_is_asked_of_the_registry()
        {
            var (_, files, lockfile, _) = await LockedAsync();

            var check = await LockCheck.CheckAsync(lockfile, new LockedFromCache(lockfile, new MemoryAssetStore(), new RegistryPackageSource(files)), TestContext.Current.CancellationToken);

            check.ShouldSucceed();
            files.Asked.Order(StringComparer.Ordinal).ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/deflate.json", "v1/packages/thatplatypus/grapevine.json"]);
        }

        [Fact]
        public async Task What_the_cache_holds_under_a_locked_digest_and_is_not_those_bytes_does_not_answer_for_the_version()
        {
            var (_, files, lockfile, _) = await LockedAsync();
            var spoiled = new MemoryAssetStore();
            foreach (var package in lockfile.Packages)
            {
                await spoiled.KeepAsync(package.Name, package.Version, package.Sha256, "spoiled"u8.ToArray(), TestContext.Current.CancellationToken);
            }

            var check = await LockCheck.CheckAsync(lockfile, new LockedFromCache(lockfile, spoiled, new RegistryPackageSource(files)), TestContext.Current.CancellationToken);

            check.ShouldSucceed();
            files.Asked.Count.ShouldBe(3, "what is in the cache is not the bytes that were locked, so the registry is asked");
        }

        [Fact]
        public async Task A_version_the_lockfile_does_not_hold_is_asked_of_the_registry_though_the_cache_has_others()
        {
            var (_, files, lockfile, store) = await LockedAsync();
            var source = new LockedFromCache(lockfile, store, new RegistryPackageSource(files));

            (await source.FindAsync(Sample.Name("@thatplatypus/crypto"), Sample.Version("9.9.9"), TestContext.Current.CancellationToken)).ShouldBeNull();

            files.Asked.ShouldBe(["v1/packages/thatplatypus/crypto.json"]);
        }

        [Fact]
        public async Task A_quarantine_is_learned_of_when_the_registry_is_asked_and_not_from_the_cache()
        {
            var (_, files, lockfile, store) = await LockedAsync();
            var crypto = Sample.Name("@thatplatypus/crypto");
            var held = RegistryPackageReader.Read(await files.ReadAsync(RegistryPaths.Package(crypto), int.MaxValue, TestContext.Current.CancellationToken) ?? default, crypto, "crypto.json").ShouldSucceed();
            files.Put(RegistryPaths.Package(crypto), RegistryPackageWriter.Write(held with { Versions = [held.Versions[0].Quarantined()] }));
            files.Asked.Clear();

            var fromCache = await LockCheck.CheckAsync(lockfile, new LockedFromCache(lockfile, store, new RegistryPackageSource(files)), TestContext.Current.CancellationToken);
            var asked = await LockCheck.CheckAsync(lockfile, new RegistryPackageSource(files), TestContext.Current.CancellationToken);

            fromCache.ShouldSucceed();
            asked.Succeeded.ShouldBeFalse();
            asked.Diagnostics.ShouldContain(diagnostic => diagnostic.Code == DiagnosticCodes.ResolveQuarantined);
        }
    }
}
