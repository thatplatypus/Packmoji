using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using Packmoji.Cli.Commands;
using Packmoji.Cli.Output;
using Packmoji.Core.Direct;

namespace Packmoji.Cli
{
    /// <summary>
    /// The command line of <c>pmj</c>. It is built apart from <c>Main</c>, and over a host that a
    /// test can make up, so that a test can run it and read what it printed and what it wrote.
    /// </summary>
    public static class PmjCommandLine
    {
        private const string JsonOption = "--json";

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
                Add(host),
                Remove(host),
                Install(host),
                Update(host),
                Tree(host),
                Pack(host),
                Verify(host),
                Building(host),
                Running(host),
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
            catch (OperationCanceledException)
            {
                // Stopped from outside, as by Ctrl+C. Each file pmj writes is put in its place in one step, so nothing is half done.
                return ExitStatus.Interrupted;
            }
            catch (PackageSourceException failure)
            {
                // Not being able to find something out is a problem to report, and no fault of pmj's.
                // A tool that asked to be answered with JSON is answered with it, whatever went wrong.
                return DiagnosticPrinter.Report(host, [failure.Diagnostic], json: AskedForJson(parsed));
            }
            catch (Exception failure)
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

        private static Command Add(PmjHost host)
        {
            var package = new Argument<string>("package")
            {
                Description = "The package to depend on, as @scope/name. To ask for a version, write @scope/name@1.2: without one, the latest is asked for.",
            };
            var dev = new Option<bool>("--dev") { Description = "The package is needed only to develop this project, and not by what depends on it." };
            var repository = new Option<string?>(RepositoryOption.Name)
            {
                Description = "Where the package lives, as github.com/owner/repo. It is needed only for a package that shares its repository with others and cannot be found beside something already depended on.",
            };

            var json = Json();
            var command = new Command("add", "Depend on a package: write it into packmoji.json, lock it and fetch it.") { package, dev, repository, json };
            command.SetAction((parseResult, cancellationToken) =>
                AddCommand.RunAsync(host, parseResult.GetValue(package)!, parseResult.GetValue(dev), parseResult.GetValue(repository), parseResult.GetValue(json), cancellationToken));
            return command;
        }

        private static Command Remove(PmjHost host)
        {
            var package = new Argument<string>("package") { Description = "The package to stop depending on, as @scope/name." };
            var repositories = AlsoLookIn();
            var json = Json();
            var command = new Command("remove", "Stop depending on a package: take it out of packmoji.json and lock what is left.") { package, repositories, json };
            command.SetAction((parseResult, cancellationToken) =>
                RemoveCommand.RunAsync(host, parseResult.GetValue(package)!, parseResult.GetValue(repositories) ?? [], parseResult.GetValue(json), cancellationToken));
            return command;
        }

        private static Command Install(PmjHost host)
        {
            var locked = new Option<bool>("--locked")
            {
                Description = "Fail if packmoji.lock would have to be written or changed. This is for CI, where nobody is there to see it change.",
            };
            var repositories = AlsoLookIn();
            var json = Json();
            var command = new Command("install", "Fetch exactly what packmoji.lock holds. Versions are chosen only if packmoji.json asks for something else.") { locked, repositories, json };
            command.SetAction((parseResult, cancellationToken) =>
                InstallCommand.RunAsync(host, parseResult.GetValue(locked), parseResult.GetValue(repositories) ?? [], parseResult.GetValue(json), cancellationToken));
            return command;
        }

        private static Command Update(PmjHost host)
        {
            var packages = new Argument<string[]>("packages")
            {
                Arity = ArgumentArity.ZeroOrMore,
                Description = "The packages whose requirements to raise, each as @scope/name. Every dependency, when none is named.",
            };
            var dryRun = new Option<bool>("--dry-run") { Description = "Say what would change, and write nothing." };
            var repositories = AlsoLookIn();
            var json = Json();
            var command = new Command("update", "Raise what packmoji.json asks for to the latest version on each requirement's line, and lock what that gives.") { packages, dryRun, repositories, json };
            command.SetAction((parseResult, cancellationToken) =>
                UpdateCommand.RunAsync(host, parseResult.GetValue(packages) ?? [], parseResult.GetValue(dryRun), parseResult.GetValue(repositories) ?? [], parseResult.GetValue(json), cancellationToken));
            return command;
        }

        private static Command Tree(PmjHost host)
        {
            var json = Json();
            var command = new Command("tree", "Show the packages that packmoji.lock holds, and what each depends on.") { json };
            command.SetAction(parseResult => TreeCommand.Run(host, parseResult.GetValue(json)));
            return command;
        }

        private static Command Verify(PmjHost host)
        {
            var json = Json();
            var command = new Command("verify", "Download every locked package again, and hold it and the cache's copy to packmoji.lock.") { json };
            command.SetAction((parseResult, cancellationToken) => VerifyCommand.RunAsync(host, parseResult.GetValue(json), cancellationToken));
            return command;
        }

        private static Command Building(PmjHost host)
        {
            var release = new Option<bool>("--release") { Description = "Have the compiler optimize, the packages as well as the project." };
            var dependenciesOnly = new Option<bool>("--dependencies-only")
            {
                Description = "Build the packages the project depends on and put them in packages/, and stop before the project itself. Nothing is needed here but packmoji.json and packmoji.lock.",
            };
            var json = Json();
            var command = new Command("build", "Compile what packmoji.lock holds, each package once for the whole machine, and then the project.") { release, dependenciesOnly, json };
            command.SetAction((parseResult, cancellationToken) =>
                BuildCommand.RunAsync(host, parseResult.GetValue(release), parseResult.GetValue(dependenciesOnly), parseResult.GetValue(json), cancellationToken));
            return command;
        }

        private static Command Running(PmjHost host)
        {
            var release = new Option<bool>("--release") { Description = "Have the compiler optimize, and run what that builds." };
            var arguments = new Argument<string[]>("arguments")
            {
                Arity = ArgumentArity.ZeroOrMore,
                Description = "What the program is given. Write -- before them, so that pmj takes none of them for an option of its own.",
            };
            var command = new Command("run", "Build this application, and run it.") { release, arguments };
            command.SetAction((parseResult, cancellationToken) =>
                RunCommand.RunAsync(host, parseResult.GetValue(release), parseResult.GetValue(arguments) ?? [], cancellationToken));
            return command;
        }

        private static Option<string[]> AlsoLookIn() =>
            new(RepositoryOption.Name)
            {
                Description = "A repository to look in as well, as github.com/owner/repo. It is needed only when packmoji.lock is not there to say where a package lives. It can be given more than once.",
            };

        // Whether the command that was run has --json and was given it.
        private static bool AskedForJson(ParseResult parsed) =>
            parsed.CommandResult.Command.Options.OfType<Option<bool>>().FirstOrDefault(option => option.Name == JsonOption) is { } json && parsed.GetValue(json);

        private static Option<bool> Json() =>
            new(JsonOption) { Description = "Answer a tool: one JSON object on standard output, with what was found and any problems in it." };

        private static Command Pack(PmjHost host)
        {
            var command = new Command("pack", "Write the file that a release of this package carries, into target/.");
            command.SetAction(_ => PackCommand.Run(host));
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
