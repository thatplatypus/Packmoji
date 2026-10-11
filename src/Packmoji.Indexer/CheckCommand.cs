using System.Text;
using Packmoji.Core.Registry;

namespace Packmoji.Indexer
{
    /// <summary>
    /// <c>check</c>: holds what a run made of a registry to the registry as it stood, and writes the
    /// words of the commit that records the change. It reads both as data and asks nothing of
    /// anyone: it is run where the registry can be written, on the work of a run that could not.
    /// </summary>
    internal static class CheckCommand
    {
        private const string TitlePrefix = "chore(registry): ";

        /// <param name="message">Where the words of a commit are written, when they are wanted. The file is left empty when there is nothing to commit, or nothing that may be.</param>
        public static int Run(IndexerHost host, string before, string after, string? message)
        {
            // Emptied first, so that whatever stops the check, no words are left that a commit could be made with.
            if (message is not null)
            {
                File.WriteAllBytes(message, []);
            }

            var stood = RegistryDirectory.Read(before);
            if (stood.State is null)
            {
                return Stop(host, "The registry as it stands cannot be read.", stood.Problems);
            }

            var made = RegistryDirectory.Read(after);
            if (made.State is null)
            {
                return Stop(host, "What the run made cannot be read as a registry.", made.Problems);
            }

            var result = RegistryCheck.Between(stood.State, made.State);
            if (!result.Allowed)
            {
                return Stop(host, "The registry may not become what the run made of it.", result.Broken);
            }

            if (result.Changes.Count == 0)
            {
                host.Out.WriteLine("Nothing has changed.");
                return ExitStatus.Success;
            }

            foreach (var change in result.Changes)
            {
                host.Out.WriteLine(change);
            }

            if (message is not null)
            {
                var words = new StringBuilder().Append(TitlePrefix).Append(result.Summary).Append("\n\n");
                foreach (var change in result.Changes)
                {
                    words.Append(change).Append('\n');
                }

                File.WriteAllBytes(message, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(words.ToString()));
            }

            return ExitStatus.Success;
        }

        private static int Stop(IndexerHost host, string what, IEnumerable<string> problems)
        {
            host.Error.WriteLine(what);
            foreach (var problem in problems)
            {
                host.Error.WriteLine("  " + problem);
            }

            return ExitStatus.Failure;
        }
    }
}
