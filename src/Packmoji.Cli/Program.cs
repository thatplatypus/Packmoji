using System.Text;
using Packmoji.Cli;

// What pmj writes through a pipe or into a file is UTF-8 on every machine, for that is where a tool
// reads it. Left to .NET it is written in the machine's own characters: on Windows those of the
// console pmj was started from, where the name of a source file, main.🍇, comes out as question
// marks. What goes to a console is left as it is, for the console says what it can show.
static TextWriter Piped(Stream stream) =>
    TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true });

var output = Console.IsOutputRedirected ? Piped(Console.OpenStandardOutput()) : Console.Out;
var error = Console.IsErrorRedirected ? Piped(Console.OpenStandardError()) : Console.Error;

return await PmjCommandLine.RunAsync(args, output, error, CancellationToken.None);
