using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Versioning;

namespace Packmoji.Core.Identity
{
    /// <summary>
    /// The git tag of one version of one package: the name, <c>-v</c>, and the version. The name is in
    /// the tag because a repository may hold several packages and has one set of tags for all of them.
    /// A name holds no hyphen, so the first hyphen in a tag always ends the name.
    /// </summary>
    public static class ReleaseTag
    {
        public static string For(PackageName package, SemanticVersion version)
        {
            ArgumentNullException.ThrowIfNull(package);
            ArgumentNullException.ThrowIfNull(version);
            return $"{package.Name}-v{version}";
        }

        public static bool TryParse(string tag, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out SemanticVersion? version)
        {
            ArgumentNullException.ThrowIfNull(tag);
            name = null;
            version = null;

            var dash = tag.IndexOf('-');
            if (dash <= 0 || dash + 1 >= tag.Length || tag[dash + 1] != 'v' || !PackageName.IsValidBareName(tag.AsSpan(0, dash)))
            {
                return false;
            }

            if (!SemanticVersion.TryParse(tag[(dash + 2)..], out version, out _))
            {
                return false;
            }

            name = tag[..dash];
            return true;
        }
    }
}
