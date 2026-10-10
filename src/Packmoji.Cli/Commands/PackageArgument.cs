using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Versioning;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// A package as <c>pmj add</c> is given it: <c>@scope/name</c>, and after a second <c>@</c> the
    /// requirement on it, when there is one.
    /// </summary>
    internal static class PackageArgument
    {
        /// <param name="requirement">Null when none was given.</param>
        public static bool TryParse(string text, [NotNullWhen(true)] out PackageName? name, out VersionRequirement? requirement, [NotNullWhen(false)] out Diagnostic? error)
        {
            requirement = null;
            var at = text.LastIndexOf('@');
            return PackageName.TryParse(at > 0 ? text[..at] : text, out name, out error)
                && (at <= 0 || VersionRequirement.TryParse(text[(at + 1)..], out requirement, out error));
        }
    }
}
