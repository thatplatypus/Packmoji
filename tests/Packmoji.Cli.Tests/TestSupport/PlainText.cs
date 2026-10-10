using System.Text.RegularExpressions;

namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>
    /// What pmj said, with every path of this machine written as a test writes one: with <c>/</c>
    /// between its parts. A test then says what it expects once, for every machine.
    /// </summary>
    internal static class PlainText
    {
        /// <summary>A text with the separator of this machine's paths written as <c>/</c>.</summary>
        public static string Slashed(string text) => Slashed(text, Path.DirectorySeparatorChar);

        /// <summary>
        /// A text with a separator written as <c>/</c>. The text may be a JSON answer, in which a
        /// backslash is written as two. A backslash before a quote is JSON's own way of writing the
        /// quote, and no part of a path: no name on Windows holds one.
        /// </summary>
        /// <param name="separator">What stands between the parts of a path on the machine that wrote the text.</param>
        public static string Slashed(string text, char separator) =>
            separator == '/' ? text : Regex.Replace(text.Replace(@"\\", "/"), @"\\(?!"")", "/");
    }
}
