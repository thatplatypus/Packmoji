namespace Packmoji.Core.Manifests
{
    /// <summary>How a kind is spelled in a manifest, in the one place the reader and the writer both use.</summary>
    internal static class PackageKinds
    {
        private const string Library = "library";
        private const string App = "app";

        public static string Name(PackageKind kind) => kind == PackageKind.App ? App : Library;

        public static bool TryParse(string text, out PackageKind kind)
        {
            kind = text == App ? PackageKind.App : PackageKind.Library;
            return text is Library or App;
        }
    }
}
