using Packmoji.Core.Diagnostics;
using Packmoji.Core.Identity;
using Packmoji.Core.Json;
using Packmoji.Core.Lockfiles;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Reads the file of what was refused in a repository. As with a package's file, a key that is
    /// not known is passed over and a key that is known has to be well formed. This file is shown to
    /// people, and what a release holds is a stranger's text, so a reason that is more than one line
    /// of text that can be seen is not read.
    /// </summary>
    public static class RefusedReleasesReader
    {
        /// <summary>The most characters a reason may be.</summary>
        public const int MaxWhyLength = 300;

        private const string KeeperFix = RegistryPackageReader.KeeperFix;

        /// <param name="expected">The repository whose file this is taken to be, by the path it was read from.</param>
        /// <param name="file">The file's path, for the place a diagnostic gives.</param>
        public static ReadResult<RefusedReleases> Read(ReadOnlyMemory<byte> utf8, RepositoryRef expected, string file)
        {
            ArgumentNullException.ThrowIfNull(expected);
            ArgumentNullException.ThrowIfNull(file);
            if (utf8.Length > RegistryPackageReader.MaxBytes)
            {
                return ReadResult<RefusedReleases>.Failure(
                    [
                        new Diagnostic(
                            DiagnosticCodes.FileTooLarge,
                            $"\"{file}\" is too large to be a file of what a registry refused.",
                            $"a file of a registry is at most {RegistryPackageReader.MaxBytes} bytes, which is 4 MiB",
                            KeeperFix,
                            new SourceLocation(file, 1, 1)),
                    ],
                    0);
            }

            var diagnostics = new DiagnosticList();
            var refused = ReadRefused(utf8, expected, file, diagnostics);
            return diagnostics.Count > 0 || refused is null
                ? ReadResult<RefusedReleases>.Failure(diagnostics.Select(diagnostic => diagnostic with { Fix = KeeperFix }).ToList(), diagnostics.Omitted)
                : ReadResult<RefusedReleases>.Success(refused);
        }

        private static RefusedReleases? ReadRefused(ReadOnlyMemory<byte> utf8, RepositoryRef expected, string file, DiagnosticList diagnostics)
        {
            var tree = JsonTreeReader.Read(utf8, file, KeeperFix, diagnostics);
            if (tree is null)
            {
                return null;
            }

            if (tree.Kind != JsonKind.Object)
            {
                diagnostics.Add(JsonDiagnostics.RootNotObject(tree));
                return null;
            }

            var root = new JsonObjectReader(tree, diagnostics);
            var formatItem = root.Required("version", JsonKind.Number, "\"version\": 1");
            if (formatItem is not null && formatItem.Text != "1")
            {
                diagnostics.Clear();
                diagnostics.Add(Rule(
                    formatItem,
                    $"The file says it is of version {formatItem.Text} of the format.",
                    $"this pmj reads version {RegistryPackage.FormatVersion}, which is the only one that a directory named v1 holds"));
                return null;
            }

            var repositoryItem = root.Required("repository", JsonKind.String, $"\"repository\": \"{expected}\"");
            var releasesItem = root.Required("releases", JsonKind.Array, "\"releases\": []");

            var repository = repositoryItem.Parse<RepositoryRef>(RepositoryRef.TryParse, diagnostics);
            if (repository is not null && repository != expected)
            {
                diagnostics.Add(Rule(
                    repositoryItem!,
                    $"The file is that of {repository}.",
                    $"it was read as the file of {expected}, and a repository's file is at the path that its name gives"));
            }

            var releases = new List<RefusedRelease>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in releasesItem?.Items ?? [])
            {
                if (item.Kind != JsonKind.Object)
                {
                    diagnostics.Add(JsonDiagnostics.WrongType("An entry of \"releases\"", JsonKind.Object, item, removable: false));
                }
                else if (ReadRelease(item, seen, diagnostics) is { } release)
                {
                    releases.Add(release);
                }
            }

            return diagnostics.Count > 0 || repository is null || releasesItem is null
                ? null
                : new RefusedReleases(repository, releases.OrderBy(release => release.Tag, StringComparer.Ordinal).ToList());
        }

        private static RefusedRelease? ReadRelease(JsonItem item, HashSet<string> seen, DiagnosticList diagnostics)
        {
            var reader = new JsonObjectReader(item, diagnostics);
            var tagItem = reader.Required("tag", JsonKind.String, "\"tag\": \"name-v1.0.0\"");
            var sha256Item = reader.Required("sha256", JsonKind.String, "\"sha256\": \"<64 hexadecimal digits>\"");
            var seenItem = reader.Required("seen", JsonKind.String, $"\"seen\": \"{RegistryTime.Example}\"");
            var codeItem = reader.Required("code", JsonKind.String, $"\"code\": \"{DiagnosticCodes.ReleaseInvalid}\"");
            var whyItem = reader.Required("why", JsonKind.String, "\"why\": \"<why the release was refused>\"");

            string? tag = null;
            if (tagItem is not null)
            {
                if (!ReleaseTag.TryParse(tagItem.Text, out _, out _))
                {
                    diagnostics.Add(Rule(tagItem, $"\"{tagItem.Text}\" is not the tag of a version of a package.", "such a tag is the package's name, -v, and the version, as in crypto-v1.0.0"));
                }
                else if (!seen.Add(tagItem.Text))
                {
                    diagnostics.Add(Rule(tagItem, $"The tag {tagItem.Text} is there twice.", "a release has one entry in a repository's file"));
                }
                else
                {
                    tag = tagItem.Text;
                }
            }

            var sha256 = sha256Item.Parse<Sha256Digest>(Sha256Digest.TryParse, diagnostics);

            DateTimeOffset? when = null;
            if (seenItem is not null)
            {
                if (RegistryTime.TryParse(seenItem.Text, out var time))
                {
                    when = time;
                }
                else
                {
                    diagnostics.Add(Rule(seenItem, $"\"{seenItem.Text}\" is not a time as a registry writes one.", $"a time is written in UTC, to the second, as {RegistryTime.Example}"));
                }
            }

            if (codeItem is not null && !IsCode(codeItem.Text))
            {
                diagnostics.Add(Rule(codeItem, $"\"{codeItem.Text}\" is not written as a code.", $"a code is two words in lowercase with a full stop between them, as in {DiagnosticCodes.ReleaseInvalid}"));
                codeItem = null;
            }

            if (whyItem is not null && !IsFitToShow(whyItem.Text))
            {
                diagnostics.Add(Rule(
                    whyItem,
                    "The reason a release was refused is not fit to show.",
                    $"a reason is one line of 1 to {MaxWhyLength} characters, with no space at either end and no character that cannot be seen"));
                whyItem = null;
            }

            return tag is null || sha256 is null || when is null || codeItem is null || whyItem is null
                ? null
                : new RefusedRelease(tag, sha256, when.Value, codeItem.Text, whyItem.Text);
        }

        /// <summary>Whether text is written as a code is: two words with a full stop between them, each of lowercase letters and digits with single hyphens inside.</summary>
        public static bool IsCode(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            var parts = text.Split('.');
            return parts.Length == 2 && parts.All(IsWord);
        }

        /// <summary>Whether text may stand as the reason a release was refused: one line that can be seen, of a length a person reads.</summary>
        public static bool IsFitToShow(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            return text.Length is > 0 and <= MaxWhyLength
                && !char.IsWhiteSpace(text[0])
                && !char.IsWhiteSpace(text[^1])
                && !TextSafety.HasUnsafe(text);
        }

        private static bool IsWord(string word) =>
            word.Length > 0
            && word[0] != '-'
            && word[^1] != '-'
            && !word.Contains("--", StringComparison.Ordinal)
            && word.All(character => Ascii.IsLowerLetter(character) || Ascii.IsDigit(character) || character == '-');

        private static Diagnostic Rule(JsonItem at, string message, string reason) =>
            new(DiagnosticCodes.RegistryInvalid, message, reason, KeeperFix, at.Location);
    }
}
