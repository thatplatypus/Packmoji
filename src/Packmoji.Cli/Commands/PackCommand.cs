using Packmoji.Cli.Output;
using Packmoji.Cli.Projects;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Lockfiles;
using Packmoji.Core.Packing;

namespace Packmoji.Cli.Commands
{
    /// <summary>
    /// <c>pmj pack</c>: writes the one file a release carries. The same files give the same bytes on
    /// every machine, so the digest printed here is the digest that everyone who installs the package
    /// will lock.
    /// </summary>
    internal static class PackCommand
    {
        // What is kept in a package's directory and is no part of the package: what pmj writes, where
        // built packages go, and git's own files. No pattern is given the chance to select them.
        private static readonly string[] NotThePackage = [".git", ProjectFiles.Target, "packages"];

        public static int Run(PmjHost host)
        {
            var manifest = ProjectFiles.ReadManifest(host.WorkingDirectory);
            if (!manifest.Succeeded)
            {
                return DiagnosticPrinter.Report(host, manifest.Diagnostics, manifest.Omitted);
            }

            try
            {
                var selected = PackSelection.Select(manifest.Value, FileTree.List(host.WorkingDirectory, NotThePackage));
                if (!selected.Succeeded)
                {
                    return DiagnosticPrinter.Report(host, selected.Diagnostics, selected.OmittedDiagnostics);
                }

                var files = selected.Value
                    .Select(path => new ArchiveFile(path, File.ReadAllBytes(Path.Combine(host.WorkingDirectory, path.Value.Replace('/', Path.DirectorySeparatorChar)))))
                    .ToList();

                // The files were read after they were listed, and one may have grown in between.
                if (PackageArchive.Check(files) is { Count: > 0 } problems)
                {
                    return DiagnosticPrinter.Report(host, TooLarge(problems[0]));
                }

                var archive = PackageArchive.Write(files);
                if (archive.Length > PackageArchive.MaxBytes)
                {
                    return DiagnosticPrinter.Report(host, TooLarge(
                        $"its archive would be {archive.Length} bytes, and an archive is at most {PackageArchive.MaxBytes} bytes"));
                }

                var package = manifest.Value.Package;
                var asset = AssetName.For(package.Name, package.Version);
                var target = Path.Combine(host.WorkingDirectory, ProjectFiles.Target);
                Directory.CreateDirectory(target);
                var beside = Path.Combine(target, asset + ".tmp-" + Guid.NewGuid().ToString("N"));
                File.WriteAllBytes(beside, archive);
                File.Move(beside, Path.Combine(target, asset), overwrite: true);

                host.Out.WriteLine($"Packed {package.Name} {package.Version}: {files.Count} file{(files.Count == 1 ? "" : "s")}, {archive.Length} bytes.");
                host.Out.WriteLine($"  {ProjectFiles.Target}/{asset}");
                host.Out.WriteLine($"  sha256 {Sha256Digest.Of(archive)}");
                host.Out.WriteLine($"To publish it, release that file under the tag {ReleaseTag.For(package.Name, package.Version)} in {manifest.Value.Repository}.");
                return ExitStatus.Success;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return DiagnosticPrinter.Report(host, ProjectFiles.Unreadable(host.WorkingDirectory, "packed", failure));
            }
        }

        private static Diagnostic TooLarge(string reason) =>
            new(
                DiagnosticCodes.ArchiveInvalid,
                "The package is too large to pack.",
                reason,
                "narrow the manifest's patterns so that they select only what the package needs to be built");
    }
}
