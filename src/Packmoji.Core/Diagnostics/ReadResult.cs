using System.Diagnostics.CodeAnalysis;

namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// What reading a file gave: the model, or the reasons there is none. Never both, so nothing built
    /// from a file that had a problem can be used by mistake.
    /// </summary>
    public sealed class ReadResult<T> where T : class
    {
        private ReadResult(T? value, IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics)
        {
            Value = value;
            Diagnostics = diagnostics;
            OmittedDiagnostics = omittedDiagnostics;
        }

        public T? Value { get; }

        /// <summary>The problems found, in the order they were found. At most a hundred are listed.</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>
        /// How many more problems were found than <see cref="Diagnostics"/> lists. It is 0 unless a file
        /// has more than a hundred, which only a mangled or a hostile one does.
        /// </summary>
        public int OmittedDiagnostics { get; }

        [MemberNotNullWhen(true, nameof(Value))]
        public bool Succeeded => Value is not null;

        internal static ReadResult<T> Success(T value) => new(value, [], 0);

        internal static ReadResult<T> Failure(DiagnosticList diagnostics) => new(null, diagnostics, diagnostics.Omitted);

        internal static ReadResult<T> Failure(IReadOnlyList<Diagnostic> diagnostics, int omittedDiagnostics) =>
            new(null, diagnostics, omittedDiagnostics);
    }
}
