using System.Text.Json;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Lockfiles;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj verify</c>: every locked package downloaded again, and it and the cache's copy held to the lockfile. It changes nothing.</summary>
    public sealed class VerifyTests
    {
        private static async Task<Sandbox> InstalledAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            sandbox.GitHub.Requests.Clear();
            return sandbox;
        }

        private static LockedPackage Crypto(Sandbox sandbox) => sandbox.Lockfile().Packages.Single(package => package.Name.Name == "crypto");

        private static string CacheOf(Sandbox sandbox, LockedPackage package, string rest) =>
            Path.Combine(sandbox.Home, "cache", package.Name.Scope, package.Name.Name, package.Version.ToString(), package.Sha256.Hex + rest);

        [Fact]
        public async Task Verify_downloads_every_locked_package_again_though_the_cache_holds_it_and_says_that_all_is_as_locked()
        {
            using var sandbox = await InstalledAsync();

            var run = await sandbox.RunAsync("verify");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Verified 3 packages: the release of each is what packmoji.lock holds.
                The cache's copy of each is what was locked too.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Downloads.Count().ShouldBe(3);
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task Verify_changes_nothing_and_so_leaves_an_empty_cache_empty()
        {
            using var sandbox = await InstalledAsync();
            sandbox.ForgetCache();
            var locked = sandbox.Read("packmoji.lock");

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Verified 3 packages: the release of each is what packmoji.lock holds.
                The cache holds none of them.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Downloads.Count().ShouldBe(3);
            sandbox.Cached().ShouldBeEmpty();
            Directory.Exists(sandbox.Home).ShouldBeFalse();
            sandbox.Read("packmoji.lock").ShouldBe(locked);
        }

        [Fact]
        public async Task A_release_whose_bytes_were_replaced_is_reported_against_the_lockfile()
        {
            using var sandbox = await InstalledAsync();
            sandbox.Upload(Sandbox.Grapevine, "@thatplatypus/crypto", "1.0.0", TestPackage.Archive("@thatplatypus/crypto", "1.0.0", Sandbox.Grapevine, "@thatplatypus/deflate@0.1"));

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[lock.mismatch]: ");
            run.Error.ShouldContain("@thatplatypus/crypto");
            run.Error.ShouldNotContain("@thatplatypus/deflate\" 0.1.0");
        }

        [Fact]
        public async Task An_archive_in_the_cache_that_was_spoiled_is_reported_and_left_as_it_was_found()
        {
            using var sandbox = await InstalledAsync();
            var archive = CacheOf(sandbox, Crypto(sandbox), ".pmj.tar.gz");
            File.SetAttributes(archive, FileAttributes.Normal);
            File.WriteAllText(archive, "spoiled");

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[cache.mismatch]: The cache's copy of \"@thatplatypus/crypto\" 1.0.0 is not what it should be.");
            run.Error.ShouldContain($"fix: delete \"{archive}\"");
            run.Error.ShouldContain("run pmj install");
            File.ReadAllText(archive).ShouldBe("spoiled");
        }

        [Theory]
        [InlineData("change")]
        [InlineData("add")]
        [InlineData("remove")]
        [InlineData("orphan")]
        public async Task Unpacked_files_that_are_no_longer_what_their_archive_holds_are_reported(string how)
        {
            using var sandbox = await InstalledAsync();
            var crypto = Crypto(sandbox);
            var directory = CacheOf(sandbox, crypto, "");
            var source = Path.Combine(directory, "src", "lib.🍇");
            File.SetAttributes(source, FileAttributes.Normal);
            switch (how)
            {
                case "change":
                    File.WriteAllText(source, "💭 @thatplatypus/crypto 1.0.1\n");
                    break;
                case "add":
                    File.WriteAllText(Path.Combine(directory, "src", "extra.🍇"), "💭 planted\n");
                    break;
                case "remove":
                    File.Delete(source);
                    break;
                default:
                    File.SetAttributes(CacheOf(sandbox, crypto, ".pmj.tar.gz"), FileAttributes.Normal);
                    File.Delete(CacheOf(sandbox, crypto, ".pmj.tar.gz"));
                    break;
            }

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[cache.mismatch]: The cache's copy of \"@thatplatypus/crypto\" 1.0.0 is not what it should be.");
            run.Error.ShouldContain($"fix: delete \"{directory}\"");
        }

        [Fact]
        public async Task An_unpacked_file_that_was_replaced_by_a_link_is_reported_though_it_reads_the_same_and_is_as_long()
        {
            // What a link leads to can be changed without the cache being touched, so the same bytes
            // today are no reason to trust it. This one is made as a careful one would be: a link is
            // as long as the path it holds, and the path here is as long as the file was.
            Assert.SkipWhen(OperatingSystem.IsWindows(), "Making a symbolic link there needs a right that a test does not have.");
            using var sandbox = await InstalledAsync();
            var source = Path.Combine(CacheOf(sandbox, Crypto(sandbox), ""), "src", "lib.🍇");
            var content = File.ReadAllBytes(source);
            var twin = "../../" + new string('x', content.Length - "../../".Length);
            File.Copy(source, Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, twin)));
            File.SetAttributes(source, FileAttributes.Normal);
            File.Delete(source);
            File.CreateSymbolicLink(source, twin);
            File.ReadAllBytes(source).ShouldBe(content);
            new FileInfo(source).Length.ShouldBe(content.Length);

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[cache.mismatch]: The cache's copy of \"@thatplatypus/crypto\" 1.0.0 is not what it should be.");
            run.Error.ShouldContain("\"src/lib.🍇\" is not the file the archive holds");
        }

        [Fact]
        public async Task When_GitHub_cannot_be_reached_verify_says_so_and_still_holds_the_cache_to_the_lockfile()
        {
            using var sandbox = await InstalledAsync();
            var archive = CacheOf(sandbox, Crypto(sandbox), ".pmj.tar.gz");
            File.SetAttributes(archive, FileAttributes.Normal);
            File.WriteAllText(archive, "spoiled");
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[github.unreachable]: ");
            run.Error.ShouldContain("error[cache.mismatch]: ");

            // What cannot be reached for one package cannot be reached for the next, and is said once.
            run.Error.Split("error[github.unreachable]").Length.ShouldBe(2);
            sandbox.GitHub.Requests.Count.ShouldBe(1);
        }

        [Fact]
        public async Task Every_package_is_checked_and_every_problem_is_reported_whatever_is_wrong_with_the_one_before()
        {
            using var sandbox = await InstalledAsync();
            var grapevine = CacheOf(sandbox, sandbox.Lockfile().Packages.Single(package => package.Name.Name == "grapevine"), ".pmj.tar.gz");
            File.SetAttributes(grapevine, FileAttributes.Normal);
            File.WriteAllText(grapevine, "spoiled");
            sandbox.Upload(Sandbox.Grapevine, "@thatplatypus/crypto", "1.0.0", TestPackage.Archive("@thatplatypus/crypto", "1.0.0", Sandbox.Grapevine, "@thatplatypus/deflate@0.1"));
            sandbox.Upload(Sandbox.Grapevine, "@thatplatypus/deflate", "0.1.0", System.Text.Encoding.UTF8.GetBytes("no archive at all"));

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[lock.mismatch]: packmoji.lock does not agree with what is published for \"@thatplatypus/crypto\" 1.0.0.");
            run.Error.ShouldContain("error[lock.mismatch]: packmoji.lock does not agree with what is published for \"@thatplatypus/deflate\" 0.1.0.");
            run.Error.ShouldContain("error[cache.mismatch]: The cache's copy of \"@thatplatypus/grapevine\" 0.3.0 is not what it should be.");
            sandbox.GitHub.Downloads.Count().ShouldBe(3);
        }

        [Fact]
        public async Task The_cache_is_said_to_hold_what_it_holds_and_no_more()
        {
            using var sandbox = await InstalledAsync();
            var crypto = Crypto(sandbox);
            File.SetAttributes(CacheOf(sandbox, crypto, ".pmj.tar.gz"), FileAttributes.Normal);
            File.Delete(CacheOf(sandbox, crypto, ".pmj.tar.gz"));
            foreach (var file in Directory.EnumerateFiles(CacheOf(sandbox, crypto, ""), "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(CacheOf(sandbox, crypto, ""), recursive: true);

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(0);
            run.Output.ShouldEndWith($"The cache holds 2 of them, and each of those is what was locked too.{Environment.NewLine}");
        }

        [Fact]
        public async Task With_json_what_was_verified_and_what_was_found_are_one_object_on_standard_output()
        {
            using var sandbox = await InstalledAsync();

            var sound = await sandbox.RunAsync("verify", "--json");

            sound.Status.ShouldBe(0);
            sound.Error.ShouldBeEmpty();
            using (var json = JsonDocument.Parse(sound.Output))
            {
                json.RootElement.GetProperty("ok").GetBoolean().ShouldBeTrue();
                json.RootElement.GetProperty("packages").GetArrayLength().ShouldBe(3);
            }

            sandbox.GitHub.Unreachable = true;
            var failed = await sandbox.RunAsync("verify", "--json");

            failed.Status.ShouldBe(1);
            failed.Error.ShouldBeEmpty();
            using (var json = JsonDocument.Parse(failed.Output))
            {
                json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
                json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().ShouldBe("github.unreachable");
                json.RootElement.GetProperty("packages").GetArrayLength().ShouldBe(3);
            }
        }

        [Fact]
        public async Task Before_anything_is_installed_there_is_nothing_to_verify_against()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("verify");
            var json = await sandbox.RunAsync("verify", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[lock.out-of-date]: There is no packmoji.lock.");
            json.Status.ShouldBe(1);
            json.Error.ShouldBeEmpty();
            json.Output.ShouldContain("\"code\": \"lock.out-of-date\"");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_that_requires_attestation_is_not_called_verified_by_a_pmj_that_cannot_check_one()
        {
            using var sandbox = await InstalledAsync();
            sandbox.RequireAttestation();

            var run = await sandbox.RunAsync("verify");
            var json = await sandbox.RunAsync("verify", "--json");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldStartWith("error[attestation.unverifiable]: ");
            json.Status.ShouldBe(1);
            json.Error.ShouldBeEmpty();
            json.Output.ShouldContain("\"ok\": false");
            json.Output.ShouldContain("\"code\": \"attestation.unverifiable\"");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_that_depends_on_nothing_has_nothing_to_verify()
        {
            using var sandbox = new Sandbox();
            sandbox.Project("@someone/app");
            await sandbox.RunAsync("install");

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"Nothing to verify: the project depends on no package.{Environment.NewLine}");
        }

        [Fact]
        public async Task One_package_is_spoken_of_as_one()
        {
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");
            await sandbox.RunAsync("install");

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Verified 1 package: its release is what packmoji.lock holds.
                The cache's copy of it is what was locked too.

                """.ReplaceLineEndings(Environment.NewLine));
        }
    }
}
