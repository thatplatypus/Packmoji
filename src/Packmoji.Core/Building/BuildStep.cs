using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Building
{
    /// <summary>
    /// What one step of a build gave: the thing it was for, or the problem that stopped it. Either
    /// way it carries what the tool printed, because a compiler's warnings are for whoever is building,
    /// whether or not the step succeeded.
    /// </summary>
    public sealed class BuildStep<T> where T : class
    {
        private BuildStep(T? value, Diagnostic? problem, string printed)
        {
            Value = value;
            Problem = problem;
            Printed = printed;
        }

        public T? Value { get; }

        public Diagnostic? Problem { get; }

        /// <summary>What the tool wrote, to either of its streams. Empty when it wrote nothing, or when no tool was run.</summary>
        public string Printed { get; }

        [MemberNotNullWhen(true, nameof(Value))]
        [MemberNotNullWhen(false, nameof(Problem))]
        public bool Succeeded => Problem is null;

        public static BuildStep<T> Of(T value, string printed = "") => new(value, null, printed);

        public static BuildStep<T> Failed(Diagnostic problem, string printed = "") => new(null, problem, printed);
    }
}
