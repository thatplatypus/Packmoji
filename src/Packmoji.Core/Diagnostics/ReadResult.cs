using System.Diagnostics.CodeAnalysis;

namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// What reading a file gave: the model, or the reasons there is none. Never both, so nothing built
    /// from a file that had a problem can be used by mistake.
    /// </summary>
    public sealed class ReadResult<T> where T : class
    {
        private ReadResult(T? value, IReadOnlyList<Diagnostic> diagnostics)
        {
            Value = value;
            Diagnostics = diagnostics;
        }

        public T? Value { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        [MemberNotNullWhen(true, nameof(Value))]
        public bool Succeeded => Value is not null;

        internal static ReadResult<T> Success(T value) => new(value, []);

        internal static ReadResult<T> Failure(IReadOnlyList<Diagnostic> diagnostics) => new(null, diagnostics);
    }
}
