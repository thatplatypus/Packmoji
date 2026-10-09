using System.Collections;

namespace Packmoji.Core.Diagnostics
{
    /// <summary>
    /// The diagnostics of one read. It keeps the first hundred and counts the rest. A mangled or a
    /// hostile file can have a problem at every key, and a list of all of them costs a hundred times
    /// the memory of the file, to tell a person more than anyone reads.
    /// </summary>
    internal sealed class DiagnosticList : IReadOnlyList<Diagnostic>
    {
        public const int Limit = 100;

        private readonly List<Diagnostic> _kept = [];

        public int Count => _kept.Count;

        /// <summary>How many diagnostics were added after the list was full.</summary>
        public int Omitted { get; private set; }

        public Diagnostic this[int index] => _kept[index];

        public void Add(Diagnostic diagnostic)
        {
            if (_kept.Count < Limit)
            {
                _kept.Add(diagnostic);
            }
            else
            {
                Omitted++;
            }
        }

        /// <summary>
        /// Adds a diagnostic that is made only if there is room for it. Saying how a graph came to a
        /// problem takes a walk through the graph, and a walk for a diagnostic that would only be
        /// counted is work a hostile graph could ask for ten thousand times.
        /// </summary>
        public void Add(Func<Diagnostic> make)
        {
            if (_kept.Count < Limit)
            {
                _kept.Add(make());
            }
            else
            {
                Omitted++;
            }
        }

        public void Clear()
        {
            _kept.Clear();
            Omitted = 0;
        }

        public IEnumerator<Diagnostic> GetEnumerator() => _kept.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
