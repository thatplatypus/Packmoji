using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Commands
{
    /// <summary>
    /// A package's native code: its C and C++ files are compiled with the machine's own compilers,
    /// against the Emojicode compiler's headers and the package's own, and go into the package's
    /// archive. A manifest can say which files and where their headers are, and can give no flag.
    /// </summary>
    public sealed class BuildNativeTests
    {
        private const string Net = "@thatplatypus/net";

        private const string Shim = "#include \"runtime/Runtime.h\"\n#include \"net.h\"\nextern \"C\" runtime::Integer netSeven(runtime::ClassInfo*) { return 7; }\n";

        private static string Manifest(string native) =>
            $"{{ \"package\": {{ \"name\": \"{Net}\", \"version\": \"1.0.0\", \"kind\": \"library\", \"emojicode\": \">=1.0.0-beta.2\" }}, \"native\": {native} }}\n";

        // A library with a C++ file and a header of its own, released, depended on and installed.
        private static async Task<Sandbox> WithNetInstalledAsync(string native = "{ \"sources\": [\"native/*.cpp\"], \"includeDirs\": [\"native/include\"], \"link\": [\"pthread\"] }", params (string Path, string Text)[] more)
        {
            var sandbox = new Sandbox();
            sandbox.Upload("github.com/thatplatypus/net", Net, "1.0.0", TestPackage.Archive(
            [
                ("packmoji.json", Manifest(native)),
                ("src/lib.🍇", "💭 net\n"),
                ("native/net.cpp", Shim),
                ("native/include/net.h", "// net\n"),
                .. more,
            ]));
            await sandbox.InstallAsync("@someone/app", Net + "@1.0");
            return sandbox;
        }

        private static string Cache(Sandbox sandbox) =>
            sandbox.Plain(Path.Combine(sandbox.Home, "cache", "thatplatypus", "net", "1.0.0", sandbox.Lockfile().Packages.Single().Sha256.Hex));

        [Fact]
        public async Task A_cpp_file_is_compiled_against_the_compilers_headers_and_the_packages_own_and_goes_into_the_archive()
        {
            using var sandbox = await WithNetInstalledAsync();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            var cache = Cache(sandbox);
            var staged = $"~/home/built/thatplatypus/net/1.0.0/{sandbox.Key("net")}.tmp";
            sandbox.Tools.Calls.Select(sandbox.Plain).ShouldBe(
            [
                "emojicodec --help",
                "c++ --version",
                $"emojicodec {cache}/src/lib.🍇 -p net -c -o {staged}/work/net.o -i {staged}/net/🏛 -r",
                $"c++ -std=c++17 -O2 -c {cache}/native/net.cpp -I ~/include -I {cache}/native/include -o {staged}/work/native-0.o",
                $"ar rcs {staged}/net/libnet.a {staged}/work/net.o {staged}/work/native-0.o",
            ]);
            sandbox.Read("packages/net/libnet.a").ShouldContain(
                """
                member native-0.o
                  made-up native object
                  language c++
                  flags -std=c++17 -O2 -c
                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Read("packages/net/pmj-build.json").ShouldContain("\"link\": [\n    \"pthread\"\n  ]");
        }

        [Fact]
        public async Task A_c_file_goes_to_the_c_compiler_and_files_are_compiled_in_order_of_their_paths()
        {
            using var sandbox = await WithNetInstalledAsync(
                "{ \"sources\": [\"native/**/*.c\", \"native/*.cpp\", \"native/more/*.cc\", \"native/more/*.cxx\"] }",
                ("native/clock.c", "int netClock(void) { return 0; }\n"),
                ("native/more/a.cc", "// a\n"),
                ("native/more/b.cxx", "// b\n"),
                ("native/more/c.c", "// c\n"));
            sandbox.Tools.Calls.Clear();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            // The package's own header is found beside nothing now: the manifest names no directory of headers.
            run.Status.ShouldBe(1);
            run.Error.ShouldContain("fatal error: net.h: No such file or directory");
            var cache = Cache(sandbox);
            sandbox.Tools.Calls.Where(call => call.Tool is "cc" or "c++").Select(sandbox.Plain).Select(call => call.Split(" -o ")[0]).ShouldBe(
            [
                "cc --version",
                "c++ --version",
                $"cc -std=gnu11 -O2 -c {cache}/native/clock.c -I ~/include",
                $"c++ -std=c++17 -O2 -c {cache}/native/more/a.cc -I ~/include",
                $"c++ -std=c++17 -O2 -c {cache}/native/more/b.cxx -I ~/include",
                $"cc -std=gnu11 -O2 -c {cache}/native/more/c.c -I ~/include",
                $"c++ -std=c++17 -O2 -c {cache}/native/net.cpp -I ~/include",
            ]);
        }

        [Fact]
        public async Task Each_native_file_has_an_object_of_its_own_whatever_two_of_them_are_called()
        {
            using var sandbox = await WithNetInstalledAsync(
                "{ \"sources\": [\"native/**/*.cpp\"], \"includeDirs\": [\"native/include\"] }",
                ("native/posix/net.cpp", "// another net.cpp\n"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            var archive = sandbox.Read("packages/net/libnet.a");
            archive.ShouldContain("member native-0.o");
            archive.ShouldContain("member native-1.o");
        }

        [Fact]
        public async Task CXX_and_CC_name_the_two_compilers()
        {
            using var sandbox = await WithNetInstalledAsync(
                "{ \"sources\": [\"native/*.cpp\", \"native/*.c\"], \"includeDirs\": [\"native/include\"] }",
                ("native/clock.c", "// clock\n"));
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "cc"));
            var cxx = sandbox.Tool("elsewhere/clang++-17", "c++", "clang version 17");
            var cc = sandbox.Tool("elsewhere/clang-17", "cc", "clang version 17");
            sandbox.Variables["CXX"] = cxx;
            sandbox.Variables["CC"] = cc;

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Tools.Calls.Where(call => call.Tool == "c++").Select(call => call.Program).Distinct().ShouldBe([cxx]);
            sandbox.Tools.Calls.Where(call => call.Tool == "cc").Select(call => call.Program).Distinct().ShouldBe([cc]);
        }

        [Fact]
        public async Task With_no_cpp_compiler_a_package_that_has_cpp_is_not_built_and_pmj_says_how_to_name_one()
        {
            using var sandbox = await WithNetInstalledAsync();
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

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
        public async Task With_no_c_compiler_a_package_that_has_c_is_not_built()
        {
            using var sandbox = await WithNetInstalledAsync("{ \"sources\": [\"native/*.c\"] }", ("native/clock.c", "// clock\n"));
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "cc"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[tool.not-found]: The C compiler was not found.
                  why: no program called cc is in any directory of PATH
                  fix: install a C toolchain, or set CC to where it is

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_cpp_compiler_that_will_not_say_which_it_is_is_not_used()
        {
            using var sandbox = await WithNetInstalledAsync();
            var silent = sandbox.Tool("tools/c++", "c++");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                $"""
                error[tool.not-found]: The C++ compiler did not say which it is.
                  why: "{silent}" was asked for its version, as pmj asks so that what one compiler built is never taken for another's: it ended with status 1, and said: made-up c++: unrecognized command-line option '--version'
                  fix: check that it is a compiler that answers --version, or set CXX to one that does

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_package_of_Emojicode_alone_needs_neither_compiler()
        {
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            await sandbox.InstallAsync("@someone/app", "@thatplatypus/crypto@1.0");
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "c++"));
            File.Delete(Path.Combine(sandbox.ToolsDirectory, "cc"));
            Directory.Delete(sandbox.IncludeDirectory, recursive: true);

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Fact]
        public async Task A_native_file_the_compiler_refuses_stops_the_build_and_nothing_of_the_package_is_kept()
        {
            using var sandbox = await WithNetInstalledAsync("{ \"sources\": [\"native/*.cpp\"], \"includeDirs\": [\"native/include\"] }", ("native/bad.cpp", "#error made up\n"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            var bad = Cache(sandbox) + "/native/bad.cpp";
            sandbox.Plain(run.Error).ShouldBe(
                $"""
                [net] {bad}:1:2: error: #error made up
                error[build.native-failed]: A native file of "@thatplatypus/net" 1.0.0 could not be compiled.
                  why: the C++ compiler ended with status 1, and said: {bad}:1:2: error: #error made up
                  fix: mend what the compiler names, which is printed above; if the code is a package's and not yours, tell its author

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Built().ShouldBeEmpty();
            sandbox.Has("packages/net/🏛").ShouldBeFalse();
        }

        [Fact]
        public async Task A_native_compiler_that_ends_well_and_writes_no_object_has_failed()
        {
            using var sandbox = await WithNetInstalledAsync();
            sandbox.Tools.Idle.Add("c++");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldContain("error[build.native-failed]: A native file of \"@thatplatypus/net\" 1.0.0 could not be compiled.");
            sandbox.Plain(run.Error).ShouldMatch("  why: the C\\+\\+ compiler ended as if all were well and did not write \"~/home/built/thatplatypus/net/1\\.0\\.0/[0-9a-f]{8}\\.tmp/work/native-0\\.o\"");
            sandbox.Built().ShouldBeEmpty();
        }

        [Fact]
        public async Task What_a_native_compiler_says_of_a_file_it_accepts_is_passed_on()
        {
            using var sandbox = await WithNetInstalledAsync("{ \"sources\": [\"native/*.cpp\"], \"includeDirs\": [\"native/include\"] }", ("native/old.cpp", "#warning made up\n"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(0);
            sandbox.Plain(run.Error).ShouldBe($"[net] {Cache(sandbox)}/native/old.cpp:1:2: warning: #warning made up{Environment.NewLine}");
        }

        [Fact]
        public async Task Files_that_are_neither_c_nor_cpp_are_each_named_and_nothing_is_compiled()
        {
            using var sandbox = await WithNetInstalledAsync(
                "{ \"sources\": [\"native/*\"] }",
                ("native/net.h", "// a header among the sources\n"),
                ("native/notes.txt", "notes\n"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                """
                error[native.unsupported]: "native/net.h" of "@thatplatypus/net" 1.0.0 is neither C nor C++.
                  why: its manifest selects it under "native.sources", and pmj compiles a file that ends .c as C, and one that ends .cpp, .cc or .cxx as C++
                  fix: tell its author: "native.sources" is for the files to compile, and headers are named by "native.includeDirs"
                error[native.unsupported]: "native/notes.txt" of "@thatplatypus/net" 1.0.0 is neither C nor C++.
                  why: its manifest selects it under "native.sources", and pmj compiles a file that ends .c as C, and one that ends .cpp, .cc or .cxx as C++
                  fix: tell its author: "native.sources" is for the files to compile, and headers are named by "native.includeDirs"

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task Without_the_compilers_headers_a_package_with_native_code_is_not_built_and_pmj_says_where_it_looked()
        {
            using var sandbox = await WithNetInstalledAsync();
            File.Delete(Path.Combine(sandbox.IncludeDirectory, "runtime", "Runtime.h"));

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Status.ShouldBe(1);
            run.Error.ShouldBe(
                $"""
                error[compiler.incomplete]: The Emojicode compiler's headers were not found.
                  why: native code is compiled against them, and "{Path.Combine(sandbox.IncludeDirectory, "runtime", "Runtime.h")}" is not there
                  fix: set EMOJICODE_INCLUDE to the directory that holds runtime/Runtime.h, which is where Emojicode's installer was told to put its headers

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Tools.Compiles.ShouldBeEmpty();
        }

        [Fact]
        public async Task Headers_that_EMOJICODE_INCLUDE_names_from_where_pmj_is_run_are_named_in_full_to_the_compiler()
        {
            // The native compiler is run in another directory, where a path from here leads somewhere else.
            using var sandbox = await WithNetInstalledAsync();
            sandbox.Variables["EMOJICODE_INCLUDE"] = "../include";

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            sandbox.Tools.Calls.Select(sandbox.Plain).ShouldContain(call => call.Contains(" -I ~/include -I "));
        }

        [Fact]
        public async Task With_EMOJICODE_INCLUDE_not_set_the_headers_are_looked_for_where_the_installer_puts_them()
        {
            using var sandbox = await WithNetInstalledAsync();
            sandbox.Variables.Remove("EMOJICODE_INCLUDE");
            var installed = Path.Combine(sandbox.InstallRoot, "include", "emojicode");

            var without = await sandbox.RunAsync("build", "--dependencies-only");
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            Directory.Move(sandbox.IncludeDirectory, installed);
            var with = await sandbox.RunAsync("build", "--dependencies-only");

            without.Status.ShouldBe(1);
            without.Error.ShouldContain($"native code is compiled against them, and \"{Path.Combine(installed, "runtime", "Runtime.h")}\" is not there");
            with.Error.ShouldBeEmpty();
            with.Status.ShouldBe(0);
            sandbox.Tools.Calls.Select(sandbox.Plain).ShouldContain(call => call.Contains(" -I ~/usr-local/include/emojicode -I "));
        }

        [Fact]
        public async Task Once_a_package_with_native_code_is_built_its_headers_are_not_needed_again()
        {
            using var sandbox = await WithNetInstalledAsync();
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            Directory.Delete(sandbox.IncludeDirectory, recursive: true);

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Fact]
        public async Task Another_cpp_compiler_builds_a_package_with_cpp_again_and_leaves_a_package_of_Emojicode_alone()
        {
            // One package of Emojicode alone is built before the one with C++, and one after it.
            using var sandbox = new Sandbox();
            sandbox.Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            sandbox.Release("github.com/thatplatypus/zebra", "@thatplatypus/zebra", "1.0.0");
            sandbox.Upload("github.com/thatplatypus/net", Net, "1.0.0", TestPackage.Archive(
                ("packmoji.json", Manifest("{ \"sources\": [\"native/*.cpp\"] }")),
                ("src/lib.🍇", "💭 net\n"),
                ("native/net.cpp", "// net\n")));
            await sandbox.InstallAsync("@someone/app", Net + "@1.0", "@thatplatypus/crypto@1.0", "@thatplatypus/zebra@1.0");
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            sandbox.Tool("tools/c++", "c++", "c++ (Made Up) 12.1.0");

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Output.ShouldBe(
                """
                Building @thatplatypus/net 1.0.0
                Built 1 package into packages/, and 2 were built before.

                """.ReplaceLineEndings(Environment.NewLine));
            sandbox.Keys("net").Count.ShouldBe(2);
            sandbox.Keys("crypto").Count.ShouldBe(1);
            sandbox.Keys("zebra").Count.ShouldBe(1);
        }

        [Fact]
        public async Task A_c_compiler_that_changes_does_not_build_a_package_with_cpp_alone_again()
        {
            using var sandbox = await WithNetInstalledAsync();
            (await sandbox.RunAsync("build", "--dependencies-only")).Status.ShouldBe(0);
            sandbox.Tool("tools/cc", "cc", "cc (Made Up) 12.1.0");
            sandbox.Tools.Calls.Clear();

            var run = await sandbox.RunAsync("build", "--dependencies-only");

            run.Output.ShouldBe($"1 package was built before, and is in packages/.{Environment.NewLine}");
            sandbox.Tools.Calls.ShouldNotContain(call => call.Tool == "cc");
        }
    }
}
