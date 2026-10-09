namespace Packmoji.Core.Identity
{
    /// <summary>
    /// The stock packages of Emojicode 1.0 beta 2. The compiler takes the first package of a name that
    /// it finds, and it looks where Packmoji puts packages before it looks at its own, so a package
    /// with one of these names would replace the stock one for a whole build.
    /// </summary>
    public static class ReservedNames
    {
        private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
        {
            "s", "runtime", "files", "sockets", "json", "testtube",
        };

        public static IReadOnlyCollection<string> All => Names;

        public static bool Contains(string name) => Names.Contains(name);
    }
}
