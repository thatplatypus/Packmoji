namespace Packmoji.Core.Registry
{
    /// <summary>
    /// How much one run may download. A listed repository is someone else's, and may release a
    /// thousand things in an hour: what is over the limit is left for the next run, so that no run is
    /// longer than its owner meant.
    /// </summary>
    /// <param name="MaxDownloads">The most files a run asks for.</param>
    /// <param name="MaxBytes">The most bytes a run downloads, counted by what the list says each file is.</param>
    public sealed record RegistryLimits(int MaxDownloads, long MaxBytes)
    {
        /// <summary>64 files and 256 MiB.</summary>
        public static RegistryLimits Default { get; } = new(64, 256L * 1024 * 1024);
    }
}
