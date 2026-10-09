using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>
    /// One JSON value and where it stood in its file. System.Text.Json's own trees forget positions,
    /// and a diagnostic that cannot say where is of little use, so Packmoji keeps this one.
    /// </summary>
    internal sealed class JsonItem(JsonKind kind, SourceLocation location)
    {
        public JsonKind Kind { get; } = kind;

        public SourceLocation Location { get; } = location;

        /// <summary>The decoded text of a string, or a number's characters exactly as they were written.</summary>
        public string Text { get; init; } = "";

        public bool IsTrue { get; init; }

        /// <summary>The members of an object, in the order of the file. A key that was repeated is here once.</summary>
        public List<JsonMember> Members { get; } = [];

        public List<JsonItem> Items { get; } = [];
    }
}
