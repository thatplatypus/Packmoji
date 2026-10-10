using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Identity
{
    /// <summary>
    /// The scopes a pmj may depend on, as whoever runs it has said. It is for a machine that runs
    /// other people's projects: a website that lets a visitor write a manifest, and the service that
    /// compiles what the manifest asks for. A scope is the GitHub owner its packages are released
    /// by, so the limit is also on which owners are ever asked for anything.
    /// </summary>
    public sealed class ScopeLimit
    {
        /// <summary>The variable of the environment that holds the list.</summary>
        public const string Variable = "PACKMOJI_SCOPES";

        private ScopeLimit(IReadOnlyList<string> scopes, bool isSet)
        {
            Scopes = scopes;
            IsSet = isSet;
        }

        /// <summary>No limit: every scope is allowed.</summary>
        public static ScopeLimit None { get; } = new([], isSet: false);

        /// <summary>Whether there is a limit at all.</summary>
        public bool IsSet { get; }

        /// <summary>The scopes that are allowed, in order. Empty when there is no limit.</summary>
        public IReadOnlyList<string> Scopes { get; }

        public bool Allows(PackageName name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return AllowsOwner(name.Scope);
        }

        /// <summary>Whether a GitHub owner may be asked for anything. A package's scope is its owner.</summary>
        public bool AllowsOwner(string owner) => !IsSet || Scopes.Contains(owner, StringComparer.Ordinal);

        /// <summary>
        /// Reads the list: scopes with commas between them, each as a package's name has it, without
        /// the <c>@</c>. A variable that is not set is no limit. Anything that is not a list is
        /// refused, with why: a limit that was mistyped must never be taken for no limit.
        /// </summary>
        /// <remarks>
        /// A variable that is set to nothing is refused too, though every other variable of pmj's
        /// that is set to nothing says nothing. On a server this one is filled in from a setting,
        /// and a setting that is missing fills it with nothing: that must stop pmj, and not free it.
        /// </remarks>
        /// <param name="text">The variable's value, or null when it is not set.</param>
        public static bool TryParse(string? text, [NotNullWhen(true)] out ScopeLimit? limit, [NotNullWhen(false)] out string? reason)
        {
            (limit, reason) = (null, null);
            if (text is null)
            {
                limit = None;
                return true;
            }

            if (text.AsSpan().Trim().IsEmpty)
            {
                reason = "it names no scope";
                return false;
            }

            var scopes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var part in text.Split(','))
            {
                var scope = part.Trim();
                if (scope.Length == 0)
                {
                    reason = "it has a comma with no scope beside it";
                    return false;
                }

                if (!PackageName.IsValidScope(scope))
                {
                    reason = scope[0] == '@'
                        ? $"\"{scope}\" is not a scope: a scope is written without the @"
                        : $"\"{scope}\" is not a scope: a scope is a GitHub owner's name in lowercase, of letters, digits and single hyphens";
                    return false;
                }

                scopes.Add(scope);
            }

            limit = new ScopeLimit([.. scopes], isSet: true);
            return true;
        }

        /// <summary>The problem for a package that the limit does not allow.</summary>
        /// <param name="met">Where the package was met, as the beginning of a sentence: who asks for it, or what holds it.</param>
        public Diagnostic Refuses(PackageName name, string met)
        {
            ArgumentNullException.ThrowIfNull(name);

            // Written for someone who may never have seen the variable: a visitor to a website reads this.
            return new Diagnostic(
                DiagnosticCodes.ScopeNotAllowed,
                $"\"{name}\" is outside the scopes pmj is limited to here.",
                $"{met}, and {Variable} allows only {this}",
                Fix);
        }

        private string Fix => $"depend only on packages of {(Scopes.Count == 1 ? "that scope" : "those scopes")}; the list is set by whoever runs pmj here";

        /// <summary>The problem for a repository whose owner the limit does not allow, said where GitHub would have been asked.</summary>
        public Diagnostic RefusesOwner(RepositoryRef repository)
        {
            ArgumentNullException.ThrowIfNull(repository);
            return new Diagnostic(
                DiagnosticCodes.ScopeNotAllowed,
                $"{repository} belongs to an owner outside the scopes pmj is limited to here.",
                $"{Variable} allows only {this}, and nothing is asked of any other owner",
                Fix);
        }

        /// <summary>The scopes as a person would say them: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
        public override string ToString() => Scopes.Count switch
        {
            0 => "",
            1 => Scopes[0],
            _ => string.Join(", ", Scopes.Take(Scopes.Count - 1)) + " and " + Scopes[^1],
        };
    }
}
