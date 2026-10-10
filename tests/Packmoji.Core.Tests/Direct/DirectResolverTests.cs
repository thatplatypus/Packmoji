using CsCheck;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Direct
{
    /// <summary>
    /// A whole resolution against releases, with no registry: Grapevine's three packages in their one
    /// repository, asked for by an application that knows only the name of one of them.
    /// </summary>
    public sealed class DirectResolverTests
    {
        private const string Shared = "github.com/thatplatypus/grapevine";

        private static FakeReleaseHost Grapevine() => new FakeReleaseHost()
            .Release(Shared, "@thatplatypus/crypto", "1.0.0")
            .Release(Shared, "@thatplatypus/deflate", "0.1.0")
            .Release(Shared, "@thatplatypus/grapevine", "0.3.0", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

        private static Task<ResolveResult> Resolve(FakeReleaseHost host, Manifest manifest, Lockfile? existing = null, IReadOnlyDictionary<PackageName, RepositoryRef>? told = null) =>
            DirectResolver.ResolveAsync(manifest, existing, new DirectPackageSource(host, new MemoryAssetStore(), manifest, existing, told), TestContext.Current.CancellationToken);

        [Fact]
        public async Task An_application_that_asks_for_grapevine_gets_the_two_packages_beside_it()
        {
            var manifest = Project.Named("@someone/app", ["@thatplatypus/grapevine@0.3"], []);

            var graph = (await Resolve(Grapevine(), manifest)).ShouldSucceed();

            graph.Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
            graph.Packages.ShouldAllBe(package => package.Published.Source == Sample.Repository(Shared));
            LockfileReader.Read(LockfileWriter.Write(graph.ToLockfile(manifest))).ShouldSucceed();
        }

        [Fact]
        public async Task It_does_not_matter_that_a_package_is_asked_for_before_the_one_that_shows_where_it_lives()
        {
            // crypto comes before grapevine by name, so it is looked for first, and not found. Finding
            // grapevine shows where the owner keeps packages, and the resolution is run again.
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0", "@thatplatypus/grapevine@0.3"], []);
            var host = Grapevine();

            var result = await Resolve(host, manifest);

            result.ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.0.0", "@thatplatypus/deflate@0.1.0", "@thatplatypus/grapevine@0.3.0"]);
            result.Diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_that_cannot_be_found_is_said_to_be_so_with_where_pmj_looked_and_how_to_say_where_it_is()
        {
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0"], []);

            var result = await Resolve(Grapevine(), manifest);

            // Not finding a package is all pmj knows. It does not also say the version was never published.
            result.Succeeded.ShouldBeFalse();
            var notFound = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            notFound.Code.ShouldBe(DiagnosticCodes.PackageNotFound);
            notFound.Message.ShouldBe("No release of \"@thatplatypus/crypto\" 1.0.0 was found.");
            notFound.Reason.ShouldContain("crypto-v1.0.0");
            notFound.Reason.ShouldContain("github.com/thatplatypus/crypto");
            notFound.Reason.ShouldEndWith("it is asked for: @someone/app → @thatplatypus/crypto@1.0");
            notFound.Fix.ShouldContain("--repository github.com/thatplatypus/");
        }

        [Fact]
        public async Task A_package_that_another_package_needs_and_that_cannot_be_found_is_not_blamed_on_the_one_that_needs_it()
        {
            // The tool is in a repository of its own name. What it needs is released, in a repository pmj has no way to know of.
            var host = new FakeReleaseHost()
                .Release("github.com/thatplatypus/tool", "@thatplatypus/tool", "1.0.0", "@thatplatypus/helper@2.0")
                .Release("github.com/thatplatypus/mono", "@thatplatypus/helper", "2.0.0");
            var manifest = Project.Named("@someone/app", ["@thatplatypus/tool@1.0"], []);

            var result = await Resolve(host, manifest);

            var notFound = result.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            notFound.Code.ShouldBe(DiagnosticCodes.PackageNotFound);
            notFound.Message.ShouldBe("No release of \"@thatplatypus/helper\" 2.0.0 was found.");
            notFound.Reason.ShouldEndWith("it is asked for: @someone/app → @thatplatypus/tool@1.0.0 → @thatplatypus/helper@2.0");
            (notFound.Message + notFound.Reason + notFound.Fix).ShouldNotContain("never published");
            (notFound.Message + notFound.Reason + notFound.Fix).ShouldNotContain("has to publish");
        }

        [Fact]
        public async Task Told_where_it_lives_the_same_package_is_found()
        {
            var manifest = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0"], []);
            var told = new Dictionary<PackageName, RepositoryRef> { [Sample.Name("@thatplatypus/crypto")] = Sample.Repository(Shared) };

            (await Resolve(Grapevine(), manifest, told: told)).ShouldSucceed().Selected().ShouldBe(["@thatplatypus/crypto@1.0.0"]);
        }

        [Fact]
        public async Task The_lockfile_remembers_where_a_package_lives_when_the_manifest_changes()
        {
            var told = new Dictionary<PackageName, RepositoryRef> { [Sample.Name("@thatplatypus/crypto")] = Sample.Repository(Shared) };
            var before = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0"], []);
            var locked = (await Resolve(Grapevine(), before, told: told)).ShouldSucceed().ToLockfile(before);
            var host = Grapevine().Release("github.com/someone/tools", "@someone/tools", "1.0.0");
            var after = Project.Named("@someone/app", ["@thatplatypus/crypto@1.0", "@someone/tools@1.0"], []);

            var graph = (await Resolve(host, after, locked)).ShouldSucceed();

            graph.Selected().ShouldBe(["@someone/tools@1.0.0", "@thatplatypus/crypto@1.0.0"]);
            host.Downloads.ShouldNotContain("github.com/thatplatypus/crypto crypto-v1.0.0");
        }

        [Fact]
        public async Task A_version_that_is_not_released_where_its_package_lives_is_missing_and_no_more_is_said()
        {
            // The package is found, at another version: so pmj knows where it lives, and has nothing to add.
            var host = Grapevine();
            var manifest = Project.Named("@someone/app", ["@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.5"], []);

            var result = await Resolve(host, manifest);

            result.Diagnostics.Select(diagnostic => diagnostic.Code).ShouldBe([DiagnosticCodes.ResolveVersionMissing]);
        }

        [Fact]
        public async Task Whatever_the_graph_releases_resolve_to_what_the_resolver_gives_from_memory()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var selected = 0;

            await GeneratedProject.Any.Where(project => !project.VersionsRepeat && !project.ManifestRepeats).SampleAsync(
                async project =>
                {
                    // Every package in one repository beside the project, or each in a repository of its own name.
                    var shared = project.Order % 2 == 0;
                    var manifest = project.Manifest();
                    if (shared)
                    {
                        manifest = manifest with { Package = manifest.Package with { Repository = Sample.Repository("github.com/generated/everything") } };
                    }

                    var host = new FakeReleaseHost();
                    foreach (var (name, version, asks) in project.Released())
                    {
                        host.Release(shared ? "github.com/generated/everything" : RepositoryRef.DefaultFor(Sample.Name(name)).ToString(), name, version, asks);
                    }

                    var fromMemory = await Resolver.ResolveAsync(manifest, null, project.Universe(yanks: false), cancellation);
                    var fromReleases = await DirectResolver.ResolveAsync(manifest, null, new DirectPackageSource(host, new MemoryAssetStore(), manifest, null), cancellation);

                    // The same problems in the same order, but for one: where memory knows that a
                    // version was never published, releases can only say that none was found.
                    fromReleases.Succeeded.ShouldBe(fromMemory.Succeeded);
                    fromReleases.Diagnostics.Count.ShouldBe(fromMemory.Diagnostics.Count);
                    foreach (var (known, found) in fromMemory.Diagnostics.Zip(fromReleases.Diagnostics))
                    {
                        if (found.Code == DiagnosticCodes.PackageNotFound)
                        {
                            known.Code.ShouldBe(DiagnosticCodes.ResolveVersionMissing);
                            var missing = System.Text.RegularExpressions.Regex.Match(known.Message, "^Version (.+) of (\".+\") was never published\\.$");
                            missing.Success.ShouldBeTrue(known.Message);
                            found.Message.ShouldBe($"No release of {missing.Groups[2].Value} {missing.Groups[1].Value} was found.");
                        }
                        else
                        {
                            found.ShouldBe(known);
                        }
                    }
                    if (fromMemory.Succeeded && fromReleases.Succeeded)
                    {
                        fromReleases.Graph.Selected().ShouldBe(fromMemory.Graph.Selected());
                        foreach (var package in fromMemory.Graph.Packages)
                        {
                            fromReleases.Graph.Pins(package.Published.Name.ToString()).ShouldBe(fromMemory.Graph.Pins(package.Published.Name.ToString()));
                        }

                        if (fromMemory.Graph.Packages.Count > 0)
                        {
                            Interlocked.Increment(ref selected);
                        }
                    }
                },
                iter: 300,
                print: project => project.Describe());

            selected.ShouldBeGreaterThan(60);
        }
    }
}
