using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Identity;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Direct
{
    /// <summary>
    /// The last line of a limit on scopes: the place that asks GitHub refuses an owner outside the
    /// list, whatever asked. Nothing above it should let such a request come. This is what keeps the
    /// promise if something above it is ever wrong.
    /// </summary>
    public sealed class ScopedReleaseHostTests
    {
        private static ScopeLimit Only(string scopes)
        {
            ScopeLimit.TryParse(scopes, out var limit, out _).ShouldBeTrue();
            return limit!;
        }

        [Fact]
        public async Task An_owner_outside_the_list_is_asked_nothing_neither_for_what_it_released_nor_for_a_download()
        {
            var github = new FakeReleaseHost().Release("github.com/someone/thing", "@someone/thing", "1.0.0");
            var scoped = new ScopedReleaseHost(github, Only("thatplatypus,emojicode"));
            var repository = Sample.Repository("github.com/someone/thing");

            var listing = await Should.ThrowAsync<PackageSourceException>(() => scoped.ListAsync(repository, TestContext.Current.CancellationToken));
            var download = await Should.ThrowAsync<PackageSourceException>(() => scoped.DownloadAsync(repository, "thing-v1.0.0", "thing-1.0.0.pmj.tar.gz", 1024, TestContext.Current.CancellationToken));

            listing.Diagnostic.Code.ShouldBe(DiagnosticCodes.ScopeNotAllowed);
            listing.Diagnostic.Message.ShouldBe("github.com/someone/thing belongs to an owner outside the scopes pmj is limited to here.");
            listing.Diagnostic.Reason.ShouldBe("PACKMOJI_SCOPES allows only emojicode and thatplatypus, and nothing is asked of any other owner");
            listing.Diagnostic.Fix.ShouldBe("depend only on packages of those scopes; the list is set by whoever runs pmj here");
            download.Diagnostic.Code.ShouldBe(DiagnosticCodes.ScopeNotAllowed);
            github.Listings.ShouldBeEmpty();
            github.Downloads.ShouldBeEmpty();
        }

        [Fact]
        public async Task An_owner_inside_the_list_is_asked_as_ever()
        {
            var github = new FakeReleaseHost().Release("github.com/thatplatypus/crypto", "@thatplatypus/crypto", "1.0.0");
            var scoped = new ScopedReleaseHost(github, Only("thatplatypus"));
            var repository = Sample.Repository("github.com/thatplatypus/crypto");

            var listed = await scoped.ListAsync(repository, TestContext.Current.CancellationToken);
            var downloaded = await scoped.DownloadAsync(repository, "crypto-v1.0.0", "crypto-1.0.0.pmj.tar.gz", 1024 * 1024, TestContext.Current.CancellationToken);

            listed.Count.ShouldBe(1);
            downloaded.ShouldNotBeNull();
            github.Listings.Count.ShouldBe(1);
            github.Downloads.Count.ShouldBe(1);
        }
    }
}
