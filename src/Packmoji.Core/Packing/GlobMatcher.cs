using System.Text;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Packing
{
    /// <summary>
    /// Says whether a pattern selects a path. <c>*</c> is any run of characters within one part of a
    /// path, <c>?</c> is one character, and <c>**</c> as a whole part is any number of parts. A name
    /// that begins with a dot is selected only by a pattern that writes the dot, as in a shell, so
    /// that a pattern never sweeps up what an editor or an operating system left behind.
    /// </summary>
    /// <remarks>
    /// Matching is by code point and takes no recursion: a pattern comes from a manifest that someone
    /// else wrote, and the simple way of matching stars takes for ever on a pattern built to make it.
    /// </remarks>
    public static class GlobMatcher
    {
        private const string AnyParts = "**";

        /// <param name="path">A path inside the package, with <c>/</c> between its parts.</param>
        public static bool IsMatch(GlobPattern pattern, string path)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            ArgumentNullException.ThrowIfNull(path);

            var wanted = pattern.Value.Split('/').ToList();
            if (wanted[^1] == AnyParts)
            {
                // At the end of a pattern, ** is everything inside a directory and not the directory.
                wanted.Add("*");
            }

            var parts = path.Split('/');
            var at = 0;
            var part = 0;
            var star = -1;
            var starredFrom = 0;
            while (part < parts.Length)
            {
                if (at < wanted.Count && wanted[at] == AnyParts)
                {
                    star = at++;
                    starredFrom = part;
                }
                else if (at < wanted.Count && MatchesPart(wanted[at], parts[part]))
                {
                    at++;
                    part++;
                }
                else if (star >= 0 && !IsHidden(parts[starredFrom]))
                {
                    // Let ** take one part more, and try what follows it again from there.
                    at = star + 1;
                    part = ++starredFrom;
                }
                else
                {
                    return false;
                }
            }

            while (at < wanted.Count && wanted[at] == AnyParts)
            {
                at++;
            }

            return at == wanted.Count;
        }

        private static bool IsHidden(string part) => part.StartsWith('.');

        private static bool MatchesPart(string wanted, string part)
        {
            if (IsHidden(part) && !IsHidden(wanted))
            {
                return false;
            }

            var pattern = wanted.EnumerateRunes().ToArray();
            var text = part.EnumerateRunes().ToArray();
            var any = new Rune('*');
            var one = new Rune('?');
            var at = 0;
            var read = 0;
            var star = -1;
            var starredFrom = 0;
            while (read < text.Length)
            {
                if (at < pattern.Length && pattern[at] == any)
                {
                    star = at++;
                    starredFrom = read;
                }
                else if (at < pattern.Length && (pattern[at] == one || pattern[at] == text[read]))
                {
                    at++;
                    read++;
                }
                else if (star >= 0)
                {
                    at = star + 1;
                    read = ++starredFrom;
                }
                else
                {
                    return false;
                }
            }

            while (at < pattern.Length && pattern[at] == any)
            {
                at++;
            }

            return at == pattern.Length;
        }
    }
}
