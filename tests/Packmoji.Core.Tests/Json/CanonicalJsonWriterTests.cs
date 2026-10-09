using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Json
{
    public sealed class CanonicalJsonWriterTests
    {
        [Fact]
        public void The_layout_is_two_spaces_one_entry_to_a_line_and_a_final_newline()
        {
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            json.WriteString("name", "@thatplatypus/crypto");
            json.WriteNumber("version", 1);
            json.WriteBoolean("flag", false);
            json.WriteStrings("empty", []);
            json.WriteStrings("link", ["pthread", "m"]);
            json.WriteStartObject("nothing");
            json.WriteEndObject();
            json.WriteStartArray("packages");
            json.WriteStartObject();
            json.WriteString("k", "v");
            json.WriteEndObject();
            json.WriteStartObject();
            json.WriteEndObject();
            json.WriteEndArray();
            json.WriteEndObject();

            json.ToString().ShouldBe("""
                {
                  "name": "@thatplatypus/crypto",
                  "version": 1,
                  "flag": false,
                  "empty": [],
                  "link": [
                    "pthread",
                    "m"
                  ],
                  "nothing": {},
                  "packages": [
                    {
                      "k": "v"
                    },
                    {}
                  ]
                }

                """.ReplaceLineEndings("\n"));
        }

        [Fact]
        public void A_string_is_escaped_only_where_json_requires_it()
        {
            var json = new CanonicalJsonWriter();
            json.WriteStartArray();
            json.WriteStringValue("src/lib.🍇");
            json.WriteStringValue(">=1.0.0-beta.2");
            json.WriteStringValue("@thatplatypus/c++ é");
            json.WriteStringValue("quote \" and backslash \\");
            json.WriteStringValue("bell \u0007 tab \t");
            json.WriteStringValue("half \ud83c pair");
            json.WriteEndArray();

            json.ToString().ShouldBe(
                "[\n" +
                "  \"src/lib.🍇\",\n" +
                "  \">=1.0.0-beta.2\",\n" +
                "  \"@thatplatypus/c++ é\",\n" +
                "  \"quote \\\" and backslash \\\\\",\n" +
                "  \"bell \\u0007 tab \\u0009\",\n" +
                "  \"half \\uD83C pair\"\n" +
                "]\n");
        }

        [Fact]
        public void What_is_written_is_read_back_the_same()
        {
            var json = new CanonicalJsonWriter();
            json.WriteStartObject();
            json.WriteString("entry", "src/lib.🍇");
            json.WriteString("odd", "quote \" backslash \\ bell \u0007");
            json.WriteEndObject();

            var diagnostics = new DiagnosticList();
            var root = JsonTreeReader.Read(Encoding.UTF8.GetBytes(json.ToString()), "test.json", "resolve the conflict", diagnostics);

            diagnostics.ShouldBeEmpty();
            root.ShouldNotBeNull().Members[0].Value.Text.ShouldBe("src/lib.🍇");
            root.Members[1].Value.Text.ShouldBe("quote \" backslash \\ bell \u0007");
        }

        [Fact]
        public void A_value_that_is_only_a_scalar_is_written_with_its_newline()
        {
            var json = new CanonicalJsonWriter();
            json.WriteStringValue("alone");

            json.ToString().ShouldBe("\"alone\"\n");
        }
    }
}
