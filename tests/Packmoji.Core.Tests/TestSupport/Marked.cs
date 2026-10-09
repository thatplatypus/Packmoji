using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// A fixture with a § just before the place a diagnostic is expected. The mark is taken out before
    /// the text is read, so the expected line and column never have to be counted by hand.
    /// </summary>
    internal sealed record Marked(string Text, int Line, int Column)
    {
        public const char Mark = '§';

        public static Marked From(string textWithMark)
        {
            var text = textWithMark.ReplaceLineEndings("\n");
            var index = text.IndexOf(Mark);
            index.ShouldBeGreaterThanOrEqualTo(0, "the fixture has no § mark");

            var before = text[..index];
            var lineStart = before.LastIndexOf('\n') + 1;
            return new Marked(
                text.Remove(index, 1),
                before.Count(c => c == '\n') + 1,
                before[lineStart..].EnumerateRunes().Count() + 1);
        }
    }
}
