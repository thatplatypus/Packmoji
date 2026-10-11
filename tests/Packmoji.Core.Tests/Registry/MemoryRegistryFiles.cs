using System.Text;
using Packmoji.Core.Diagnostics;
using Packmoji.Core.Direct;
using Packmoji.Core.Registry;

namespace Packmoji.Core.Tests.Registry
{
    /// <summary>
    /// A registry's files, held in memory: what whoever reads a registry is given in a test. It keeps
    /// what was asked for, so that a test can say what was read and what was left alone.
    /// </summary>
    internal sealed class MemoryRegistryFiles : IRegistryFiles
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public string Address => "https://registry.example";

        /// <summary>Every path that was asked for, in the order of the asking.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>When set, nothing can be read, as when the registry cannot be reached.</summary>
        public bool Down { get; set; }

        /// <summary>Holds every file of a registry as it stands, in place of whatever was held.</summary>
        public MemoryRegistryFiles Hold(RegistryState state)
        {
            _files.Clear();
            foreach (var (path, text) in state.Files())
            {
                _files[path] = Encoding.UTF8.GetBytes(text);
            }

            return this;
        }

        /// <summary>Holds one file with whatever text a test gives it.</summary>
        public MemoryRegistryFiles Put(string path, string text)
        {
            _files[path] = Encoding.UTF8.GetBytes(text);
            return this;
        }

        public Task<ReadOnlyMemory<byte>?> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Asked.Add(path);
            if (Down)
            {
                throw new PackageSourceException(new Diagnostic(DiagnosticCodes.RegistryUnreachable, "The registry could not be reached.", "the test said so", "try again"));
            }

            return Task.FromResult(_files.TryGetValue(path, out var bytes) ? bytes : (ReadOnlyMemory<byte>?)null);
        }
    }
}
