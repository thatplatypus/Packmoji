using Packmoji.Cli.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Samples
{
    /// <summary>
    /// The sample projects, taken through everything pmj does with a package and with what depends
    /// on it: packed, released, installed, built and run. The compiler here is made up. The same
    /// sample is built by the real one in <see cref="RealCompilerTests"/>.
    /// </summary>
    public sealed class HelloSampleTests
    {
        private const string App = "hello-pkg/app";

        [Fact]
        public async Task The_lockfile_the_sample_is_kept_with_is_the_one_pmj_writes_for_it()
        {
            using var sandbox = new Sandbox();
            await sandbox.ReleaseTheSampleAsync();
            var kept = sandbox.Has($"{App}/packmoji.lock") ? sandbox.Read($"{App}/packmoji.lock") : "";
            File.Delete(sandbox.PathOf($"{App}/packmoji.lock"));

            var run = await sandbox.RunInAsync(App, "install");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
            kept.ShouldBe(sandbox.Read($"{App}/packmoji.lock"), "samples/hello-pkg/app/packmoji.lock is behind the two libraries: it should hold what is shown here");
        }

        [Fact]
        public async Task The_sample_is_installed_as_it_is_locked_and_built_in_the_order_its_imports_need()
        {
            using var sandbox = new Sandbox();
            await sandbox.ReleaseTheSampleAsync();

            var installed = await sandbox.RunInAsync(App, "install", "--locked");
            var built = await sandbox.RunInAsync(App, "build");

            installed.Error.ShouldBeEmpty();
            installed.Status.ShouldBe(0);
            built.Error.ShouldBeEmpty();
            built.Status.ShouldBe(0);
            built.Output.ShouldBe(
                """
                Building @thatplatypus/hello_words 0.1.0
                Building @thatplatypus/hello_greeter 0.1.0
                Built 2 packages into packages/.
                Building @thatplatypus/hello 0.1.0
                Built the application target/debug/hello.

                """.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public async Task The_greeters_cpp_is_compiled_against_its_own_header_and_its_library_is_linked_into_the_program()
        {
            using var sandbox = new Sandbox();
            await sandbox.ReleaseTheSampleAsync();
            (await sandbox.RunInAsync(App, "install", "--locked")).Status.ShouldBe(0);

            (await sandbox.RunInAsync(App, "build")).Status.ShouldBe(0);

            var native = sandbox.Tools.Calls.Single(call => call.Tool == "c++" && call.Arguments.Contains("-c"));
            sandbox.Plain(native).ShouldMatch("^c\\+\\+ -std=c\\+\\+17 -O2 -c ~/home/cache/thatplatypus/hello_greeter/0\\.1\\.0/[0-9a-f]{8}/native/answer\\.cpp -I ~/include -I ~/home/cache/thatplatypus/hello_greeter/0\\.1\\.0/[0-9a-f]{8}/native/include -o ");
            sandbox.Read($"{App}/packages/hello_greeter/libhello_greeter.a").ShouldContain("  include answer.h from include");
            sandbox.Plain(sandbox.Tools.Calls.Last()).ShouldBe(
                "c++ ~/work/hello-pkg/app/target/obj/debug/hello.o -Wl,--start-group ~/work/hello-pkg/app/packages/hello_greeter/libhello_greeter.a ~/work/hello-pkg/app/packages/hello_words/libhello_words.a "
                + "~/stock/files/libfiles.a ~/stock/json/libjson.a ~/stock/runtime/libruntime.a ~/stock/s/libs.a ~/stock/sockets/libsockets.a ~/stock/testtube/libtesttube.a -Wl,--end-group -lm -lpthread -o ~/work/hello-pkg/app/target/debug/hello");
        }

        [Fact]
        public async Task The_sample_runs()
        {
            using var sandbox = new Sandbox();
            await sandbox.ReleaseTheSampleAsync();
            (await sandbox.RunInAsync(App, "install", "--locked")).Status.ShouldBe(0);

            var run = await sandbox.RunInAsync(App, "run");

            run.Status.ShouldBe(0);
            sandbox.Tools.Calls.Last().Program.ShouldBe(sandbox.PathOf($"{App}/target/debug/hello"));
        }
    }
}
