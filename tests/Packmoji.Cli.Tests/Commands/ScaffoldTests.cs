using Packmoji.Cli.Tests.TestSupport;
using Packmoji.Core.Manifests;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary><c>pmj new</c> and <c>pmj init</c>: a project that reads, and that says nothing its author did not.</summary>
    public sealed class ScaffoldTests
    {
        private static Manifest ManifestOf(Sandbox sandbox, string directory)
        {
            var read = ManifestReader.Read(sandbox.Read(directory + "packmoji.json"));
            read.Diagnostics.ShouldBeEmpty();
            return read.Value!;
        }

        [Fact]
        public async Task New_makes_an_application_in_a_directory_of_its_bare_name()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("new", "@thatplatypus/hello");

            run.Status.ShouldBe(0);
            run.Error.ShouldBeEmpty();
            sandbox.Files().ShouldBe(["hello/.gitignore", "hello/README.md", "hello/packmoji.json", "hello/src/main.🍇"]);
            sandbox.Read("hello/packmoji.json").ShouldBe(
                """
                {
                  "package": {
                    "name": "@thatplatypus/hello",
                    "version": "0.1.0",
                    "kind": "app",
                    "emojicode": ">=1.0.0-beta.2"
                  },
                  "build": {
                    "entry": "src/main.🍇"
                  }
                }

                """.ReplaceLineEndings("\n"));
            sandbox.Read("hello/src/main.🍇").ShouldBe("🏁 🍇\n  😀 🔤Hello from @thatplatypus/hello!🔤❗️\n🍉\n");
            sandbox.Read("hello/.gitignore").ShouldBe("target/\npackages/\n");
            sandbox.Read("hello/README.md").ShouldBe("# hello\n");
            run.Output.ShouldBe(
                """
                Made the application @thatplatypus/hello in hello/:
                  packmoji.json
                  src/main.🍇
                  .gitignore
                  README.md

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task New_with_lib_makes_a_library_whose_entry_file_shows_what_others_can_use()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("new", "@thatplatypus/greeter", "--lib");

            run.Status.ShouldBe(0);
            var manifest = ManifestOf(sandbox, "greeter/");
            manifest.Package.Kind.ShouldBe(PackageKind.Library);
            manifest.Build!.Entry!.Value.ShouldBe("src/lib.🍇");
            sandbox.Read("greeter/src/lib.🍇").ShouldContain("🌍 🐇 👋 🍇");
            sandbox.Has("greeter/src/main.🍇").ShouldBeFalse();
            run.Output.ShouldStartWith("Made the library @thatplatypus/greeter in greeter/:");
        }

        [Fact]
        public async Task App_says_what_new_makes_anyway()
        {
            using var sandbox = new Sandbox();

            (await sandbox.RunAsync("new", "@thatplatypus/hello", "--app")).Status.ShouldBe(0);

            ManifestOf(sandbox, "hello/").Package.Kind.ShouldBe(PackageKind.App);
        }

        [Fact]
        public async Task New_fills_a_directory_that_is_there_and_empty_and_refuses_one_that_holds_something()
        {
            using var sandbox = new Sandbox();
            Directory.CreateDirectory(sandbox.PathOf("empty"));
            sandbox.Write("taken/notes.txt", "mine");

            (await sandbox.RunAsync("new", "@thatplatypus/empty")).Status.ShouldBe(0);
            var refused = await sandbox.RunAsync("new", "@thatplatypus/taken");

            sandbox.Has("empty/packmoji.json").ShouldBeTrue();
            refused.Status.ShouldBe(1);
            refused.Output.ShouldBeEmpty();
            refused.Error.ShouldContain("error[project.exists]: \"taken\" is here already, and is not empty.");
            refused.Error.ShouldContain("  why: ");
            refused.Error.ShouldContain("  fix: ");
            sandbox.Files("taken").ShouldBe(["notes.txt"]);
        }

        [Fact]
        public async Task Init_makes_the_project_in_the_directory_it_is_run_in()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("init", "@thatplatypus/here", "--lib");

            run.Status.ShouldBe(0);
            sandbox.Files().ShouldBe([".gitignore", "README.md", "packmoji.json", "src/lib.🍇"]);
            ManifestOf(sandbox, "").Package.Name.ToString().ShouldBe("@thatplatypus/here");
            run.Output.ShouldStartWith("Made the library @thatplatypus/here in this directory:");
        }

        [Fact]
        public async Task Init_leaves_alone_a_file_that_is_already_there_and_says_only_what_it_wrote()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("README.md", "# Mine\n");
            sandbox.Write("src/main.🍇", "🏁 🍇 🍉\n");
            sandbox.Write(".gitignore", "*.o\n");

            var run = await sandbox.RunAsync("init", "@thatplatypus/here");

            run.Status.ShouldBe(0);
            sandbox.Read("README.md").ShouldBe("# Mine\n");
            sandbox.Read("src/main.🍇").ShouldBe("🏁 🍇 🍉\n");
            sandbox.Read(".gitignore").ShouldBe("*.o\n");
            run.Output.ShouldBe($"Made the application @thatplatypus/here in this directory:{Environment.NewLine}  packmoji.json{Environment.NewLine}");
        }

        [Fact]
        public async Task Init_refuses_a_directory_that_is_a_project_already_and_changes_nothing()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", "{ \"mine\": true }");

            var run = await sandbox.RunAsync("init", "@thatplatypus/here");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[project.exists]: There is a project here already.");
            sandbox.Files().ShouldBe(["packmoji.json"]);
            sandbox.Read("packmoji.json").ShouldBe("{ \"mine\": true }");
        }

        [Theory]
        [InlineData("hello", "name.invalid")]
        [InlineData("@ThatPlatypus/hello", "name.invalid")]
        [InlineData("@thatplatypus/json", "name.reserved")]
        public async Task A_name_that_cannot_be_a_packages_is_refused_with_its_reason_and_nothing_is_made(string name, string code)
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("new", name);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"error[{code}]: ");
            sandbox.Files().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_project_cannot_be_asked_for_as_both_a_library_and_an_application()
        {
            using var sandbox = new Sandbox();

            var run = await sandbox.RunAsync("new", "@thatplatypus/hello", "--lib", "--app");

            run.Status.ShouldBe(2);
            run.Error.ShouldContain("give --lib or --app, and not both");
            sandbox.Files().ShouldBeEmpty();
        }
    }
}
