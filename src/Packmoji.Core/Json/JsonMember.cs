using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Json
{
    internal sealed record JsonMember(string Name, SourceLocation NameLocation, JsonItem Value);
}
