using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Resolution
{
    /// <summary>
    /// The resolver asks its source for exact versions and for nothing else, which is what lets a
    /// source be a repository's releases as readily as a registry.
    /// </summary>
    public sealed class ResolverSourceTests
    {
        [Fact]
        public async Task Each_version_is_asked_for_once_however_many_requirements_name_it()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/c@1.0")
                .Publish("@thatplatypus/b", "1.0.0", "@thatplatypus/c@1.0.0")
                .Publish("@thatplatypus/c", "1.0.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/a@1.0", "@thatplatypus/b@1.0", "@thatplatypus/c@1.0"))).ShouldSucceed();

            universe.Asked.ShouldBe(["@thatplatypus/a@1.0.0", "@thatplatypus/b@1.0.0", "@thatplatypus/c@1.0.0"]);
        }

        [Fact]
        public async Task Versions_are_asked_for_nearest_first_and_in_order_of_name_however_they_were_written()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/z", "1.0.0", "@thatplatypus/b@1.0")
                .Publish("@thatplatypus/a", "1.0.0", "@thatplatypus/y@1.0", "@thatplatypus/c@1.0")
                .Publish("@thatplatypus/b", "1.0.0")
                .Publish("@thatplatypus/c", "1.0.0")
                .Publish("@thatplatypus/y", "1.0.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/z@1.0", "@thatplatypus/a@1.0"))).ShouldSucceed();

            universe.Asked.ShouldBe(
            [
                "@thatplatypus/a@1.0.0",
                "@thatplatypus/z@1.0.0",
                "@thatplatypus/c@1.0.0",
                "@thatplatypus/y@1.0.0",
                "@thatplatypus/b@1.0.0",
            ]);
        }

        [Fact]
        public async Task No_version_is_asked_for_that_no_requirement_names()
        {
            var universe = new Universe()
                .Publish("@thatplatypus/crypto", "1.0.0")
                .Publish("@thatplatypus/crypto", "1.0.1")
                .Publish("@thatplatypus/crypto", "1.1.0");

            (await universe.Resolve(Project.Asking("@thatplatypus/crypto@1.0"))).ShouldSucceed();

            universe.Asked.ShouldBe(["@thatplatypus/crypto@1.0.0"]);
        }

        [Fact]
        public async Task An_answer_for_another_version_than_was_asked_is_a_fault_in_the_source_and_is_thrown()
        {
            var source = new AnsweringSource((_, _) => Sample.Published("@thatplatypus/crypto", "1.0.1"));

            var thrown = await Should.ThrowAsync<InvalidOperationException>(
                () => Resolver.ResolveAsync(Project.Asking("@thatplatypus/crypto@1.0"), null, source, TestContext.Current.CancellationToken));

            thrown.Message.ShouldContain("@thatplatypus/crypto@1.0.0");
            thrown.Message.ShouldContain("@thatplatypus/crypto@1.0.1");
        }

        [Fact]
        public async Task An_answer_for_another_package_than_was_asked_is_thrown_as_well()
        {
            var source = new AnsweringSource((_, _) => Sample.Published("@thatplatypus/deflate", "1.0.0"));

            var thrown = await Should.ThrowAsync<InvalidOperationException>(
                () => Resolver.ResolveAsync(Project.Asking("@thatplatypus/crypto@1.0"), null, source, TestContext.Current.CancellationToken));

            thrown.Message.ShouldContain("@thatplatypus/crypto@1.0.0");
            thrown.Message.ShouldContain("@thatplatypus/deflate@1.0.0");
        }

        [Fact]
        public async Task A_source_that_could_not_find_out_throws_and_the_resolver_does_not_take_that_for_an_answer()
        {
            var source = new AnsweringSource((_, _) => throw new IOException("the network is down"));

            var thrown = await Should.ThrowAsync<IOException>(
                () => Resolver.ResolveAsync(Project.Asking("@thatplatypus/crypto@1.0"), null, source, TestContext.Current.CancellationToken));

            thrown.Message.ShouldBe("the network is down");
        }

        [Fact]
        public async Task A_resolution_that_was_cancelled_asks_nothing_more()
        {
            var universe = new Universe().Publish("@thatplatypus/crypto", "1.0.0");
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(
                () => Resolver.ResolveAsync(Project.Asking("@thatplatypus/crypto@1.0"), null, universe, cancelled.Token));

            universe.Asked.ShouldBeEmpty();
        }

        [Fact]
        public async Task A_manifest_and_a_source_must_be_given()
        {
            await Should.ThrowAsync<ArgumentNullException>(
                () => Resolver.ResolveAsync(null!, null, new Universe(), TestContext.Current.CancellationToken));
            await Should.ThrowAsync<ArgumentNullException>(
                () => Resolver.ResolveAsync(Project.Asking(), null, null!, TestContext.Current.CancellationToken));
        }
    }
}
