using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Manifests;

namespace Packmoji.Cli.Commands
{
    /// <summary>The problems that more than one command reports, said in one place so that each is said one way.</summary>
    internal static class CommandDiagnostics
    {
        /// <summary>
        /// For a command that would lock or fetch packages for a project that requires each to be
        /// attested. This pmj holds a package to its checksum and can verify no attestation, and a
        /// requirement that cannot be checked is not met: so it stops, and does not go on without it.
        /// </summary>
        public static Diagnostic AttestationRequired() =>
            new(
                DiagnosticCodes.AttestationUnverifiable,
                "This project requires attestation, and pmj cannot verify one yet.",
                $"{ManifestReader.FileName} sets \"requireAttestation\" under \"policy\", so no package may be used unless its build is attested, and this pmj can hold a package only to its checksum",
                $"set \"requireAttestation\" to false in {ManifestReader.FileName} to accept packages that are verified by checksum alone, or wait for a pmj that verifies attestations");

        public static Diagnostic NotADependency(PackageName name) =>
            new(
                DiagnosticCodes.DependencyNotFound,
                $"\"{name}\" is not a dependency of this project.",
                $"{ManifestReader.FileName} does not ask for it, under \"dependencies\" or under \"devDependencies\"",
                $"check the name against {ManifestReader.FileName}");

        /// <summary>For a command that takes packages by name alone, and was given one with a requirement after it.</summary>
        public static Diagnostic NameAlone(string command, string given, PackageName name) =>
            new(
                DiagnosticCodes.NameInvalid,
                $"\"{given}\" is more than a package's name.",
                $"pmj {command} takes a package by its name alone, and this has a requirement after it",
                $"run pmj {command} {name}");

        /// <summary>For a command that reads what is locked, and finds no lockfile, or one the manifest has moved on from.</summary>
        public static Diagnostic NotInstalled(bool missing) =>
            new(
                DiagnosticCodes.LockOutOfDate,
                missing ? $"There is no {LockfileReader.FileName}." : $"{LockfileReader.FileName} no longer answers what {ManifestReader.FileName} asks for.",
                "this command reads what is locked, and chooses nothing itself",
                $"run pmj install, which writes {LockfileReader.FileName}");

        /// <summary>For a command that is forbidden to write the lockfile, and finds that it would have to.</summary>
        /// <param name="missing">Whether there is no lockfile at all, as against one the manifest has moved on from.</param>
        public static Diagnostic LockWouldChange(bool missing) =>
            new(
                DiagnosticCodes.LockOutOfDate,
                missing
                    ? $"There is no {LockfileReader.FileName}, and --locked forbids writing one."
                    : $"{LockfileReader.FileName} no longer answers what {ManifestReader.FileName} asks for, and --locked forbids changing it.",
                "with --locked, pmj installs exactly what the lockfile holds and chooses nothing",
                $"run pmj install without --locked, and commit the {LockfileReader.FileName} it writes");
    }
}
