using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Tests.TestSupport;
using Shouldly;
using Xunit;

namespace Packmoji.Core.Tests.Json
{
    public sealed class JsonObjectReaderTests
    {
        private const string File = "test.json";

        private static (JsonObjectReader Reader, JsonItem Root, List<Diagnostic> Diagnostics) Open(string text)
        {
            var diagnostics = new List<Diagnostic>();
            var root = JsonTreeReader.Read(Encoding.UTF8.GetBytes(text), File, "resolve the conflict", diagnostics).ShouldNotBeNull();
            return (new JsonObjectReader(root, diagnostics), root, diagnostics);
        }

        [Fact]
        public void A_key_of_the_kind_asked_for_is_given()
        {
            var (reader, _, diagnostics) = Open("""{"name": "x", "flag": true}""");

            reader.Required("name", JsonKind.String, "\"name\": \"x\"").ShouldNotBeNull().Text.ShouldBe("x");
            reader.Optional("flag", JsonKind.Boolean).ShouldNotBeNull().IsTrue.ShouldBeTrue();
            reader.Optional("absent", JsonKind.String).ShouldBeNull();
            reader.Finish();

            diagnostics.ShouldBeEmpty();
        }

        [Fact]
        public void A_missing_required_key_is_reported_at_the_object_with_how_to_write_it()
        {
            var marked = Marked.From("""{"inner": §{"other": 1}}""");
            var (outer, _, diagnostics) = Open(marked.Text);
            var inner = new JsonObjectReader(outer.Required("inner", JsonKind.Object, "\"inner\": {}").ShouldNotBeNull(), diagnostics);

            inner.Required("name", JsonKind.String, "\"name\": \"@owner/name\"").ShouldBeNull();

            var diagnostic = diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.KeyMissing);
            diagnostic.Message.ShouldContain("\"name\"");
            diagnostic.Fix.ShouldContain("\"name\": \"@owner/name\"");
            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
        }

        [Fact]
        public void A_value_of_another_kind_is_reported_where_it_stands_and_names_both_kinds()
        {
            var marked = Marked.From("""{"version": §1}""");
            var (reader, _, diagnostics) = Open(marked.Text);

            reader.Required("version", JsonKind.String, "\"version\": \"0.1.0\"").ShouldBeNull();

            var diagnostic = diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.JsonWrongType);
            diagnostic.Message.ShouldBe("\"version\" must be a string.");
            diagnostic.Reason.ShouldContain("a number");
            diagnostic.Fix.ShouldContain("a string");
            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
        }

        [Fact]
        public void Null_for_an_optional_key_may_be_removed_and_for_a_required_key_may_not()
        {
            var (reader, _, diagnostics) = Open("""{"description": null, "name": null}""");

            reader.Optional("description", JsonKind.String).ShouldBeNull();
            reader.Required("name", JsonKind.String, "\"name\": \"x\"").ShouldBeNull();

            diagnostics.Count.ShouldBe(2);
            diagnostics[0].ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.JsonWrongType);
            diagnostics[0].Reason.ShouldContain("null");
            diagnostics[0].Fix.ShouldStartWith("remove it");
            diagnostics[1].ShouldBeComplete().Fix.ShouldStartWith("change it");
        }

        [Fact]
        public void A_key_that_was_never_asked_for_is_unknown_and_the_allowed_keys_are_listed()
        {
            var marked = Marked.From("""{"name": "x", §"nmae": "y"}""");
            var (reader, _, diagnostics) = Open(marked.Text);

            reader.Required("name", JsonKind.String, "\"name\": \"x\"");
            reader.Optional("version", JsonKind.String);
            reader.Finish();

            var diagnostic = diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.KeyUnknown);
            diagnostic.Message.ShouldContain("\"nmae\"");
            diagnostic.Reason.ShouldContain("\"name\", \"version\"");
            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
        }

        [Fact]
        public void A_value_type_is_parsed_from_a_string_and_a_failure_is_placed_at_the_string()
        {
            var marked = Marked.From("""{"good": "@thatplatypus/crypto", "bad": §"Crypto"}""");
            var (reader, _, diagnostics) = Open(marked.Text);

            var good = reader.Optional("good", JsonKind.String).Parse<PackageName>(PackageName.TryParse, diagnostics);
            var bad = reader.Optional("bad", JsonKind.String).Parse<PackageName>(PackageName.TryParse, diagnostics);
            var absent = reader.Optional("absent", JsonKind.String).Parse<PackageName>(PackageName.TryParse, diagnostics);

            good.ShouldBe(Sample.Name("@thatplatypus/crypto"));
            bad.ShouldBeNull();
            absent.ShouldBeNull();
            var diagnostic = diagnostics.ShouldHaveSingleItem().ShouldBeComplete();
            diagnostic.Code.ShouldBe(DiagnosticCodes.NameInvalid);
            diagnostic.Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
        }

        [Fact]
        public void The_strings_of_an_array_are_given_with_their_places()
        {
            var marked = Marked.From("""{"link": ["pthread", §"m"]}""");
            var (reader, _, diagnostics) = Open(marked.Text);

            var strings = reader.Optional("link", JsonKind.Array).Strings("link", diagnostics).ShouldNotBeNull();

            diagnostics.ShouldBeEmpty();
            strings.Select(located => located.Value).ShouldBe(["pthread", "m"]);
            strings[1].Location.ShouldBe(new SourceLocation(File, marked.Line, marked.Column));
            reader.Optional("absent", JsonKind.Array).Strings("absent", diagnostics).ShouldBeNull();
        }

        [Fact]
        public void An_entry_that_is_not_a_string_or_that_repeats_is_reported_and_left_out()
        {
            var (reader, _, diagnostics) = Open("""{"link": ["m", 7, "m", "z"]}""");

            var strings = reader.Optional("link", JsonKind.Array).Strings("link", diagnostics).ShouldNotBeNull();

            strings.Select(located => located.Value).ShouldBe(["m", "z"]);
            diagnostics.Count.ShouldBe(2);
            diagnostics[0].ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.JsonWrongType);
            diagnostics[0].Message.ShouldBe("An entry of \"link\" must be a string.");
            diagnostics[1].ShouldBeComplete().Code.ShouldBe(DiagnosticCodes.ListDuplicate);
            diagnostics[1].Message.ShouldContain("\"m\"");
        }

        [Fact]
        public void A_root_that_is_not_an_object_has_a_diagnostic_of_its_own()
        {
            var diagnostics = new List<Diagnostic>();
            var root = JsonTreeReader.Read(Encoding.UTF8.GetBytes("[]"), File, "resolve the conflict", diagnostics).ShouldNotBeNull();

            var diagnostic = JsonDiagnostics.RootNotObject(root).ShouldBeComplete();

            diagnostic.Code.ShouldBe(DiagnosticCodes.JsonWrongType);
            diagnostic.Reason.ShouldContain("an array");
            diagnostic.Location.ShouldBe(new SourceLocation(File, 1, 1));
        }
    }
}
