namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// What a path in a manifest may be. A path stays inside the package and is spelled one way on
    /// every platform, because what it selects is packed into an archive that other machines unpack,
    /// and it is handed to a compiler as an argument, where it must not be mistaken for an option. A
    /// published manifest can never be made stricter, so these rules are strict from the start.
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

                if (c is '[' or ']' or '{' or '}')
                {
                    return glob ? "character classes and braces are not supported in a pattern" : "it holds a character that only a pattern could use";
                }

                if (!glob && c is '*' or '?')
                {
                    return "it holds a wildcard, and this must name one file or directory";
                }
            }

            if (TextSafety.HasUnsafe(text))
            {
                return "it holds a control character, or a character that cannot be seen or that reorders text";
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

                if (part[0] is '-' or '@')
                {
                    return "a part of it begins with - or @, which a compiler would take for an option or for a file of options";
                }

                if (part[0] == ' ' || part[^1] == ' ')
                {
                    return "a part of it begins or ends with a space";
                }

                // Windows drops a dot or a space from the end of a name, so ".. " would be ".." there.
                if (part[^1] == '.')
                {
                    return "a part of it ends with a dot, which some platforms drop";
                }
            }

            return null;
        }
    }
}
