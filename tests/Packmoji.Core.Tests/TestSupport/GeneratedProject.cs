using CsCheck;
using Packmoji.Core.Manifests;

namespace Packmoji.Core.Tests.TestSupport
{
    /// <summary>
    /// A project, and everything published that it could lead to, made up by CsCheck: up to six
    /// packages, each with up to five versions on two compatibility lines, some yanked and some never
    /// published. A version depends only on packages that come after its own, so there is no circle.
    /// Now and then a version names one package twice, and more rarely the manifest does: no file can
    /// say either, and a source or a manifest made in code can.
    /// </summary>
    /// <remarks>
    /// What is generated is plain numbers, and the members below say what they mean. That keeps every
    /// generated value a valid project, whatever CsCheck does to make a failing one smaller.
    /// </remarks>
    internal sealed record GeneratedProject(
        GeneratedProject.Package[] Packages,
        GeneratedProject.Ask[] Dependencies,
        GeneratedProject.Ask[] DevDependencies,
        GeneratedProject.Ask More,
        int Order,
        bool VersionsRepeat,
        bool ManifestRepeats)
    {
        public const string Name = "@generated/app";

        // Four versions on a package's main line, in order, and one on another line.
        private static readonly string[] FromOne = ["1.0.0", "1.1.0", "1.2.0-rc.1", "1.2.0", "2.0.0"];
        private static readonly string[] BelowOne = ["0.3.0", "0.3.1", "0.3.2-rc.1", "0.3.2", "0.4.0"];

        private static readonly Gen<Ask> Asks = Gen.Select(Gen.Int[0, 5], Gen.Int[0, 23], (target, pick) => new Ask(target, pick));

        private static readonly Gen<Version> Versions = Gen.Select(
            Gen.Int[0, 15].Select(roll => roll != 0),
            Gen.Int[0, 15].Select(roll => roll == 0),
            Asks.Array[0, 3],
            (published, yanked, asks) => new Version(published, yanked, asks));

        public static readonly Gen<GeneratedProject> Any = Gen.Select(
            Gen.Select(Gen.Bool, Versions.Array[5], (belowOne, versions) => new Package(belowOne, versions)).Array[1, 6],
            Asks.Array[0, 4],
            Asks.Array[0, 2],
            Asks,
            Gen.Int[0, 1_000_000],
            Gen.Int[0, 7].Select(roll => roll == 0),
            Gen.Int[0, 15].Select(roll => roll == 0),
            (packages, dependencies, devDependencies, more, order, versionsRepeat, manifestRepeats) =>
                new GeneratedProject(packages, dependencies, devDependencies, more, order, versionsRepeat, manifestRepeats));

        /// <param name="Target">Which package is asked for, counted among those that may be asked for.</param>
        /// <param name="Pick">Which version of it: mostly one on its main line, at times the one on its other line, and now and then one that was never published.</param>
        internal sealed record Ask(int Target, int Pick);

        internal sealed record Version(bool Published, bool Yanked, Ask[] Asks);

        internal sealed record Package(bool BelowOne, Version[] Versions);

        public static string PackageName(int package) => $"@generated/p{package}";

        public Manifest Manifest(bool reordered = false)
        {
            var root = Root();
            return Project.Named(
                Name,
                InOrder(root.Where(ask => !ask.Dev).Select(ask => ask.Text), reordered, 1),
                InOrder(root.Where(ask => ask.Dev).Select(ask => ask.Text), reordered, 2));
        }

        /// <summary>
        /// The manifest with a requirement on one package more, which is the only way to ask for more
        /// without ceasing to ask for something. Null when the project already names every package.
        /// </summary>
        public Manifest? ManifestAskingForMore()
        {
            var root = Root();
            var named = root.Select(ask => ask.Package).ToHashSet();
            for (var offset = 0; offset < Packages.Length; offset++)
            {
                var package = (More.Target + offset) % Packages.Length;
                if (!named.Contains(package))
                {
                    return Project.Named(
                        Name,
                        [.. root.Where(ask => !ask.Dev).Select(ask => ask.Text), Written(More, package)],
                        root.Where(ask => ask.Dev).Select(ask => ask.Text).ToArray());
                }
            }

            return null;
        }

        /// <param name="yanks">Whether the versions that were generated as yanked are. A source with no registry behind it knows of none.</param>
        public Universe Universe(bool reordered = false, bool yanks = true)
        {
            var universe = new Universe();
            foreach (var (package, version) in Published(reordered))
            {
                var text = VersionsOf(package)[version];
                universe.Publish(PackageName(package), text, InOrder(AsksOf(package, Packages[package].Versions[version]), reordered, (package * 10) + version));
                if (yanks && Packages[package].Versions[version].Yanked)
                {
                    universe.Yank(PackageName(package), text);
                }
            }

            return universe;
        }

        /// <summary>Every version that is published, with what it asks for, for a test that publishes them somewhere else.</summary>
        public IEnumerable<(string Name, string Version, string[] Asks)> Released() =>
            Published(reordered: false).Select(published => (
                PackageName(published.Package),
                VersionsOf(published.Package)[published.Version],
                AsksOf(published.Package, Packages[published.Package].Versions[published.Version])));

        /// <summary>The project as a person would write it down, for when a property fails.</summary>
        public string Describe()
        {
            var manifest = Manifest();
            var lines = new List<string>
            {
                $"{Name} asks for [{string.Join(", ", manifest.Dependencies!.Select(Show))}] and to develop [{string.Join(", ", manifest.DevDependencies!.Select(Show))}]",
            };
            foreach (var (package, version) in Published(reordered: false))
            {
                var generated = Packages[package].Versions[version];
                lines.Add($"{PackageName(package)}@{VersionsOf(package)[version]}{(generated.Yanked ? " (yanked)" : "")} asks for [{string.Join(", ", AsksOf(package, generated))}]");
            }

            return string.Join("\n", lines);
        }

        private static string Show(Dependency dependency) => $"{dependency.Name}@{dependency.Requirement}";

        private string[] VersionsOf(int package) => Packages[package].BelowOne ? BelowOne : FromOne;

        private IEnumerable<(int Package, int Version)> Published(bool reordered)
        {
            var published =
                from package in Enumerable.Range(0, Packages.Length)
                from version in Enumerable.Range(0, Packages[package].Versions.Length)
                where Packages[package].Versions[version].Published
                select (package, version);
            return reordered ? published.Reverse() : published;
        }

        // A manifest names a package once, in one of its two tables, so a package asked for again is
        // passed over, but for the few projects that are made to name one twice.
        private List<(int Package, bool Dev, string Text)> Root()
        {
            var named = new HashSet<int>();
            var root = new List<(int Package, bool Dev, string Text)>();
            foreach (var (ask, dev) in Dependencies.Select(ask => (ask, false)).Concat(DevDependencies.Select(ask => (ask, true))))
            {
                var package = ask.Target % Packages.Length;
                if (named.Add(package) || ManifestRepeats)
                {
                    root.Add((package, dev, Written(ask, package)));
                }
            }

            return root;
        }

        private string[] AsksOf(int package, Version version)
        {
            var later = Packages.Length - package - 1;
            var named = new HashSet<int>();
            var asks = new List<string>();
            foreach (var ask in version.Asks)
            {
                if (later > 0 && (named.Add(package + 1 + (ask.Target % later)) || VersionsRepeat))
                {
                    asks.Add(Written(ask, package + 1 + (ask.Target % later)));
                }
            }

            return [.. asks];
        }

        private string Written(Ask ask, int package)
        {
            var version = ask.Pick switch
            {
                < 21 => VersionsOf(package)[ask.Pick % 4],
                < 23 => VersionsOf(package)[4],
                _ => Packages[package].BelowOne ? "0.3.9" : "1.9.0",
            };

            // A release that ends in .0 is written with two numbers every other time, as people write it.
            var twoNumbers = ask.Pick % 2 == 0 && version.EndsWith(".0", StringComparison.Ordinal);
            return $"{PackageName(package)}@{(twoNumbers ? version[..^2] : version)}";
        }

        // The same things in another order, and the same other order every time for one generated project.
        private string[] InOrder(IEnumerable<string> items, bool reordered, int salt)
        {
            var array = items.ToArray();
            if (reordered)
            {
                new Random(Order + salt).Shuffle(array);
            }

            return array;
        }
    }
}
