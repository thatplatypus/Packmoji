using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Registry;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;
using static Packmoji.Core.Tests.Registry.RegistrySample;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// The last line of a limit on scopes, for a registry: the place that reads a registry asks for
    /// the file of no package outside the list, whatever asked. Nothing above it should let such a
    /// request come. This is what keeps the promise if something above it is ever wrong.
    /// </summary>
    public sealed class ScopedRegistryFilesTests
    {
        private static ScopeLimit Only(string scopes)
        {
            ScopeLimit.TryParse(scopes, out var limit, out _).ShouldBeTrue();
            return limit!;
        }

        private static MemoryRegistryFiles Held() =>
            new MemoryRegistryFiles().Hold(State(Package("@thatplatypus/crypto", Version("1.0.0")), Package("@someone/thing", Version("1.0.0"))));

        [Fact]
        public async Task The_file_of_a_package_outside_the_list_is_asked_of_nobody()
        {
            var files = Held();
            var scoped = new ScopedRegistryFiles(files, Only("thatplatypus,emojicode"));

            var thrown = await Should.ThrowAsync<PackageSourceException>(() => scoped.ReadAsync("v1/packages/someone/thing.json", 1024 * 1024, TestContext.Current.CancellationToken));

            thrown.Diagnostic.Code.ShouldBe(DiagnosticCodes.ScopeNotAllowed);
            thrown.Diagnostic.Message.ShouldBe("\"@someone/thing\" is outside the scopes pmj is limited to here.");
            thrown.Diagnostic.Reason.ShouldBe("the registry was about to be asked for it, and PACKMOJI_SCOPES allows only emojicode and thatplatypus");
            thrown.Diagnostic.Fix.ShouldBe("depend only on packages of those scopes; the list is set by whoever runs pmj here");
            files.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task The_file_of_a_package_inside_the_list_is_read_as_ever()
        {
            var files = Held();
            var scoped = new ScopedRegistryFiles(files, Only("thatplatypus"));

            var read = await scoped.ReadAsync("v1/packages/thatplatypus/crypto.json", 1024 * 1024, TestContext.Current.CancellationToken);
            var absent = await scoped.ReadAsync("v1/packages/thatplatypus/deflate.json", 1024 * 1024, TestContext.Current.CancellationToken);

            read.ShouldNotBeNull().Length.ShouldBeGreaterThan(0);
            absent.ShouldBeNull();
            files.Asked.ShouldBe(["v1/packages/thatplatypus/crypto.json", "v1/packages/thatplatypus/deflate.json"]);
            scoped.Address.ShouldBe(files.Address);
        }

        [Fact]
        public async Task A_source_over_it_reads_what_is_allowed_and_is_stopped_at_what_is_not()
        {
            var files = Held();
            var source = new RegistryPackageSource(new ScopedRegistryFiles(files, Only("thatplatypus")));

            (await source.FindAsync(Sample.Name("@thatplatypus/crypto"), Sample.Version("1.0.0"), TestContext.Current.CancellationToken)).ShouldNotBeNull();
            var thrown = await Should.ThrowAsync<PackageSourceException>(async () => await source.FindAsync(Sample.Name("@someone/thing"), Sample.Version("1.0.0"), TestContext.Current.CancellationToken));

            thrown.Diagnostic.Code.ShouldBe(DiagnosticCodes.ScopeNotAllowed);
            files.Asked.ShouldBe(["v1/packages/thatplatypus/crypto.json"]);
        }
    }
}
