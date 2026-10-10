using System.Text;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// The key a built package is kept under: one digest of everything that changes what is built.
    /// Two builds with the same key are the same build, so a package is compiled once for the whole
    /// machine, and a project is never given what was built from something else.
    /// </summary>
    public static class BuildKey
    {
        /// <summary>
        /// Raised whenever pmj builds in another way: another flag, another layout of what it keeps.
        /// Everything built before then has another key, and is built again.
        /// </summary>
        public const int Recipe = 1;

        /// <param name="compiler">The digest of the compiler's own file. Its banner will not do: a fork prints the same one.</param>
        /// <param name="archive">The digest of the package's archive, which holds its sources and its manifest.</param>
        /// <param name="dependencies">The key of each package it depends on directly. Theirs hold what they depend on.</param>
        /// <param name="native">For each language the package has native code in, the digest of what that language's compiler says of itself.</param>
        public static Sha256Digest Of(
            Sha256Digest compiler,
            bool optimized,
            Sha256Digest archive,
            IReadOnlyDictionary<PackageName, Sha256Digest> dependencies,
            IReadOnlyDictionary<NativeLanguage, Sha256Digest> native)
        {
            ArgumentNullException.ThrowIfNull(compiler);
            ArgumentNullException.ThrowIfNull(archive);
            ArgumentNullException.ThrowIfNull(dependencies);
            ArgumentNullException.ThrowIfNull(native);

            var lines = new StringBuilder();
            lines.Append("packmoji build ").Append(Recipe).Append('\n');
            lines.Append("compiler ").Append(compiler.Hex).Append('\n');
            lines.Append("optimized ").Append(optimized ? "yes" : "no").Append('\n');
            lines.Append("package ").Append(archive.Hex).Append('\n');
            foreach (var (name, key) in dependencies.OrderBy(dependency => dependency.Key))
            {
                lines.Append("dependency ").Append(name).Append(' ').Append(key.Hex).Append('\n');
            }

            foreach (var (language, says) in native.OrderBy(compiler => compiler.Key))
            {
                lines.Append("native ").Append(language == NativeLanguage.C ? "c" : "c++").Append(' ').Append(says.Hex).Append('\n');
            }

            return Sha256Digest.Of(Encoding.UTF8.GetBytes(lines.ToString()));
        }
    }
}
