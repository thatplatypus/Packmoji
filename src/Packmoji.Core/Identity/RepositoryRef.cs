using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Identity
{
    /// <summary>
    /// Where a package's releases live: a GitHub repository, written <c>github.com/owner/repo</c>. The
    /// owner has to be the package's scope, and that is the whole of Packmoji's ownership check: only
    /// someone who can publish a release in an owner's repository can publish under that owner's scope.
    /// </summary>
    public sealed record RepositoryRef
    {
        public const string Host = "github.com";
        public const int MaxNameLength = 100;

        private RepositoryRef(string owner, string name)
        {
            Owner = owner;
            Name = name;
        }

        public string Owner { get; }

        public string Name { get; }

        public override string ToString() => $"{Host}/{Owner}/{Name}";

        /// <summary>
        /// Where a package lives when its manifest does not say: a repository of its own name under its
        /// scope. Every valid package name is a valid repository name, so this cannot fail.
        /// </summary>
        public static RepositoryRef DefaultFor(PackageName package)
        {
            ArgumentNullException.ThrowIfNull(package);
            return new RepositoryRef(package.Scope, package.Name);
        }

        public bool BelongsTo(PackageName package)
        {
            ArgumentNullException.ThrowIfNull(package);
            return string.Equals(Owner, package.Scope, StringComparison.Ordinal);
        }

        public static bool TryParse(string text, [NotNullWhen(true)] out RepositoryRef? repository, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            repository = null;
            error = null;

            if (TrySplit(text, out var owner, out var name))
            {
                repository = new RepositoryRef(owner, name);
                return true;
            }

            error = new Diagnostic(
                DiagnosticCodes.RepositoryInvalid,
                $"\"{text}\" is not a valid repository.",
                "a repository is written github.com/owner/repo in lowercase, with no scheme and nothing after the repository's name",
                FixFor(text));
            return false;
        }

        private static bool TrySplit(string text, out string owner, out string name)
        {
            owner = "";
            name = "";
            var prefix = Host + "/";
            if (!text.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var rest = text[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0 || rest.IndexOf('/', slash + 1) >= 0)
            {
                return false;
            }

            owner = rest[..slash];
            name = rest[(slash + 1)..];
            return PackageName.IsValidScope(owner) && IsValidName(name);
        }

        private static bool IsValidName(string name) =>
            name.Length is > 0 and <= MaxNameLength
            && name is not ("." or "..")
            && !name.EndsWith(".git", StringComparison.Ordinal)
            && name.All(c => Ascii.IsLowerLetter(c) || Ascii.IsDigit(c) || c is '.' or '_' or '-');

        private static string FixFor(string text)
        {
            var candidate = text.ToLowerInvariant();
            foreach (var prefix in new[] { "https://", "http://", "www." })
            {
                if (candidate.StartsWith(prefix, StringComparison.Ordinal))
                {
                    candidate = candidate[prefix.Length..];
                }
            }

            candidate = candidate.TrimEnd('/');
            if (candidate.EndsWith(".git", StringComparison.Ordinal))
            {
                candidate = candidate[..^4];
            }

            return candidate != text && TrySplit(candidate, out _, out _)
                ? $"write \"{candidate}\""
                : "write it as \"github.com/<owner>/<repo>\"";
        }
    }
}
