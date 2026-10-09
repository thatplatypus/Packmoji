using System.CommandLine;
using System.CommandLine.Help;

namespace Packmoji.Cli
{
    /// <summary>
    /// The command line of <c>pmj</c>. It is built apart from <c>Main</c> so that a test can run it
    /// against writers of its own and read what was printed.
    /// </summary>
    public static class PmjCommandLine
    {
        public static RootCommand Build()
        {
            var root = new RootCommand("Packmoji, the package manager for Emojicode.");

            // Given no command there is nothing to do, and saying nothing would look like success.
            root.SetAction(parseResult => new HelpAction().Invoke(parseResult));
            return root;
        }

        public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
        {
            var configuration = new InvocationConfiguration { Output = output, Error = error };
            return Build().Parse(args).InvokeAsync(configuration, cancellationToken);
        }
    }
}
