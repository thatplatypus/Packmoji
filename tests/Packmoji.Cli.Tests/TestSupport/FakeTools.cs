using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Packmoji.Cli.Building;

namespace Packmoji.Cli.Tests.TestSupport
{
    /// <summary>
    /// The tools of a build, made up: the Emojicode compiler, the C and C++ compilers, the archiver,
    /// and the program that comes out. Each is a file whose first line says which it is, so that pmj
    /// finds it, and takes its digest, as it does a real one. What each writes is text that says what
    /// it was made from, so that a test can read what was compiled against what and linked with what.
    /// </summary>
    /// <remarks>
    /// The compiler here does what the released one was read and seen to do (decision 0001, and
    /// section 2 of the M3 design): it takes one main file and the flags pmj uses, looks for each
    /// imported package in the search paths in order, reads that package's interface and what the
    /// interface imports in its turn, and fails when one is not found. So a build that is run out of
    /// order, or that leaves a search path out, fails here as it would with the real compiler.
    /// </remarks>
    internal sealed partial class FakeTools(Func<string, string?> variable) : IToolRunner
    {
        public const string Banner = "Emojicode Compiler 1.0 beta 2. Visit https://www.emojicode.org for help.";

        /// <summary>Every tool that was run, in order.</summary>
        public List<ToolCall> Calls { get; } = [];

        /// <summary>Where a built program writes what it says.</summary>
        public TextWriter ProgramOutput { get; set; } = TextWriter.Null;

        /// <summary>Whether the linker is the one of macOS, which refuses a group of archives and needs none.</summary>
        public bool MacLinker { get; set; }

        /// <summary>The directory that stands in for the one the compiler has built into it, which it searches last.</summary>
        public string BuiltInPackages { get; set; } = "";

        /// <summary>Libraries that the linker is to say it cannot find.</summary>
        public HashSet<string> MissingLibraries { get; } = new(StringComparer.Ordinal);

        /// <summary>Tools that end with status 1 whatever they are asked, each by the name its file gives it.</summary>
        public HashSet<string> Fails { get; } = new(StringComparer.Ordinal);

        /// <summary>Tools that are there and cannot be started, each by the name its file gives it.</summary>
        public HashSet<string> Unstartable { get; } = new(StringComparer.Ordinal);

        /// <summary>Tools that end with status 0 and have done nothing, each by the name its file gives it.</summary>
        public HashSet<string> Idle { get; } = new(StringComparer.Ordinal);

        /// <summary>When set, it is awaited before each tool is run, which lets a test hold a build where it is.</summary>
        public Func<ToolCall, Task>? Before { get; set; }

        /// <summary>The compiler's runs alone.</summary>
        public IEnumerable<ToolCall> Compiles => Calls.Where(call => call.Tool == "emojicodec" && !call.Arguments.Contains("--help"));

        public async Task<ToolRun?> RunAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (Kind(program) is not { } tool || Unstartable.Contains(tool.Name))
            {
                return null;
            }

            var call = new ToolCall(tool.Name, program, arguments, workingDirectory);
            Calls.Add(call);
            if (Before is not null)
            {
                await Before(call);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var asked = !arguments.Contains("--help") && !arguments.Contains("--version");
            if (Fails.Contains(tool.Name) && asked)
            {
                return Refused($"made-up {tool.Name}: told to fail");
            }

            if (Idle.Contains(tool.Name) && asked)
            {
                return new ToolRun(0, "", "");
            }

            return tool.Name switch
            {
                "emojicodec" => Compile(tool.Says, arguments, workingDirectory),
                "c++" or "cc" when arguments.Contains("--version") => tool.Says.Length == 0
                    ? Refused($"made-up {tool.Name}: unrecognized command-line option '--version'")
                    : new ToolRun(0, string.Join('\n', tool.Says) + "\n", ""),
                "c++" or "cc" => arguments.Contains("-c") ? CompileNative(tool.Name, arguments)
                    : Link(arguments),
                "ar" => Archive(arguments),
                _ => Program(program, arguments),
            };
        }

