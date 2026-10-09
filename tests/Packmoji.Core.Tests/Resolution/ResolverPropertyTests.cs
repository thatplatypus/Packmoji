using CsCheck;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// What must hold for every graph, tried on a thousand that are made up afresh on each run. When
    /// one of these fails it prints the project it failed on and a seed that makes it again, and the
    /// project is then added to the tests by example.
    /// </summary>
    public sealed class ResolverPropertyTests
    {
        private const int Iterations = 1_000;

        // A property that only speaks of resolutions that select something says nothing if few of them
        // do, and would hold of a resolver that selected nothing at all.
        private const int FewestThatMustSelect = Iterations / 5;

        /// <summary>The graph of a resolution that selected something, or null.</summary>
        private static ResolvedGraph? Selected(ResolveResult result) => result.Graph is { Packages.Count: > 0 } graph ? graph : null;

        private static Task<ResolveResult> Resolve(Universe universe, Manifest manifest, Lockfile? existing, CancellationToken cancellation) =>
            Resolver.ResolveAsync(manifest, existing, universe, cancellation);

        // Everything a resolution gave, as text: the lockfile it makes, or what stopped it, and any warning.
        private static string Outcome(ResolveResult result, Manifest manifest)
        {
            var said = result.Diagnostics.Select(diagnostic => $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message} | {diagnostic.Reason} | {diagnostic.Fix}");
            var gave = result.Succeeded ? LockfileWriter.Write(result.Graph.ToLockfile(manifest)) : "no graph";
            return string.Join("\n", said.Append($"omitted: {result.OmittedDiagnostics}").Append(gave));
        }

        [Fact]
        public async Task The_answer_does_not_depend_on_the_order_anything_was_written_in()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var selected = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    var written = await Resolve(project.Universe(), project.Manifest(), null, cancellation);
                    var reordered = await Resolve(project.Universe(reordered: true), project.Manifest(reordered: true), null, cancellation);
                    if (Selected(written) is not null)
                    {
                        Interlocked.Increment(ref selected);
                    }

                    Outcome(reordered, project.Manifest()).ShouldBe(Outcome(written, project.Manifest()));
                },
                iter: Iterations,
                print: project => project.Describe());

            selected.ShouldBeGreaterThan(FewestThatMustSelect);
        }

        [Fact]
        public async Task Every_selected_version_is_the_highest_minimum_asked_for_and_nothing_newer()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var selected = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    var manifest = project.Manifest();
                    var universe = project.Universe();
                    if (Selected(await Resolve(universe, manifest, null, cancellation)) is not { } graph)
                    {
                        return;
                    }

                    Interlocked.Increment(ref selected);
                    var highest = HighestMinimums(manifest, universe);
                    foreach (var package in graph.Packages)
                    {
                        package.Published.Version.ShouldBe(highest[package.Published.Name]);
                    }
                },
                iter: Iterations,
                print: project => project.Describe());

            selected.ShouldBeGreaterThan(FewestThatMustSelect);
        }

        [Fact]
        public async Task What_is_selected_needs_nothing_that_is_not_selected_and_makes_a_lockfile_that_reads_back()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var selected = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    var manifest = project.Manifest();
                    var result = await Resolve(project.Universe(), manifest, null, cancellation);
                    if (!result.Succeeded)
                    {
                        return;
                    }

                    if (Selected(result) is not null)
                    {
                        Interlocked.Increment(ref selected);
                    }

                    var versions = result.Graph.Packages.ToDictionary(package => package.Published.Name, package => package.Published.Version);
                    foreach (var dependency in RootRequirements.From(manifest).Dependencies.Concat(RootRequirements.From(manifest).DevDependencies))
                    {
                        dependency.Requirement.IsSatisfiedBy(versions[dependency.Name]).ShouldBeTrue();
                    }

                    foreach (var package in result.Graph.Packages)
                    {
                        foreach (var dependency in package.Published.Dependencies)
                        {
                            dependency.Requirement.IsSatisfiedBy(versions[dependency.Name]).ShouldBeTrue();
                            package.Dependencies.ShouldContain(new LockedDependency(dependency.Name, versions[dependency.Name]));
                        }
                    }

                    LockfileReader.Read(LockfileWriter.Write(result.Graph.ToLockfile(manifest))).ShouldSucceed();
                },
                iter: Iterations,
                print: project => project.Describe());

            selected.ShouldBeGreaterThan(FewestThatMustSelect);
        }

        [Fact]
        public async Task Resolving_again_with_the_lockfile_a_resolution_gave_changes_nothing()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var selected = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    var manifest = project.Manifest();
                    var first = await Resolve(project.Universe(), manifest, null, cancellation);
                    if (Selected(first) is not { } graph)
                    {
                        return;
                    }

                    Interlocked.Increment(ref selected);
                    var again = await Resolve(project.Universe(), manifest, graph.ToLockfile(manifest), cancellation);

                    Outcome(again, manifest).ShouldBe(Outcome(first, manifest));
                },
                iter: Iterations,
                print: project => project.Describe());

            selected.ShouldBeGreaterThan(FewestThatMustSelect);
        }

        [Fact]
        public async Task Asking_for_one_package_more_never_lowers_a_version()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var compared = 0;

            await GeneratedProject.Any.SampleAsync(
                async project =>
                {
                    if (project.ManifestAskingForMore() is not { } more)
                    {
                        return;
                    }

                    var before = await Resolve(project.Universe(), project.Manifest(), null, cancellation);
                    var after = await Resolve(project.Universe(), more, null, cancellation);
                    if (Selected(before) is not { } earlier || !after.Succeeded)
                    {
                        return;
                    }

                    Interlocked.Increment(ref compared);
                    var later = after.Graph.Packages.ToDictionary(package => package.Published.Name, package => package.Published.Version);
                    foreach (var package in earlier.Packages)
                    {
                        if (later.TryGetValue(package.Published.Name, out var version))
                        {
                            version.ShouldBeGreaterThanOrEqualTo(package.Published.Version);
                        }
                    }
                },
                iter: Iterations,
                print: project => project.Describe());

            compared.ShouldBeGreaterThan(FewestThatMustSelect / 2);
        }

        /// <summary>
        /// The highest minimum asked for of each package, worked out here in the plainest way and with
        /// none of the resolver's code: follow every requirement to the version it names, and take that
        /// version's requirements in turn, until there is nothing new.
        /// </summary>
        private static Dictionary<PackageName, SemanticVersion> HighestMinimums(Manifest manifest, Universe universe)
        {
            var highest = new Dictionary<PackageName, SemanticVersion>();
            var followed = new HashSet<(PackageName, SemanticVersion)>();
            var waiting = new Stack<Dependency>((manifest.Dependencies ?? []).Concat(manifest.DevDependencies ?? []));
            while (waiting.TryPop(out var asked))
            {
                var minimum = asked.Requirement.Minimum;
                if (!highest.TryGetValue(asked.Name, out var known) || minimum > known)
                {
                    highest[asked.Name] = minimum;
                }

                if (followed.Add((asked.Name, minimum)))
                {
                    foreach (var next in universe.Get(asked.Name.ToString(), minimum.ToString()).Dependencies)
                    {
                        waiting.Push(next);
                    }
                }
            }

            return highest;
        }
    }
}
