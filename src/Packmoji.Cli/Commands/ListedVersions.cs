using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Commands
{
    /// <summary>The versions of a package that something may begin to use, lowest first.</summary>
    /// <param name="Holder">Who has them, as the beginning of a sentence that goes on with how many: <c>github.com/owner/repo has released</c>, or <c>the registry holds</c>.</param>
    internal sealed record ListedVersions(IReadOnlyList<SemanticVersion> Versions, string Holder);
}
