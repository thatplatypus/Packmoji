namespace Packmoji.Core.Manifests
{
    /// <param name="Entry">The package's main file. Null when the manifest leaves it to the entry convention.</param>
    /// <param name="Sources">Null when the manifest leaves it to <c>Manifest.DefaultSources</c>.</param>
    public sealed record BuildSection(RelativePath? Entry = null, IReadOnlyList<GlobPattern>? Sources = null);
}
