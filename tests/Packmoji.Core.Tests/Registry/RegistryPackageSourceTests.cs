using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
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
            notFound.Reason.ShouldBe("the registry at https://registry.example has no file of it; it is asked for: @someone/app → @thatplatypus/sockets_more@1.0");
            notFound.Fix.ShouldContain("--direct");
        }

        [Fact]
        public async Task Of_a_version_that_is_not_in_the_registry_though_its_package_is_a_resolution_says_it_was_never_published()
        {
            var files = await IndexedAsync(Released());
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.1"], []);

            var result = await Resolver.ResolveAsync(manifest, null, new RegistryPackageSource(files), Packmoji.Core.Identity.ScopeLimit.None, TestContext.Current.CancellationToken);

            result.ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);
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
        public void A_package_that_is_not_in_the_registry_is_said_the_same_way_when_nothing_led_to_it()
        {
            var notFound = RegistryPackageSource.NotFound(Sample.Name("@thatplatypus/sockets_more"), "https://registry.example").ShouldBeComplete();

            notFound.Code.ShouldBe(DiagnosticCodes.PackageNotFound);
            notFound.Reason.ShouldBe("the registry at https://registry.example has no file of it");
        }
    }
}
