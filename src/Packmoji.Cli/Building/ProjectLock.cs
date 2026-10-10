using Packmoji.Cli.Projects;

namespace Packmoji.Cli.Building
{
    /// <summary>
    /// What a build holds while it writes into a project, so that two builds of one project do not
    /// write into its directories at once. It is a file that one pmj has open and no other can open,
    /// and the machine lets go of it when that pmj ends, however it ends.
    /// </summary>
    internal sealed class ProjectLock : IDisposable
    {
        private const string FileName = ".pmj-lock";

        /// <summary>Where in a project the lock is kept.</summary>
        public const string Place = ProjectFiles.Target + "/" + FileName;

        private readonly FileStream _held;

        private ProjectLock(FileStream held)
        {
            _held = held;
        }

        /// <summary>Takes the lock on a project, waiting for a pmj that holds it.</summary>
        /// <param name="waiting">Called once, when there turns out to be something to wait for.</param>
        public static async Task<ProjectLock> TakeAsync(string project, Action waiting, CancellationToken cancellationToken)
        {
            var target = Path.Combine(project, ProjectFiles.Target);
            Directory.CreateDirectory(target);
            var said = false;
            while (true)
            {
                try
                {
                    return new ProjectLock(new FileStream(Path.Combine(target, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (IOException) when (said || CanBeWrittenIn(target))
                {
                    if (!said)
                    {
                        waiting();
                        said = true;
                    }

                    await Task.Delay(100, cancellationToken);
                }
            }
        }

        // Opening the lock fails in the same way when another pmj holds it and when nothing can be
        // written here at all, as on a disk that is read-only. Only the first is worth waiting
        // for, and the second would be waited for without end. A file made beside the lock tells
        // them apart, and what could not be written is then the problem that is reported.
        private static bool CanBeWrittenIn(string target)
        {
            var beside = Path.Combine(target, FileName + ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(beside, []);
                File.Delete(beside);
                return true;
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose() => _held.Dispose();
    }
}
