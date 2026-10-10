using Packmoji.Core.Packing;

namespace Packmoji.Cli.Projects
{
    /// <summary>Lists the files under a directory, each by its path inside it, written with <c>/</c>.</summary>
    internal static class FileTree
    {
        /// <param name="skippedAtTop">Directories at the top of the tree that are not gone into.</param>
        public static IReadOnlyList<TreeEntry> List(string directory, params string[] skippedAtTop)
        {
            var entries = new List<TreeEntry>();
            var waiting = new Stack<(string Directory, string Prefix)>();
            waiting.Push((directory, ""));
            while (waiting.TryPop(out var next))
            {
                foreach (var entry in new DirectoryInfo(next.Directory).EnumerateFileSystemInfos())
                {
                    var path = next.Prefix + entry.Name;
                    if (entry.LinkTarget is not null)
                    {
                        // A link is listed and never followed: what it points at is somewhere else.
                        entries.Add(new TreeEntry(path, 0, IsLink: true));
                    }
                    else if (entry is FileInfo file)
                    {
                        entries.Add(new TreeEntry(path, file.Length, IsLink: false));
                    }
                    else if (next.Prefix.Length > 0 || !skippedAtTop.Contains(entry.Name, StringComparer.Ordinal))
                    {
                        waiting.Push((entry.FullName, path + "/"));
                    }
                }
            }

            return entries;
        }
    }
}
