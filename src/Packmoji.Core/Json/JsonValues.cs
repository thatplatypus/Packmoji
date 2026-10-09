using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    internal static class JsonValues
    {
        /// <summary>
        /// Makes a value type from the text of a string, and when that fails reports why at the place
        /// the string stands. A value type knows what is wrong with a text; only the file knows where.
        /// </summary>
        public static T? Parse<T>(this JsonItem? item, TryParser<T> parser, List<Diagnostic> diagnostics) where T : class
        {
            if (item is null)
            {
                return null;
            }

            if (parser(item.Text, out var value, out var error))
            {
                return value;
            }

            diagnostics.Add(error! with { Location = item.Location });
            return null;
        }

        /// <summary>
        /// The strings of an array, each with its place. An entry that is not a string, or that repeats
        /// an earlier one, is reported and left out. Null when there is no array.
        /// </summary>
        public static List<Located<string>>? Strings(this JsonItem? array, string key, List<Diagnostic> diagnostics)
        {
            if (array is null)
            {
                return null;
            }

            var strings = new List<Located<string>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in array.Items)
            {
                if (item.Kind != JsonKind.String)
                {
                    diagnostics.Add(JsonDiagnostics.WrongType($"An entry of \"{key}\"", JsonKind.String, item, removable: false));
                }
                else if (!seen.Add(item.Text))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.ListDuplicate,
                        $"\"{item.Text}\" is in \"{key}\" twice.",
                        "an entry may appear once",
                        "remove one of the two",
                        item.Location));
                }
                else
                {
                    strings.Add(new Located<string>(item.Text, item.Location));
                }
            }

            return strings;
        }
    }
}
