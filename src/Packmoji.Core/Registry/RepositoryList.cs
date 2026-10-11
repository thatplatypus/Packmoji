using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// The repositories whose releases a registry indexes. It is a file that a person writes and a
    /// pull request changes: one repository to a line, written as a manifest writes one, in order
    /// and none twice, so that a line added to it has one place to go and a change to it reads as
    /// what it is. A line that begins with <c>#</c>, and a line with nothing on it, are for a person.
    /// </summary>
    public sealed record RepositoryList(IReadOnlyList<RepositoryRef> Repositories)
    {
        public const string FileName = "registry-repositories.txt";

        /// <summary>The most bytes a list may be: 1 MiB, which is room for some twenty thousand repositories.</summary>
        public const int MaxBytes = 1_048_576;

        public static ReadResult<RepositoryList> Read(string text, string file = FileName)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(file);
            if (text.Length > MaxBytes || Encoding.UTF8.GetByteCount(text) > MaxBytes)
            {
                return ReadResult<RepositoryList>.Failure(
                    [
                        new Diagnostic(
                            DiagnosticCodes.FileTooLarge,
                            $"\"{file}\" is too large to be a list of repositories.",
                            $"a list is at most {MaxBytes} bytes, which is 1 MiB and room for some twenty thousand repositories",
                            "check that the file is the list, and that nothing was added to it by mistake",
                            new SourceLocation(file, 1, 1)),
                    ],
                    0);
            }

            var diagnostics = new DiagnosticList();
            var repositories = new List<RepositoryRef>();
            var lines = new Dictionary<string, int>(StringComparer.Ordinal);
            var number = 0;
            foreach (var raw in text.Split('\n'))
            {
                number++;
                var line = raw.EndsWith('\r') ? raw[..^1] : raw;
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                var here = new SourceLocation(file, number, 1);
                if (!RepositoryRef.TryParse(line, out var repository, out var notOne))
                {
                    diagnostics.Add(notOne with { Location = here });
                    continue;
                }

                var written = repository.ToString();
                if (lines.TryGetValue(written, out var first))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.ListDuplicate,
                        $"{written} is in the list twice.",
                        $"it is at line {first} as well",
                        "remove one of the two lines",
                        here));
                    continue;
                }

                // Held against every line before it, so that one line out of place is one problem and not one for each line after it.
                if (repositories.Select(earlier => earlier.ToString()).FirstOrDefault(earlier => string.CompareOrdinal(earlier, written) > 0) is { } later)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.RegistryInvalid,
                        $"{written} is out of order.",
                        "the list is kept in order, so that a line added to it has one place to go",
                        $"move the line to before {later}",
                        here));
                }

                lines.Add(written, number);
                repositories.Add(repository);
            }

            return diagnostics.Count > 0
                ? ReadResult<RepositoryList>.Failure(diagnostics)
                : ReadResult<RepositoryList>.Success(new RepositoryList(repositories));
        }
    }
}
