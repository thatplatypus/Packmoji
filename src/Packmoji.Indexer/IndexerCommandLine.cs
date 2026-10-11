namespace Packmoji.Indexer
{
    /// <summary>
    /// How the indexer is asked: a command, and its options, each with a value and each once. There
    /// are two commands and five options, so they are read here by hand.
    /// </summary>
    public static class IndexerCommandLine
    {
        private const string Run = "run";
        private const string Check = "check";

        private const string Usage =
            """
            usage:
              Packmoji.Indexer run --registry <directory> --repositories <file>
              Packmoji.Indexer check --before <directory> --after <directory> [--message <file>]
            """;

        public static async Task<int> RunAsync(string[] args, IndexerHost host, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(args);
            ArgumentNullException.ThrowIfNull(host);
            if (args.Length == 0)
            {
                return Misused(host, "No command was given.");
            }

            var command = args[0];
            string[]? needed = command switch
            {
                Run => ["--registry", "--repositories"],
                Check => ["--before", "--after"],
                _ => null,
            };
            if (needed is null)
            {
                return Misused(host, $"\"{command}\" is not a command.");
            }

            string[] allowed = command == Check ? [.. needed, "--message"] : needed;
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index += 2)
            {
                var name = args[index];
                if (!allowed.Contains(name, StringComparer.Ordinal))
                {
                    return Misused(host, $"\"{name}\" is not an option of {command}.");
                }

                if (options.ContainsKey(name))
                {
                    return Misused(host, $"{name} was given twice.");
                }

                // What begins as an option does is an option that was meant, and not the value of the one before it.
                if (index + 1 >= args.Length || args[index + 1].Length == 0 || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    return Misused(host, $"{name} needs a value.");
                }

                options.Add(name, args[index + 1]);
            }

            foreach (var name in needed)
            {
                if (!options.ContainsKey(name))
                {
                    return Misused(host, $"{command} needs {name}.");
                }
            }

            return command == Run
                ? await RunCommand.RunAsync(host, options["--registry"], options["--repositories"], cancellationToken)
                : CheckCommand.Run(host, options["--before"], options["--after"], options.GetValueOrDefault("--message"));
        }

        private static int Misused(IndexerHost host, string what)
        {
            host.Error.WriteLine(what);
            host.Error.WriteLine(Usage);
            return ExitStatus.Misused;
        }
    }
}
