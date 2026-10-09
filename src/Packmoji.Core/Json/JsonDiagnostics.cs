using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>The diagnostics that are about the JSON itself and not about what a file means.</summary>
    internal static class JsonDiagnostics
    {
        /// <param name="what">The thing that has the wrong kind, as the sentence should begin: <c>"name"</c>, or <c>An entry of "link"</c>.</param>
        /// <param name="removable">Whether leaving the key out is a way to fix it, which is so for an optional key.</param>
        public static Diagnostic WrongType(string what, JsonKind expected, JsonItem found, bool removable) =>
            new(
                DiagnosticCodes.JsonWrongType,
                $"{what} must be {Describe(expected)}.",
                $"it is {Describe(found.Kind)} here",
                removable && found.Kind == JsonKind.Null
                    ? $"remove it, or give it {Describe(expected)}"
                    : $"change it to {Describe(expected)}",
                found.Location);

        public static Diagnostic RootNotObject(JsonItem root) =>
            new(
                DiagnosticCodes.JsonWrongType,
                "The file must hold a JSON object.",
                $"it holds {Describe(root.Kind)}",
                "begin the file with { and end it with }",
                root.Location);

        public static string Describe(JsonKind kind) => kind switch
        {
            JsonKind.Object => "an object",
            JsonKind.Array => "an array",
            JsonKind.String => "a string",
            JsonKind.Number => "a number",
            JsonKind.Boolean => "true or false",
            _ => "null",
        };
    }
}
