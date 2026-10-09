using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Json;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Json
{
    public sealed class JsonTreeReaderTests
    {
        private const string File = "test.json";
        private const string ConflictFix = "resolve the conflict";

        private static (JsonItem? Root, DiagnosticList Diagnostics) Read(string text) => Read(Encoding.UTF8.GetBytes(text));

        private static (JsonItem? Root, DiagnosticList Diagnostics) Read(byte[] bytes)
        {
            var diagnostics = new DiagnosticList();
            var root = JsonTreeReader.Read(bytes, File, ConflictFix, diagnostics);
            return (root, diagnostics);
        }

        private static Diagnostic ShouldBeOneSyntaxError((JsonItem? Root, DiagnosticList Diagnostics) read)
        {
            read.Root.ShouldBeNull();
            var diagnostic = read.Diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.JsonSyntax);
            diagnostic.Location.ShouldNotBeNull().File.ShouldBe(File);
            return diagnostic;
        }

        [Fact]
        public void Every_kind_of_value_is_read()
        {
            var (root, diagnostics) = Read("""{"text": "lib.🍇", "number": 1, "yes": true, "no": false, "nothing": null, "list": ["a", 2], "inner": {}}""");

            diagnostics.ShouldBeEmpty();
            root.ShouldNotBeNull().Kind.ShouldBe(JsonKind.Object);
            root.Members.Select(member => member.Name).ShouldBe(["text", "number", "yes", "no", "nothing", "list", "inner"]);

            var values = root.Members.ToDictionary(member => member.Name, member => member.Value);
            values["text"].Kind.ShouldBe(JsonKind.String);
            values["text"].Text.ShouldBe("lib.🍇");
            values["number"].Kind.ShouldBe(JsonKind.Number);
            values["number"].Text.ShouldBe("1");
            values["yes"].Kind.ShouldBe(JsonKind.Boolean);
            values["yes"].IsTrue.ShouldBeTrue();
            values["no"].IsTrue.ShouldBeFalse();
            values["nothing"].Kind.ShouldBe(JsonKind.Null);
            values["list"].Kind.ShouldBe(JsonKind.Array);
            values["list"].Items.Select(item => item.Text).ShouldBe(["a", "2"]);
            values["inner"].Kind.ShouldBe(JsonKind.Object);
            values["inner"].Members.ShouldBeEmpty();
        }

        [Fact]
        public void A_number_keeps_the_spelling_it_was_written_with()
        {
            var (root, _) = Read("[1, 1.0, 1e0, -0]");

            root.ShouldNotBeNull().Items.Select(item => item.Text).ShouldBe(["1", "1.0", "1e0", "-0"]);
        }

        [Fact]
        public void An_escape_in_a_key_or_a_string_is_decoded()
        {
            var (root, _) = Read("""{"\u0061b": "c\n\ud83c\udf47"}""");

            root.ShouldNotBeNull().Members[0].Name.ShouldBe("ab");
            root.Members[0].Value.Text.ShouldBe("c\n🍇");
        }

        [Fact]
        public void A_key_and_a_value_know_their_line_and_their_column_in_code_points()
        {
            var (root, _) = Read("{\n  \"a\": \"🍇\",\n  \"🍇🍇\": true\n}");

            root.ShouldNotBeNull().Location.ShouldBe(new SourceLocation(File, 1, 1));
            root.Members[0].NameLocation.ShouldBe(new SourceLocation(File, 2, 3));
            root.Members[0].Value.Location.ShouldBe(new SourceLocation(File, 2, 8));
            root.Members[1].NameLocation.ShouldBe(new SourceLocation(File, 3, 3));
            root.Members[1].Value.Location.ShouldBe(new SourceLocation(File, 3, 9));
        }

        [Fact]
        public void A_syntax_error_is_reported_where_it_is_even_after_an_emoji()
        {
            var marked = Marked.From("""
                {
                  "a": "🍇",
                  "🍇🍇": §oops
                }
                """);

            var diagnostic = ShouldBeOneSyntaxError(Read(marked.Text));

            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
        }

        [Fact]
        public void Line_ends_of_carriage_return_and_line_feed_read_and_keep_their_line_numbers()
        {
            var (root, diagnostics) = Read("{\r\n  \"a\": 1,\r\n  \"b\": 2\r\n}\r\n");

            diagnostics.ShouldBeEmpty();
            root.ShouldNotBeNull().Members[1].NameLocation.ShouldBe(new SourceLocation(File, 3, 3));

            var broken = ShouldBeOneSyntaxError(Read("{\r\n  \"a\": 1,\r\n  \"b\": oops\r\n}\r\n"));
            broken.Location.ShouldBe(new SourceLocation(File, 3, 8));
        }

        [Fact]
        public void A_byte_order_mark_is_skipped_and_is_not_a_column()
        {
            byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{\"a\": 1}")];

            var (root, diagnostics) = Read(bytes);

            diagnostics.ShouldBeEmpty();
            root.ShouldNotBeNull().Members[0].NameLocation.ShouldBe(new SourceLocation(File, 1, 2));
        }

        [Theory]
        [InlineData("{\"a\": 1 // a comment\n}")]
        [InlineData("{\"a\": 1 /* a comment */}")]
        [InlineData("{\"a\": 1,}")]
        [InlineData("[1, 2,]")]
        [InlineData("{'a': 1}")]
        [InlineData("{a: 1}")]
        [InlineData("{} {}")]
        [InlineData("{\"a\": [1, 2")]
        [InlineData("{\"a\": \"unclosed}")]
        [InlineData("{\"a\": \"line\nbreak\"}")]
        [InlineData("{\"a\": 01}")]
        [InlineData("{\"a\": NaN}")]
        [InlineData("nope")]
        public void Anything_that_is_not_strict_json_is_one_syntax_error(string text)
        {
            var diagnostic = ShouldBeOneSyntaxError(Read(text));

            diagnostic.Reason.ShouldContain("strict JSON");
        }

        [Fact]
        public void Nesting_may_be_sixteen_deep_and_no_deeper()
        {
            Read(new string('[', 16) + new string(']', 16)).Root.ShouldNotBeNull();

            ShouldBeOneSyntaxError(Read(new string('[', 17) + new string(']', 17)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\n\n")]
        [InlineData("\r\n\t ")]
        public void An_empty_file_is_a_syntax_error_that_says_so(string text)
        {
            var diagnostic = ShouldBeOneSyntaxError(Read(text));

            diagnostic.Reason.ShouldContain("empty");
            diagnostic.Fix.ShouldContain("{");
        }

        [Fact]
        public void Merge_conflict_markers_are_named_and_the_callers_fix_is_given()
        {
            var diagnostic = ShouldBeOneSyntaxError(Read("""
                {
                <<<<<<< HEAD
                  "a": 1
                =======
                  "a": 2
                >>>>>>> other
                }
                """));

            diagnostic.Reason.ShouldContain("merge conflict");
            diagnostic.Fix.ShouldBe(ConflictFix);
            diagnostic.Location.ShouldNotBeNull().Line.ShouldBe(2);
        }

        [Fact]
        public void Bytes_that_are_not_utf8_inside_a_string_are_a_syntax_error()
        {
            byte[] inAValue = [(byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xFF, (byte)'"', (byte)'}'];
            byte[] inAKey = [(byte)'{', (byte)'"', 0xFF, (byte)'"', (byte)':', (byte)'1', (byte)'}'];

            ShouldBeOneSyntaxError(Read(inAValue)).Reason.ShouldContain("Unicode");
            ShouldBeOneSyntaxError(Read(inAKey)).Reason.ShouldContain("Unicode");
        }

        [Fact]
        public void Half_of_a_surrogate_pair_in_an_escape_is_a_syntax_error()
        {
            var marked = Marked.From("""{"a": §"\ud83c"}""");

            var diagnostic = ShouldBeOneSyntaxError(Read(marked.Text));

            diagnostic.Reason.ShouldContain("Unicode");
            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
        }

        [Fact]
        public void A_repeated_key_is_reported_and_the_first_value_is_kept()
        {
            var marked = Marked.From("""{"a": 1, §"a": 2, "b": 3}""");

            var (root, diagnostics) = Read(marked.Text);

            var diagnostic = diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.JsonDuplicateKey);
            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
            diagnostic.Message.ShouldContain("\"a\"");
            root.ShouldNotBeNull().Members.Select(member => member.Name).ShouldBe(["a", "b"]);
            root.Members[0].Value.Text.ShouldBe("1");
        }

        [Fact]
        public void A_syntax_error_is_the_only_diagnostic_even_when_others_came_first()
        {
            ShouldBeOneSyntaxError(Read("""{"a": 1, "a": 2, oops}"""));
        }

        [Fact]
        public void The_root_may_be_any_value_and_the_caller_decides_what_it_must_be()
        {
            var (root, diagnostics) = Read("\"just text\"");

            diagnostics.ShouldBeEmpty();
            root.ShouldNotBeNull().Kind.ShouldBe(JsonKind.String);
        }
    }
}
