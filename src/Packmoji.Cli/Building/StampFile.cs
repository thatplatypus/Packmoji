using Packmoji.Core.Building;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Cli.Building
{
    /// <summary>Reads the stamp that lies in a built package's folder, among what pmj keeps or in a project.</summary>
    internal static class StampFile
    {
        /// <summary>
        /// The key that a folder's stamp gives, or null: when there is no stamp, when what is there
        /// is too large to be one, which is never read, and when it gives no key.
        /// </summary>
        public static Sha256Digest? KeyIn(string folder)
        {
            var stamp = new FileInfo(Path.Combine(folder, BuildStamp.FileName));
            return stamp.Exists && stamp.Length <= BuildStamp.MaxBytes ? BuildStamp.KeyIn(File.ReadAllBytes(stamp.FullName)) : null;
        }
    }
}
