using System.Globalization;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;
using Packmoji.Core.Registry;
using Packmoji.Core.Resolution;
using Packmoji.Core.Tests.TestSupport;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>Makes what a registry holds, for tests that are about what may become of it.</summary>
    internal static class RegistrySample
    {
        public const string Grapevine = "github.com/thatplatypus/grapevine";

        public static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);

        /// <summary>An active version, as a run records one. Each dependency is written as <c>@owner/name@1.2</c>.</summary>
        public static RegistryVersion Version(string version, params string[] dependencies) => new(
            Sample.Version(version),
            VersionStatus.Active,
            Sample.Repository(Grapevine),
            Sample.ShaOf(RegistryFixture.Sha),
            48213,
            VerificationLevel.Checksum,
            IsImmutable: false,
            Time("2026-10-11T15:02:11Z"),
            Time("2026-10-11T15:17:40Z"),
            dependencies
                .Select(dependency => dependency.Split('@', StringSplitOptions.RemoveEmptyEntries))
                .Select(parts => new Dependency(Sample.Name("@" + parts[0]), Sample.Requirement(parts[1])))
                .ToList());

        /// <summary>The same version, quarantined as a run quarantines one.</summary>
        public static RegistryVersion Quarantined(this RegistryVersion version, string seen = "2026-11-02T09:17:12Z", string sha256 = RegistryFixture.OtherSha) =>
            version with { Status = VersionStatus.Quarantined, Quarantined = new RegistryQuarantine(Time(seen), Sample.ShaOf(sha256)) };

        public static RegistryPackage Package(string name, params RegistryVersion[] versions) => new(Sample.Name(name), versions);

        /// <summary>A refusal as a run records one: under the code that goes with the reason, or under <c>archive.invalid</c> for words that are no reason of the registry's.</summary>
        public static RefusedRelease Refusal(string tag, string why = "its file is not an archive as pmj pack writes one", string sha256 = RegistryFixture.Sha, string? code = null) =>
            new(
                tag,
                Sample.ShaOf(sha256),
                Time("2026-10-12T08:17:03Z"),
                code ?? RefusalReasons.All.Select(reason => reason.Code).Distinct().FirstOrDefault(known => RefusalReasons.IsOne(known, why)) ?? "archive.invalid",
                why);

        public static RefusedReleases Refused(string repository, params RefusedRelease[] releases) => new(Sample.Repository(repository), releases);

        public static RegistryState State(params RegistryPackage[] packages) => new(packages, []);
    }
}
