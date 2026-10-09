using System.Text;
using System.Text.Json;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>
    /// Reads strict JSON into a tree that remembers where everything was. Strict means RFC 8259 and no
    /// more: no comments and no trailing commas, so that any JSON tool can read what Packmoji reads.
    /// </summary>
    internal static class JsonTreeReader
    {
        public const int MaxDepth = 16;

        private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

        private static readonly JsonReaderOptions Options = new()
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = MaxDepth,
        };

        /// <summary>
        /// Gives the root value, or null when the text is not JSON at all. In that case the syntax error
        /// is the only thing in <paramref name="diagnostics"/>: nothing seen before it can be trusted.
        /// </summary>
        /// <param name="conflictFix">
        /// What to do about git conflict markers, which depends on the file: a person resolves a
        /// manifest by hand, and a lockfile is written again.
        /// </param>
        public static JsonItem? Read(ReadOnlyMemory<byte> utf8, string file, string conflictFix, List<Diagnostic> diagnostics)
        {
            if (utf8.Span.StartsWith(ByteOrderMark))
            {
                utf8 = utf8[ByteOrderMark.Length..];
            }

            var source = new SourceText(utf8, file);
            var reader = new Utf8JsonReader(utf8.Span, Options);
            try
            {
                reader.Read();
                var root = ReadValue(ref reader, source, diagnostics);
                reader.Read();
                return root;
            }
            catch (JsonException exception)
            {
                diagnostics.Clear();
                diagnostics.Add(Syntax(source, source.Locate(exception.LineNumber ?? 0, exception.BytePositionInLine ?? 0), conflictFix, notUnicode: false));
                return null;
            }
            catch (NotUnicodeException)
            {
                diagnostics.Clear();
                diagnostics.Add(Syntax(source, source.Locate(reader.TokenStartIndex), conflictFix, notUnicode: true));
                return null;
            }
        }

        private static JsonItem ReadValue(ref Utf8JsonReader reader, SourceText source, List<Diagnostic> diagnostics)
        {
            var location = source.Locate(reader.TokenStartIndex);
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                {
                    var item = new JsonItem(JsonKind.Object, location);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var nameLocation = source.Locate(reader.TokenStartIndex);
                        var name = Text(ref reader);
                        reader.Read();
                        var value = ReadValue(ref reader, source, diagnostics);
                        if (seen.Add(name))
                        {
                            item.Members.Add(new JsonMember(name, nameLocation, value));
                        }
                        else
                        {
                            diagnostics.Add(new Diagnostic(
                                DiagnosticCodes.JsonDuplicateKey,
                                $"The key \"{name}\" appears twice.",
                                "a key may appear once in an object",
                                $"remove one of the two \"{name}\" entries",
                                nameLocation));
                        }
                    }

                    return item;
                }

                case JsonTokenType.StartArray:
                {
                    var item = new JsonItem(JsonKind.Array, location);
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        item.Items.Add(ReadValue(ref reader, source, diagnostics));
                    }

                    return item;
                }

                case JsonTokenType.String:
                    return new JsonItem(JsonKind.String, location) { Text = Text(ref reader) };

                case JsonTokenType.Number:
                    return new JsonItem(JsonKind.Number, location) { Text = Encoding.ASCII.GetString(reader.ValueSpan) };

                case JsonTokenType.True:
                case JsonTokenType.False:
                    return new JsonItem(JsonKind.Boolean, location) { IsTrue = reader.TokenType == JsonTokenType.True };

                default:
                    return new JsonItem(JsonKind.Null, location);
            }
        }

        private static string Text(ref Utf8JsonReader reader)
        {
            try
            {
                return reader.GetString() ?? "";
            }
            catch (InvalidOperationException)
            {
                // The reader checks the grammar as it goes, but it finds out that a string is not valid
                // Unicode only when the string is asked for.
                throw new NotUnicodeException();
            }
        }

        private static Diagnostic Syntax(SourceText source, SourceLocation location, string conflictFix, bool notUnicode)
        {
            const string Message = "The file is not valid JSON.";

            if (source.Bytes.Trim(" \t\r\n"u8).IsEmpty)
            {
                return new Diagnostic(DiagnosticCodes.JsonSyntax, Message, "the file is empty", "write a JSON object, beginning with { and ending with }", location);
            }

            if (source.HasLineStartingWith("<<<<<<<"u8))
            {
                return new Diagnostic(DiagnosticCodes.JsonSyntax, Message, "the file holds git merge conflict markers", conflictFix, location);
            }

            return new Diagnostic(
                DiagnosticCodes.JsonSyntax,
                Message,
                notUnicode
                    ? "a string holds text that is not valid Unicode"
                    : "strict JSON is required: no comments, no trailing commas, every string closed, and nothing after the final brace",
                "correct the JSON at this position",
                location);
        }

        private sealed class NotUnicodeException : Exception;
    }
}
