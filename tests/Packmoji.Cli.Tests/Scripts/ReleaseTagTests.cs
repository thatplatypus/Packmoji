using Packmoji.Cli.Building;
using Shouldly;
using Xunit;

namespace Packmoji.Cli.Tests.Scripts
{
    /// <summary>
    /// <c>scripts/release-tag.sh</c>, which holds the tag that started a release to the version the
    /// programs will say they are. A release names what it is.
    /// </summary>
    public sealed class ReleaseTagTests
    {
        private static readonly string Script = Path.Combine(AppContext.BaseDirectory, "scripts", "release-tag.sh");

        private static async Task<ToolRun> RunAsync(params string[] arguments)
        {
            Assert.SkipWhen(OperatingSystem.IsWindows(), "The script is run by bash, which a test does not look for on Windows.");
            var run = await new ProcessToolRunner().RunAsync("/bin/bash", [Script, .. arguments], AppContext.BaseDirectory, TestContext.Current.CancellationToken);
            return run.ShouldNotBeNull();
        }

        [Fact]
        public async Task The_tag_of_this_version_is_v_and_the_version()
        {
            var run = await RunAsync($"v{PmjHost.Version}");

            run.Error.ShouldBeEmpty();
            run.Status.ShouldBe(0);
        }

        [Theory]
        [InlineData("v9.9.9")]
        [InlineData("0.1.0")]
        [InlineData("v0.1.0-rc.1")]
        [InlineData("V0.1.0")]
        [InlineData("")]
        public async Task Any_other_tag_is_refused_and_both_are_named(string tag)
        {
            var run = await RunAsync(tag);

            run.Status.ShouldBe(1);
            run.Error.ShouldContain($"v{PmjHost.Version}");
            run.Error.ShouldContain($"\"{tag}\"");
        }
    }
}
