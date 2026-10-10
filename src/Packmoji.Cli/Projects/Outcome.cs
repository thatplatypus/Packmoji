using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Cli.Projects
{
    /// <summary>What a step of a command gave: the thing it was for, or the problems to print in its place.</summary>
    internal sealed class Outcome<T> where T : class
    {
        private Outcome(T? value, IReadOnlyList<Diagnostic> diagnostics, int omitted)
        {
            Value = value;
            Diagnostics = diagnostics;
            Omitted = omitted;
        }

        public T? Value { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public int Omitted { get; }

        [MemberNotNullWhen(true, nameof(Value))]
        public bool Succeeded => Value is not null;

        public static Outcome<T> Of(T value) => new(value, [], 0);

        public static Outcome<T> Failed(Diagnostic diagnostic) => new(null, [diagnostic], 0);

        public static Outcome<T> Failed(IReadOnlyList<Diagnostic> diagnostics, int omitted = 0) => new(null, diagnostics, omitted);
    }
}
