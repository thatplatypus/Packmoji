using Packmoji.Core.Lockfiles;
using Packmoji.Core.Resolution;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Holds a state of a registry to the state before it. A run adds versions, refuses releases and
    /// quarantines a version whose bytes changed, and that is all a run may do: what is recorded of a
    /// version never changes, and no version and no package is ever removed.
    /// </summary>
    /// <remarks>
    /// The run holds itself to this. It is checked again here, by what commits a run's work, so that
    /// a run that was misled, by a mistake in it or by an archive made to mislead it, still cannot
    /// change which bytes a version is.
    /// </remarks>
    public static class RegistryCheck
    {
        private const string Added = "added";
        private const string Quarantined = "quarantined";
        private const string Refused = "refused";
        private const string NoLongerRefused = "no longer refused";

        public static RegistryCheckResult Between(RegistryState before, RegistryState after)
        {
            ArgumentNullException.ThrowIfNull(before);
            ArgumentNullException.ThrowIfNull(after);
            var changes = new List<(string Kind, string Text)>();
            var broken = new List<string>();

            foreach (var name in before.Packages.Concat(after.Packages).Select(package => package.Name).Distinct().Order())
            {
                var was = before.Package(name);
                var now = after.Package(name);
                if (now is null)
                {
                    broken.Add($"the file of {name} is gone, and a package is never removed");
                    continue;
                }

                // A run writes a package's file when it records a version of it, and at no other time.
                if (was is null && now.Versions.Count == 0)
                {
                    broken.Add($"the file of {name} is new and holds no version, and a package is in the registry by a version of it");
                }

                foreach (var version in (was?.Versions ?? []).Concat(now.Versions).Select(held => held.Version).Distinct().Order())
                {
                    var subject = $"{name} {version}";
                    var held = was?.Find(version);
                    var holds = now.Find(version);
                    if (holds is null)
                    {
                        broken.Add($"{subject} is gone, and a version is never removed");
                    }
                    else if (held is null)
                    {
                        if (holds.Status != VersionStatus.Active)
                        {
                            broken.Add($"{subject} is new and is {RegistryStatuses.Name(holds.Status)}, and a new version begins active");
                        }
                        else
                        {
                            changes.Add((Added, $"added {subject} from {holds.Source}, sha256 {holds.Sha256}"));
                        }
                    }
                    else
                    {
                        Compare(subject, held, holds, changes, broken);
                    }
                }
            }

            foreach (var repository in before.Refused.Concat(after.Refused).Select(file => file.Repository).Distinct().OrderBy(repository => repository.ToString(), StringComparer.Ordinal))
            {
                var was = before.RefusedIn(repository);
                var now = after.RefusedIn(repository);
                foreach (var tag in (was?.Releases ?? []).Concat(now?.Releases ?? []).Select(release => release.Tag).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    var refused = now?.Find(tag);
                    if (refused is null)
                    {
                        changes.Add((NoLongerRefused, $"no longer refused: {tag} in {repository}"));
                    }
                    else if (refused != was?.Find(tag))
                    {
                        changes.Add((Refused, $"refused {tag} in {repository}: {refused.Why}"));
                    }
                }
            }

            return new RegistryCheckResult(
                changes.Select(change => change.Text).ToList(),
                broken,
                broken.Count > 0 ? "" : Summary(changes.Select(change => change.Kind).ToList()));
        }

        private static void Compare(string subject, RegistryVersion held, RegistryVersion holds, List<(string Kind, string Text)> changes, List<string> broken)
        {
            if (Difference(held, holds) is { } difference)
            {
                broken.Add($"what is recorded of {subject} has changed: {difference}");
            }
            else if (held.Status == holds.Status)
            {
                if (held.Quarantined != holds.Quarantined)
                {
                    broken.Add($"what is recorded of the quarantine of {subject} has changed");
                }
            }
            else if (holds.Status != VersionStatus.Quarantined)
            {
                broken.Add($"the status of {subject} went from {RegistryStatuses.Name(held.Status)} to {RegistryStatuses.Name(holds.Status)}, and a run changes a status only to quarantined");
            }
            else if (holds.Quarantined is null)
            {
                broken.Add($"{subject} is quarantined and does not say what was seen of its bytes");
            }
            else
            {
                changes.Add((Quarantined, $"quarantined {subject}: its file is now sha256 {holds.Quarantined.Sha256}"));
            }
        }

        // The first thing recorded of a version that is not what it was, said with both, or null when all of it is.
        private static string? Difference(RegistryVersion held, RegistryVersion holds)
        {
            (string Key, string Was, string Is)[] recorded =
            [
                ("source", held.Source.ToString(), holds.Source.ToString()),
                ("sha256", held.Sha256.Hex, holds.Sha256.Hex),
                ("size", Number(held.Size), Number(holds.Size)),
                ("verified", VerificationLevels.Name(held.Verified), VerificationLevels.Name(holds.Verified)),
                ("immutable", held.IsImmutable ? "true" : "false", holds.IsImmutable ? "true" : "false"),
                ("published", RegistryTime.Write(held.Published), RegistryTime.Write(holds.Published)),
                ("indexed", RegistryTime.Write(held.Indexed), RegistryTime.Write(holds.Indexed)),
            ];
            foreach (var (key, was, now) in recorded)
            {
                if (!string.Equals(was, now, StringComparison.Ordinal))
                {
                    return $"its {key} was {was} and is {now}";
                }
            }

            return held.SameButForStatus(holds) ? null : $"its dependencies were {Needs(held)} and are {Needs(holds)}";
        }

        private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static string Needs(RegistryVersion version) =>
            version.Dependencies.Count == 0
                ? "none"
                : string.Join(", ", version.Dependencies.Select(dependency => $"{dependency.Name}@{dependency.Requirement.Text}").Order(StringComparer.Ordinal));

        private static string Summary(List<string> kinds) =>
            string.Join(", ", new[] { Added, Quarantined, Refused, NoLongerRefused }
                .Select(kind => (Kind: kind, Count: kinds.Count(found => found == kind)))
                .Where(counted => counted.Count > 0)
                .Select(counted => $"{counted.Count} {counted.Kind}"));
    }
}
