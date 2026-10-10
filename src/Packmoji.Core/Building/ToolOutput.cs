using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// What a tool printed, made ready to be shown. A compiler repeats the code it objects to, and
    /// the code may be a package's that someone else wrote: so each line is made fit to print, as the
    /// parts of a diagnostic are, before it reaches a terminal.
    /// </summary>
    public static class ToolOutput
    {
        /// <summary>The lines of what a tool printed, without the empty ones, each fit to print.</summary>
        public static IReadOnlyList<string> Lines(string printed)
        {
            ArgumentNullException.ThrowIfNull(printed);
            return printed
                .Split('\n', '\r')
                .Select(line => line.TrimEnd())
                .Where(line => line.Length > 0)
                .Select(Printable.Text)
                .ToList();
        }
    }
}
