using System.Net;
using System.Text;
using System.Text.Json;
using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// pmj with a registry named. Versions are listed and chosen from the registry's files, with
    /// nothing asked of GitHub's API and nothing downloaded to find out what a version needs. What
    /// was chosen is downloaded from GitHub itself, and held to what the registry recorded of it.
    /// </summary>
    public sealed class RegistryTests
    {
        private const string Grapevine = "v1/packages/thatplatypus/grapevine.json";
        private const string Crypto = "v1/packages/thatplatypus/crypto.json";
        private const string Deflate = "v1/packages/thatplatypus/deflate.json";

        // Grapevine as it is, a project that asks for nothing yet, and a registry that has indexed Grapevine's repository.
        private static async Task<Sandbox> WithARegistryAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            (await sandbox.IndexAsync(Sandbox.Grapevine)).Count.ShouldBe(3);
            return sandbox;
        }

        [Fact]
        public async Task Add_lists_and_chooses_from_the_registry_and_downloads_what_it_chose_from_GitHub_itself()
        {
            using var sandbox = await WithARegistryAsync();

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Added @thatplatypus/grapevine 0.3.0 to dependencies.
                packmoji.lock now holds 3 packages:
                  + @thatplatypus/crypto 1.0.0
                  + @thatplatypus/deflate 0.1.0
                  + @thatplatypus/grapevine 0.3.0

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Listings.ShouldBeEmpty("a registry lists versions, so GitHub's API is asked nothing");
            sandbox.GitHub.RegistryReads.Order(StringComparer.Ordinal).ToArray().ShouldBe([Crypto, Deflate, Grapevine], customMessage: "each package's file is read once");
            sandbox.GitHub.Downloads.Order(StringComparer.Ordinal).ShouldBe(
            [
                "/thatplatypus/grapevine/releases/download/crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "/thatplatypus/grapevine/releases/download/deflate-v0.1.0/deflate-0.1.0.pmj.tar.gz",
                "/thatplatypus/grapevine/releases/download/grapevine-v0.3.0/grapevine-0.3.0.pmj.tar.gz",
            ]);
            sandbox.Cached().Count.ShouldBe(9);
        }

        [Fact]
        public async Task The_two_files_a_registry_leads_to_are_to_the_byte_what_GitHub_itself_leads_to()
        {
            using var through = await WithARegistryAsync();
            using var direct = Sandbox.WithGrapevine();
            direct.Project("@someone/app");

            var one = await through.RunAsync("add", "@thatplatypus/grapevine");
            var other = await direct.RunAsync("add", "@thatplatypus/grapevine");

            one.Status.ShouldBe(0);
            through.GitHub.RegistryReads.ShouldNotBeEmpty();
            direct.GitHub.RegistryReads.ShouldBeEmpty();
            through.Read("packmoji.lock").ShouldBe(direct.Read("packmoji.lock"));
            through.Read("packmoji.json").ShouldBe(direct.Read("packmoji.json"));
            one.Output.ShouldBe(other.Output);
            through.Cached().ShouldBe(direct.Cached());
        }

        [Fact]
        public async Task Install_with_no_lockfile_resolves_through_the_registry()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Project("@someone/app", "@thatplatypus/grapevine@0.3");

            var run = await sandbox.RunAsync("install");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Locked().Count.ShouldBe(3);
            sandbox.GitHub.Listings.ShouldBeEmpty();
            sandbox.GitHub.RegistryReads.Order(StringComparer.Ordinal).ShouldBe([Crypto, Deflate, Grapevine]);
        }

        [Fact]
        public async Task An_install_of_what_is_locked_asks_the_registry_only_about_what_the_cache_does_not_hold()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            var asked = sandbox.GitHub.Requests.Count;

            var fromTheCache = await sandbox.RunAsync("install", "--locked");

            fromTheCache.Error.ShouldBeEmpty();
            fromTheCache.Status.ShouldBe(0);
            sandbox.GitHub.Requests.Count.ShouldBe(asked, "what the lockfile holds is in the cache, so nobody is asked anything");

            sandbox.ForgetCache();
            var fetched = await sandbox.RunAsync("install", "--locked");

            fetched.Error.ShouldBeEmpty();
            fetched.Status.ShouldBe(0);
            fetched.Output.ShouldBe("Installed what packmoji.lock holds: 3 packages." + Environment.NewLine);
            sandbox.GitHub.RegistryReads.Skip(3).Order(StringComparer.Ordinal).ShouldBe([Crypto, Deflate, Grapevine]);
            sandbox.GitHub.Listings.ShouldBeEmpty();
            sandbox.Cached().Count.ShouldBe(9);
        }

        [Fact]
        public async Task Update_raises_a_requirement_to_the_latest_the_registry_holds_on_its_line()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/crypto")).Status.ShouldBe(0);
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "1.4.0");
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "2.0.0");

            var before = await sandbox.RunAsync("update");
            before.Output.ShouldBe("Every requirement already asks for the latest version on its line." + Environment.NewLine, customMessage: "a release is in the registry when a run has seen it, and not before");

            (await sandbox.IndexAsync(Sandbox.Grapevine)).ShouldBe(["added @thatplatypus/crypto 1.4.0 from github.com/thatplatypus/grapevine", "added @thatplatypus/crypto 2.0.0 from github.com/thatplatypus/grapevine"]);
            var run = await sandbox.RunAsync("update");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("Raised 1 requirement in packmoji.json:" + Environment.NewLine + "  @thatplatypus/crypto from 1.0.0 to 1.4.0");
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.4.0 in github.com/thatplatypus/grapevine"]);
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task Remove_locks_what_is_left_through_the_registry()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            (await sandbox.RunAsync("add", "@thatplatypus/crypto")).Status.ShouldBe(0);

            var run = await sandbox.RunAsync("remove", "@thatplatypus/grapevine");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine"]);
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_build_fetches_what_is_locked_through_the_registry_and_builds_it()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            sandbox.ForgetCache();
            var asked = sandbox.GitHub.RegistryReads.Count();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.GitHub.RegistryReads.Count().ShouldBe(asked + 3);
            sandbox.GitHub.Listings.ShouldBeEmpty();
            sandbox.Files("packages").ShouldNotBeEmpty();
        }

        [Fact]
        public async Task A_package_the_registry_does_not_have_is_said_to_be_that_and_GitHub_is_not_asked_in_its_place()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release("github.com/thatplatypus/sockets_more", "@thatplatypus/sockets_more", "1.0.0");

            var run = await sandbox.RunAsync("add", "@thatplatypus/sockets_more");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[package.not-found]: "@thatplatypus/sockets_more" is not in the registry.
                  why: https://registry.example/files/v1/packages/thatplatypus/sockets_more.json is not there
                  fix: check that https://registry.example/files is the address a registry's v1 is under, for one that is not answers the same way; if it is, and the package's repository is on the registry's list, a release is in the registry within a day; to ask GitHub itself, run the command again with --direct

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.Select(request => request.Uri.Host).Distinct().ToArray().ShouldBe(["registry.example"], customMessage: "someone who names a registry is held to it, and pmj does not look past it on its own");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        // An address with no registry behind it answers as a registry does of a package it does not
        // have. So what is said names the file that was asked for, where a mistake in the address
        // can be seen, and says first to check the address.
        [Fact]
        public async Task An_address_with_no_registry_behind_it_is_told_which_file_was_not_there_and_to_check_the_address()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            sandbox.NamesRegistry = true;

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("  why: https://registry.example/files/v1/packages/thatplatypus/grapevine.json is not there" + Environment.NewLine);
            run.Error.ShouldContain("  fix: check that https://registry.example/files is the address a registry's v1 is under, for one that is not answers the same way; ");
            sandbox.GitHub.Downloads.ShouldBeEmpty();
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task Told_to_ask_GitHub_itself_on_the_command_line_pmj_asks_the_registry_nothing()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release("github.com/thatplatypus/sockets_more", "@thatplatypus/sockets_more", "1.0.0");

            var run = await sandbox.RunAsync("add", "@thatplatypus/sockets_more", "--direct");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.GitHub.RegistryReads.ShouldBeEmpty();
            sandbox.GitHub.Listings.ShouldBe(["/repos/thatplatypus/sockets_more/releases"]);
            sandbox.Locked().ShouldBe(["@thatplatypus/sockets_more 1.0.0 in github.com/thatplatypus/sockets_more"]);
        }

        [Fact]
        public async Task The_option_is_taken_wherever_on_the_line_it_is_written()
        {
            using var sandbox = await WithARegistryAsync();

            (await sandbox.RunAsync("--direct", "add", "@thatplatypus/grapevine")).Status.ShouldBe(0);

            sandbox.GitHub.RegistryReads.ShouldBeEmpty();
        }

        [Theory]
        [InlineData(VersionStatus.Yanked)]
        [InlineData(VersionStatus.Quarantined)]
        public async Task A_version_that_was_withdrawn_or_quarantined_is_not_the_latest_that_add_chooses(VersionStatus status)
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "1.4.0");
            await sandbox.IndexAsync(Sandbox.Grapevine);
            sandbox.Rerecord("@thatplatypus/crypto", "1.4.0", version => Marked(version, status));

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("Added @thatplatypus/crypto 1.0.0 to dependencies.");
        }

        [Theory]
        [InlineData(VersionStatus.Yanked, "resolve.yanked")]
        [InlineData(VersionStatus.Quarantined, "resolve.quarantined")]
        public async Task A_version_that_was_withdrawn_or_quarantined_is_refused_when_it_is_named(VersionStatus status, string code)
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, status));

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.0.0");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith($"error[{code}]: Version 1.0.0 of \"@thatplatypus/crypto\" ");
            sandbox.GitHub.Downloads.ShouldBeEmpty("what may not be used is not downloaded");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_locked_version_that_was_withdrawn_since_still_installs_with_a_warning()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/crypto")).Status.ShouldBe(0);
            sandbox.ForgetCache();
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, VersionStatus.Yanked));

            var run = await sandbox.RunAsync("install", "--locked");

            run.Status.ShouldBe(0);
            run.Error.ShouldStartWith("warning[resolve.yanked-locked]: Version 1.0.0 of \"@thatplatypus/crypto\" has been yanked.");
            sandbox.Cached().Count.ShouldBe(3);
        }

        [Fact]
        public async Task A_locked_version_that_was_quarantined_since_is_not_fetched()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/crypto")).Status.ShouldBe(0);
            sandbox.ForgetCache();
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, VersionStatus.Quarantined));

            var run = await sandbox.RunAsync("install", "--locked");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[resolve.quarantined]: Version 1.0.0 of \"@thatplatypus/crypto\" is quarantined.");
            sandbox.GitHub.Downloads.Count().ShouldBe(1, "only the download of the add that came before");
            sandbox.Cached().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_whose_every_version_was_withdrawn_or_quarantined_has_none_for_add_to_begin_with()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, VersionStatus.Quarantined));

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[version.none-released]: No version of "@thatplatypus/crypto" can be begun with.
                  why: the registry holds versions of it, and each has been withdrawn by its author or quarantined
                  fix: ask its author for a new release: a version that was withdrawn or quarantined is refused when it is named, too

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task A_package_with_pre_releases_alone_is_said_to_have_them_in_the_registry()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/sockets_more", "1.0.0-rc.1");
            await sandbox.IndexAsync(Sandbox.Grapevine);

            var run = await sandbox.RunAsync("add", "@thatplatypus/sockets_more");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("  why: the registry holds 1 of it, each a pre-release, and pmj takes a pre-release only when it is named");
            run.Error.ShouldContain("  fix: to use one, name it: pmj add @thatplatypus/sockets_more@1.0.0-rc.1");
        }

        [Fact]
        public async Task Update_says_which_package_the_registry_does_not_have()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release("github.com/thatplatypus/sockets_more", "@thatplatypus/sockets_more", "1.0.0");
            sandbox.Project("@someone/app", "@thatplatypus/sockets_more@1.0");

            var run = await sandbox.RunAsync("update");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[package.not-found]: \"@thatplatypus/sockets_more\" is not in the registry.");
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task Update_with_dry_run_says_what_the_registry_would_lead_to_and_writes_and_downloads_nothing()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/crypto")).Status.ShouldBe(0);
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "1.4.0");
            await sandbox.IndexAsync(Sandbox.Grapevine);
            var (manifest, locked, downloads) = (sandbox.Read("packmoji.json"), sandbox.Read("packmoji.lock"), sandbox.GitHub.Downloads.Count());

            var run = await sandbox.RunAsync("update", "--dry-run");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("Would raise 1 requirement in packmoji.json:");
            run.Output.ShouldEndWith("Nothing was written." + Environment.NewLine);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.GitHub.Downloads.Count().ShouldBe(downloads, "a registry says what a version needs, so nothing is downloaded to find out");
        }

        [Fact]
        public async Task A_registry_that_cannot_be_read_stops_the_command_and_GitHub_is_not_asked_in_its_place()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.GitHub.RegistryAnswer = HttpStatusCode.InternalServerError;

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[registry.unreachable]: The registry could not be read.
                  why: the request for https://registry.example/files/v1/packages/thatplatypus/grapevine.json was answered with status 500
                  fix: check the network and the address of the registry, and try again; to ask GitHub itself in the meantime, run the command again with --direct

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.Count.ShouldBe(1);
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task What_is_locked_and_not_in_the_cache_is_not_fetched_while_the_registry_cannot_be_read()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            sandbox.ForgetCache();
            var downloads = sandbox.GitHub.Downloads.Count();
            sandbox.GitHub.RegistryAnswer = HttpStatusCode.TooManyRequests;

            var run = await sandbox.RunAsync("install", "--locked");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[registry.unreachable]: The registry could not be read.");
            sandbox.GitHub.Downloads.Count().ShouldBe(downloads);
            sandbox.Cached().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_file_of_the_registry_that_does_not_read_stops_the_command_and_names_the_file()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.GitHub.Registry[Crypto] = Encoding.UTF8.GetBytes("{ \"formatVersion\": 1 ");

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[registry.invalid]: The registry's file of \"@thatplatypus/crypto\" cannot be read." + Environment.NewLine + "  why: https://registry.example/files/v1/packages/thatplatypus/crypto.json does not read: ");
            run.Error.ShouldContain("  fix: tell whoever keeps the registry; to ask GitHub itself in the meantime, run the command again with --direct");
            sandbox.GitHub.Downloads.ShouldBeEmpty();
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task Bytes_that_are_not_the_ones_the_registry_recorded_are_refused_and_nothing_is_written_or_kept_of_them()
        {
            using var sandbox = await WithARegistryAsync();
            var recorded = sandbox.Registry.Packages.Single(package => package.Name.Name == "crypto").Versions[0].Sha256;
            sandbox.Upload(Sandbox.Grapevine, "@thatplatypus/crypto", "1.0.0", TestPackage.Archive("@thatplatypus/crypto", "1.0.0", Sandbox.Grapevine, "@thatplatypus/deflate@0.1"));

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith(
                "error[registry.mismatch]: What GitHub serves for \"@thatplatypus/crypto\" 1.0.0 is not what the registry recorded." + Environment.NewLine
                + $"  why: the registry recorded the SHA-256 {recorded} when it first saw the release crypto-v1.0.0 in github.com/thatplatypus/grapevine, and the file there now has ");
            run.Error.ShouldContain("  fix: do not use this version.");
            sandbox.Files().ShouldBe(["packmoji.json"]);
            sandbox.Manifest().Dependencies.ShouldBeNull();
            sandbox.Cached().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_record_that_says_a_version_needs_other_than_its_own_manifest_says_is_refused_and_nothing_is_kept_of_it()
        {
            // A registry that is wrong, or that someone changed, must not have a resolution choose by one set of dependencies and a build use another.
            using var sandbox = await WithARegistryAsync();
            VersionRequirement.TryParse("0.1", out var requirement, out _).ShouldBeTrue();
            var deflate = sandbox.Registry.Packages.Single(package => package.Name.Name == "deflate").Name;
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => version with { Dependencies = [new Dependency(deflate, requirement!)] });

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[registry.mismatch]: What GitHub serves for \"@thatplatypus/crypto\" 1.0.0 is not what the registry recorded.");
            run.Error.ShouldContain("  why: the registry says it needs @thatplatypus/deflate@0.1, and the manifest in its archive says nothing");
            sandbox.Files().ShouldBe(["packmoji.json"]);
            sandbox.Cached().Where(file => file.Contains("/crypto/", StringComparison.Ordinal)).ShouldBeEmpty();
        }

        // With the bytes in the cache nothing is downloaded, so this is the only place such a record
        // can be found out before the two files are written from it.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_record_that_says_otherwise_than_the_manifest_is_refused_though_the_cache_already_holds_the_version(bool archiveAlone)
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/crypto")).Status.ShouldBe(0);
            if (archiveAlone)
            {
                foreach (var unpacked in Directory.GetDirectories(Path.Combine(sandbox.Home, "cache"), "*", SearchOption.AllDirectories).Where(directory => Path.GetFileName(directory).Length == 64))
                {
                    Sandbox.Forget(unpacked);
                }
            }

            VersionRequirement.TryParse("0.1", out var requirement, out _).ShouldBeTrue();
            var deflate = sandbox.Registry.Packages.Single(package => package.Name.Name == "deflate").Name;
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => version with { Dependencies = [new Dependency(deflate, requirement!)] });
            var (manifest, locked, downloads) = (sandbox.Read("packmoji.json"), sandbox.Read("packmoji.lock"), sandbox.GitHub.Downloads.Count());

            var run = await sandbox.RunAsync("add", "@thatplatypus/deflate");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[registry.mismatch]: What GitHub serves for \"@thatplatypus/crypto\" 1.0.0 is not what the registry recorded.");
            run.Error.ShouldContain("  why: the registry says it needs @thatplatypus/deflate@0.1, and the manifest in its archive says nothing");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.GitHub.Downloads.Count().ShouldBe(downloads, "the version's bytes are in the cache, and nothing else is fetched once the record is found out");
        }

        [Fact]
        public async Task A_release_the_registry_recorded_and_that_is_gone_is_said_to_be_gone()
        {
            using var sandbox = new Sandbox();
            sandbox.Project("@someone/app");
            var archive = sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            await sandbox.IndexAsync("github.com/thatplatypus/crypto");
            using var elsewhere = new Sandbox();
            elsewhere.Project("@someone/app");
            elsewhere.Serve(sandbox.Registry);
            elsewhere.NamesRegistry = true;
            archive.ShouldNotBeEmpty();

            var run = await elsewhere.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("  why: the release crypto-v1.0.0 in github.com/thatplatypus/crypto, which the registry recorded, is no longer there");
            elsewhere.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task A_package_outside_the_scopes_pmj_is_limited_to_is_not_asked_of_the_registry()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Variables["PACKMOJI_SCOPES"] = "someone";

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[scope.not-allowed]: \"@thatplatypus/grapevine\" is outside the scopes pmj is limited to here.");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_outside_the_scopes_that_an_allowed_package_depends_on_is_refused_and_its_file_is_never_asked_for()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/someone/wrapper", "@someone/wrapper", "1.0.0", "@thatplatypus/crypto@1.0");
            sandbox.Project("@someone/app", "@someone/wrapper@1.0");
            await sandbox.IndexAsync(Sandbox.Grapevine, "github.com/someone/wrapper");
            sandbox.Variables["PACKMOJI_SCOPES"] = "someone";

            var run = await sandbox.RunAsync("install");

            // In the resolver's own words, which say what led to it. The place that reads the
            // registry would refuse it too, and would not know what led there.
            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[scope.not-allowed]: "@thatplatypus/crypto" is outside the scopes pmj is limited to here.
                  why: "@someone/wrapper" 1.0.0 depends on it (@someone/app → @someone/wrapper@1.0.0 → @thatplatypus/crypto@1.0), and PACKMOJI_SCOPES allows only someone
                  fix: depend only on packages of that scope; the list is set by whoever runs pmj here

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.RegistryReads.ShouldBe(["v1/packages/someone/wrapper.json"]);
            sandbox.GitHub.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_every_package_inside_the_scopes_a_registry_is_read_as_it_is_with_no_limit()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Variables["PACKMOJI_SCOPES"] = "thatplatypus";

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Locked().Count.ShouldBe(3);
        }

        [Theory]
        [InlineData("add", "@thatplatypus/grapevine", "--repository", "github.com/thatplatypus/grapevine")]
        [InlineData("add", "@thatplatypus/grapevine", "--repository", "nowhere at all")]
        [InlineData("install", "--repository", "github.com/thatplatypus/grapevine")]
        [InlineData("update", "--repository", "github.com/thatplatypus/grapevine")]
        [InlineData("remove", "@thatplatypus/crypto", "--repository", "github.com/thatplatypus/grapevine", "--repository", "github.com/thatplatypus/crypto")]
        public async Task A_repository_cannot_be_named_while_a_registry_is_for_the_registry_says_where_a_package_lives(params string[] command)
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync(command);

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[registry.repository]: --repository cannot be given while a registry is named.
                  why: the registry says where each package lives, and pmj looks nowhere else for one
                  fix: leave --repository out; or give --direct as well, and pmj asks GitHub itself and asks the registry nothing

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.Requests.ShouldBeEmpty();
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task A_tool_is_told_that_a_repository_cannot_be_named_in_its_answer()
        {
            using var sandbox = await WithARegistryAsync();

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine", "--repository", "github.com/thatplatypus/grapevine", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldBeEmpty();
            using var json = JsonDocument.Parse(run.Output);
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().ShouldBe("registry.repository");
        }

        [Fact]
        public async Task A_repository_can_be_named_when_GitHub_itself_is_asked_though_a_registry_is_named()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            var run = await sandbox.RunAsync("install", "--repository", Sandbox.Grapevine, "--direct");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.GitHub.RegistryReads.ShouldBeEmpty();
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine"]);
        }

        [Fact]
        public async Task With_no_registry_named_a_repository_is_named_as_it_always_was()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0");

            (await sandbox.RunAsync("install", "--repository", Sandbox.Grapevine)).Status.ShouldBe(0);
        }

        [Theory]
        [InlineData("tree")]
        [InlineData("pack")]
        public async Task A_command_that_finds_no_package_asks_a_registry_nothing(string command)
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            sandbox.Write("src/main.🍇", "🏁 🍇🍉\n");
            sandbox.GitHub.RegistryAnswer = HttpStatusCode.InternalServerError;
            var asked = sandbox.GitHub.Requests.Count;

            var run = await sandbox.RunAsync(command);

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.GitHub.Requests.Count.ShouldBe(asked);
        }

        [Fact]
        public async Task A_tool_is_told_of_a_registry_that_cannot_be_read_in_its_answer()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.GitHub.RegistryAnswer = HttpStatusCode.Forbidden;

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldBeEmpty();
            using var json = JsonDocument.Parse(run.Output);
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().ShouldBe("registry.unreachable");
        }

        [Fact]
        public async Task What_is_locked_and_not_in_the_cache_is_held_to_the_registrys_record_before_anything_is_downloaded()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            sandbox.ForgetCache();
            var downloads = sandbox.GitHub.Downloads.Count();
            var other = Packmoji.Core.Lockfiles.Sha256Digest.Of("other bytes"u8);
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => version with { Sha256 = other });

            var run = await sandbox.RunAsync("install", "--locked");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[lock.mismatch]: packmoji.lock does not agree with what is published for \"@thatplatypus/crypto\" 1.0.0.");
            run.Error.ShouldContain($"and what is published has {other}");
            sandbox.GitHub.Downloads.Count().ShouldBe(downloads, "nothing is fetched for a lockfile that the registry does not bear out");
            sandbox.Cached().ShouldBeEmpty();
        }

        [Fact]
        public async Task What_was_locked_on_GitHub_itself_and_is_not_in_the_registry_is_said_to_be_that_by_an_install_that_has_to_fetch_it()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release("github.com/thatplatypus/sockets_more", "@thatplatypus/sockets_more", "1.0.0");
            (await sandbox.RunAsync("add", "@thatplatypus/sockets_more", "--direct")).Status.ShouldBe(0);

            var fromTheCache = await sandbox.RunAsync("install", "--locked");
            sandbox.ForgetCache();
            var fetching = await sandbox.RunAsync("install", "--locked");
            var direct = await sandbox.RunAsync("install", "--locked", "--direct");

            fromTheCache.Status.ShouldBe(0, "bytes that are locked and are in the cache are the version, whatever the registry holds");
            fetching.Status.ShouldBe(1);
            fetching.Error.ShouldStartWith("error[package.not-found]: \"@thatplatypus/sockets_more\" is not in the registry.");
            fetching.Error.ShouldContain("packmoji.lock holds 1.0.0 of it");
            direct.Error.ShouldBeEmpty();
            direct.Status.ShouldBe(0);
        }

        // The cache answers for what is locked only where nothing is chosen. A command that chooses
        // asks the registry about every version, and that is where it learns what has changed.
        [Fact]
        public async Task A_command_that_chooses_versions_learns_of_a_quarantine_though_the_cache_holds_the_bytes()
        {
            using var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", "@thatplatypus/grapevine")).Status.ShouldBe(0);
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, VersionStatus.Quarantined));
            var manifest = sandbox.Read("packmoji.json");
            var downloads = sandbox.GitHub.Downloads.Count();

            var run = await sandbox.RunAsync("add", "@thatplatypus/deflate");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[resolve.quarantined]: Version 1.0.0 of \"@thatplatypus/crypto\" is quarantined.");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.GitHub.Downloads.Count().ShouldBe(downloads);
        }

        [Fact]
        public async Task An_option_that_is_written_wrongly_is_a_mistake_in_the_command_and_no_fault_of_pmj()
        {
            using var sandbox = await WithARegistryAsync();

            var run = await sandbox.RunAsync("add", "@thatplatypus/grapevine", "--direct=maybe");

            run.Status.ShouldBe(2);
            run.Error.ShouldBe("error: Unrecognized command or argument 'maybe'." + Environment.NewLine + "Run pmj add --help to see how it is used." + Environment.NewLine);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        // Grapevine added through a registry, and nothing counted of what that asked.
        private static async Task<Sandbox> AddedAsync(string package = "@thatplatypus/grapevine")
        {
            var sandbox = await WithARegistryAsync();
            (await sandbox.RunAsync("add", package)).Status.ShouldBe(0);
            sandbox.GitHub.Requests.Clear();
            return sandbox;
        }

        [Fact]
        public async Task Verify_holds_every_locked_package_to_the_registry_as_well_as_to_its_release()
        {
            using var sandbox = await AddedAsync();

            var run = await sandbox.RunAsync("verify");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Verified 3 packages: the release of each, and what the registry records of it, are what packmoji.lock holds.
                The cache's copy of each is what was locked too.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.GitHub.RegistryReads.Order(StringComparer.Ordinal).ShouldBe([Crypto, Deflate, Grapevine]);
            sandbox.GitHub.Downloads.Count().ShouldBe(3);
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task Of_one_package_verify_speaks_as_of_one()
        {
            using var sandbox = await AddedAsync("@thatplatypus/crypto");

            var run = await sandbox.RunAsync("verify");

            run.Output.ShouldStartWith("Verified 1 package: its release, and what the registry records of it, are what packmoji.lock holds." + Environment.NewLine);
        }

        [Fact]
        public async Task With_no_registry_named_verify_says_nothing_of_one()
        {
            using var sandbox = await AddedAsync();
            sandbox.NamesRegistry = false;

            var run = await sandbox.RunAsync("verify");

            run.Output.ShouldStartWith("Verified 3 packages: the release of each is what packmoji.lock holds." + Environment.NewLine);
            sandbox.GitHub.RegistryReads.ShouldBeEmpty();
        }

        // An install of what the cache holds asks nobody anything, and so does not learn of it. This is what does.
        [Fact]
        public async Task Verify_learns_of_a_quarantine_that_an_install_from_the_cache_does_not()
        {
            using var sandbox = await AddedAsync();
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, VersionStatus.Quarantined));

            var installed = await sandbox.RunAsync("install", "--locked");
            var verified = await sandbox.RunAsync("verify");

            installed.Status.ShouldBe(0);
            installed.Error.ShouldBeEmpty();
            verified.Status.ShouldBe(1);
            verified.Output.ShouldBeEmpty();
            verified.Error.ShouldStartWith("error[resolve.quarantined]: Version 1.0.0 of \"@thatplatypus/crypto\" is quarantined.");
            verified.Error.Split("error[").Length.ShouldBe(2, "the release and the cache are still what was locked, so nothing else is wrong");
        }

        [Fact]
        public async Task Verify_says_that_a_locked_version_was_withdrawn_and_is_sound_all_the_same()
        {
            using var sandbox = await AddedAsync();
            sandbox.Rerecord("@thatplatypus/deflate", "0.1.0", version => Marked(version, VersionStatus.Yanked));

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(0);
            run.Error.ShouldStartWith("warning[resolve.yanked-locked]: Version 0.1.0 of \"@thatplatypus/deflate\" has been yanked.");
            run.Output.ShouldStartWith("Verified 3 packages: ");
        }

        [Fact]
        public async Task Verify_says_once_that_the_registry_cannot_be_read_and_still_holds_the_releases_and_the_cache_to_the_lockfile()
        {
            using var sandbox = await AddedAsync();
            sandbox.GitHub.RegistryAnswer = HttpStatusCode.BadGateway;

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.Split("error[registry.unreachable]").Length.ShouldBe(2);
            run.Error.Split("error[").Length.ShouldBe(2);
            sandbox.GitHub.RegistryReads.Count().ShouldBe(1, "what could not be read for one package is not asked for again for the next");
            sandbox.GitHub.Downloads.Count().ShouldBe(3);
        }

        [Fact]
        public async Task Verify_goes_on_asking_the_registry_when_one_of_its_files_does_not_read()
        {
            using var sandbox = await AddedAsync();
            sandbox.Rerecord("@thatplatypus/grapevine", "0.3.0", version => Marked(version, VersionStatus.Quarantined));

            // After the registry is recorded again, which serves every file of it anew.
            sandbox.GitHub.Registry[Crypto] = Encoding.UTF8.GetBytes("not a file of a registry");

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[registry.invalid]: The registry's file of \"@thatplatypus/crypto\" cannot be read.");
            run.Error.ShouldContain("error[resolve.quarantined]: Version 0.3.0 of \"@thatplatypus/grapevine\" is quarantined.");
            sandbox.GitHub.RegistryReads.Count().ShouldBe(3);
        }

        [Fact]
        public async Task Verify_says_of_a_locked_package_the_registry_does_not_have_that_it_is_not_in_the_registry()
        {
            using var sandbox = await WithARegistryAsync();
            sandbox.Release("github.com/thatplatypus/sockets_more", "@thatplatypus/sockets_more", "1.0.0");
            (await sandbox.RunAsync("add", "@thatplatypus/sockets_more", "--direct")).Status.ShouldBe(0);

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[package.not-found]: "@thatplatypus/sockets_more" is not in the registry.
                  why: https://registry.example/files/v1/packages/thatplatypus/sockets_more.json is not there; packmoji.lock holds 1.0.0 of it
                  fix: check that https://registry.example/files is the address a registry's v1 is under, for one that is not answers the same way; if it is, and the package's repository is on the registry's list, a release is in the registry within a day; to ask GitHub itself, run the command again with --direct

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task What_the_registry_and_the_release_both_say_against_the_lockfile_in_the_same_words_is_said_once()
        {
            using var sandbox = await AddedAsync();
            // As a bad merge leaves it: deflate is gone, and grapevine no longer says that it needs it.
            var locked = sandbox.Lockfile();
            var edited = locked with
            {
                Packages = locked.Packages
                    .Where(package => package.Name.Name != "deflate")
                    .Select(package => package with { Dependencies = package.Dependencies.Where(pin => pin.Name.Name != "deflate").ToList() })
                    .ToList(),
            };
            sandbox.Write("packmoji.lock", LockfileWriter.Write(edited));

            var run = await sandbox.RunAsync("verify");

            run.Status.ShouldBe(1);
            run.Error.Split("what is published asks for @thatplatypus/deflate@0.1, and the lockfile gives it no such dependency").Length.ShouldBe(2, run.Error);
        }

        [Fact]
        public async Task A_tool_is_told_what_verify_found_against_the_registry_in_its_answer()
        {
            using var sandbox = await AddedAsync();
            sandbox.Rerecord("@thatplatypus/crypto", "1.0.0", version => Marked(version, VersionStatus.Quarantined));

            var run = await sandbox.RunAsync("verify", "--json");

            run.Status.ShouldBe(1);
            run.Error.ShouldBeEmpty();
            using var json = JsonDocument.Parse(run.Output);
            json.RootElement.GetProperty("ok").GetBoolean().ShouldBeFalse();
            json.RootElement.GetProperty("diagnostics")[0].GetProperty("code").GetString().ShouldBe("resolve.quarantined");
            json.RootElement.GetProperty("packages").GetArrayLength().ShouldBe(3);
        }

        // A version as a person's commit to the registry would mark it.
        private static RegistryVersion Marked(RegistryVersion version, VersionStatus status) =>
            version with
            {
                Status = status,
                Quarantined = status == VersionStatus.Quarantined ? new RegistryQuarantine(version.Indexed.AddHours(1), version.Sha256) : null,
            };
    }
}
