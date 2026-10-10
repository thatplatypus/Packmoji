using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using Packmoji.Cli.Commands;

namespace Packmoji.Cli
{
    /// <summary>
    /// The command line of <c>pmj</c>. It is built apart from <c>Main</c>, and over a host that a
    /// test can make up, so that a test can run it and read what it printed and what it wrote.
    /// </summary>
    public static class PmjCommandLine
    {
        public static RootCommand Build(PmjHost host)
        {
            ArgumentNullException.ThrowIfNull(host);
            var root = new RootCommand("Packmoji, the package manager for Emojicode.")
            {
                new Option<bool>("--direct")
                {
                    Recursive = true,
                    Description = "Find packages on GitHub itself, with no registry. That is the only way there is yet, so this changes nothing.",
                },
                Scaffolding(host, "new", "Make a project in a new directory named after it.", inPlace: false),
                Scaffolding(host, "init", "Make a project in this directory.", inPlace: true),
            };

            // Given no command there is nothing to do, and saying nothing would look like success.
            root.SetAction(parseResult => new HelpAction().Invoke(parseResult));
            return root;
        }

        /// <summary>Runs pmj on the machine it is on, as its environment describes it.</summary>
        public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken) =>
            RunAsync(args, PmjHost.FromEnvironment(output, error), cancellationToken);

        /// <returns>One of the statuses of <see cref="ExitStatus"/>.</returns>
        public static async Task<int> RunAsync(string[] args, PmjHost host, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(host);

            // A package is named @scope/name, and the parser would otherwise take that for the name of a file of arguments.
            var parsed = Build(host).Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
            if (parsed.Errors.Count > 0)
            {
                foreach (var error in parsed.Errors)
                {
                    host.Error.WriteLine($"error: {error.Message}");
                }

                host.Error.WriteLine($"Run {Asked(parsed)} --help to see how it is used.");
                return ExitStatus.Usage;
            }

            try
            {
                var configuration = new InvocationConfiguration { Output = host.Out, Error = host.Error, EnableDefaultExceptionHandler = false };
                return await parsed.InvokeAsync(configuration, cancellationToken);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                host.Error.WriteLine($"error: pmj failed in a way it should not have: {failure.GetType().Name}: {failure.Message}");
                host.Error.WriteLine("This is a fault in pmj and nothing you did. Please report it at https://github.com/thatplatypus/Packmoji/issues, with the command you ran.");
                return ExitStatus.InternalError;
            }
        }

        private static Command Scaffolding(PmjHost host, string name, string description, bool inPlace)
        {
            var package = new Argument<string>("package")
            {
                Description = "The project's name, as @scope/name. The scope is the GitHub owner who will publish it.",
            };
            var library = new Option<bool>("--lib") { Description = "Make a library." };
            var application = new Option<bool>("--app") { Description = "Make an application. This is what is made when neither is said." };

            var command = new Command(name, description) { package, library, application };
            command.Validators.Add(result =>
            {
                if (result.GetValue(library) && result.GetValue(application))
                {
                    result.AddError("A project is a library or an application: give --lib or --app, and not both.");
                }
            });
            command.SetAction(parseResult => Scaffold.Run(host, parseResult.GetValue(package)!, parseResult.GetValue(library), inPlace));
            return command;
        }

        // The command as it was typed up to its last name, for pointing at the help that fits.
        private static string Asked(ParseResult parsed)
        {
            var names = new List<string>();
            for (var result = parsed.CommandResult; result.Parent is CommandResult parent; result = parent)
            {
                names.Insert(0, result.Command.Name);
            }

            return string.Join(' ', ["pmj", .. names]);
        }
    }
}