        public async Task<int?> RunAttachedAsync(string program, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken) =>
            (await RunAsync(program, arguments, workingDirectory, cancellationToken))?.Status;

        /// <summary>The first eight digits of the SHA-256 of a text, which is how what a tool wrote names what it read.</summary>
        public static string Short(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8];

        // Which tool a file is: its first line says, and what follows is what the tool says of itself.
        private static (string Name, string[] Says)? Kind(string program)
        {
            if (!File.Exists(program))
            {
                return null;
            }

            var lines = File.ReadAllLines(program);
            return lines.Length > 0 && lines[0].StartsWith("made-up ", StringComparison.Ordinal) ? (lines[0]["made-up ".Length..], lines[1..]) : null;
        }

        private static ToolRun Refused(string error) => new(1, "", error + "\n");

        private ToolRun Compile(string[] says, IReadOnlyList<string> arguments, string workingDirectory)
        {
            string? main = null, package = null, output = null, interfacePath = null;
            var (report, objectOnly, optimized) = (false, false, false);
            var search = new List<string>();
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                switch (argument)
                {
                    case "--help":
                        return new ToolRun(0, $"  emojicodec file {{OPTIONS}}\n\n    {(says.Length > 0 ? says[0] : "")}\n\n  OPTIONS:\n\n", "");
                    case "-p":
                        package = arguments[++index];
                        break;
                    case "-o":
                        output = arguments[++index];
                        break;
                    case "-i":
                        interfacePath = arguments[++index];
                        break;
                    case "-S":
                        search.Add(arguments[++index]);
                        break;
                    case "-r":
                        report = true;
                        break;
                    case "-c":
                        objectOnly = true;
                        break;
                    case "-O":
                        optimized = true;
                        break;
                    default:
                        if (argument.StartsWith('-'))
                        {
                            // The released compiler says this of a flag it does not have, and ends with status 0.
                            return new ToolRun(0, $"👉  Flag could not be matched: {argument.TrimStart('-')}\n", "");
                        }

                        main = argument;
                        break;
                }
            }

            if (main is null)
            {
                return new ToolRun(0, "👉  Option 'file' is required\n", "");
            }

            if (!objectOnly)
            {
                return Refused("🚨 error: the made-up compiler was asked to link or to make an archive, and pmj never asks a compiler for either");
            }

            if (!File.Exists(main))
            {
                return Refused($"🚨 error: File {main} couldn't be read.");
            }

