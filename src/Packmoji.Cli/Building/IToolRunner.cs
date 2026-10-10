namespace Packmoji.Cli.Building
{
    /// <summary>
    /// Starts the programs a build needs: the compiler, the C and C++ compilers, the archiver, and in
    /// the end the program that was built. A test puts made-up tools in its place, so that a build can
    /// be tested where no compiler runs.
    /// </summary>
    public interface IToolRunner
    {
        /// <summary>
        /// Runs a program to its end with a list of arguments, each given as it is and never through
        /// a shell, and gives its status and what it wrote. Null when the program could not be started.
        /// </summary>
        Task<ToolRun?> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);

        /// <summary>
        /// Runs a program to its end with pmj's own three streams, and gives its status. Null when
        /// the program could not be started.
        /// </summary>
        Task<int?> RunAttachedAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken);
    }
}
