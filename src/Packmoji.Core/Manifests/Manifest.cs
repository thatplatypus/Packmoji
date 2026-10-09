using Packmoji.Core.Identity;

namespace Packmoji.Core.Manifests
{
    /// <summary>
    /// What a <c>packmoji.json</c> says. It records what was written and no more: a key the file left
    /// out is null here, and its default is applied by the members below. That is what lets a manifest
    /// be written back without gaining keys its author never wrote.
    /// </summary>
    public sealed record Manifest(
        PackageSection Package,
        IReadOnlyList<Dependency>? Dependencies = null,
        IReadOnlyList<Dependency>? DevDependencies = null,
        BuildSection? Build = null,
        NativeSection? Native = null,
        PolicySection? Policy = null)
    {
        /// <summary>
        /// The sources of a package that names none: every Emojicode file under <c>src</c>, with either
        /// suffix the compiler accepts.
        /// </summary>
        public static IReadOnlyList<GlobPattern> DefaultSources { get; } =
            [GlobPattern.Known("src/**/*.emojic"), GlobPattern.Known("src/**/*.🍇")];

        public RepositoryRef Repository => Package.Repository ?? RepositoryRef.DefaultFor(Package.Name);

        public IReadOnlyList<GlobPattern> Sources => Build?.Sources ?? DefaultSources;

        public bool RequireAttestation => Policy?.RequireAttestation ?? false;
    }
}
