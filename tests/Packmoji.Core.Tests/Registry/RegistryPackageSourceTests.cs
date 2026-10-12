using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;
using static Packmoji.Core.Tests.Registry.RegistrySample;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// What a resolution is told when it asks a registry. Every version asked about is answered from
    /// its package's file, which is read once, and nothing is downloaded to find out what a version
    /// needs. A registry that cannot be read is never taken for a package that is not there.
    /// </summary>
    public sealed class RegistryPackageSourceTests
    {
        private const string G = ListedReleases.Grapevine;

        private static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 17, 40, TimeSpan.Zero);

        private static ListedReleases Released() => new ListedReleases()
            .Package(G, "@thatplatypus/crypto", "1.0.0")
            .Package(G, "@thatplatypus/crypto", "1.2.0")
            .Package(G, "@thatplatypus/crypto", "2.0.0-rc.1")
            .Package(G, "@thatplatypus/deflate", "0.1.0")
            .Package(G, "@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

        /// <summary>The registry a run makes of what is released, as files.</summary>
        private static async Task<MemoryRegistryFiles> IndexedAsync(ListedReleases host)
        {
            var run = await RegistryRun.RunAsync(RegistryState.Empty, [Sample.Repository(G)], host, Now, RegistryLimits.Default, TestContext.Current.CancellationToken);
            run.After.Refused.ShouldBeEmpty();
            return new MemoryRegistryFiles().Hold(run.After);
        }

        private static ValueTask<PublishedVersion?> Find(RegistryPackageSource source, string name, string version) =>
            source.FindAsync(Sample.Name(name), Sample.Version(version), TestContext.Current.CancellationToken);

        private static async Task<Diagnostic> ShouldStop(Func<Task> act, string code)
        {
            var thrown = await Should.ThrowAsync<PackageSourceException>(act);
            thrown.Diagnostic.ShouldBeComplete().Code.ShouldBe(code);
            return thrown.Diagnostic;
        }

        [Fact]
        public async Task A_version_is_answered_from_its_packages_file_with_everything_a_resolution_needs()
        {
            var host = Released();
            var files = await IndexedAsync(host);

            var published = (await Find(new RegistryPackageSource(files), "@thatplatypus/grapevine", "0.3.0")).ShouldNotBeNull();

            published.Name.ShouldBe(Sample.Name("@thatplatypus/grapevine"));
            published.Version.ShouldBe(Sample.Version("0.3.0"));
            published.Status.ShouldBe(VersionStatus.Active);
            published.Source.ShouldBe(Sample.Repository(G));
            published.Sha256.ShouldBe(Sha256Digest.Of(host.Find(G, "grapevine-v0.3.0").Bytes));
            published.Verified.ShouldBe(VerificationLevel.Checksum);
            published.Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Requirement.Text}").Order(StringComparer.Ordinal)
                .ShouldBe(["@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1"]);
            files.Asked.ShouldBe(["v1/packages/thatplatypus/grapevine.json"]);
            host.Downloads.Count.ShouldBe(5, "nothing was downloaded but by the run that made the registry");
        }

        [Fact]
        public async Task A_packages_file_is_read_once_however_many_of_its_versions_are_asked_about()
        {
            var files = await IndexedAsync(Released());
            var source = new RegistryPackageSource(files);

            (await Find(source, "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull();
            (await Find(source, "@thatplatypus/crypto", "1.2.0")).ShouldNotBeNull();
            (await Find(source, "@thatplatypus/crypto", "9.9.9")).ShouldBeNull();
            await source.ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken);

            files.Asked.ShouldBe(["v1/packages/thatplatypus/crypto.json"]);
        }

        [Fact]
        public async Task A_version_or_a_package_that_the_registry_does_not_have_is_no_version()
        {
            var files = await IndexedAsync(Released());
            var source = new RegistryPackageSource(files);

            (await Find(source, "@thatplatypus/crypto", "1.1.0")).ShouldBeNull();
            (await Find(source, "@thatplatypus/sockets_more", "1.0.0")).ShouldBeNull();
            (await Find(source, "@thatplatypus/sockets_more", "2.0.0")).ShouldBeNull();
            files.Asked.ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/sockets_more.json"]);
        }

        [Theory]
        [InlineData(VersionStatus.Yanked)]
        [InlineData(VersionStatus.Quarantined)]
        public async Task A_versions_status_is_what_the_registry_says_it_is(VersionStatus status)
        {
            var held = status == VersionStatus.Quarantined ? Version("1.0.0").Quarantined() : Version("1.0.0") with { Status = status };
            var files = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", held)));

            (await Find(new RegistryPackageSource(files), "@thatplatypus/crypto", "1.0.0")).ShouldNotBeNull().Status.ShouldBe(status);
        }

        // How far a version is verified is what whoever uses it has done, and pmj holds bytes to a
        // digest. What a registry says of an attestation is for a pmj that can check one: until
        // then a lockfile that said so would say what nobody did.
        [Fact]
        public async Task A_version_a_registry_calls_attested_is_verified_as_far_as_pmj_verifies_which_is_by_its_checksum()
        {
            var files = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", Version("1.0.0") with { Verified = VerificationLevel.Attestation })));
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0"], []);

            var graph = (await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldSucceed();

            graph.Packages.ShouldHaveSingleItem().Published.Verified.ShouldBe(VerificationLevel.Checksum);
            LockfileWriter.Write(graph.ToLockfile(manifest)).ShouldContain("\"verified\": \"checksum\"");
            LockfileWriter.Write(graph.ToLockfile(manifest)).ShouldNotContain("attestation");
        }

        [Fact]
        public async Task The_versions_of_a_package_are_those_that_may_still_be_begun_with_lowest_first()
        {
            var files = new MemoryRegistryFiles().Hold(State(Package(
                "@thatplatypus/crypto",
                Version("1.10.0"),
                Version("1.2.0"),
                Version("1.3.0") with { Status = VersionStatus.Yanked },
                Version("1.4.0").Quarantined(),
                Version("2.0.0-rc.1"))));
            var source = new RegistryPackageSource(files);

            var versions = await source.ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken);

            versions.ShouldNotBeNull().Select(version => version.ToString()).ShouldBe(["1.2.0", "1.10.0", "2.0.0-rc.1"]);
            (await source.ListVersionsAsync(Sample.Name("@thatplatypus/deflate"), TestContext.Current.CancellationToken)).ShouldBeNull();
        }

        [Fact]
        public async Task A_package_whose_versions_have_all_been_withdrawn_is_there_and_has_none_to_begin_with()
        {
            var files = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", Version("1.0.0") with { Status = VersionStatus.Yanked })));

            (await new RegistryPackageSource(files).ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken)).ShouldNotBeNull().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_that_does_not_read_stops_whatever_asked_and_says_which_file_and_what_is_wrong_with_it()
        {
            var files = new MemoryRegistryFiles().Put("v1/packages/thatplatypus/crypto.json", RegistryFixture.Package(RegistryFixture.Version().With("status", "\"fine\"")).ToString());
            var source = new RegistryPackageSource(files);

            var diagnostic = await ShouldStop(async () => await Find(source, "@thatplatypus/crypto", "1.0.0"), DiagnosticCodes.RegistryInvalid);

            diagnostic.Message.ShouldBe("The registry's file of \"@thatplatypus/crypto\" cannot be read.");
            diagnostic.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/crypto.json does not read: \"fine\" is not a status.");
            diagnostic.Fix.ShouldContain("--direct");
            await ShouldStop(() => source.ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken), DiagnosticCodes.RegistryInvalid);
            files.Asked.Count.ShouldBe(2, "a file that did not read is asked for again, and is not remembered as read");
        }

        [Fact]
        public async Task A_file_that_is_another_packages_at_this_packages_path_is_refused()
        {
            var files = new MemoryRegistryFiles().Put("v1/packages/thatplatypus/crypto.json", RegistryPackageWriter.Write(Package("@thatplatypus/deflate", Version("1.0.0"))));

            var diagnostic = await ShouldStop(async () => await Find(new RegistryPackageSource(files), "@thatplatypus/crypto", "1.0.0"), DiagnosticCodes.RegistryInvalid);

            diagnostic.Reason.ShouldContain("The file is that of \"@thatplatypus/deflate\".");
        }

        [Fact]
        public async Task A_registry_that_cannot_be_reached_is_said_to_be_that_and_is_not_taken_for_a_package_that_is_not_there()
        {
            var files = await IndexedAsync(Released());
            files.Down = true;
            var source = new RegistryPackageSource(files);

            await ShouldStop(async () => await Find(source, "@thatplatypus/crypto", "1.0.0"), DiagnosticCodes.RegistryUnreachable);
            await ShouldStop(() => source.ListVersionsAsync(Sample.Name("@thatplatypus/crypto"), TestContext.Current.CancellationToken), DiagnosticCodes.RegistryUnreachable);
        }

        [Fact]
        public async Task A_resolution_through_a_registry_chooses_what_one_on_GitHub_itself_chooses_and_locks_the_same_bytes()
        {
            var host = Released();
            var files = await IndexedAsync(host);
            var manifest = Project.Named("@someone/app", ["@thatplatypus/grapevine@0.3"], []);
            host.Downloads.Clear();
            host.Listings.Clear();

            var through = (await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldSucceed();
            host.Downloads.ShouldBeEmpty();
            host.Listings.ShouldBeEmpty();
            var direct = (await DirectResolver.ResolveAsync(manifest, null, new DirectPackageSource(host, new MemoryAssetStore(), manifest, null), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldSucceed();

            through.Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
            LockfileWriter.Write(through.ToLockfile(manifest)).ShouldBe(LockfileWriter.Write(direct.ToLockfile(manifest)));
            files.Asked.Order(StringComparer.Ordinal).ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/deflate.json", "v1/packages/thatplatypus/grapevine.json"]);
        }

        [Fact]
        public async Task Of_a_package_that_is_not_in_the_registry_a_resolution_says_so_with_where_the_registry_is_and_how_to_ask_GitHub_itself()
        {
            var files = await IndexedAsync(Released());
            var manifest = Project.Named("@someone/app", ["@thatplatypus/sockets_more@1.0"], []);

            var result = await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            var notFound = result.ShouldFailWith(DiagnosticCodes.PackageNotFound);
            notFound.Message.ShouldBe("\"@thatplatypus/sockets_more\" is not in the registry.");
            notFound.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/sockets_more.json is not there; it is asked for: @someone/app → @thatplatypus/sockets_more@1.0");
            notFound.Fix.ShouldBe("check that https://registry.example is the address a registry's v1 is under, for one that is not answers the same way; if it is, and the package's repository is on the registry's list, a release is in the registry within a day; to ask GitHub itself, run the command again with --direct");
            notFound.Fix.ShouldContain("--direct");
        }

        [Fact]
        public async Task Of_a_version_that_is_not_in_the_registry_though_its_package_is_a_resolution_says_that_and_not_that_it_was_never_published()
        {
            // A release may be newer than the last run, or may have been refused: that the registry
            // does not hold a version is all a registry can say of it.
            var files = await IndexedAsync(Released());
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.1"], []);

            var result = await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            var missing = result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).ShouldBeComplete();
            missing.Message.ShouldBe("Version 1.1.0 of \"@thatplatypus/crypto\" is not in the registry.");
            missing.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/crypto.json holds 3 versions of it, and this is not one of them; it is asked for: @someone/app → @thatplatypus/crypto@1.1");
            missing.Fix.ShouldBe("ask for a version the registry holds; a release is in the registry within a day of being made, unless the registry refused it, which https://registry.example/v1/refused/thatplatypus/grapevine.json says; to ask GitHub itself, run the command again with --direct");
        }

        [Fact]
        public async Task Of_a_package_with_one_version_the_count_is_said_as_one()
        {
            var files = await IndexedAsync(Released());
            var manifest = Project.Named("@someone/app", ["@thatplatypus/deflate@0.2"], []);

            var result = await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).Reason.ShouldStartWith("https://registry.example/v1/packages/thatplatypus/deflate.json holds 1 version of it, and this is not one of them; ");
        }

        // A source with no registry behind it, told of the one repository all of these packages share.
        private static DirectPackageSource OnGitHubItself(ListedReleases host, Manifest manifest, Lockfile? lockfile) =>
            new(host, new MemoryAssetStore(), manifest, lockfile, alsoLookIn: [Sample.Repository(G)]);

        // A lockfile written with no registry behind it, for a project that asks for one package at one version.
        private static async Task<(Manifest Manifest, Lockfile Lockfile)> LockedOnGitHubItselfAsync(string name, string version)
        {
            var host = Released().Package(G, name, version);
            var manifest = Project.Named("@someone/app", [$"{name}@{version}"], []);
            var graph = (await DirectResolver.ResolveAsync(manifest, null, OnGitHubItself(host, manifest, null), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldSucceed();
            return (manifest, graph.ToLockfile(manifest));
        }

        [Fact]
        public async Task Of_a_version_that_a_lockfile_holds_and_the_registry_does_not_a_resolution_says_that_and_not_that_the_version_is_gone()
        {
            // The registry never held it, so nothing has gone: the lockfile was written without this registry.
            var (manifest, lockfile) = await LockedOnGitHubItselfAsync("@thatplatypus/crypto", "1.1.0");
            var files = await IndexedAsync(Released());

            var result = await Resolver.ResolveAsync(manifest, lockfile, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            var missing = result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).ShouldBeComplete();
            missing.Message.ShouldBe("Version 1.1.0 of \"@thatplatypus/crypto\" is not in the registry.");
            missing.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/crypto.json holds 3 versions of it, and this is not one of them; packmoji.lock holds it, and it is asked for: @someone/app → @thatplatypus/crypto@1.1.0");
            missing.Fix.ShouldBe("keep packmoji.lock: to use what it holds, ask GitHub itself, by running the command again with --direct; to use what the registry holds, ask for one of its versions in packmoji.json");
        }

        [Fact]
        public async Task Of_a_package_that_a_lockfile_holds_and_the_registry_does_not_have_a_resolution_says_which_version_the_lockfile_holds()
        {
            var (manifest, lockfile) = await LockedOnGitHubItselfAsync("@thatplatypus/sockets_more", "1.0.0");
            var files = await IndexedAsync(Released());

            var result = await Resolver.ResolveAsync(manifest, lockfile, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            var notFound = result.ShouldFailWith(DiagnosticCodes.PackageNotFound).ShouldBeComplete();
            notFound.Message.ShouldBe("\"@thatplatypus/sockets_more\" is not in the registry.");
            notFound.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/sockets_more.json is not there; packmoji.lock holds 1.0.0 of it, and it is asked for: @someone/app → @thatplatypus/sockets_more@1.0.0");
            notFound.Fix.ShouldContain("--direct");
        }

        [Theory]
        [InlineData("@thatplatypus/crypto", "1.1.0", DiagnosticCodes.ResolveVersionMissing, "https://registry.example/v1/packages/thatplatypus/crypto.json holds 3 versions of it, and this is not one of them; packmoji.lock holds it")]
        [InlineData("@thatplatypus/sockets_more", "1.0.0", DiagnosticCodes.PackageNotFound, "https://registry.example/v1/packages/thatplatypus/sockets_more.json is not there; packmoji.lock holds 1.0.0 of it")]
        public async Task A_lockfile_that_is_held_to_a_registry_which_lacks_what_it_holds_is_told_so_whether_or_not_the_cache_is_asked_first(string name, string version, string code, string reason)
        {
            var (_, lockfile) = await LockedOnGitHubItselfAsync(name, version);
            var files = await IndexedAsync(Released());

            var asked = await LockCheck.CheckAsync(lockfile, new RegistryPackageSource(files), TestContext.Current.CancellationToken);
            var throughTheCache = await LockCheck.CheckAsync(lockfile, new LockedFromCache(lockfile, new MemoryAssetStore(), new RegistryPackageSource(files)), TestContext.Current.CancellationToken);

            asked.ShouldFailWith(code).ShouldBeComplete().Reason.ShouldBe(reason);
            throughTheCache.ShouldFailWith(code).Reason.ShouldBe(reason);
        }

        [Fact]
        public async Task What_a_registry_says_of_a_version_it_lacks_is_said_through_a_source_that_asks_the_cache_first()
        {
            var files = await IndexedAsync(Released());
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.1"], []);
            var nothingLocked = new Lockfile(RootRequirements.From(manifest), []);

            var result = await Resolver.ResolveAsync(manifest, null, new LockedFromCache(nothingLocked, new MemoryAssetStore(), new RegistryPackageSource(files)), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).Message.ShouldBe("Version 1.1.0 of \"@thatplatypus/crypto\" is not in the registry.");
        }

        [Fact]
        public async Task With_no_registry_a_version_that_the_lockfile_holds_and_is_gone_is_still_said_to_be_no_longer_published()
        {
            var (manifest, lockfile) = await LockedOnGitHubItselfAsync("@thatplatypus/crypto", "1.1.0");
            var without = Released();

            var resolved = await DirectResolver.ResolveAsync(manifest, lockfile, OnGitHubItself(without, manifest, lockfile), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);
            var checkedAgain = await LockCheck.CheckAsync(lockfile, OnGitHubItself(without, manifest, lockfile), TestContext.Current.CancellationToken);

            resolved.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).Message.ShouldBe("Version 1.1.0 of \"@thatplatypus/crypto\" is no longer published.");
            checkedAgain.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).Message.ShouldBe("Version 1.1.0 of \"@thatplatypus/crypto\" is no longer published.");
        }

        [Fact]
        public async Task A_version_that_was_withdrawn_or_quarantined_stops_a_new_resolution_as_the_resolver_has_it()
        {
            var yanked = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", Version("1.0.0") with { Status = VersionStatus.Yanked })));
            var quarantined = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", Version("1.0.0").Quarantined())));
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0"], []);

            (await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(yanked), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldFailWith(DiagnosticCodes.ResolveYanked);
            (await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(quarantined), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken)).ShouldFailWith(DiagnosticCodes.ResolveQuarantined);
        }

        [Fact]
        public async Task Of_a_package_whose_file_holds_no_version_nothing_is_said_of_where_a_refusal_would_be()
        {
            var files = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto")));
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0"], []);

            var result = await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            var missing = result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).ShouldBeComplete();
            missing.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/crypto.json holds no version of it; it is asked for: @someone/app → @thatplatypus/crypto@1.0");
            missing.Fix.ShouldBe("ask for a version the registry holds; a release is in the registry within a day of being made; to ask GitHub itself, run the command again with --direct");
        }

        [Fact]
        public async Task Where_a_refusal_would_be_recorded_is_told_from_the_latest_version_the_registry_holds()
        {
            var first = Version("1.0.0") with { Source = Sample.Repository("github.com/thatplatypus/crypto") };
            var files = new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", first, Version("2.0.0"))));
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.5"], []);

            var result = await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing).Fix.ShouldContain("which https://registry.example/v1/refused/thatplatypus/grapevine.json says;");
        }

        [Fact]
        public async Task Of_a_package_it_was_never_asked_about_a_registry_has_nothing_to_say()
        {
            var source = new RegistryPackageSource(await IndexedAsync(Released()));
            var explains = (IExplainsMissing)source;

            explains.Missing(Sample.Name("@thatplatypus/crypto"), Sample.Version("9.9.9"), "a chain").ShouldBeNull();
            explains.MissingThoughLocked(Sample.Name("@thatplatypus/crypto"), Sample.Version("9.9.9"), null).ShouldBeNull();
        }

        [Fact]
        public void A_package_that_is_not_in_the_registry_is_said_the_same_way_when_nothing_led_to_it()
        {
            var notFound = RegistryPackageSource.NotFound(Sample.Name("@thatplatypus/sockets_more"), "https://registry.example").ShouldBeComplete();

            notFound.Code.ShouldBe(DiagnosticCodes.PackageNotFound);
            notFound.Reason.ShouldBe("https://registry.example/v1/packages/thatplatypus/sockets_more.json is not there");
        }
    }
}
