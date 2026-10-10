using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Packmoji.Cli.Building
{
    /// <summary>Starts real programs, on the machine pmj runs on.</summary>
    public sealed class ProcessToolRunner : IToolRunner
    {
        public async Task<ToolRun?> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            var start = Start(program, arguments, workingDirectory);
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;

            using var process = new Process { StartInfo = start };
            if (!Started(process))
            {
                return null;
            }

            // Nothing is typed to a tool. One that waits to be is told at once that nothing will come.
            process.StandardInput.Close();

            // Both streams are read while the program runs: one that fills a pipe nobody reads waits for ever.
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await Task.WhenAll(output, error, process.WaitForExitAsync(cancellationToken));
            }
            catch (OperationCanceledException)
            {
                await StopAsync(process);
                throw;
            }

            return new ToolRun(process.ExitCode, await output, await error);
        }

        public async Task<int?> RunAttachedAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            using var process = new Process { StartInfo = Start(program, arguments, workingDirectory) };
            if (!Started(process))
            {
                return null;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await StopAsync(process);
                throw;
            }

            return process.ExitCode;
        }

        private static ProcessStartInfo Start(string program, IReadOnlyList<string> arguments, string workingDirectory)
        {
            var start = new ProcessStartInfo { FileName = program, WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }

        // A program that is not there, or that is no program, is something to report and no fault of pmj's.
        private static bool Started(Process process)
        {
            try
            {
                return process.Start();
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        // A pmj that is stopped leaves nothing running: not the tool, and not what the tool started.
        // It is waited for, so that by the time pmj goes on the tool writes nothing more: what it
        // was writing is about to be thrown away.
        private static async Task StopAsync(Process process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It had ended by itself, which is all that was wanted.
            }

            await process.WaitForExitAsync(CancellationToken.None);
        }
    }
}
