using System.Text;
using Packmoji.Core.Building;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Building
{
    /// <summary>
    /// A stamp lies beside every built package. It says what the package was built from, and in a
    /// project's own directory it is how pmj knows a folder as one it put there, and which build of
    /// the package the folder holds.
    /// </summary>
    public sealed class BuildStampTests
    {
        private static BuildStamp Grapevine(params string[] link) => new(
            Sample.Sha('5'),
            Sample.Name("@thatplatypus/grapevine"),
            Sample.Version("0.1.0"),
            Sample.Sha('e'),
            Sample.Version("1.0.0-beta.2"),
            Sample.Sha('0'),
            Optimized: false,
            new Dictionary<PackageName, Sha256Digest>
            {
                [Sample.Name("@thatplatypus/deflate")] = Sample.Sha('2'),
                [Sample.Name("@thatplatypus/crypto")] = Sample.Sha('9'),
            },
            link);

        private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

        [Fact]
        public void A_stamp_is_written_as_this_text()
        {
            Grapevine("pthread", "m").Write().ShouldBe(
                $$"""
                {
                  "version": 1,
                  "key": "{{new string('5', 64)}}",
                  "package": {
                    "name": "@thatplatypus/grapevine",
                    "version": "0.1.0",
                    "sha256": "{{new string('e', 64)}}"
                  },
                  "compiler": {
                    "version": "1.0.0-beta.2",
                    "sha256": "{{new string('0', 64)}}"
                  },
                  "optimized": false,
                  "dependencies": [
                    {
                      "name": "@thatplatypus/crypto",
                      "key": "{{new string('9', 64)}}"
                    },
                    {
                      "name": "@thatplatypus/deflate",
                      "key": "{{new string('2', 64)}}"
                    }
                  ],
                  "link": [
                    "pthread",
                    "m"
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void The_key_a_stamp_gives_is_the_one_it_was_written_with() =>
            BuildStamp.KeyIn(Bytes(Grapevine().Write())).ShouldBe(Sample.Sha('5'));

        [Fact]
        public void A_stamp_that_begins_with_a_byte_order_mark_still_gives_its_key() =>
            BuildStamp.KeyIn((byte[])[0xEF, 0xBB, 0xBF, .. Bytes(Grapevine().Write())]).ShouldBe(Sample.Sha('5'));

        [Theory]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("{ \"version\": 1 }")]
        [InlineData("{ \"version\": 1, \"key\": 5 }")]
        [InlineData("{ \"version\": 1, \"key\": \"5555\" }")]
        [InlineData("{ \"version\": 1, \"key\": \"5555555555555555555555555555555555555555555555555555555555555555\", }")]
        [InlineData("{ \"version\": 1, \"key\": \"5555555555555555555555555555555555555555555555555555555555555555\", \"key\": \"5555555555555555555555555555555555555555555555555555555555555555\" }")]
        public void What_is_not_a_stamp_gives_no_key(string text) => BuildStamp.KeyIn(Bytes(text)).ShouldBeNull();

        [Theory]
        [InlineData("2")]
        [InlineData("\"1\"")]
        [InlineData("1.0")]
        public void A_stamp_of_another_version_gives_no_key_whatever_else_it_holds(string version) =>
            BuildStamp.KeyIn(Bytes($"{{ \"version\": {version}, \"key\": \"{new string('5', 64)}\" }}")).ShouldBeNull();

        [Fact]
        public void A_later_stamp_of_this_version_may_hold_more_and_still_gives_its_key() =>
            BuildStamp.KeyIn(Bytes($"{{ \"version\": 1, \"key\": \"{new string('5', 64)}\", \"more\": [1, 2] }}")).ShouldBe(Sample.Sha('5'));
    }
}
