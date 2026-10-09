namespace Packmoji.Core.Diagnostics
{
    /// <param name="File">The file as the caller named it.</param>
    /// <param name="Line">1-based.</param>
    /// <param name="Column">1-based and counted in Unicode code points, so an emoji is one column.</param>
    public sealed record SourceLocation(string File, int Line, int Column);
}
