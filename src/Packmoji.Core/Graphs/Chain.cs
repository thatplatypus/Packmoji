using System.Globalization;

namespace Packmoji.Core.Graphs
{
    /// <summary>
    /// How a path through a graph of packages is written in a diagnostic: its steps, with an arrow
    /// between each. A path can be thousands of steps long in a graph that was built to be hostile,
    /// and what a person needs of a long one is its two ends: where it leaves their own project, and
    /// what it arrives at. So a long path keeps those and counts what lies between.
    /// </summary>
    internal static class Chain
    {
        /// <summary>A path of up to this many steps is shown whole.</summary>
        public const int MaxSteps = 8;

        private const string Arrow = " → ";
        private const int Head = 4;
        private const int Tail = 3;

        public static string Text(IReadOnlyList<string> steps) => Text(steps.Count, index => steps[index]);

        /// <summary>
        /// Writes a path of <paramref name="count"/> steps, asking for the text of a step only if it
        /// is shown, so that the cost of writing a path does not grow with its length.
        /// </summary>
        public static string Text(int count, Func<int, string> step)
        {
            var shown = new List<string>(Math.Min(count, MaxSteps));
            if (count <= MaxSteps)
            {
                for (var index = 0; index < count; index++)
                {
                    shown.Add(step(index));
                }
            }
            else
            {
                for (var index = 0; index < Head; index++)
                {
                    shown.Add(step(index));
                }

                shown.Add(string.Create(CultureInfo.InvariantCulture, $"({count - Head - Tail} more)"));
                for (var index = count - Tail; index < count; index++)
                {
                    shown.Add(step(index));
                }
            }

            return string.Join(Arrow, shown);
        }
    }
}
