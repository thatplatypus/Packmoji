using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// The project itself: an application is compiled and linked to a program, and a library is
    /// compiled and archived as a package is. pmj links, because the released compiler cannot be
    /// trusted to: every archive goes into one group, where their order does not matter.
    /// </summary>
    public sealed class BuildProjectTests
    {
        private const string Main = "📦 grapevine 🏠\n🏁 🍇\n  😀 🔤Hello from the app🔤❗️\n🍉\n";

        private const string Stock = "~/stock/files/libfiles.a ~/stock/json/libjson.a ~/stock/runtime/libruntime.a ~/stock/s/libs.a ~/stock/sockets/libsockets.a ~/stock/testtube/libtesttube.a";

        // An application that imports grapevine, with its three packages installed and nothing built.
        private static async Task<Sandbox> WithAnApplicationAsync()
        {
            var sandbox = Sandbox.WithGrapevine();
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/grapevine@0.3");
            sandbox.Write("src/main.🍇", Main);
            return sandbox;
        }

        // An application that depends on nothing.
        private static Sandbox Alone(string source = "🏁 🍇\n  😀 🔤Hello🔤❗️\n🍉\n")
        {
            var sandbox = new Sandbox();
            sandbox.Project("@someone/app");
            sandbox.Write("src/main.🍇", source);
            return sandbox;
        }

        private static string Manifest(string kind, string more = "") =>
            $"{{ \"package\": {{ \"name\": \"@someone/{(kind == "app" ? "app" : "shelf")}\", \"version\": \"0.1.0\", \"kind\": \"{kind}\", \"emojicode\": \">=1.0.0-beta.2\" }}{more} }}\n";

        [Fact]
        public async Task An_application_is_compiled_after_its_packages_and_linked_with_them_and_the_stock_archives_in_one_group()
        {
            using var sandbox = await WithAnApplicationAsync();

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldBe(
                """
                Building @thatplatypus/crypto 1.0.0
                Building @thatplatypus/deflate 0.1.0
                Building @thatplatypus/grapevine 0.3.0
                Built 3 packages into packages/.
                Building @someone/app 0.1.0
                Built the application target/debug/app.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Calls.TakeLast(2).Select(sandbox.Plain).ShouldBe(
            [
                "emojicodec ~/work/src/main.🍇 -c -o ~/work/target/obj/debug/app.o -S ~/work/packages",
                $"c++ ~/work/target/obj/debug/app.o -Wl,--start-group ~/work/packages/crypto/libcrypto.a ~/work/packages/deflate/libdeflate.a ~/work/packages/grapevine/libgrapevine.a {Stock} -Wl,--end-group -lm -lpthread -o ~/work/target/debug/app",
            ]);
            sandbox.Files("target").ShouldBe([".pmj-lock", "debug/app", "obj/debug/app.o"]);
            sandbox.Read("target/debug/app").ShouldEndWith("says Hello from the app" + Environment.NewLine);
        }

        [Fact]
        public async Task The_project_is_compiled_where_there_is_no_packages_directory_but_the_one_it_is_given()
        {
            using var sandbox = await WithAnApplicationAsync();

            (await sandbox.RunAsync("build")).Status.ShouldBe(0);

            var compile = sandbox.Tools.Compiles.Last();
            compile.WorkingDirectory.ShouldNotBe(sandbox.Work);
            Directory.Exists(Path.Combine(compile.WorkingDirectory, "packages")).ShouldBeFalse();
        }

        [Fact]
        public async Task On_macOS_the_archives_are_given_without_a_group_which_its_linker_refuses_and_does_not_need()
        {
            using var sandbox = await WithAnApplicationAsync();
            sandbox.MacOS = true;

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldBe(
                $"c++ ~/work/target/obj/debug/app.o ~/work/packages/crypto/libcrypto.a ~/work/packages/deflate/libdeflate.a ~/work/packages/grapevine/libgrapevine.a {Stock} -lm -lpthread -o ~/work/target/debug/app");
        }

        [Fact]
        public async Task An_application_that_depends_on_nothing_is_given_no_search_path_and_is_linked_with_the_stock_archives_alone()
        {
            using var sandbox = Alone();

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Output.ShouldBe(
                """
                Building @someone/app 0.1.0
                Built the application target/debug/app.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Calls.Select(sandbox.Plain).ShouldBe(
            [
                "emojicodec --help",
                "emojicodec ~/work/src/main.🍇 -c -o ~/work/target/obj/debug/app.o",
                $"c++ ~/work/target/obj/debug/app.o -Wl,--start-group {Stock} -Wl,--end-group -lm -lpthread -o ~/work/target/debug/app",
            ]);
            sandbox.Has("packages").ShouldBeFalse();
            Directory.Exists(sandbox.PathOf("packages")).ShouldBeFalse();
        }

        [Fact]
        public async Task A_library_is_compiled_and_archived_into_a_folder_of_its_name_and_nothing_is_linked()
        {
            using var sandbox = Sandbox.WithGrapevine();
            sandbox.Write("packmoji.json", Manifest("library", ", \"dependencies\": { \"@thatplatypus/crypto\": \"1.0\" }"));
            sandbox.Write("src/lib.🍇", "📦 crypto 🏠\n💭 a shelf\n");
            (await sandbox.RunAsync("install", "--repository", Sandbox.Grapevine)).Status.ShouldBe(0);

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            run.Output.ShouldEndWith(
                """
                Building @someone/shelf 0.1.0
                Built the library target/debug/shelf/.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Calls.TakeLast(2).Select(sandbox.Plain).ShouldBe(
            [
                "emojicodec ~/work/src/lib.🍇 -p shelf -c -o ~/work/target/obj/debug/shelf.o -i ~/work/target/debug/shelf/🏛 -r -S ~/work/packages",
                "ar rcs ~/work/target/debug/shelf/libshelf.a ~/work/target/obj/debug/shelf.o",
            ]);
            sandbox.Files("target").ShouldBe([".pmj-lock", "debug/shelf/documentation.json", "debug/shelf/libshelf.a", "debug/shelf/🏛", "obj/debug/shelf.o"]);
            sandbox.Tools.Calls.ShouldNotContain(call => call.Tool == "c++");
        }

        [Fact]
        public async Task The_projects_own_native_files_are_compiled_and_linked_into_the_program()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"sources\": [\"**/*.cpp\", \"native/*.c\"], \"includeDirs\": [\"native/include\"], \"link\": [\"curl\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Write("native/shim.cpp", "#include \"runtime/Runtime.h\"\n#include \"shim.h\"\n");
            sandbox.Write("native/clock.c", "// clock\n");
            sandbox.Write("native/include/shim.h", "// shim\n");
            sandbox.Write("target/native/left.cpp", "#error what pmj itself wrote is no part of the project\n");
            sandbox.Write("packages/mine/native/left.cpp", "#error nor is a package that is kept beside it\n");

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Tools.Calls.Skip(1).Select(sandbox.Plain).ShouldBe(
            [
                "emojicodec ~/work/src/main.🍇 -c -o ~/work/target/obj/debug/app.o",
                "cc -std=gnu11 -O2 -c ~/work/native/clock.c -I ~/include -I ~/work/native/include -o ~/work/target/obj/debug/native-0.o",
                "c++ -std=c++17 -O2 -c ~/work/native/shim.cpp -I ~/include -I ~/work/native/include -o ~/work/target/obj/debug/native-1.o",
                $"c++ ~/work/target/obj/debug/app.o ~/work/target/obj/debug/native-0.o ~/work/target/obj/debug/native-1.o -Wl,--start-group {Stock} -Wl,--end-group -lcurl -lm -lpthread -o ~/work/target/debug/app",
            ]);
        }

        [Fact]
        public async Task A_library_with_native_files_has_them_in_its_archive()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library", ", \"native\": { \"sources\": [\"native/*.cpp\"] }"));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");
            sandbox.Write("native/shim.cpp", "// shim\n");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldBe("ar rcs ~/work/target/debug/shelf/libshelf.a ~/work/target/obj/debug/shelf.o ~/work/target/obj/debug/native-0.o");
        }

        [Fact]
        public async Task The_libraries_of_the_project_and_of_every_package_are_linked_each_once_in_order_of_name()
        {
            using var sandbox = new Sandbox();
            foreach (var (name, link) in new[] { ("net", "[\"pthread\", \"z\"]"), ("web", "[\"curl\", \"z\"]") })
            {
                sandbox.Upload($"github.com/thatplatypus/{name}", $"@thatplatypus/{name}", "1.0.0", TestPackage.Archive(
                    ("packmoji.json", $"{{ \"package\": {{ \"name\": \"@thatplatypus/{name}\", \"version\": \"1.0.0\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }}, \"native\": {{ \"link\": {link} }} }}\n"),
                    ("src/lib.🍇", $"💭 {name}\n")));
            }

            sandbox.Write("packmoji.json", Manifest("app", ", \"dependencies\": { \"@thatplatypus/net\": \"1.0\", \"@thatplatypus/web\": \"1.0\" }, \"native\": { \"link\": [\"ssl\", \"curl\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            (await sandbox.RunAsync("install")).Status.ShouldBe(0);

            (await sandbox.RunAsync("build")).Status.ShouldBe(0);

            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldEndWith(" -Wl,--end-group -lcurl -lpthread -lssl -lz -lm -o ~/work/target/debug/app");
        }

        [Fact]
        public async Task With_release_everything_is_optimized_and_kept_apart_from_what_was_built_without_it()
        {
            using var sandbox = await WithAnApplicationAsync();
            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            sandbox.Tools.Calls.Clear();

            var run = await sandbox.RunAsync("build", "--release");

            run.Error.ShouldBeEmpty();
            run.Output.ShouldBe(
                """
                Building @thatplatypus/crypto 1.0.0
                Building @thatplatypus/deflate 0.1.0
                Building @thatplatypus/grapevine 0.3.0
                Built 3 packages into packages/.
                Building @someone/app 0.1.0
                Built the application target/release/app.

                """.ReplaceLineEndings(Environment.NewLine));
            var compiles = sandbox.Tools.Compiles.Select(sandbox.Plain).ToList();
            compiles[0].ShouldEndWith("/crypto/🏛 -r -O");
            compiles[2].ShouldContain("/grapevine/🏛 -r -O -S ");
            compiles[3].ShouldBe("emojicodec ~/work/src/main.🍇 -c -o ~/work/target/obj/release/app.o -O -S ~/work/packages");
            sandbox.Keys("crypto").Count.ShouldBe(2);
            sandbox.Read("packages/crypto/pmj-build.json").ShouldContain("\"optimized\": true");
            sandbox.Read("packages/crypto/libcrypto.a").ShouldContain("  optimized yes");
            sandbox.Has("target/debug/app").ShouldBeTrue();
            sandbox.Has("target/release/app").ShouldBeTrue();
        }

        [Fact]
        public async Task The_project_is_compiled_every_time_and_its_packages_are_not()
        {
            using var sandbox = await WithAnApplicationAsync();
            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            sandbox.Tools.Calls.Clear();
            sandbox.Write("src/main.🍇", Main.Replace("Hello from the app", "Hello again"));

            var run = await sandbox.RunAsync("build");

            run.Output.ShouldBe(
                """
                3 packages were built before, and are in packages/.
                Building @someone/app 0.1.0
                Built the application target/debug/app.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.Count().ShouldBe(1);
            sandbox.Read("target/debug/app").ShouldEndWith("says Hello again" + Environment.NewLine);
        }

        [Fact]
        public async Task Code_of_the_project_that_the_compiler_refuses_is_reported_behind_the_projects_name_and_no_program_is_left()
        {
            using var sandbox = Alone();
            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            sandbox.Write("src/main.🍇", "🏁 🍇\n💥 Variable \"nothing\" not defined.\n🍉\n");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Output.ShouldBe($"Building @someone/app 0.1.0{Environment.NewLine}");
            sandbox.Plain(run.Error).ShouldBe(
                """
                [app] ~/work/src/main.🍇:2:1: 🚨 error: Variable "nothing" not defined.
                [app]   💥 Variable "nothing" not defined.
                [app]   ⬆️
                error[build.compile-failed]: "@someone/app" 0.1.0 could not be compiled.
                  why: the compiler ended with status 1, and said: ~/work/src/main.🍇:2:1: 🚨 error: Variable "nothing" not defined.
                  fix: mend what the compiler names, which is printed above; if the code is a package's and not yours, tell its author

                """.ReplaceLineEndings(Environment.NewLine));

            // The program of the build before would be taken for the one that was asked for.
            sandbox.Has("target/debug/app").ShouldBeFalse();
        }

        [Fact]
        public async Task A_program_that_cannot_be_linked_is_reported_in_the_linkers_words()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"link\": [\"curl\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Tools.MissingLibraries.Add("curl");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                [app] /usr/bin/ld: cannot find -lcurl: No such file or directory
                [app] collect2: error: ld returned 1 exit status
                error[build.link-failed]: "@someone/app" 0.1.0 could not be linked.
                  why: the linker ended with status 1, and said: /usr/bin/ld: cannot find -lcurl: No such file or directory
                  fix: read what the linker printed, which is above; a library it cannot find is one that a package names under "native.link", and has to be on this machine

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Has("target/debug/app").ShouldBeFalse();
        }

        [Fact]
        public async Task A_linker_that_ends_well_and_leaves_no_program_has_failed()
        {
            using var sandbox = Alone();
            sandbox.Tools.Before = call =>
            {
                // From here on the C++ compiler does nothing and says all is well, and it is the linker.
                if (call.Arguments.Contains("-c"))
                {
                    sandbox.Tools.Idle.Add("c++");
                }

                return Task.CompletedTask;
            };

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[build.link-failed]: \"@someone/app\" 0.1.0 could not be linked.");
            sandbox.Plain(run.Error).ShouldContain("  why: the linker ended as if all were well and did not write \"~/work/target/debug/app\"");
        }

        [Fact]
        public async Task An_application_with_no_main_file_is_not_built_and_none_of_its_packages_is_compiled_first()
        {
            using var sandbox = await WithAnApplicationAsync();
            File.Delete(sandbox.PathOf("src/main.🍇"));

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[entry.not-found]: The package has no entry file.
                  why: the manifest names none, and neither of "src/main.emojic" and "src/main.🍇" exists
                  fix: create one of them, or set "entry" under "build" to the package's main file

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_main_file_that_the_manifest_names_and_that_is_not_there_is_said_to_be_missing()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"build\": { \"entry\": \"app.🍇\" }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[entry.not-found]: The entry file "app.🍇" was not found.
                  why: the manifest names it under "build", and the project has no such file
                  fix: create it, or set "entry" under "build" to the project's main file

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task The_project_is_held_to_the_compiler_it_asks_for_before_anything_is_compiled()
        {
            using var sandbox = await WithAnApplicationAsync();
            sandbox.Write("packmoji.json", sandbox.Read("packmoji.json").Replace(">=1.0.0-beta.2", ">=1.0.0"));
            var compiler = Path.Combine(sandbox.ToolsDirectory, "emojicodec");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                $"""
                error[compiler.too-old]: "@someone/app" 0.1.0 needs a newer Emojicode compiler than the one here.
                  why: its manifest asks for >=1.0.0, and the compiler at "{compiler}" says it is 1.0.0-beta.2
                  fix: use a compiler that is new enough, naming it with EMOJICODEC if it is not the first on the PATH, or lower "emojicode" in packmoji.json if the project builds with this one

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_only_its_dependencies_to_build_the_project_is_held_to_nothing()
        {
            using var sandbox = await WithAnApplicationAsync();
            sandbox.Write("packmoji.json", sandbox.Read("packmoji.json").Replace(">=1.0.0-beta.2", ">=9.0.0"));
            File.Delete(sandbox.PathOf("src/main.🍇"));
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));
            Directory.Delete(sandbox.StockDirectory, recursive: true);

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            Directory.Exists(sandbox.PathOf("target/debug")).ShouldBeFalse();
        }

        [Fact]
        public async Task A_native_file_of_the_project_that_is_neither_c_nor_cpp_is_the_projects_to_mend()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"sources\": [\"native/*\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Write("native/shim.h", "// a header\n");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[native.unsupported]: "native/shim.h" of "@someone/app" 0.1.0 is neither C nor C++.
                  why: its manifest selects it under "native.sources", and pmj compiles a file that ends .c as C, and one that ends .cpp, .cc or .cxx as C++
                  fix: narrow "native.sources" in packmoji.json to the files to compile; headers are named by "native.includeDirs"

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_no_cpp_compiler_an_application_cannot_be_linked_and_nothing_is_compiled()
        {
            using var sandbox = await WithAnApplicationAsync();
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[tool.not-found]: The C++ compiler was not found.
                  why: no program called c++ is in any directory of PATH
                  fix: install a C++ toolchain, or set CXX to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_library_needs_no_linker_and_none_of_the_compilers_own_packages()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library"));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));
            Directory.Delete(sandbox.StockDirectory, recursive: true);

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Fact]
        public async Task A_library_whose_archive_could_not_be_made_leaves_nothing_where_it_was_being_built()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library"));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");
            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            sandbox.Files("target/debug").ShouldBe(["shelf/documentation.json", "shelf/libshelf.a", "shelf/🏛"]);
            sandbox.Tools.Fails.Add("ar");

            var run = await sandbox.RunAsync("build");

            // An interface with no archive beside it is half a package, and would be taken for a whole one.
            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[build.archive-failed]: The archive of \"@someone/shelf\" 0.1.0 could not be made.");
            sandbox.Files("target").ShouldNotContain(file => file.StartsWith("debug/", StringComparison.Ordinal));
        }

        [Fact]
        public async Task With_no_archiver_a_library_is_not_compiled()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library"));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "ar"));

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[tool.not-found]: The archiver was not found." + Environment.NewLine);
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task Without_the_compilers_headers_a_project_with_native_code_is_not_compiled()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"sources\": [\"native/*.cpp\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Write("native/shim.cpp", "// shim\n");
            Directory.Delete(sandbox.IncludeDirectory, recursive: true);

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldStartWith("error[compiler.incomplete]: The Emojicode compiler's headers were not found." + Environment.NewLine);
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task With_no_c_compiler_an_application_with_c_of_its_own_is_not_compiled()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"sources\": [\"native/*.c\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Write("native/clock.c", "// clock\n");
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "cc"));

            var run = await sandbox.RunAsync("build");

            // What is missing is said before anything is compiled, the project's own files as a package's.
            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[tool.not-found]: The C compiler was not found.
                  why: no program called cc is in any directory of PATH
                  fix: install a C toolchain, or set CC to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.Files("target").ShouldBe([".pmj-lock"]);
        }

        [Fact]
        public async Task With_no_cpp_compiler_a_library_with_cpp_of_its_own_is_not_compiled()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library", ", \"native\": { \"sources\": [\"native/*.cpp\"] }"));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");
            sandbox.Write("native/shim.cpp", "// shim\n");
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Output.ShouldBeEmpty();
            run.Error.ShouldBe(
                """
                error[tool.not-found]: The C++ compiler was not found.
                  why: no program called c++ is in any directory of PATH
                  fix: install a C++ toolchain, or set CXX to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
            sandbox.Files("target").ShouldBe([".pmj-lock"]);
        }

        [Fact]
        public async Task An_application_with_cpp_of_its_own_and_no_cpp_compiler_is_told_so_once_though_it_is_needed_twice()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"sources\": [\"native/*.cpp\"] }"));
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Write("native/shim.cpp", "// shim\n");
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));

            var run = await sandbox.RunAsync("build");

            // It compiles the project's C++ and it links the program, and it is one thing to install.
            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[tool.not-found]: The C++ compiler was not found.
                  why: no program called c++ is in any directory of PATH
                  fix: install a C++ toolchain, or set CXX to where it is

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task When_the_compilers_packages_are_where_EMOJICODE_PACKAGES_PATH_says_those_are_linked_and_not_the_installed_ones()
        {
            using var sandbox = Alone();
            var installed = Directory.CreateDirectory(Path.Combine(sandbox.InstallRoot, "EmojicodePackages", "s")).FullName;
            File.WriteAllText(Path.Combine(installed, "libs.a"), "made-up archive\n");

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldContain(" ~/stock/s/libs.a ");
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldNotContain("usr-local");
        }

        [Theory]
        [InlineData("s", "libs.a")]
        [InlineData("runtime", "libruntime.a")]
        public async Task Without_the_compilers_own_packages_an_application_is_not_built_and_pmj_says_where_it_looked(string package, string archive)
        {
            using var sandbox = await WithAnApplicationAsync();
            File.Delete(Path.Combine(sandbox.StockDirectory, package, archive));

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                $"""
                error[compiler.incomplete]: The Emojicode compiler's own packages were not found.
                  why: a program is linked with them, and "{Path.Combine(sandbox.StockDirectory, package, archive)}" is not there
                  fix: set EMOJICODE_PACKAGES_PATH to the directory that holds the compiler's own packages, s and runtime among them: it is where Emojicode's installer was told to put them

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task The_compilers_packages_that_EMOJICODE_PACKAGES_PATH_names_from_where_pmj_is_run_are_named_in_full_to_the_linker()
        {
            // The linker is run where the program is made, where a path from here leads somewhere else.
            using var sandbox = Alone();
            sandbox.Variables["EMOJICODE_PACKAGES_PATH"] = "../stock";

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldContain($" -Wl,--start-group {Stock} -Wl,--end-group ");
        }

        [Fact]
        public async Task With_EMOJICODE_PACKAGES_PATH_not_set_the_compilers_packages_are_looked_for_where_the_installer_puts_them()
        {
            using var sandbox = Alone();
            sandbox.Variables.Remove("EMOJICODE_PACKAGES_PATH");
            var installed = Path.Combine(sandbox.InstallRoot, "EmojicodePackages");

            var without = await sandbox.RunAsync("build");
            Directory.CreateDirectory(sandbox.InstallRoot);
            Directory.Move(sandbox.StockDirectory, installed);
            var with = await sandbox.RunAsync("build");

            without.Status.ShouldBe(1);
            without.Error.ShouldContain($"a program is linked with them, and \"{Path.Combine(installed, "s", "libs.a")}\" is not there");
            with.Error.ShouldBeEmpty();
            with.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldContain(" ~/usr-local/EmojicodePackages/s/libs.a ");
        }

        [Fact]
        public async Task When_EMOJICODE_PACKAGES_PATH_names_packages_of_someones_own_the_compilers_are_taken_from_where_they_were_installed()
        {
            using var sandbox = Alone();
            Directory.CreateDirectory(sandbox.InstallRoot);
            Directory.Move(sandbox.StockDirectory, Path.Combine(sandbox.InstallRoot, "EmojicodePackages"));
            sandbox.Variables["EMOJICODE_PACKAGES_PATH"] = Directory.CreateDirectory(Path.Combine(sandbox.Root, "my-packages", "mine")).Parent!.FullName;

            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldContain(" ~/usr-local/EmojicodePackages/s/libs.a ");
        }

        [Fact]
        public async Task A_stock_package_that_is_not_installed_is_left_out_of_the_link()
        {
            using var sandbox = Alone();
            Directory.Delete(Path.Combine(sandbox.StockDirectory, "testtube"), recursive: true);
            File.Delete(Path.Combine(sandbox.StockDirectory, "json", "libjson.a"));

            var run = await sandbox.RunAsync("build");

            run.Status.ShouldBe(0);
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldContain("-Wl,--start-group ~/stock/files/libfiles.a ~/stock/runtime/libruntime.a ~/stock/s/libs.a ~/stock/sockets/libsockets.a -Wl,--end-group");
        }

        [Fact]
        public async Task What_an_earlier_build_left_in_target_is_not_in_the_way_of_this_one_and_is_no_part_of_it()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library", ", \"native\": { \"sources\": [\"native/*.cpp\"] }").Replace("@someone/shelf", "@someone/app"));
            sandbox.Write("src/lib.🍇", "💭 a library called app\n");
            sandbox.Write("src/main.🍇", "🏁 🍇\n🍉\n");
            sandbox.Write("native/one.cpp", "// one\n");
            sandbox.Write("native/two.cpp", "// two\n");
            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            Directory.Exists(sandbox.PathOf("target/debug/app")).ShouldBeTrue();

            // The same project becomes an application, with one native file fewer.
            sandbox.Write("packmoji.json", Manifest("app", ", \"native\": { \"sources\": [\"native/*.cpp\"] }"));
            File.Delete(sandbox.PathOf("native/two.cpp"));
            var run = await sandbox.RunAsync("build");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Files("target").ShouldBe([".pmj-lock", "debug/app", "obj/debug/app.o", "obj/debug/native-0.o"]);

            // And back: a folder takes the place of the program.
            sandbox.Write("packmoji.json", Manifest("library").Replace("@someone/shelf", "@someone/app"));
            (await sandbox.RunAsync("build")).Status.ShouldBe(0);
            sandbox.Files("target").ShouldBe([".pmj-lock", "debug/app/documentation.json", "debug/app/libapp.a", "debug/app/🏛", "obj/debug/app.o"]);
        }

        [Fact]
        public async Task A_tool_is_told_what_was_made_of_the_project()
        {
            using var sandbox = await WithAnApplicationAsync();

            var run = await sandbox.RunAsync("build", "--release", "--json");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Plain(run.Output).ShouldEndWith(
                """
                  ],
                  "project": {
                    "name": "@someone/app",
                    "version": "0.1.0",
                    "kind": "app",
                    "output": "~/work/target/release/app"
                  }
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public async Task A_tool_is_told_where_a_library_was_built_to_and_nothing_of_a_project_that_could_not_be()
        {
            using var sandbox = new Sandbox();
            sandbox.Write("packmoji.json", Manifest("library"));
            sandbox.Write("src/lib.🍇", "💭 a shelf\n");

            var built = await sandbox.RunAsync("build", "--json");
            sandbox.Write("src/lib.🍇", "💥 no\n");
            var failed = await sandbox.RunAsync("build", "--json");

            sandbox.Plain(built.Output).ShouldContain("    \"kind\": \"library\",\n    \"output\": \"~/work/target/debug/shelf\"\n");
            failed.Status.ShouldBe(1);
            failed.Output.ShouldNotContain("\"project\"");
            failed.Output.ShouldContain("\"code\": \"build.compile-failed\"");
        }
    }
}
