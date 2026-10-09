using Packmoji.Core.Manifests;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>The manifest of a project that asks for the packages a test names, each written as <c>@owner/name@1.2</c>.</summary>
    internal static class Project
    {
        public const string Name = "@thatplatypus/app";

        public static Manifest Asking(params string[] dependencies) => Named(Name, dependencies, []);

        public static Manifest Named(string name, string[] dependencies, string[] devDependencies) => new(
            new PackageSection(Sample.Name(name), Sample.Version("0.1.0"), PackageKind.App, Sample.Compiler(">=1.0.0-beta.2")),
            dependencies.Select(Sample.Asks).ToList(),
            devDependencies.Select(Sample.Asks).ToList());
    }
}
