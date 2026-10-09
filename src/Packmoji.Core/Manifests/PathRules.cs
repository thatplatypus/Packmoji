namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// What a path in a manifest may be. A path stays inside the package and is spelled one way on
    /// every platform, because what it selects is packed into an archive that other machines unpack.
    /// </summary>
    internal static class PathRules
    {
        public const int MaxLength = 255;

        /// <summary>
        /// Says why a text is not a path, or not a glob pattern when <paramref name="glob"/> is set.
        /// Null when it is one.
        /// </summary>
        public static string? Problem(string text, bool glob)
        {
            if (text.Length == 0)
            {
                return "it is empty";
            }

            if (text.Length > MaxLength)
            {
                return $"it is longer than {MaxLength} characters";
            }

            foreach (var c in text)
            {
                if (c == '\\')
                {
                    return "it uses a backslash, and a path is written with /";
                }

                if (c == ':')
                {
                    return "it holds a colon";
                }

                if (char.IsControl(c))
                {
                    return "it holds a control character";
                }

                if (c is '[' or ']' or '{' or '}')
                {
                    return glob ? "character classes and braces are not supported in a pattern" : "it holds a character that only a pattern could use";
                }

                if (!glob && c is '*' or '?')
                {
                    return "it holds a wildcard, and this must name one file or directory";
                }
            }

            if (glob && text[0] == '!')
            {
                return "a pattern cannot begin with !, since there are no exclusions";
            }

            foreach (var part in text.Split('/'))
            {
                if (part.Length == 0)
                {
                    return "it has an empty part, from a leading, trailing or doubled /";
                }

                if (part is "." or "..")
                {
                    return "it has a . or .. part, and a path stays inside the package";
                }

                if (part.Contains("**", StringComparison.Ordinal) && part != "**")
                {
                    return "** must be a whole part of the pattern, as in src/**/x";
                }
            }

            return null;
        }
    }
}
