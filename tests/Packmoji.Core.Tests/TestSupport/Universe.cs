using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;
using Packmoji.Core.Versioning;
using Xunit;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// Everything that has been published, as far as one test is concerned: a package source held in
    /// memory and built in a few lines. It keeps what it was asked, so that a test can hold the
    /// resolver to asking for each version once, and in an order that does not change.
    /// </summary>
    internal sealed class Universe : IPackageSource
    {
        private readonly Dictionary<(PackageName Name, SemanticVersion Version), PublishedVersion> _published = [];

        /// <summary>Every version asked for, as <c>@owner/name@1.2.3</c>, in the order of the asking.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>Publishes a version, as <see cref="Sample.Published"/> makes one. Each dependency is written as <c>@owner/name@1.2</c>.</summary>
        public Universe Publish(string name, string version, params string[] dependencies)
        {
            var published = Sample.Published(name, version, dependencies);
            _published.Add((published.Name, published.Version), published);
            return this;
        }

        public Universe Yank(string name, string version) => Change(name, version, published => published with { Status = VersionStatus.Yanked });

        public Universe Quarantine(string name, string version) => Change(name, version, published => published with { Status = VersionStatus.Quarantined });

        public Universe Change(string name, string version, Func<PublishedVersion, PublishedVersion> change)
        {
            var key = (Sample.Name(name), Sample.Version(version));
            _published[key] = change(_published[key]);
            return this;
        }

        public PublishedVersion Get(string name, string version) => _published[(Sample.Name(name), Sample.Version(version))];

        public Task<ResolveResult> Resolve(Manifest manifest, Lockfile? existing = null) =>
            Resolver.ResolveAsync(manifest, existing, this, TestContext.Current.CancellationToken);

        public ValueTask<PublishedVersion?> FindAsync(PackageName name, SemanticVersion version, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Asked.Add($"{name}@{version}");
            return ValueTask.FromResult(_published.GetValueOrDefault((name, version)));
        }
    }
}
