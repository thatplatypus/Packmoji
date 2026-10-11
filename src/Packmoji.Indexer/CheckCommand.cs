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

        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

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
            var broken = NotAsWritten(stood, made.State, made.Bytes).Concat(result.Broken).ToList();
            if (broken.Count > 0)
            {
                return Stop(host, "The registry may not become what the run made of it.", broken);
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

                File.WriteAllBytes(message, Utf8.GetBytes(words.ToString()));
            }

            return ExitStatus.Success;
        }

        // A run writes a file in the one form the registry is written in, and leaves every other
        // file as it found it. So each file of what a run made is, to the byte, either the file that
        // stood or what the registry writes of what the file holds. A reader passes over a key it
        // does not know, and so a file that is neither holds something no reader shows: a key that a
        // later reader might give a meaning to, or text that only someone who opens the file sees.
        private static IEnumerable<string> NotAsWritten(RegistryRead stood, RegistryState made, IReadOnlyDictionary<string, byte[]> bytes)
        {
            var written = made.Files();
            foreach (var (path, file) in bytes.OrderBy(read => read.Key, StringComparer.Ordinal))
            {
                var asItStood = stood.Bytes.TryGetValue(path, out var was) && was.AsSpan().SequenceEqual(file);
                if (!asItStood && !(written.TryGetValue(path, out var text) && Utf8.GetBytes(text).AsSpan().SequenceEqual(file)))
                {
                    yield return $"{path} is not written as the registry writes it: to the byte, it is neither as it stood nor what the registry writes of what it holds.";
                }
            }
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
