using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// A license, as an SPDX license expression. Only the syntax is checked: the SPDX list of
    /// identifiers changes, and a published version can never be corrected, so a list built into pmj
    /// would in time refuse licenses that are real.
    /// </summary>
    public sealed record SpdxExpression
    {
        public const int MaxLength = 100;

        private SpdxExpression(string text)
        {
            Text = text;
        }

        public string Text { get; }

        public override string ToString() => Text;

        public static bool TryParse(string text, [NotNullWhen(true)] out SpdxExpression? expression, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            var tokens = Tokenize(text);
            if (text.Length is > 0 and <= MaxLength && tokens is not null && new Parser(tokens).IsExpression())
            {
                expression = new SpdxExpression(text);
                error = null;
                return true;
            }

            expression = null;
            error = new Diagnostic(
                DiagnosticCodes.LicenseInvalid,
                $"\"{text}\" is not an SPDX license expression.",
                "a license is an SPDX identifier such as MIT, or identifiers joined by AND, OR and WITH, with one space between",
                text.Split(' ').Any(word => word is "and" or "or" or "with")
                    ? "write the operators in capitals: AND, OR, WITH"
                    : "write an identifier from spdx.org/licenses such as \"MIT\", or an expression such as \"MIT OR Apache-2.0\"");
            return false;
        }

        // Null when the spacing is not one space between words, which is the only spelling accepted.
        private static List<string>? Tokenize(string text)
        {
            if (text.StartsWith(' ') || text.EndsWith(' ') || text.Contains("  ", StringComparison.Ordinal))
            {
                return null;
            }

            var tokens = new List<string>();
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var c = i < text.Length ? text[i] : ' ';
                if (c is ' ' or '(' or ')')
                {
                    if (start >= 0)
                    {
                        tokens.Add(text[start..i]);
                        start = -1;
                    }

                    if (c != ' ')
                    {
                        tokens.Add(c.ToString());
                    }
                }
                else if (start < 0)
                {
                    start = i;
                }
            }

            return tokens;
        }

        private static bool IsLicense(string token)
        {
            const string DocumentRef = "DocumentRef-";
            const string LicenseRef = "LicenseRef-";

            var colon = token.IndexOf(':');
            if (colon >= 0)
            {
                var document = token[..colon];
                var license = token[(colon + 1)..];
                return document.StartsWith(DocumentRef, StringComparison.Ordinal) && IsIdentifier(document[DocumentRef.Length..])
                    && license.StartsWith(LicenseRef, StringComparison.Ordinal) && IsIdentifier(license[LicenseRef.Length..]);
            }

            return IsIdentifier(token.EndsWith('+') ? token[..^1] : token);
        }

        private static bool IsIdentifier(string token) =>
            token.Length > 0
            && token is not ("AND" or "OR" or "WITH")
            && token.All(c => Ascii.IsLetter(c) || Ascii.IsDigit(c) || c is '.' or '-');

        // expression := and ( "OR" and )*      and := with ( "AND" with )*
        // with := "(" expression ")" | license [ "WITH" identifier ]
        private sealed class Parser(List<string> tokens)
        {
            private int _next;

            public bool IsExpression() => Or() && _next == tokens.Count;

            private bool Or()
            {
                if (!And())
                {
                    return false;
                }

                while (Peek("OR"))
                {
                    _next++;
                    if (!And())
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool And()
            {
                if (!With())
                {
                    return false;
                }

                while (Peek("AND"))
                {
                    _next++;
                    if (!With())
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool With()
            {
                if (Peek("("))
                {
                    _next++;
                    return Or() && Take(token => token == ")");
                }

                if (!Take(IsLicense))
                {
                    return false;
                }

                if (Peek("WITH"))
                {
                    _next++;
                    return Take(IsIdentifier);
                }

                return true;
            }

            private bool Peek(string token) => _next < tokens.Count && tokens[_next] == token;

            private bool Take(Func<string, bool> accepts)
            {
                if (_next < tokens.Count && accepts(tokens[_next]))
                {
                    _next++;
                    return true;
                }

                return false;
            }
        }
    }
}
