using System.Text;
using Packmoji.Indexer;

// What the indexer writes is read by a workflow and kept in its log, so it is UTF-8 whatever machine
// it runs on. Left to .NET it is written in the machine's own characters.
static TextWriter Utf8(Stream stream) =>
    TextWriter.Synchronized(new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true });

var output = Console.IsOutputRedirected ? Utf8(Console.OpenStandardOutput()) : Console.Out;
var error = Console.IsErrorRedirected ? Utf8(Console.OpenStandardError()) : Console.Error;

return await IndexerCommandLine.RunAsync(args, IndexerHost.FromEnvironment(output, error), CancellationToken.None);
