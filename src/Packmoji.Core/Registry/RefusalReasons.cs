using System.Globalization;
using Packmoji.Core.Archives;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// Why a release that claims to be a version of a package is refused, as it is recorded and
    /// shown. Each is a sentence of the registry's own, and they are all here, so that the guide can
    /// be held to them. Where one ends with what was found, that is a name, a version or a
    /// repository that was read and found well formed: nothing else from inside an archive is
    /// ever part of a reason, for an archive is a stranger's and a reason is shown to people.
    /// </summary>
    internal static class RefusalReasons
    {
        public const string NotAnArchive = "its file is not an archive as pmj pack writes one";
        public const string ManifestUnreadable = "the packmoji.json in its archive cannot be read as a manifest";
        public const string NotTheListedDigest = "what GitHub serves for it does not have the digest that GitHub lists for it";

        /// <summary>Ends with the package whose manifest it is.</summary>
        public const string OtherPackage = "the manifest in its archive is that of {0}";

        /// <summary>Ends with the version the manifest gives.</summary>
        public const string OtherVersion = "the manifest in its archive gives the version {0}";

        /// <summary>Ends with the repository the manifest names.</summary>
        public const string OtherRepository = "the manifest in its archive says that the package lives in {0}";

        /// <summary>Ends with the repository the version was indexed from.</summary>
        public const string AlreadyIndexed = "this version is already in the registry, from {0}";

        /// <summary>Holds the name.</summary>
        public const string ReservedName = "the name {0} is one of the compiler's own packages";

        public static string TooLarge { get; } = $"its file is larger than {PackageArchive.MaxBytes.ToString(CultureInfo.InvariantCulture)} bytes";

        /// <summary>Every reason there is, with the code it is recorded under. A reason that says what was found has <c>{0}</c> where that goes.</summary>
        public static IReadOnlyList<(string Code, string Words)> All { get; } =
        [
            (DiagnosticCodes.ArchiveInvalid, NotAnArchive),
            (DiagnosticCodes.ArchiveInvalid, TooLarge),
            (DiagnosticCodes.ReleaseInvalid, ManifestUnreadable),
            (DiagnosticCodes.ReleaseInvalid, OtherPackage),
            (DiagnosticCodes.ReleaseInvalid, OtherVersion),
            (DiagnosticCodes.ReleaseInvalid, OtherRepository),
            (DiagnosticCodes.ReleaseInvalid, AlreadyIndexed),
            (DiagnosticCodes.ReleaseInvalid, NotTheListedDigest),
            (DiagnosticCodes.NameReserved, ReservedName),
        ];

        /// <summary>A reason that says what was found, with it.</summary>
        public static string Of(string reason, object found) => string.Format(CultureInfo.InvariantCulture, reason, found);
    }
}
