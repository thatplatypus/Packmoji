using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj add</c>: one more thing the project asks for, the lockfile that answers it, and the packages fetched.</summary>
    public sealed class AddTests
    {
        private const string Crypto = "github.com/thatplatypus/crypto";

        // A package in a repository of its own name, with a history: three versions on one line and a pre-release of the next.
        private static Sandbox WithCrypto()
        {
            var sandbox = new Sandbox();
            foreach (var version in new[] { "1.0.0", "1.2.0", "1.10.0", "2.0.0-rc.1" })
            {
                sandbox.Release(Crypto, "@thatplatypus/crypto", version);
            }

            sandbox.Project("@someone/app");
            return sandbox;
        }

        [Fact]
        public async Task With_no_requirement_add_asks_for_the_latest_version_that_is_not_a_pre_release_written_in_full()
        {
            using var sandbox = WithCrypto();

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Added @thatplatypus/crypto 1.10.0 to dependencies.
                packmoji.lock now holds 1 package:
                  + @thatplatypus/crypto 1.10.0

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Read("packmoji.json").ShouldBe(
                """
                {
                  "package": {
                    "name": "@someone/app",
                    "version": "0.1.0",
                    "kind": "app",
                    "emojicode": ">=1.0.0-beta.2"
                  },
                  "dependencies": {
                    "@thatplatypus/crypto": "1.10.0"
                  }
                }

                """.ReplaceLineEndings("\n"));
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.10.0 in github.com/thatplatypus/crypto"]);
            sandbox.Cached().Count.ShouldBe(3);
            sandbox.Files().ShouldBe(["packmoji.json", "packmoji.lock"]);
        }

        [Fact]
        public async Task With_a_requirement_add_writes_it_as_it_was_given_and_asks_for_no_list_of_versions()
        {
            using var sandbox = WithCrypto();

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("Added @thatplatypus/crypto 1.2 to dependencies.");
            sandbox.Manifest().Dependencies!.Single().Requirement.Text.ShouldBe("1.2");
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.2.0 in github.com/thatplatypus/crypto"]);
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_pre_release_is_added_only_by_naming_it()
        {
            using var sandbox = new Sandbox();
            sandbox.Release(Crypto, "@thatplatypus/crypto", "2.0.0-rc.1");
            sandbox.Release(Crypto, "@thatplatypus/crypto", "2.0.0-beta.3");
            sandbox.Project("@someone/app");

            var unnamed = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            unnamed.Status.ShouldBe(1);
            unnamed.Error.ShouldContain("error[version.none-released]: No version of \"@thatplatypus/crypto\" has been released that is not a pre-release.");
            unnamed.Error.ShouldContain("fix: to use one, name it: pmj add @thatplatypus/crypto@2.0.0-rc.1");
            sandbox.Has("packmoji.lock").ShouldBeFalse();

            var named = await sandbox.RunAsync("add", "@thatplatypus/crypto@2.0.0-rc.1");

            named.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 2.0.0-rc.1 in github.com/thatplatypus/crypto"]);
        }

        [Fact]
        public async Task With_dev_the_package_is_needed_only_to_develop_the_project()
        {
            using var sandbox = WithCrypto();

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto", "--dev");

            run.Status.ShouldBe(0);
            run.Output.ShouldStartWith("Added @thatplatypus/crypto 1.10.0 to devDependencies.");
            sandbox.Manifest().Dependencies.ShouldBeNull();
            sandbox.Manifest().DevDependencies!.Single().Name.ToString().ShouldBe("@thatplatypus/crypto");
            sandbox.Lockfile().Root.DevDependencies.Count.ShouldBe(1);
        }

        [Fact]
        public async Task Adding_grapevine_finds_the_packages_that_share_its_repository_and_the_lockfile_remembers_where()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");

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
            sandbox.Locked().ShouldBe(
            [
                "@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine",
                "@thatplatypus/deflate 0.1.0 in github.com/thatplatypus/grapevine",
                "@thatplatypus/grapevine 0.3.0 in github.com/thatplatypus/grapevine",
            ]);
            sandbox.GitHub.Listings.ShouldBe(["/repos/thatplatypus/grapevine/releases"]);

            // Each package is looked for in a repository of its own name first, and then beside what has been found.
            sandbox.GitHub.Downloads.Select(path => path.Replace("/releases/download/", " ")).ShouldBe(
            [
                "/thatplatypus/grapevine grapevine-v0.3.0/grapevine-0.3.0.pmj.tar.gz",
                "/thatplatypus/crypto crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "/thatplatypus/grapevine crypto-v1.0.0/crypto-1.0.0.pmj.tar.gz",
                "/thatplatypus/deflate deflate-v0.1.0/deflate-0.1.0.pmj.tar.gz",
                "/thatplatypus/grapevine deflate-v0.1.0/deflate-0.1.0.pmj.tar.gz",
            ]);
        }

        [Fact]
        public async Task A_package_that_shares_a_repository_and_is_wanted_alone_is_found_once_pmj_is_told_where()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");

            var lost = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            lost.Status.ShouldBe(1);
            lost.Error.ShouldContain("error[package.not-found]: No release of \"@thatplatypus/crypto\" was found.");
            lost.Error.ShouldContain("pmj looked in github.com/thatplatypus/crypto");
            lost.Error.ShouldContain("fix: if the package shares a repository with others, run this again with --repository github.com/thatplatypus/<repository>; pmj reads public repositories only");
            sandbox.Has("packmoji.lock").ShouldBeFalse();

            var told = await sandbox.RunAsync("add", "@thatplatypus/crypto", "--repository", Sandbox.Grapevine);

            told.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine"]);

            // Once is enough: the lockfile says where it lives, and after it the packages beside it are found too.
            sandbox.ForgetCache();
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);
            (await sandbox.RunAsync("add", "@thatplatypus/deflate")).Status.ShouldBe(0);
            sandbox.Locked().ShouldContain("@thatplatypus/deflate 0.1.0 in github.com/thatplatypus/grapevine");
        }

        [Fact]
        public async Task A_project_whose_lockfile_is_gone_is_locked_again_by_saying_where_to_look_and_by_the_very_command_pmj_names()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            (await sandbox.RunAsync("add", "@thatplatypus/crypto", "--repository", Sandbox.Grapevine)).Status.ShouldBe(0);
            var manifest = sandbox.Read("packmoji.json");

            // The lockfile was the only record of where crypto lives. A clone without it, or a merge that lost it, is here.
            File.Delete(sandbox.PathOf("packmoji.lock"));
            sandbox.ForgetCache();

            var lost = await sandbox.RunAsync("install");
            lost.Status.ShouldBe(1);
            lost.Error.ShouldContain("error[package.not-found]: No release of \"@thatplatypus/crypto\" 1.0.0 was found.");
            lost.Error.ShouldContain("run this again with --repository github.com/thatplatypus/<repository>");

            var found = await sandbox.RunAsync("install", "--repository", Sandbox.Grapevine);

            found.Error.ShouldBeEmpty();
            found.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/crypto 1.0.0 in github.com/thatplatypus/grapevine"]);
            sandbox.Read("packmoji.json").ShouldBe(manifest);
        }

        [Fact]
        public async Task Add_of_what_is_already_asked_for_takes_a_place_to_look_and_locks_again_without_changing_the_manifest()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");
            await sandbox.RunAsync("add", "@thatplatypus/crypto", "--repository", Sandbox.Grapevine);
            var manifest = sandbox.Read("packmoji.json");
            File.Delete(sandbox.PathOf("packmoji.lock"));

            var again = await sandbox.RunAsync("add", "@thatplatypus/crypto", "--repository", Sandbox.Grapevine);

            again.Error.ShouldBeEmpty();
            again.Status.ShouldBe(0);
            again.Output.ShouldBe($"packmoji.lock now holds 1 package:{Environment.NewLine}  + @thatplatypus/crypto 1.0.0{Environment.NewLine}");
            sandbox.Read("packmoji.json").ShouldBe(manifest);

            File.Delete(sandbox.PathOf("packmoji.lock"));
            var withTheSameRequirement = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.0.0", "--repository", Sandbox.Grapevine);

            withTheSameRequirement.Status.ShouldBe(0);
            sandbox.Has("packmoji.lock").ShouldBeTrue();
            sandbox.Read("packmoji.json").ShouldBe(manifest);
        }

        [Fact]
        public async Task The_same_requirement_again_still_locks_a_project_that_is_not_locked()
        {
            using var sandbox = WithCrypto();
            await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");
            File.Delete(sandbox.PathOf("packmoji.lock"));

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");

            run.Status.ShouldBe(0);
            run.Output.ShouldBe($"packmoji.lock now holds 1 package:{Environment.NewLine}  + @thatplatypus/crypto 1.2.0{Environment.NewLine}");
        }

        [Fact]
        public async Task A_package_that_only_another_package_needs_is_found_in_a_repository_pmj_is_told_to_look_in_as_well()
        {
            // The tool is in a repository of its own name. What it needs is in one that nothing leads to.
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/tool", "@thatplatypus/tool", "1.0.0", "@thatplatypus/helper@2.0");
            sandbox.Release("github.com/thatplatypus/mono", "@thatplatypus/helper", "2.0.0");
            sandbox.Project("@someone/app");

            var lost = await sandbox.RunAsync("add", "@thatplatypus/tool");

            lost.Status.ShouldBe(1);
            lost.Error.ShouldStartWith("error[package.not-found]: No release of \"@thatplatypus/helper\" 2.0.0 was found.");
            lost.Error.ShouldContain("it is asked for: @someone/app → @thatplatypus/tool@1.0.0 → @thatplatypus/helper@2.0");

            var found = await sandbox.RunAsync("add", "@thatplatypus/tool", "--repository", "github.com/thatplatypus/mono");

            found.Error.ShouldBeEmpty();
            found.Status.ShouldBe(0);
            sandbox.Locked().ShouldBe(["@thatplatypus/helper 2.0.0 in github.com/thatplatypus/mono", "@thatplatypus/tool 1.0.0 in github.com/thatplatypus/tool"]);

            // And again from the manifest alone, by any of the commands that resolve.
            File.Delete(sandbox.PathOf("packmoji.lock"));
            (await sandbox.RunAsync("update", "--repository", "github.com/thatplatypus/mono")).Status.ShouldBe(0);
            sandbox.Locked().Count.ShouldBe(2);
        }

        [Fact]
        public async Task With_dev_and_no_requirement_a_dependency_is_moved_and_goes_on_asking_for_what_it_asked()
        {
            using var sandbox = WithCrypto();
            await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");

            var moved = await sandbox.RunAsync("add", "@thatplatypus/crypto", "--dev");

            moved.Status.ShouldBe(0);
            moved.Output.ShouldStartWith("Moved @thatplatypus/crypto to devDependencies, asking for 1.2.");
            sandbox.Manifest().Dependencies.ShouldBeNull();
            sandbox.Manifest().DevDependencies!.Single().Requirement.Text.ShouldBe("1.2");
            sandbox.GitHub.Listings.ShouldBeEmpty();
        }

        [Theory]
        [InlineData("github.com/someone/grapevine", "repository.owner-mismatch", "\"@thatplatypus/crypto\" cannot live in github.com/someone/grapevine.")]
        [InlineData("https://github.com/thatplatypus/Grapevine", "repository.invalid", "write \"github.com/thatplatypus/grapevine\"")]
        public async Task A_repository_that_the_package_cannot_be_in_is_refused_before_anything_is_asked(string repository, string code, string says)
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Project("@someone/app");

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto", "--repository", repository);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"error[{code}]: ");
            run.Error.ShouldContain(says);
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_already_depended_on_is_an_error_with_no_requirement_and_a_change_of_its_requirement_with_one()
        {
            using var sandbox = WithCrypto();
            await sandbox.RunAsync("add", "@thatplatypus/crypto@1.0", "--dev");
            var manifest = sandbox.Read("packmoji.json");

            var again = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            again.Status.ShouldBe(1);
            again.Error.ShouldContain("error[dependency.exists]: \"@thatplatypus/crypto\" is already a dependency of this project.");
            again.Error.ShouldContain("why: packmoji.json asks for it at 1.0, under \"devDependencies\"");
            sandbox.Read("packmoji.json").ShouldBe(manifest);

            var changed = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");

            changed.Status.ShouldBe(0);
            changed.Output.ShouldBe(
                """
                Changed the requirement on @thatplatypus/crypto from 1.0 to 1.2.
                packmoji.lock now holds 1 package:
                  ~ @thatplatypus/crypto 1.0.0 to 1.2.0

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Manifest().DevDependencies!.Single().Requirement.Text.ShouldBe("1.2");
            sandbox.Manifest().Dependencies.ShouldBeNull();
        }

        [Fact]
        public async Task Dev_moves_a_dependency_to_what_is_needed_only_to_develop_and_the_same_requirement_again_changes_nothing()
        {
            using var sandbox = WithCrypto();
            await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");

            var moved = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2", "--dev");

            moved.Status.ShouldBe(0);
            moved.Output.ShouldStartWith("Moved @thatplatypus/crypto to devDependencies, asking for 1.2.");
            sandbox.Manifest().Dependencies.ShouldBeNull();
            sandbox.Manifest().DevDependencies!.Count.ShouldBe(1);

            var manifest = sandbox.Read("packmoji.json");
            var same = await sandbox.RunAsync("add", "@thatplatypus/crypto@1.2");

            same.Status.ShouldBe(0);
            same.Output.ShouldBe($"packmoji.json already asks for @thatplatypus/crypto at 1.2.{Environment.NewLine}");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
        }

        [Fact]
        public async Task A_version_that_cannot_be_found_is_an_error_that_says_where_pmj_looked_and_no_more_than_it_knows()
        {
            using var sandbox = WithCrypto();
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto@9.9");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldStartWith("error[package.not-found]: No release of \"@thatplatypus/crypto\" 9.9.0 was found.");
            run.Error.ShouldContain("pmj looked for the release crypto-v9.9.0 in github.com/thatplatypus/crypto, and it is not there; it is asked for: @someone/app → @thatplatypus/crypto@9.9");
            run.Error.ShouldNotContain("never published");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task A_package_with_no_release_anywhere_pmj_looks_is_not_found()
        {
            using var sandbox = WithCrypto();

            var run = await sandbox.RunAsync("add", "@thatplatypus/nothing");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[package.not-found]: No release of \"@thatplatypus/nothing\" was found.");
            run.Error.ShouldContain("pmj looked in github.com/thatplatypus/nothing for a release tagged nothing-v and a version");
        }

        [Theory]
        [InlineData("thatplatypus/crypto", "name.invalid")]
        [InlineData("@thatplatypus/crypto@", "requirement.invalid")]
        [InlineData("@thatplatypus/crypto@latest", "requirement.invalid")]
        [InlineData("@someone/app", "dependency.self")]
        public async Task What_cannot_be_asked_for_is_refused_with_its_reason_and_nothing_changes(string package, string code)
        {
            using var sandbox = WithCrypto();
            sandbox.Release("github.com/someone/app", "@someone/app", "1.0.0");
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync("add", package);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"error[{code}]: ");
            run.Error.ShouldNotContain("packmoji.json:");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task Two_packages_of_one_bare_name_cannot_both_be_asked_for()
        {
            using var sandbox = WithCrypto();
            sandbox.Release("github.com/other/crypto", "@other/crypto", "3.0.0");
            await sandbox.RunAsync("add", "@thatplatypus/crypto");
            var manifest = sandbox.Read("packmoji.json");

            var run = await sandbox.RunAsync("add", "@other/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[dependency.name-collision]: \"@other/crypto\" and \"@thatplatypus/crypto\" have the same name.");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
        }

        [Fact]
        public async Task When_what_is_added_cannot_be_resolved_neither_file_is_changed()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Release("github.com/thatplatypus/other", "@thatplatypus/other", "1.0.0", "@thatplatypus/crypto@2.0");
            sandbox.Release(Sandbox.Grapevine, "@thatplatypus/crypto", "2.0.0");
            sandbox.Project("@someone/app");
            await sandbox.RunAsync("add", "@thatplatypus/grapevine");
            var manifest = sandbox.Read("packmoji.json");
            var locked = sandbox.Read("packmoji.lock");

            var run = await sandbox.RunAsync("add", "@thatplatypus/other");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain("error[resolve.line-conflict]: ");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Read("packmoji.lock").ShouldBe(locked);
            sandbox.Files().ShouldBe(["packmoji.json", "packmoji.lock"]);
        }

        [Theory]
        [InlineData("add", "@thatplatypus/crypto@1.2")]
        [InlineData("remove", "@thatplatypus/deflate")]
        [InlineData("update")]
        public async Task When_the_lockfile_cannot_be_written_the_manifest_is_put_back_as_it_was(params string[] args)
        {
            using var sandbox = WithCrypto();
            sandbox.Release("github.com/thatplatypus/deflate", "@thatplatypus/deflate", "0.1.0");
            sandbox.Project("@someone/app", "@thatplatypus/crypto@1.0", "@thatplatypus/deflate@0.1");
            var manifest = sandbox.Read("packmoji.json");

            // Something that is not a file stands where the lockfile goes, so the manifest can be written and the lockfile cannot.
            Directory.CreateDirectory(sandbox.PathOf("packmoji.lock"));

            var run = await sandbox.RunAsync(args);

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldContain($"error[project.unreadable]: \"{sandbox.PathOf("packmoji.lock")}\" could not be written.");
            sandbox.Read("packmoji.json").ShouldBe(manifest);
            sandbox.Files().ShouldBe(["packmoji.json"]);
        }

        [Fact]
        public async Task Add_keeps_everything_else_a_manifest_says()
        {
            using var sandbox = WithCrypto();
            sandbox.Write(
                "packmoji.json",
                """
                {
                  "package": { "name": "@someone/app", "version": "2.0.0", "kind": "app", "emojicode": ">=1.0.0-beta.2", "description": "An app", "license": "MIT" },
                  "build": { "entry": "main.🍇", "sources": ["*.🍇"] },
                  "native": { "link": ["pthread"] },
                  "policy": { "requireAttestation": false }
                }
                """);

            (await sandbox.RunAsync("add", "@thatplatypus/crypto@1.0")).Status.ShouldBe(0);

            sandbox.Read("packmoji.json").ShouldBe(
                """
                {
                  "package": {
                    "name": "@someone/app",
                    "version": "2.0.0",
                    "kind": "app",
                    "emojicode": ">=1.0.0-beta.2",
                    "description": "An app",
                    "license": "MIT"
                  },
                  "dependencies": {
                    "@thatplatypus/crypto": "1.0"
                  },
                  "build": {
                    "entry": "main.🍇",
                    "sources": [
                      "*.🍇"
                    ]
                  },
                  "native": {
                    "link": [
                      "pthread"
                    ]
                  },
                  "policy": {
                    "requireAttestation": false
                  }
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public async Task When_GitHub_will_not_list_releases_because_of_its_limit_add_says_when_it_lifts_and_how_to_raise_it()
        {
            using var sandbox = WithCrypto();
            sandbox.GitHub.RateLimited = true;

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[github.rate-limited]: GitHub would not give the releases of github.com/thatplatypus/crypto.");
            run.Error.ShouldContain("fix: set GITHUB_TOKEN to a token of yours");
            sandbox.Has("packmoji.lock").ShouldBeFalse();
        }

        [Fact]
        public async Task Outside_a_project_add_says_there_is_none()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("add", "@thatplatypus/crypto");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[project.not-found]: ");
            sandbox.GitHub.Requests.ShouldBeEmpty();
        }
    }
}
