using System.Security.Cryptography;
using System.Text;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        /// <summary>A requirement on a package, written as a lockfile's root writes one: <c>@owner/name@1.2</c>.</summary>
        public static Dependency Asks(string text)
        {
            var at = text.LastIndexOf('@');
            return new Dependency(Name(text[..at]), Requirement(text[(at + 1)..]));
        }

        /// <summary>The digest of a text, so that everything a test publishes has a digest of its own.</summary>
        public static Sha256Digest DigestOf(string text) => ShaOf(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

        /// <summary>
        /// A version as a source would give it: active, in a repository of the package's own name, and
        /// with a digest of its own. Each dependency is written as <c>@owner/name@1.2</c>.
        /// </summary>
        public static PublishedVersion Published(string name, string version, params string[] dependencies) => new(
            Name(name),
            Version(version),
            VersionStatus.Active,
            RepositoryRef.DefaultFor(Name(name)),
            DigestOf($"{name}@{version}"),
            VerificationLevel.Checksum,
            dependencies.Select(Asks).ToList());
    }
}
