using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// <c>pmj build</c>: what a project depends on is compiled, each package once for the whole
    /// machine, and put in the project where the compiler looks. The tools are made up, and behave
    /// as the released ones were read and seen to.
    /// </summary>
    public sealed class BuildTests
    {
        // An application that depends on crypto alone, installed, and nothing built yet.
        private static async Task<Sandbox> WithCryptoInstalledAsync()
        {
            var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");
            return sandbox;
        }

        [Fact]
        public async Task Where_there_is_no_project_nothing_is_built_and_nothing_is_written()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[project.not-found]: ");
            sandbox.Files().ShouldBeEmpty();
            sandbox.Tools.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_that_asks_for_a_package_and_has_no_lockfile_is_not_built()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[lock.out-of-date]: There is no packmoji.lock.
                  why: this command reads what is locked, and chooses nothing itself
                  fix: run pmj install, which writes packmoji.lock

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Tools.Calls.ShouldBeEmpty();
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task A_lockfile_that_the_manifest_has_moved_on_from_is_not_built_from()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Release("github.com/thatplatypus/deflate", "@thatplatypus/deflate", "0.1.0");
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[lock.out-of-date]: packmoji.lock no longer answers what packmoji.json asks for.");
            sandbox.Tools.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_lockfile_that_still_holds_packages_for_a_manifest_that_asks_for_none_is_not_built_from()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Project("@someone/app");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[lock.out-of-date]: packmoji.lock no longer answers what packmoji.json asks for.");
            sandbox.Tools.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_that_requires_attestation_is_not_built()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.RequireAttestation();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[attestation.unverifiable]: ");
            sandbox.Tools.Calls.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_that_asks_for_no_package_needs_no_lockfile_and_with_only_its_dependencies_to_build_no_compiler()
        {
            using var sandbox = new Sandbox();
            sandbox.Project("@someone/app");
            Directory.Delete(sandbox.ToolsDirectory, recursive: true);

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Nothing to build: the project depends on no package.{Environment.NewLine}");
            sandbox.Files().ShouldBe(["packmoji.json", "target/.pmj-lock"]);
        }

        [Fact]
        public async Task A_package_is_compiled_from_its_unpacked_sources_archived_kept_and_put_in_the_project()
        {
            using var sandbox = await WithCryptoInstalledAsync();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Building @thatplatypus/crypto 1.0.0
                Built 1 package into packages/.

                """.ReplaceLineEndings(Environment.NewLine));

            var sha = sandbox.Lockfile().Packages.Single().Sha256.Hex[..8];
            var key = sandbox.Key("crypto");
            var staged = $"~/home/built/thatplatypus/crypto/1.0.0/{key}.tmp";
            sandbox.Tools.Calls.Select(sandbox.Plain).ShouldBe(
            [
                "emojicodec --help",
                $"emojicodec ~/home/cache/thatplatypus/crypto/1.0.0/{sha}/src/lib.🍇 -p crypto -c -o {staged}/work/crypto.o -i {staged}/crypto/🏛 -r",
                $"ar rcs {staged}/crypto/libcrypto.a {staged}/work/crypto.o",
            ]);
            sandbox.Built().ShouldBe(
            [
                $"thatplatypus/crypto/1.0.0/{key}/crypto/documentation.json",
                $"thatplatypus/crypto/1.0.0/{key}/crypto/libcrypto.a",
                $"thatplatypus/crypto/1.0.0/{key}/crypto/pmj-build.json",
                $"thatplatypus/crypto/1.0.0/{key}/crypto/🏛",
            ]);
            sandbox.Files("packages").ShouldBe(["crypto/documentation.json", "crypto/libcrypto.a", "crypto/pmj-build.json", "crypto/🏛"]);
            sandbox.Read("packages/crypto/libcrypto.a").ShouldBe(File.ReadAllText(sandbox.BuiltFile("crypto", "libcrypto.a")));
            sandbox.Read("packages/crypto/libcrypto.a").ShouldStartWith("made-up archive\nmember crypto.o\n  made-up object\n  package crypto\n  optimized no\n".ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task The_compiler_is_run_where_there_is_no_packages_directory_and_every_file_it_is_given_is_named_in_full()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Write("packages/planted/🏛", "💭 not what the project locks\n");

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            var compile = sandbox.Tools.Compiles.Single();
            Directory.Exists(Path.Combine(compile.WorkingDirectory, "packages")).ShouldBeFalse();
            compile.WorkingDirectory.ShouldNotBe(sandbox.Work);
            foreach (var path in compile.Arguments.Where(argument => argument.Contains('/') || argument.Contains('\\')))
            {
                Path.IsPathFullyQualified(path).ShouldBeTrue(path);
            }
        }

        [Fact]
        public async Task Packages_are_built_in_the_order_their_imports_need_and_each_is_shown_what_it_depends_on()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Building @thatplatypus/crypto 1.0.0
                Building @thatplatypus/deflate 0.1.0
                Building @thatplatypus/grapevine 0.3.0
                Built 3 packages into packages/.

                """.ReplaceLineEndings(Environment.NewLine));

            var compiles = sandbox.Tools.Compiles.Select(sandbox.Plain).ToList();
            compiles.Count.ShouldBe(3);
            compiles[0].ShouldNotContain(" -S ");
            compiles[1].ShouldNotContain(" -S ");
            compiles[2].ShouldEndWith($" -r -S ~/home/built/thatplatypus/crypto/1.0.0/{sandbox.Key("crypto")} -S ~/home/built/thatplatypus/deflate/0.1.0/{sandbox.Key("deflate")}");

            // What the made-up compiler wrote says which interfaces it read.
            var grapevine = sandbox.Read("packages/grapevine/libgrapevine.a");
            grapevine.ShouldContain($"  import crypto {FakeTools.Short(sandbox.Read("packages/crypto/🏛"))}");
            grapevine.ShouldContain($"  import deflate {FakeTools.Short(sandbox.Read("packages/deflate/🏛"))}");
        }

        [Fact]
        public async Task What_a_package_depends_on_through_another_is_shown_to_the_compiler_too()
        {
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/low", "@thatplatypus/low", "1.0.0");
            sandbox.Release("github.com/thatplatypus/mid", "@thatplatypus/mid", "1.0.0", "@thatplatypus/low@1.0");
            sandbox.Release("github.com/thatplatypus/top", "@thatplatypus/top", "1.0.0", "@thatplatypus/mid@1.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/top@1.0");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            // The interface of mid begins by importing low, so whatever imports mid has to find low as well.
            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Compiles.Last()).ShouldEndWith($" -S ~/home/built/thatplatypus/low/1.0.0/{sandbox.Key("low")} -S ~/home/built/thatplatypus/mid/1.0.0/{sandbox.Key("mid")}");
        }

        [Fact]
        public async Task With_the_cache_filled_a_build_asks_nothing_of_GitHub()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.GitHub.Requests.Clear();
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task On_a_machine_with_nothing_fetched_a_build_fetches_what_is_locked_and_then_builds_it()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.ForgetCache();
            sandbox.GitHub.Requests.Clear();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.GitHub.Downloads.Count().ShouldBe(3);
            sandbox.GitHub.Listings.ShouldBeEmpty();
            sandbox.Tools.Compiles.Count().ShouldBe(3);
        }

        [Fact]
        public async Task With_only_its_dependencies_to_build_a_project_needs_nothing_in_its_directory_but_its_two_files()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Files().ShouldBe(["packmoji.json", "packmoji.lock"]);

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Files().ShouldBe(
            [
                "packages/crypto/documentation.json",
                "packages/crypto/libcrypto.a",
                "packages/crypto/pmj-build.json",
                "packages/crypto/🏛",
                "packmoji.json",
                "packmoji.lock",
                "target/.pmj-lock",
            ]);
        }

        [Fact]
        public async Task A_build_writes_into_the_project_and_among_pmjs_own_files_and_nowhere_else()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            var before = sandbox.Elsewhere();

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            sandbox.Elsewhere().ShouldBe(before);
            Directory.GetFileSystemEntries(sandbox.Home).Select(Path.GetFileName).Order().ShouldBe(["built", "cache"]);
        }

        [Fact]
        public async Task What_is_kept_of_a_package_says_what_it_was_built_from()
        {
            using var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            var compiler = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(sandbox.ToolsDirectory, "emojicodec"))));
            var locked = sandbox.Lockfile().Packages.Single(package => package.Name.Name == "grapevine");
            string KeyOf(string name) => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(sandbox.BuiltFile(name, "pmj-build.json")))!);
            File.ReadAllText(sandbox.BuiltFile("grapevine", "pmj-build.json")).ShouldBe(
                $$"""
                {
                  "version": 1,
                  "key": "{{KeyOf("grapevine")}}",
                  "package": {
                    "name": "@thatplatypus/grapevine",
                    "version": "0.3.0",
                    "sha256": "{{locked.Sha256.Hex}}"
                  },
                  "compiler": {
                    "version": "1.0.0-beta.2",
                    "sha256": "{{compiler}}"
                  },
                  "optimized": false,
                  "dependencies": [
                    {
                      "name": "@thatplatypus/crypto",
                      "key": "{{KeyOf("crypto")}}"
                    },
                    {
                      "name": "@thatplatypus/deflate",
                      "key": "{{KeyOf("deflate")}}"
                    }
                  ],
                  "link": []
                }

                """.ReplaceLineEndings("\n"));
            sandbox.Read("packages/grapevine/pmj-build.json").ShouldBe(File.ReadAllText(sandbox.BuiltFile("grapevine", "pmj-build.json")));
        }

        [Fact]
        public async Task What_pmj_keeps_is_marked_as_not_to_be_written_and_a_projects_copy_is_the_projects_to_change()
        {
            using var sandbox = await WithCryptoInstalledAsync();

            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);

            foreach (var file in new[] { "🏛", "libcrypto.a", "documentation.json", "pmj-build.json" })
            {
                File.GetAttributes(sandbox.BuiltFile("crypto", file)).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue(file);
                File.GetAttributes(sandbox.PathOf("packages/crypto/" + file)).HasFlag(FileAttributes.ReadOnly).ShouldBeFalse(file);
            }
        }

        [Fact]
        public async Task Unpacked_files_that_are_no_longer_the_archives_are_never_compiled()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var locked = sandbox.Lockfile().Packages.Single();
            var unpacked = Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0", locked.Sha256.Hex);
            var source = Path.Combine(unpacked, "src", "lib.🍇");
            File.SetAttributes(source, FileAttributes.Normal);
            File.WriteAllText(source, "💭 not what was published\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                $"""
                error[cache.mismatch]: The cache's copy of "@thatplatypus/crypto" 1.0.0 is not what it should be.
                  why: the files unpacked in "{unpacked}" cannot be built from: "src/lib.🍇" is not the file the archive holds
                  fix: delete "{unpacked}", and run pmj install, which fetches the package again

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.Built().ShouldBeEmpty();
            sandbox.Has("packages/crypto/🏛").ShouldBeFalse();
        }

        [Fact]
        public async Task A_file_planted_among_the_unpacked_ones_stops_a_build_as_a_changed_one_does()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var locked = sandbox.Lockfile().Packages.Single();
            File.WriteAllText(Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0", locked.Sha256.Hex, "src", "more.🍇"), "💭 planted\n");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[cache.mismatch]: ");
            run.Error.ShouldContain("there are 3 files there, and the archive holds 2");
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task Files_that_were_never_unpacked_are_unpacked_from_the_archive_and_built()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            var locked = sandbox.Lockfile().Packages.Single();
            var unpacked = Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0", locked.Sha256.Hex);
            foreach (var file in Directory.EnumerateFiles(unpacked, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(unpacked, recursive: true);

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            File.Exists(Path.Combine(unpacked, "src", "lib.🍇")).ShouldBeTrue();
        }

        [Fact]
        public async Task A_package_that_was_published_without_its_main_file_is_not_compiled_and_neither_is_anything_else()
        {
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            sandbox.Upload("github.com/thatplatypus/deflate", "@thatplatypus/deflate", "0.1.0", TestPackage.Archive(
                ("packmoji.json", TestPackage.Manifest("@thatplatypus/deflate", "0.1.0", "library", null)),
                ("src/deflate.🍇", "💭 not where a library's main file is looked for\n")));
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[entry.not-found]: "@thatplatypus/deflate" 0.1.0 has no entry file.
                  why: the manifest names none, and neither of "src/lib.emojic" and "src/lib.🍇" exists
                  fix: tell its author: what is published has to hold the package's one main file, and pmj pack sees to that

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_whose_manifest_names_a_main_file_it_was_published_without_is_not_compiled()
        {
            using var sandbox = new Sandbox();
            sandbox.Upload("github.com/thatplatypus/deflate", "@thatplatypus/deflate", "0.1.0", TestPackage.Archive(
                ("packmoji.json", "{ \"package\": { \"name\": \"@thatplatypus/deflate\", \"version\": \"0.1.0\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }, \"build\": { \"entry\": \"deflate.🍇\" } }\n"),
                ("src/lib.🍇", "💭 not the file the manifest names\n")));
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/deflate@0.1");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[entry.not-found]: "@thatplatypus/deflate" 0.1.0 has no entry file.
                  why: its manifest names "deflate.🍇" as its entry, and what was published holds no such file
                  fix: tell its author: what is published has to hold the package's one main file, and pmj pack sees to that

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_with_two_files_that_could_be_its_main_file_is_not_guessed_at()
        {
            using var sandbox = new Sandbox();
            sandbox.Upload("github.com/thatplatypus/deflate", "@thatplatypus/deflate", "0.1.0", TestPackage.Archive(
                ("packmoji.json", TestPackage.Manifest("@thatplatypus/deflate", "0.1.0", "library", null)),
                ("src/lib.emojic", "💭 one\n"),
                ("src/lib.🍇", "💭 the other\n")));
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/deflate@0.1");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[entry.ambiguous]: \"@thatplatypus/deflate\" 0.1.0 has two possible entry files." + Environment.NewLine);
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task When_another_pmj_has_built_the_same_package_first_what_it_built_stands_and_nothing_is_left_beside_it()
        {
            using var sandbox = await WithCryptoInstalledAsync();
            sandbox.Tools.Before = call =>
            {
                // As the archiver is about to run, another pmj finishes the same build and puts it in its place.
                if (call.Tool == "ar")
                {
                    var staged = Path.GetDirectoryName(Path.GetDirectoryName(call.Arguments[1])!)!;
                    var entry = staged[..staged.LastIndexOf(".tmp-", StringComparison.Ordinal)];
                    Directory.CreateDirectory(Path.Combine(entry, "crypto"));
                    File.WriteAllText(Path.Combine(entry, "crypto", "pmj-build.json"), $"{{ \"version\": 1, \"key\": \"{Path.GetFileName(entry)}\" }}");
                    File.WriteAllText(Path.Combine(entry, "crypto", "🏛"), "💭 what the other pmj built\n");
                }

                return Task.CompletedTask;
            };

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Built().ShouldBe([$"thatplatypus/crypto/1.0.0/{sandbox.Key("crypto")}/crypto/pmj-build.json", $"thatplatypus/crypto/1.0.0/{sandbox.Key("crypto")}/crypto/🏛"]);
            Directory.GetDirectories(Path.Combine(sandbox.Home, "built", "thatplatypus", "crypto", "1.0.0")).Length.ShouldBe(1);
            sandbox.Read("packages/crypto/🏛").ShouldBe("💭 what the other pmj built\n");
        }

        [Theory]
        [InlineData("install", "--locked")]
        [InlineData("build", "--dependencies-only")]
        public async Task An_archive_put_in_the_cache_under_one_packages_name_that_is_another_packages_is_not_installed_or_built_as_the_first(params string[] command)
        {
            // Someone writes both of a project's files, and puts an archive in the cache themselves,
            // as a tool that has no network does. The archive is deflate's. The lockfile says its
            // digest is that of crypto 1.0.0, and it is kept where crypto 1.0.0 is kept.
            using var sandbox = await WithCryptoInstalledAsync();
            var crypto = sandbox.Lockfile().Packages.Single();
            var deflate = TestPackage.Archive("@thatplatypus/deflate", "0.1.0", "github.com/thatplatypus/deflate");
            var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(deflate));
            sandbox.Write("packmoji.lock", sandbox.Read("packmoji.lock").Replace(crypto.Sha256.Hex, digest));
            sandbox.ForgetCache();
            var kept = Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0");
            Directory.CreateDirectory(kept);
            File.WriteAllBytes(Path.Combine(kept, digest + ".pmj.tar.gz"), deflate);
            sandbox.GitHub.Requests.Clear();
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync(command);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[release.invalid]: ");
            run.Error.ShouldContain("the manifest in its archive is that of \"@thatplatypus/deflate\"");
            Directory.Exists(Path.Combine(kept, digest)).ShouldBeFalse();
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }
    }
}
