using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>A value and the place in a file it was read from, for a check that can only be made once the whole file is known.</summary>
    internal sealed record Located<T>(T Value, SourceLocation Location);
}
