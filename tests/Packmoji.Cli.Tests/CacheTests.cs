using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests
{
    /// <summary>The cache: what was fetched once is not fetched again, and a file in it is what its name says or is not used.</summary>
    public sealed class CacheTests
    {
        private static async Task<(Sandbox Sandbox, string Archive)> InstalledAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            var crypto = sandbox.Lockfile().Packages.Single(package => package.Name.Name == "crypto");
            return (sandbox, Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0", crypto.Sha256.Hex + ".pmj.tar.gz"));
        }

        [Fact]
        public async Task An_archive_in_the_cache_that_is_no_longer_what_its_name_says_is_fetched_again_and_put_right()
        {
            var (sandbox, archive) = await InstalledAsync();
            using var _ = sandbox;
            var sound = File.ReadAllBytes(archive);
            File.SetAttributes(archive, FileAttributes.Normal);
            File.WriteAllText(archive, "spoiled");
            sandbox.GitHub.Requests.Clear();

            var run = await sandbox.RunAsync("install");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.GitHub.Downloads.ShouldBe(["/thatplatypus/grapevine/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz"]);
            File.ReadAllBytes(archive).ShouldBe(sound);
        }

        [Fact]
        public async Task A_spoiled_archive_is_never_unpacked_when_GitHub_cannot_be_asked_for_a_sound_one()
        {
            var (sandbox, archive) = await InstalledAsync();
            using var _ = sandbox;
            File.SetAttributes(archive, FileAttributes.Normal);
            File.WriteAllBytes(archive, TestPackage.Archive("@thatplatypus/crypto", "1.0.0", Sandbox.Grapevine, "@thatplatypus/deflate@0.1"));
            Directory.Delete(Path.ChangeExtension(Path.ChangeExtension(Path.ChangeExtension(archive, null), null), null), recursive: true);
            sandbox.GitHub.Unreachable = true;

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[github.unreachable]: ");
            sandbox.Cached().Where(file => file.StartsWith("thatplatypus/crypto/", StringComparison.Ordinal)).Count().ShouldBe(1);
        }

        [Fact]
        public async Task A_cache_that_cannot_be_written_is_a_problem_that_says_how_to_keep_it_elsewhere()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");
            File.WriteAllText(sandbox.Home, "a file where the directory should be");

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[cache.unusable]: ");
            run.Error.ShouldContain("setting PACKMOJI_HOME");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task Two_files_that_this_disk_holds_as_one_are_never_unpacked_one_over_the_other()
        {
            // A sharp s and two letters s are one name on a Mac, and two on Linux. No rule pmj can
            // check without the tables of Unicode tells them apart, so the disk is the judge.
            using var sandbox = new Sandbox();
            var archive = Packmoji.Core.Archives.PackageArchive.Write(
            [
                TestPackage.File("packmoji.json", TestPackage.Manifest("@thatplatypus/crypto", "1.0.0", "library", null)),
                TestPackage.File("src/STRASSE.x", "the first"),
                TestPackage.File("src/stra\u00DFe.x", "the second"),
                TestPackage.File("src/lib.🍇", "💭\n"),
            ]);
            sandbox.Upload("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0", archive);
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");
            var unpacked = Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive)));

            var run = await sandbox.RunAsync("install");

            if (run.Status == 0)
            {
                File.ReadAllText(Path.Combine(unpacked, "src", "STRASSE.x")).ShouldBe("the first");
                File.ReadAllText(Path.Combine(unpacked, "src", "stra\u00DFe.x")).ShouldBe("the second");
                Directory.GetFiles(Path.Combine(unpacked, "src")).Length.ShouldBe(3);
            }
            else
            {
                run.Status.ShouldBe(1);
                run.Output.ShouldBeEmpty();
                run.Error.ShouldContain("error[archive.invalid]: The archive of \"@thatplatypus/crypto\" 1.0.0 cannot be unpacked on this disk.");
                run.Error.ShouldContain("is the same file here as another of the package's files");
                sandbox.Has("packmoji.lock").ShouldBeFalse();
                Directory.Exists(unpacked).ShouldBeFalse();
                sandbox.Cached().ShouldNotContain(file => file.Contains(".tmp-", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task When_what_was_chosen_cannot_be_unpacked_the_project_is_not_left_locked_to_it()
        {
            using var sandbox = new Sandbox();
            var archive = sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            // Something that is not a directory stands where the files would be unpacked.
            var version = Path.Combine(sandbox.Home, "cache", "thatplatypus", "crypto", "1.0.0");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(archive))), "in the way");

            var run = await sandbox.RunAsync("install");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[cache.unusable]: ");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
            sandbox.Cached().ShouldNotContain(file => file.Contains(".tmp-", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Nothing_is_left_beside_the_files_pmj_writes()
        {
            var (sandbox, _) = await InstalledAsync();
            using var disposed = sandbox;

            sandbox.Files().ShouldBe(["packmoji.json", "packmoji.lock"]);
            sandbox.Cached().ShouldNotContain(file => file.Contains(".tmp-", StringComparison.Ordinal));
        }
    }
}