            // The main file and everything it includes, each by its path from the main file's directory.
            var home = Path.GetDirectoryName(main)!;
            var sources = new List<(string Path, string Text)>();
            var toRead = new Queue<string>([main]);
            while (toRead.TryDequeue(out var path))
            {
                if (!File.Exists(path))
                {
                    return Refused($"🚨 error: File {path} couldn't be read.");
                }

                var text = File.ReadAllText(path);
                sources.Add((Path.GetRelativePath(home, path).Replace(Path.DirectorySeparatorChar, '/'), text));
                foreach (Match include in Included().Matches(text))
                {
                    toRead.Enqueue(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, include.Groups[1].Value)));
                }
            }

            // Where a package is looked for, in the compiler's own order: Options.cpp:88-100.
            var places = new List<string>([.. search, Path.Combine(workingDirectory, "packages")]);
            if (variable("EMOJICODE_PACKAGES_PATH") is { Length: > 0 } fromEnvironment)
            {
                places.Add(fromEnvironment);
            }

            places.Add(BuiltInPackages);

            var direct = sources.SelectMany(source => Imported().Matches(source.Text).Select(match => match.Groups[1].Value)).Distinct(StringComparer.Ordinal).ToList();
            var loaded = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var toLoad = new Queue<(string Name, string From)>(direct.Select(name => (name, main + ":1:0")));
            while (toLoad.TryDequeue(out var import))
            {
                if (loaded.ContainsKey(import.Name))
                {
                    continue;
                }

                if (places.Select(place => Path.Combine(place, import.Name)).FirstOrDefault(Directory.Exists) is not { } found)
                {
                    return Refused($"{import.From}: 🚨 error: Could not find package {import.Name}.\nℹ️ note: Searched in:\n{string.Join('\n', places)}");
                }

                var read = Path.Combine(found, "🏛");
                if (!File.Exists(read))
                {
                    return Refused($"{import.From}: 🚨 error: File {read} couldn't be read.");
                }

                var declared = File.ReadAllText(read);
                loaded[import.Name] = Short(declared);
                foreach (Match further in Imported().Matches(declared))
                {
                    toLoad.Enqueue((further.Groups[1].Value, read + ":1:0"));
                }
            }

            var warnings = new StringBuilder();
            var marks = sources.SelectMany(source => source.Text.Split('\n').Select((line, index) => (source.Path, Line: line.Trim(), Number: index + 1))).ToList();

            // What the code has the compiler write to its output, whether or not it then compiles.
            var shouted = string.Concat(marks.Where(mark => mark.Line.StartsWith("📣 ", StringComparison.Ordinal)).Select(mark => mark.Line[3..] + "\n"));
            foreach (var (path, line, number) in marks)
            {
                var place = $"{Path.Combine(home, path)}:{number}:1: ";
                if (line.StartsWith("💥 ", StringComparison.Ordinal))
                {
                    return new ToolRun(1, shouted, $"{place}🚨 error: {line[3..]}\n  {line}\n  ⬆️\n\n");
                }

                if (line == "💣")
                {
                    return new ToolRun(70, "💣 The compiler crashed due to an internal problem: made up\nPlease report this message and the code that you were trying to compile as an issue on GitHub.\n", "");
                }

                if (line == "👻")
                {
                    return new ToolRun(0, "", "");
                }

                if (line.StartsWith("⚠️ ", StringComparison.Ordinal))
                {
                    warnings.Append(place).Append("⚠️  warning: ").Append(line[3..]).Append('\n');
                }
            }

            var name = package ?? "_";
            var made = new List<string> { "made-up object", $"package {name}", $"optimized {(optimized ? "yes" : "no")}" };
            made.AddRange(sources.Select(source => $"source {source.Path} {Short(source.Text)}"));
            made.AddRange(loaded.Select(import => $"import {import.Key} {import.Value}"));
            made.AddRange(marks.SelectMany(mark => Said().Matches(mark.Line)).Select(match => $"says {match.Groups[1].Value}"));
            made.AddRange(marks.Where(mark => mark.Line.StartsWith("🚪 ", StringComparison.Ordinal)).Select(mark => $"exit {mark.Line[3..]}"));

            var objectPath = output ?? Path.Combine(home, Path.GetFileNameWithoutExtension(main) + ".o");
            var outDirectory = Path.GetDirectoryName(objectPath)!;
            if (!Directory.Exists(outDirectory))
            {
                return Refused($"🚨 error: Could not write {objectPath}.");
            }

            File.WriteAllLines(objectPath, made);
            if (package is not null && marks.All(mark => mark.Line != "🙈"))
            {
                var declares = interfacePath ?? Path.Combine(outDirectory, "🏛");
                if (!Directory.Exists(Path.GetDirectoryName(declares)))
                {
                    return Refused($"🚨 error: Could not write {declares}.");
                }

                // An interface begins with what its own package imports, which is why whatever imports a package has to find those too.
                File.WriteAllLines(declares, [.. direct.Select(import => $"📦 {import} 🏠"), $"💭 made-up interface of {package}", .. sources.Select(source => $"💭 source {source.Path} {Short(source.Text)}")]);
            }

            if (report)
            {
                File.WriteAllText(Path.Combine(outDirectory, "documentation.json"), $"{{\n    \"documentation\": \"made up for {name}\",\n    \"types\": []\n}}");
            }

            var verifier = marks.FirstOrDefault(mark => mark.Line.StartsWith("🧨 ", StringComparison.Ordinal)).Line;
            var detected = verifier is null ? "" : $"Call parameter type does not match function signature!\nDetected in: {name}\n";
            return new ToolRun(0, shouted + (report ? "\n" : "") + (verifier == "🧨 out" ? detected : ""), warnings + (verifier == "🧨 err" ? detected : ""));
        }

        private static ToolRun CompileNative(string tool, IReadOnlyList<string> arguments)
        {
            string? source = null, output = null;
            var (flags, includes) = (new List<string>(), new List<string>());
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (argument == "-o")
                {
                    output = arguments[++index];
                }
                else if (argument == "-I")
                {
                    includes.Add(arguments[++index]);
                }
                else if (argument.StartsWith('-'))
                {
                    flags.Add(argument);
                }
                else
                {
                    source = argument;
                }
            }

            if (source is null || output is null || !File.Exists(source))
            {
                return Refused($"{tool}: fatal error: no input files");
            }

            var text = File.ReadAllText(source);
            if (text.Contains("#error", StringComparison.Ordinal))
            {
                return Refused($"{source}:1:2: error: #error made up");
            }

            var found = new List<string>();
            foreach (Match header in IncludedHeader().Matches(text))
            {
                if (includes.Prepend(Path.GetDirectoryName(source)!).FirstOrDefault(directory => File.Exists(Path.Combine(directory, header.Groups[1].Value))) is not { } directory)
                {
                    return Refused($"{source}:1:10: fatal error: {header.Groups[1].Value}: No such file or directory");
                }

                found.Add($"include {header.Groups[1].Value} from {Path.GetFileName(directory)}");
            }

            if (!Directory.Exists(Path.GetDirectoryName(output)))
            {
                return Refused($"{tool}: fatal error: cannot write {output}");
            }

            File.WriteAllLines(output, ["made-up native object", $"language {tool}", $"flags {string.Join(' ', flags)}", $"source {Path.GetFileName(source)} {Short(text)}", .. found]);
            return new ToolRun(0, "", text.Contains("#warning", StringComparison.Ordinal) ? $"{source}:1:2: warning: #warning made up\n" : "");
        }

        private static ToolRun Archive(IReadOnlyList<string> arguments)
        {
            if (arguments.Count < 3 || arguments[0] != "rcs")
            {
                return Refused("made-up ar: asked for something other than rcs, an archive and its members");
            }

            var (archive, members) = (arguments[1], arguments.Skip(2).ToList());
            if (File.Exists(archive))
            {
                return Refused($"made-up ar: {archive} is there already, and pmj makes a new archive every time");
            }

            if (members.FirstOrDefault(member => !File.Exists(member)) is { } missing)
            {
                return Refused($"made-up ar: {missing}: No such file or directory");
            }

            File.WriteAllLines(archive, ["made-up archive", .. members.SelectMany(member => File.ReadAllLines(member).Select(line => "  " + line).Prepend($"member {Path.GetFileName(member)}"))]);
            return new ToolRun(0, "", "");
        }

        private ToolRun Link(IReadOnlyList<string> arguments)
        {
            string? output = null;
            var inputs = new List<(string Path, bool Grouped)>();
            var libraries = new List<string>();
            var grouped = false;
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                if (argument is "-Wl,--start-group" or "-Wl,--end-group")
                {
                    if (MacLinker)
                    {
                        return Refused("ld: unknown options: --start-group --end-group \nclang: error: linker command failed with exit code 1 (use -v to see invocation)");
                    }

                    grouped = argument == "-Wl,--start-group";
                }
                else if (argument == "-o")
                {
                    output = arguments[++index];
                }
                else if (argument.StartsWith("-l", StringComparison.Ordinal))
                {
                    libraries.Add(argument[2..]);
                }
                else if (argument.StartsWith('-'))
                {
                    return Refused($"made-up linker: unrecognized option '{argument}'");
                }
                else
                {
                    inputs.Add((argument, grouped));
                }
            }

            if (output is null || inputs.Any(input => !File.Exists(input.Path)))
            {
                return Refused("made-up linker: no output named, or an input that is not there");
            }

            // A linker reads an archive where it stands and takes what is needed by then. So what an
            // archive needs has to come after it, unless they are in one group, which is read until
            // nothing more is taken. The linker of macOS reads every archive that way.
            var needed = new Queue<(string Name, int From)>();
            void Need(int from) =>
                File.ReadAllLines(inputs[from].Path).Select(line => line.Trim()).Where(line => line.StartsWith("import ", StringComparison.Ordinal)).ToList().ForEach(line => needed.Enqueue((line.Split(' ')[1], from)));
            for (var index = 0; index < inputs.Count; index++)
            {
                if (inputs[index].Path.EndsWith(".o", StringComparison.Ordinal))
                {
                    Need(index);
                }
            }

            var taken = new HashSet<int>();
            while (needed.TryDequeue(out var need))
            {
                var at = inputs.FindIndex(input => Path.GetFileName(input.Path) == $"lib{need.Name}.a");
                var reachable = at >= 0 && (at > need.From || MacLinker || (inputs[at].Grouped && inputs[need.From].Grouped));
                if (!reachable)
                {
                    return Refused($"/usr/bin/ld: {inputs[need.From].Path}: undefined reference to `{need.Name}_class_info'\ncollect2: error: ld returned 1 exit status");
                }

                if (taken.Add(at))
                {
                    Need(at);
                }
            }

            if (new[] { "libs.a", "libruntime.a" }.FirstOrDefault(stock => inputs.All(input => Path.GetFileName(input.Path) != stock)) is { } absent)
            {
                return Refused($"/usr/bin/ld: undefined reference to what {absent} holds\ncollect2: error: ld returned 1 exit status");
            }

            if (libraries.FirstOrDefault(MissingLibraries.Contains) is { } unfound)
            {
                return Refused($"/usr/bin/ld: cannot find -l{unfound}: No such file or directory\ncollect2: error: ld returned 1 exit status");
            }

            if (!Directory.Exists(Path.GetDirectoryName(output)))
            {
                return Refused($"made-up linker: cannot write {output}");
            }

            var words = inputs.Where(input => input.Path.EndsWith(".o", StringComparison.Ordinal)).SelectMany(input => File.ReadAllLines(input.Path)).Where(line => line.StartsWith("says ", StringComparison.Ordinal) || line.StartsWith("exit ", StringComparison.Ordinal));
            File.WriteAllLines(output, ["made-up program", .. inputs.Select(input => $"linked {Path.GetFileName(input.Path)}"), .. libraries.Select(library => $"library {library}"), .. words]);
            return new ToolRun(0, "", "");
        }

        // A program says what its sources told it to say, then what it was given, and ends as they told it to.
        private ToolRun Program(string program, IReadOnlyList<string> arguments)
        {
            var lines = File.ReadAllLines(program);
            foreach (var line in lines.Where(line => line.StartsWith("says ", StringComparison.Ordinal)))
            {
                ProgramOutput.WriteLine(line[5..]);
            }

            if (arguments.Count > 0)
            {
                ProgramOutput.WriteLine("given: " + string.Join('|', arguments));
            }

            var exit = lines.LastOrDefault(line => line.StartsWith("exit ", StringComparison.Ordinal));
            return new ToolRun(exit is null ? 0 : int.Parse(exit[5..], System.Globalization.CultureInfo.InvariantCulture), "", "");
        }

        [GeneratedRegex("📜 🔤(.+?)🔤")]
        private static partial Regex Included();

        [GeneratedRegex(@"^[ \t]*📦[ \t]+(\S+)[ \t]+\S+", RegexOptions.Multiline)]
        private static partial Regex Imported();

        [GeneratedRegex("😀 🔤(.*?)🔤")]
        private static partial Regex Said();

        [GeneratedRegex("#include \"(.+?)\"")]
        private static partial Regex IncludedHeader();
    }
}
