using System.Diagnostics.CodeAnalysis;
using Packmoji.Core.Manifests;
using Packmoji.Core.Resolution;

namespace Packmoji.Core.Direct
{
    /// <summary>What <see cref="ReleaseRule"/> found of one release: the version it is, or why it is not that version.</summary>
    public sealed class ReleaseVerdict
    {
        private ReleaseVerdict(PublishedVersion? published, ReleaseFault? fault, string reason, Manifest? manifest)
        {
            Published = published;
            Fault = fault;
            Reason = reason;
            Manifest = manifest;
        }

        /// <summary>The version, when the release was taken.</summary>
        public PublishedVersion? Published { get; }

        /// <summary>Why the release was refused. Null when it was taken.</summary>
        public ReleaseFault? Fault { get; }

        /// <summary>
        /// Why it was refused, as the end of a sentence. Of an archive or a manifest that cannot be
        /// read, this is what its reader said, and so may repeat what the file holds: a stranger's
        /// text. Empty when the release was taken.
        /// </summary>
        public string Reason { get; }

        /// <summary>The manifest in the archive, when it could be read. What it gives has been read and found well formed.</summary>
        public Manifest? Manifest { get; }

        [MemberNotNullWhen(true, nameof(Published), nameof(Manifest))]
        [MemberNotNullWhen(false, nameof(Fault))]
        public bool Taken => Published is not null;

        internal static ReleaseVerdict Of(PublishedVersion published, Manifest manifest) => new(published, null, "", manifest);

        internal static ReleaseVerdict Refused(ReleaseFault fault, string reason, Manifest? manifest = null) => new(null, fault, reason, manifest);
    }
}
