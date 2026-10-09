using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>
    /// Takes the keys of one object by name and remembers which were asked for, so that whatever is
    /// left over can be reported as unknown. A reader built on this cannot forget to refuse a key it
    /// does not know, and the list of allowed keys in the diagnostic cannot fall out of date.
    /// </summary>
    internal sealed class JsonObjectReader(JsonItem item, List<Diagnostic> diagnostics)
    {
        private readonly List<string> _asked = [];

        public JsonItem? Optional(string key, JsonKind kind) => Take(key, kind, example: null);

        /// <param name="example">How the key would be written, for the fix when it is missing.</param>
        public JsonItem? Required(string key, JsonKind kind, string example) => Take(key, kind, example);

        /// <summary>Reports every key that was never asked for. Call it after the last key has been taken.</summary>
        public void Finish()
        {
            foreach (var member in item.Members)
            {
                if (!_asked.Contains(member.Name, StringComparer.Ordinal))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.KeyUnknown,
                        $"The key \"{member.Name}\" is not known here.",
                        $"the keys allowed here are {string.Join(", ", _asked.Select(key => $"\"{key}\""))}",
                        $"remove \"{member.Name}\", or correct its spelling",
                        member.NameLocation));
                }
            }
        }

        private JsonItem? Take(string key, JsonKind kind, string? example)
        {
            _asked.Add(key);
            var member = item.Members.Find(candidate => string.Equals(candidate.Name, key, StringComparison.Ordinal));
            if (member is null)
            {
                if (example is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.KeyMissing,
                        $"The key \"{key}\" is missing.",
                        "it is required here",
                        $"add {example}",
                        item.Location));
                }

                return null;
            }

            if (member.Value.Kind != kind)
            {
                diagnostics.Add(JsonDiagnostics.WrongType($"\"{key}\"", kind, member.Value, removable: example is null));
                return null;
            }

            return member.Value;
        }
    }
}
