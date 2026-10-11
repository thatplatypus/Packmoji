namespace Packmoji.Core.Registry
{
    /// <summary>
    /// How much one run may download, and how large a file it may write. A listed repository is
    /// someone else's, and may release a thousand things in an hour: what is over the limit is left
    /// for the next run, so that no run is longer than its owner meant.
    /// </summary>
    /// <param name="MaxDownloads">The most files a run asks for.</param>
    /// <param name="MaxBytes">The most bytes a run downloads.</param>
    /// <param name="MaxFileBytes">
    /// The most bytes a package's file may come to. It is what the file's own reader reads: a version
    /// that would take a file past it is refused, for a registry that could not be read again would
    /// stop every run after it.
    /// </param>
    public sealed record RegistryLimits(int MaxDownloads, long MaxBytes, int MaxFileBytes = RegistryPackageReader.MaxBytes)
    {
        /// <summary>64 files and 256 MiB, and a file no larger than its reader reads.</summary>
        public static RegistryLimits Default { get; } = new(64, 256L * 1024 * 1024);
    }
}
