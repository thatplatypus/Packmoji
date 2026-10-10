using Packmoji.Core.Diagnostics;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// The resolver believes what its source says, and a source can be wrong or hostile. These hold it
    /// to stopping, and to saying little, whatever it is told.
    /// </summary>
    public sealed class ResolverLimitTests
    {
        private static string Name(int number) => $"@thatplatypus/p{number}";

        // A line of packages, each depending on the next, that ends after the given number of them.
        private static AnsweringSource Line(int length) => new((name, version) =>
        {
            var number = int.Parse(name.Name[1..]);
            return number >= length ? null
                : number == length - 1 ? Sample.Published(name.ToString(), version.ToString())
                : Sample.Published(name.ToString(), version.ToString(), $"{Name(number + 1)}@1.0");
        });

        private static Task<ResolveResult> Resolve(AnsweringSource source, params string[] dependencies) =>
            Resolver.ResolveAsync(Project.Asking(dependencies), null, source, TestContext.Current.CancellationToken);

        [Fact]
        public async Task A_graph_of_exactly_ten_thousand_versions_is_resolved()
        {
            var source = Line(10_000);

            var graph = (await Resolve(source, $"{Name(0)}@1.0")).ShouldSucceed();

            graph.Packages.Count.ShouldBe(10_000);
            source.Asked.ShouldBe(10_000);
        }

        [Fact]
        public async Task The_lockfile_of_the_largest_graph_that_resolves_is_read_back()
        {
            var manifest = Project.Asking($"{Name(0)}@1.0");
            var graph = (await Resolve(Line(10_000), $"{Name(0)}@1.0")).ShouldSucceed();

            var written = LockfileWriter.Write(graph.ToLockfile(manifest));

            LockfileReader.Read(written).ShouldSucceed().Packages.Count.ShouldBe(10_000);
        }

        [Fact]
        public async Task A_graph_whose_lockfile_could_not_be_read_back_is_refused()
        {
            // Five hundred packages, each depending on every one after it: far inside the limit on
            // versions, and a lockfile of more than six megabytes, which its own reader refuses.
            const int count = 500;
            var source = new AnsweringSource((name, version) =>
            {
                var number = int.Parse(name.Name[1..]);
                var later = Enumerable.Range(number + 1, count - number - 1).Select(next => $"{Name(next)}@1.0").ToArray();
                return Sample.Published(name.ToString(), version.ToString(), later);
            });

            var diagnostic = (await Resolve(source, $"{Name(0)}@1.0")).ShouldFailWith(DiagnosticCodes.ResolveGraphTooLarge);

            diagnostic.Message.ShouldBe("What \"@thatplatypus/app\" depends on makes a lockfile of more than 4194304 bytes.");
            diagnostic.Reason.ShouldContain("refused when it is read");
            source.Asked.ShouldBe(count);
        }

        [Fact]
        public async Task One_version_more_is_refused_and_nothing_else_is_said()
        {
            var source = Line(10_001);

            var diagnostic = (await Resolve(source, $"{Name(0)}@1.0")).ShouldFailWith(DiagnosticCodes.ResolveGraphTooLarge);

            diagnostic.Message.ShouldBe("What \"@thatplatypus/app\" depends on is more than 10000 versions.");
            diagnostic.Reason.ShouldEndWith(
                "the last requirement it followed was @thatplatypus/app → @thatplatypus/p0@1.0.0 → @thatplatypus/p1@1.0.0 → @thatplatypus/p2@1.0.0 → " +
                "(9995 more) → @thatplatypus/p9998@1.0.0 → @thatplatypus/p9999@1.0.0 → @thatplatypus/p10000@1.0");
            source.Asked.ShouldBe(10_000);
        }

        [Fact]
        public async Task A_source_that_never_stops_answering_does_not_keep_the_resolver_going_for_ever()
        {
            // It gives up by itself in the end, so that a resolver with no limit fails this test and does not hang it.
            var answers = 0;
            var source = new AnsweringSource((name, version) => ++answers > 50_000
                ? throw new InvalidOperationException("the resolver went on asking")
                : Sample.Published(name.ToString(), version.ToString(), $"{Name(int.Parse(name.Name[1..]) + 1)}@1.0"));

            (await Resolve(source, $"{Name(0)}@1.0")).ShouldFailWith(DiagnosticCodes.ResolveGraphTooLarge);

            source.Asked.ShouldBe(10_000);
        }

        [Fact]
        public async Task A_version_with_more_than_ten_thousand_dependencies_is_refused_in_the_same_way()
        {
            var many = Enumerable.Range(1, 10_000).Select(number => $"{Name(number)}@1.0").ToArray();
            var source = new AnsweringSource((name, version) =>
                name.Name == "p0" ? Sample.Published(name.ToString(), version.ToString(), many) : Sample.Published(name.ToString(), version.ToString()));

            (await Resolve(source, $"{Name(0)}@1.0")).ShouldFailWith(DiagnosticCodes.ResolveGraphTooLarge);

            source.Asked.ShouldBe(1);
        }

        [Fact]
        public async Task More_than_a_hundred_problems_are_counted_and_the_first_hundred_listed()
        {
            var asked = Enumerable.Range(100, 150).Select(number => $"{Name(number)}@1.0").ToArray();

            var result = await Resolve(new AnsweringSource((_, _) => null), asked);

            result.Succeeded.ShouldBeFalse();
            result.Diagnostics.Count.ShouldBe(100);
            result.OmittedDiagnostics.ShouldBe(50);
            result.Diagnostics.ShouldAllBe(diagnostic => diagnostic.Code == DiagnosticCodes.ResolveVersionMissing);
            result.Diagnostics[0].Message.ShouldContain("\"@thatplatypus/p100\"");
            result.Diagnostics[99].Message.ShouldContain("\"@thatplatypus/p199\"");
        }

        [Fact]
        public async Task A_long_chain_is_shown_by_its_two_ends_with_what_lies_between_counted()
        {
            // Thirty packages in a line, and the last of them asks for one that was never published.
            var source = new AnsweringSource((name, version) =>
            {
                var number = int.Parse(name.Name[1..]);
                return number < 30 ? Sample.Published(name.ToString(), version.ToString(), $"{Name(number + 1)}@1.0") : null;
            });

            var diagnostic = (await Resolve(source, $"{Name(0)}@1.0")).ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Reason.ShouldEndWith(
                ": @thatplatypus/app → @thatplatypus/p0@1.0.0 → @thatplatypus/p1@1.0.0 → @thatplatypus/p2@1.0.0 → (25 more) → " +
                "@thatplatypus/p28@1.0.0 → @thatplatypus/p29@1.0.0 → @thatplatypus/p30@1.0");
        }

        [Fact]
        public async Task Of_two_chains_to_one_version_the_shorter_is_shown_and_of_two_equally_short_the_one_that_sorts_first()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/m@1.0")
                .Publish("@thatplatypus/m", "1.0.0", "@thatplatypus/gone@1.0")
                .Publish("@thatplatypus/z", "1.0.0", "@thatplatypus/gone@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/gone@1.0");

            var diagnostic = (await universe.Resolve(Project.Asking("@thatplatypus/z@1.0", "@thatplatypus/a@1.0", "@thatplatypus/b@1.0")))
                .ShouldFailWith(DiagnosticCodes.ResolveVersionMissing);

            diagnostic.Reason.ShouldEndWith(": @thatplatypus/app → @thatplatypus/b@1.0.0 → @thatplatypus/gone@1.0");
        }

        [Fact]
        public async Task A_manifest_that_names_a_package_twice_is_refused_as_the_reader_refuses_one()
        {
            // The reader never gives such a manifest, and one made in code would write a lockfile that cannot be read back.
            var universe = new Universe().Publish("@thatplatypus/crypto", "1.0.0").Publish("@thatplatypus/crypto", "1.1.0");
            var manifest = Project.Named(Project.Name, ["@thatplatypus/crypto@1.0"], ["@thatplatypus/crypto@1.1"]);

            var diagnostic = (await universe.Resolve(manifest)).ShouldFailWith(DiagnosticCodes.DependencyDuplicate);

            diagnostic.Message.ShouldBe("\"@thatplatypus/crypto\" is asked for twice by \"@thatplatypus/app\".");
            diagnostic.Fix.ShouldContain("packmoji.json");
        }
    }
}
