using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Identity
{
    /// <summary>
    /// A package's full name, <c>@scope/name</c>. The scope is the GitHub owner who may publish it, and
    /// the name is what Emojicode code imports.
    /// </summary>
    public sealed record PackageName : IComparable<PackageName>
    {
        public const int MaxScopeLength = 39;
        public const int MaxNameLength = 64;

        private PackageName(string scope, string name)
        {
            Scope = scope;
            Name = name;
        }

        public string Scope { get; }

        public string Name { get; }

        /// <summary>
        /// The name in <c>📦 name 🏠</c>, in <c>-p name</c> and in <c>libname.a</c>. It is the bare name
        /// unchanged, which is why a name must also be a C++ identifier: the Emojicode runtime's
        /// <c>SET_INFO_FOR</c> macro pastes it into one.
        /// </summary>
        public string ImportName => Name;

        public override string ToString() => $"@{Scope}/{Name}";

        public int CompareTo(PackageName? other) => string.CompareOrdinal(ToString(), other?.ToString());

        public static bool TryParse(string text, [NotNullWhen(true)] out PackageName? name, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            name = null;
            error = null;

            var slash = text.IndexOf('/');
            if (!text.StartsWith('@') || slash < 0 || text.IndexOf('/', slash + 1) >= 0)
            {
                error = Invalid(text, "a package name is written @scope/name, with one @ and one /", "write it as \"@<github owner>/<name>\"");
                return false;
            }

            if (text.Any(Ascii.IsUpperLetter) && TryParse(text.ToLowerInvariant(), out var lowered, out _))
            {
                error = Invalid(text, "scopes and names are lowercase", $"write \"{lowered}\"");
                return false;
            }

            var scope = text[1..slash];
            var bare = text[(slash + 1)..];

            if (!IsValidScope(scope))
            {
                error = Invalid(
                    text,
                    "a scope is a GitHub owner in lowercase: letters and digits with single hyphens inside, 1 to 39 characters",
                    "use the lowercase name of the GitHub user or organization that owns the package");
                return false;
            }

            if (!IsValidBareName(bare))
            {
                var underscored = bare.Replace('-', '_');
                error = bare.Contains('-') && IsValidBareName(underscored)
                    ? Invalid(
                        text,
                        "a name cannot hold a hyphen, because it is also the import name and a C++ identifier in native code",
                        $"write \"@{scope}/{underscored}\"")
                    : Invalid(
                        text,
                        "a name is lowercase letters, digits and single underscores, starts with a letter, and is 1 to 64 characters",
                        "choose a name such as \"crypto\" or \"emoji_crypto\"");
                return false;
            }

            if (ReservedNames.Contains(bare))
            {
                error = new Diagnostic(
                    DiagnosticCodes.NameReserved,
                    $"\"{text}\" uses a reserved name.",
                    $"\"{bare}\" is a stock Emojicode package, and a package of the same name would replace it in every build",
                    "choose another name");
                return false;
            }

            name = new PackageName(scope, bare);
            return true;
        }

        /// <summary>The grammar of a scope, which is GitHub's for an owner, in lowercase.</summary>
        public static bool IsValidScope(ReadOnlySpan<char> text)
        {
            if (text.Length is 0 or > MaxScopeLength || text[0] == '-' || text[^1] == '-')
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '-')
                {
                    if (text[i - 1] == '-')
                    {
                        return false;
                    }
                }
                else if (!Ascii.IsLowerLetter(c) && !Ascii.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The grammar of a bare name. Whether the name is reserved is a separate question.</summary>
        public static bool IsValidBareName(ReadOnlySpan<char> text)
        {
            if (text.Length is 0 or > MaxNameLength || !Ascii.IsLowerLetter(text[0]) || text[^1] == '_')
            {
                return false;
            }

            for (var i = 1; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '_')
                {
                    if (text[i - 1] == '_')
                    {
                        return false;
                    }
                }
                else if (!Ascii.IsLowerLetter(c) && !Ascii.IsDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static Diagnostic Invalid(string text, string reason, string fix) =>
            new(DiagnosticCodes.NameInvalid, $"\"{text}\" is not a valid package name.", reason, fix);
    }
}
