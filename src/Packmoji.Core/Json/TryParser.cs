using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    /// <summary>The shape that the <c>TryParse</c> of every value type has, so that one helper can call any of them.</summary>
    internal delegate bool TryParser<T>(string text, out T? value, out Diagnostic? error) where T : class;
}
