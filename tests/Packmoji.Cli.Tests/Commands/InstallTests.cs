using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj install</c>: what the lockfile holds, fetched, and nothing chosen unless the manifest asks for something else.</summary>
    public sealed class InstallTests
    {
        private static readonly string[] GrapevineLocked =
        [
            "@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine",
            "@thatplatypus/deflate 0.1.0 in github.com/thatplatypus/grapevine",
            "@thatplatypus/grapevine 0.3.0 in github.com/thatplatypus/grapevine",
        ];

        [Fact]
        public async Task With_no_lockfile_install_resolves_writes_one_and_fetches_what_it_holds()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync("install");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                packmoji.lock now holds 3 packages:
                  + @thatplatypus/crypto 1.0.0
                  + @thatplatypus/deflate 0.1.0
                  + @thatplatypus/grapevine 0.3.0

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Locked().ShouldBe(GrapevineLocked);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Files().ShouldBe(["packmoji.json", "packmoji.lock"]);
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task What_is_fetched_is_kept_under_its_own_digest_with_its_files_beside_it()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            await sandbox.RunAsync("install");

            var crypto = sandbox.Lockfile().Packages.Single(package => package.Name.Name == "crypto");
            var sha = crypto.Sha256.Hex[..8];
            sandbox.Cached().Where(file => file.StartsWith("thatplatypus/crypto/", StringComparison.Ordinal)).ShouldBe(
            [
                $"thatplatypus/crypto/1.0.0/{sha}.pmj.tar.gz",
                $"thatplatypus/crypto/1.0.0/{sha}/packmoji.json",
                $"thatplatypus/crypto/1.0.0/{sha}/src/lib.🍇",
            ]);
            sandbox.Cached().Count.ShouldBe(9);

            var cache = Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0");
            File.ReadAllText(Path.Combine(cache, crypto.Sha256.Hex, "src", "lib.🍇")).ShouldBe("💭 @thatplatypus/crypto 1.0.0\n");
            File.GetAttributes(Path.Combine(cache, crypto.Sha256.Hex + ".pmj.tar.gz")).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            File.GetAttributes(Path.Combine(cache, crypto.Sha256.Hex, "src", "lib.🍇")).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
        }

        [Fact]
        public async Task A_lockfile_that_answers_the_manifest_is_installed_without_a_word_to_GitHub_when_the_cache_holds_it()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            await sandbox.RunAsync("install");
            var locked = sandbox.Read("packmoji.lock");
            var written = File.GetLastWriteTimeUtc(sandbox.PathOf("packmoji.lock"));
            sandbox.GitHub.Requests.Clear();
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("install");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Installed what packmoji.lock holds: 3 packages.{Environment.NewLine}");
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            File.GetLastWriteTimeUtc(sandbox.PathOf("packmoji.lock")).ShouldBe(written);
        }

        [Fact]
        public async Task On_a_machine_with_nothing_fetched_install_downloads_exactly_what_is_locked_from_where_it_is_locked()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            await sandbox.RunAsync("install");
            sandbox.ForgetCache();
            sandbox.GitHub.Requests.Clear();

            var run = await sandbox.RunAsync("install", "--locked");

            run.Status.ShouldBe(0);
            sandbox.GitHub.Downloads.ShouldBe(
            [
                "/thatplatypus/grapevine/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "/thatplatypus/grapevine/releases/download/deflate-v0.1.0/deflate-0.1.0.pmj.tar.gz",
                "/thatplatypus/grapevine/releases/download/grapevine-v0.3.0/grapevine-0.3.0.pmj.tar.gz",
            ]);
            sandbox.GitHub.Listings.ShouldBeEmpty();
            sandbox.Cached().Count.ShouldBe(9);
        }

        [Fact]
        public async Task A_release_whose_bytes_are_no_longer_what_was_locked_stops_install()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            await sandbox.RunAsync("install");
            var locked = sandbox.Read("packmoji.lock");
            sandbox.ForgetCache();
            sandbox.Upload(Sandbox.Grapevine, "@thatplatypus/crypto", "1.0.0", TestPackage.Archive("@thatplatypus/crypto", "1.0.0", Sandbox.Grapevine, "@thatplatypus/deflate@0.1"));

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[lock.mismatch]: ");
            run.Error.ShouldContain("@thatplatypus/crypto");
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.Cached().ShouldNotContain(file => file.EndsWith("/src/lib.🍇", StringComparison.Ordinal));

            // What stands where the locked release was is not kept, under any name.
            sandbox.Cached().ShouldNotContain(file => file.StartsWith("thatplatypus/crypto/", StringComparison.Ordinal));
        }

        [Fact]
        public async Task With_locked_a_lockfile_that_would_have_to_be_written_or_changed_is_an_error_and_nothing_is_asked_or_written()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var none = await sandbox.RunAsync("install", "--locked");

            none.Status.ShouldBe(1);
            none.Error.ShouldContain("error[lock.out-of-date]: There is no packmoji.lock, and --locked forbids writing one.");
            sandbox.Has("packmoji.lock").ShouldBeFalse();

            await sandbox.RunAsync("install");
            var locked = sandbox.Read("packmoji.lock");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0");
            sandbox.GitHub.Requests.Clear();

            var stale = await sandbox.RunAsync("install", "--locked");

            stale.Status.ShouldBe(1);
            stale.Output.ShouldBeEmpty();
            stale.Error.ShouldContain("error[lock.out-of-date]: packmoji.lock no longer answers what packmoji.json asks for, and --locked forbids changing it.");
            stale.Error.ShouldContain("fix: run pmj install without --locked");
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_manifest_that_asks_for_something_else_than_was_locked_is_resolved_again()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/extra", "@thatplatypus/extra", "2.1.0");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            await sandbox.RunAsync("install");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "dev:@thatplatypus/extra@2.1");

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"packmoji.lock now holds 4 packages:{Environment.NewLine}  + @thatplatypus/extra 2.1.0{Environment.NewLine}");
            sandbox.Locked().ShouldBe([.. GrapevineLocked[..2], "@thatplatypus/extra 2.1.0 in github.com/thatplatypus/extra", GrapevineLocked[2]]);
        }

        [Fact]
        public async Task A_manifest_that_was_only_rearranged_or_respelled_is_not_resolved_and_the_lockfile_is_left_as_it_is()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "@thatplatypus/crypto@1.0");
            await sandbox.RunAsync("install");
            var locked = sandbox.Read("packmoji.lock");
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0.0", "@thatplatypus/grapevine@0.3.0");

            var run = await sandbox.RunAsync("install", "--locked");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Installed what packmoji.lock holds: 3 packages.{Environment.NewLine}");
            sandbox.Read("packmoji.lock").ShouldBe(locked);
        }

        [Fact]
        public async Task A_project_that_depends_on_nothing_gets_a_lockfile_that_holds_nothing()
        {
            using var sandbox = new Sandbox();
            sandbox.Project("@someone/app");

            var first = await sandbox.RunAsync("install");
            var second = await sandbox.RunAsync("install");

            first.Status.ShouldBe(0);
            first.Output.ShouldBe($"packmoji.lock holds no package: the project depends on none.{Environment.NewLine}");
            sandbox.Lockfile().Packages.ShouldBeEmpty();
            second.Status.ShouldBe(0);
            second.Output.ShouldBe($"Nothing to install: the project depends on no package.{Environment.NewLine}");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task When_GitHub_cannot_be_reached_install_says_so_and_ends_as_a_problem_and_not_as_a_fault()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldStartWith("error[github.unreachable]: GitHub could not be reached.");
            run.Error.ShouldContain("  fix: check the network, and try again");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_run_that_is_stopped_ends_quietly_with_the_status_of_an_interruption_and_writes_nothing()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Stop = new CancellationToken(canceled: true);

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(130);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_run_that_is_stopped_part_way_keeps_whole_what_had_arrived_and_leaves_nothing_half_done()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            var manifest = sandbox.Read("packmoji.json");
            using var stop = new CancellationTokenSource();
            sandbox.Stop = stop.Token;

            // Stopped at the worst moment there is: as the last byte of the second archive arrives, and before it is kept.
            sandbox.GitHub.Delivered = path =>
            {
                if (path.EndsWith("/crypto-1.0.0.pmj.tar.gz", StringComparison.Ordinal))
                {
                    stop.Cancel();
                }
            };

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Status.ShouldBe(130);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBeEmpty();
            sandbox.Files().ShouldBe(["packmoji.json"]);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.GitHub.Downloads.Count().ShouldBe(3);

            var kept = Directory.EnumerateFiles(Path.Combine(sandbox.Home, "cache"), "*", SearchOption.AllDirectories).ToList();
            kept.Select(Path.GetFileName).ShouldAllBe(file => file!.EndsWith(".pmj.tar.gz", StringComparison.Ordinal));
            kept.Select(file => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)))).Order(StringComparer.Ordinal).ShouldBe(["crypto", "grapevine"]);
            foreach (var archive in kept)
            {
                Path.GetFileName(archive).ShouldBe(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive))) + ".pmj.tar.gz");
            }
        }

        [Fact]
        public async Task What_cannot_be_resolved_is_reported_whole_and_nothing_is_written()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/other", "@thatplatypus/other", "1.0.0", "@thatplatypus/crypto@2.0");
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "2.0.0");
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3", "@thatplatypus/other@1.0");

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[resolve.line-conflict]: ");
            run.Error.ShouldContain("@someone/app → @thatplatypus/grapevine@0.3.0 → @thatplatypus/crypto@1.0");
            run.Error.ShouldContain("@someone/app → @thatplatypus/other@1.0.0 → @thatplatypus/crypto@2.0");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Theory]
        [InlineData("install")]
        [InlineData("install", "--locked")]
        [InlineData("add", "@thatplatypus/crypto@1.0")]
        [InlineData("add", "@thatplatypus/deflate")]
        [InlineData("remove", "@thatplatypus/grapevine")]
        public async Task A_project_that_requires_attestation_is_refused_because_pmj_cannot_check_one_yet(params string[] args)
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            sandbox.RequireAttestation();
            var manifest = sandbox.Read("packmoji.json");
            var locked = sandbox.Read("packmoji.lock");
            sandbox.GitHub.Requests.Clear();

            var run = await sandbox.RunAsync(args);

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldStartWith("error[attestation.unverifiable]: This project requires attestation, and pmj cannot verify one yet.");
            run.Error.ShouldContain("  why: packmoji.json sets \"requireAttestation\"");
            run.Error.ShouldContain("  fix: ");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("https://github.com/thatplatypus/Grapevine", "repository.invalid")]
        [InlineData("grapevine", "repository.invalid")]
        public async Task A_place_to_look_that_is_not_a_repository_is_refused_before_anything_is_asked(string repository, string code)
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("install", "--repository", repository);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"error[{code}]: ");
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task Several_places_to_look_can_be_given_and_one_of_another_owner_is_never_asked_for_this_owners_packages()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("install", "--repository", "github.com/someone/else", "--repository", Sandbox.Grapevine);

            run.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine"]);
            sandbox.GitHub.Downloads.ShouldNotContain(path => path.StartsWith("/someone/", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_lockfile_that_cannot_be_written_is_a_problem_that_names_the_file_and_leaves_nothing_behind()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            Directory.CreateDirectory(sandbox.PathOf("packmoji.lock"));

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain($"error[project.unreadable]: \"{sandbox.PathOf("packmoji.lock")}\" could not be written.");
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task A_lockfile_that_does_not_read_stops_install_with_the_place_of_the_problem()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("packmoji.lock", "{ \"version\": 7 }");

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("packmoji.lock:1:14: error[lock.unsupported-version]: ");
            sandbox.Read("packmoji.lock").ShouldBe("{ \"version\": 7 }");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_release_that_is_not_the_package_it_is_tagged_as_is_refused_by_name()
        {
            using var sandbox = new Sandbox();
            sandbox.Upload("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0", TestPackage.Archive("@thatplatypus/deflate", "1.0.0", "github.com/thatplatypus/crypto"));
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[release.invalid]: The release crypto-v1.0.0 in github.com/thatplatypus/crypto is not \"@thatplatypus/crypto\" 1.0.0.");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }
    }
}
