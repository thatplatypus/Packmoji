using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Reports;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Reports
{
    public sealed class TreeReportTests
    {
        private static readonly Manifest App = ManifestReader.Read(Sample.ManifestJson("@thatplatypus/app", "0.1.0", "github.com/thatplatypus/app")).ShouldSucceed();

        /// <summary>A lockfile of packages each written <c>name version</c>, then what it depends on. Every one lives in a repository of its own name.</summary>
        private static Lockfile Locked(string[] roots, string[] devRoots, params string[][] packages) =>
            new(
                new RootRequirements(roots.Select(Sample.Asks).ToList(), devRoots.Select(Sample.Asks).ToList()),
                packages
                    .Select(package => new LockedPackage(
                        Sample.Name(package[0]),
                        Sample.Version(package[1]),
                        Sample.Repository("github.com/thatplatypus/" + Sample.Name(package[0]).Name),
                        Sample.Sha('a'),
                        VerificationLevel.Checksum,
                        package.Skip(2).Select(Pin).ToList()))
                    .ToList());

        // What a locked package depends on, written as a lockfile writes it: <c>@owner/name@1.2.0</c>.
        private static LockedDependency Pin(string text)
        {
            var at = text.LastIndexOf('@');
            return new LockedDependency(Sample.Name(text[..at]), Sample.Version(text[(at + 1)..]));
        }

        [Fact]
        public void The_tree_begins_with_the_project_and_shows_what_each_package_depends_on_in_order_of_name()
        {
            var lockfile = LockfileReader.Read(Fixtures.Lockfile).ShouldSucceed();

            TreeReport.Text(App, lockfile).ShouldBe(
                """
                @thatplatypus/app 0.1.0
                └── @thatplatypus/grapevine 0.3.0
                    ├── @thatplatypus/crypto 1.0.0
                    └── @thatplatypus/deflate 0.1.0

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_project_that_depends_on_nothing_is_one_line()
        {
            TreeReport.Text(App, LockfileReader.Read(Fixtures.EmptyLockfile).ShouldSucceed()).ShouldBe("@thatplatypus/app 0.1.0\n");
        }

        [Fact]
        public void What_is_needed_only_to_develop_the_project_comes_last_and_says_so()
        {
            var lockfile = Locked(
                ["@thatplatypus/zebra@1.0"],
                ["@thatplatypus/apple@2.0"],
                ["@thatplatypus/apple", "2.0.0"],
                ["@thatplatypus/zebra", "1.0.0"]);

            TreeReport.Text(App, lockfile).ShouldBe(
                """
                @thatplatypus/app 0.1.0
                ├── @thatplatypus/zebra 1.0.0
                └── @thatplatypus/apple 2.0.0 (dev)

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_package_met_again_is_shown_without_what_it_depends_on_and_marked()
        {
            var lockfile = Locked(
                ["@thatplatypus/a@1.0", "@thatplatypus/b@1.0"],
                [],
                ["@thatplatypus/a", "1.0.0", "@thatplatypus/c@1.0.0"],
                ["@thatplatypus/b", "1.0.0", "@thatplatypus/c@1.0.0", "@thatplatypus/d@1.0.0"],
                ["@thatplatypus/c", "1.0.0", "@thatplatypus/d@1.0.0"],
                ["@thatplatypus/d", "1.0.0"]);

            TreeReport.Text(App, lockfile).ShouldBe(
                """
                @thatplatypus/app 0.1.0
                ├── @thatplatypus/a 1.0.0
                │   └── @thatplatypus/c 1.0.0
                │       └── @thatplatypus/d 1.0.0
                └── @thatplatypus/b 1.0.0
                    ├── @thatplatypus/c 1.0.0 (*)
                    └── @thatplatypus/d 1.0.0

                (*) is shown with what it depends on where it first appears.

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_graph_whose_every_path_were_shown_would_be_endless_and_is_shown_in_a_line_for_each_requirement()
        {
            // Forty levels of two packages, each of which depends on both of the next level: more than
            // a million million paths from the top to the bottom, and 158 requirements.
            var packages = new List<string[]>();
            for (var level = 0; level < 40; level++)
            {
                var below = level == 39 ? [] : new[] { $"@thatplatypus/l{level + 1}a@1.0.0", $"@thatplatypus/l{level + 1}b@1.0.0" };
                packages.Add([$"@thatplatypus/l{level}a", "1.0.0", .. below]);
                packages.Add([$"@thatplatypus/l{level}b", "1.0.0", .. below]);
            }

            var text = TreeReport.Text(App, Locked(["@thatplatypus/l0a@1.0", "@thatplatypus/l0b@1.0"], [], [.. packages]));

            text.Split('\n').Count(line => line.Contains("@thatplatypus/l", StringComparison.Ordinal)).ShouldBe(2 + (39 * 4));
        }

        [Fact]
        public void A_chain_deeper_than_the_tree_goes_is_cut_and_the_cut_is_said()
        {
            var packages = Enumerable.Range(0, 100)
                .Select(depth => depth == 99 ? new[] { $"@thatplatypus/p{depth}", "1.0.0" } : [$"@thatplatypus/p{depth}", "1.0.0", $"@thatplatypus/p{depth + 1}@1.0.0"])
                .ToArray();

            var lines = TreeReport.Text(App, Locked(["@thatplatypus/p0@1.0"], [], packages)).Split('\n');

            lines.Count(line => line.Contains("@thatplatypus/p", StringComparison.Ordinal)).ShouldBe(TreeReport.MaxDepth);
            lines[TreeReport.MaxDepth].ShouldEndWith($"@thatplatypus/p{TreeReport.MaxDepth - 1} 1.0.0 (...)");
            lines[^2].ShouldBe($"(...) depends on more, below the {TreeReport.MaxDepth} levels shown here. pmj tree --json gives all of it.");
        }

        [Fact]
        public void A_package_cut_for_depth_is_shown_in_full_where_it_is_met_higher_up()
        {
            // The chain reaches "deep" at the last level shown, where it is cut. The project also
            // depends on "deep" itself, and "zz" sorts after the chain, so that is met second.
            var packages = Enumerable.Range(0, TreeReport.MaxDepth - 1)
                .Select(depth => new[] { $"@thatplatypus/p{depth}", "1.0.0", depth == TreeReport.MaxDepth - 2 ? "@thatplatypus/zz@1.0.0" : $"@thatplatypus/p{depth + 1}@1.0.0" })
                .Append(["@thatplatypus/zz", "1.0.0", "@thatplatypus/leaf@1.0.0"])
                .Append(["@thatplatypus/leaf", "1.0.0"])
                .ToArray();

            var lines = TreeReport.Text(App, Locked(["@thatplatypus/p0@1.0", "@thatplatypus/zz@1.0"], [], packages)).Split('\n');

            lines.ShouldContain(line => line.EndsWith("@thatplatypus/zz 1.0.0 (...)", StringComparison.Ordinal));
            lines.ShouldContain("└── @thatplatypus/zz 1.0.0");
            lines.ShouldContain("    └── @thatplatypus/leaf 1.0.0");
        }

        [Fact]
        public void A_lockfile_that_lacks_a_package_it_names_is_the_callers_mistake()
        {
            var lockfile = Locked(["@thatplatypus/a@1.0"], [], ["@thatplatypus/b", "1.0.0"]);

            Should.Throw<ArgumentException>(() => TreeReport.Text(App, lockfile)).Message.ShouldContain("@thatplatypus/a");
            Should.Throw<ArgumentException>(() => TreeReport.Json(App, lockfile)).Message.ShouldContain("@thatplatypus/a");
        }

        [Fact]
        public void For_a_tool_the_graph_is_the_project_what_it_asks_for_and_every_package_once()
        {
            var lockfile = Locked(
                ["@thatplatypus/a@1.0"],
                ["@thatplatypus/b@0.2"],
                ["@thatplatypus/a", "1.4.0", "@thatplatypus/b@0.2.1"],
                ["@thatplatypus/b", "0.2.1"]);

            TreeReport.Json(App, lockfile).ShouldBe(
                $$"""
                {
                  "ok": true,
                  "diagnostics": [],
                  "omittedDiagnostics": 0,
                  "project": {
                    "name": "@thatplatypus/app",
                    "version": "0.1.0"
                  },
                  "dependencies": [
                    {
                      "name": "@thatplatypus/a",
                      "requirement": "1.0",
                      "version": "1.4.0"
                    }
                  ],
                  "devDependencies": [
                    {
                      "name": "@thatplatypus/b",
                      "requirement": "0.2",
                      "version": "0.2.1"
                    }
                  ],
                  "packages": [
                    {
                      "name": "@thatplatypus/a",
                      "version": "1.4.0",
                      "source": "github.com/thatplatypus/a",
                      "sha256": "{{new string('a', 64)}}",
                      "verified": "checksum",
                      "dependencies": [
                        {
                          "name": "@thatplatypus/b",
                          "version": "0.2.1"
                        }
                      ]
                    },
                    {
                      "name": "@thatplatypus/b",
                      "version": "0.2.1",
                      "source": "github.com/thatplatypus/b",
                      "sha256": "{{new string('a', 64)}}",
                      "verified": "checksum",
                      "dependencies": []
                    }
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }
    }
}
