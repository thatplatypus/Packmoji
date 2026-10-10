using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Lockfiles
{
    /// <summary>
    /// The SHA-256 of a package's archive, as 64 hexadecimal digits in lowercase. There is one
    /// spelling, so that two digests are the same exactly when their texts are.
    /// </summary>
    public sealed record Sha256Digest
    {
        public const int Length = 64;

        private Sha256Digest(string hex)
        {
            Hex = hex;
        }

        public string Hex { get; }

        public override string ToString() => Hex;

        /// <summary>The digest of some bytes, which is how an archive is known to be the one that was locked.</summary>
        public static Sha256Digest Of(ReadOnlySpan<byte> bytes) => new(Convert.ToHexStringLower(SHA256.HashData(bytes)));

        public static bool TryParse(string text, [NotNullWhen(true)] out Sha256Digest? digest, [NotNullWhen(false)] out Diagnostic? error)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (IsDigest(text))
            {
                digest = new Sha256Digest(text);
                error = null;
                return true;
            }

            var lowered = text.ToLowerInvariant();
            digest = null;
            error = new Diagnostic(
                DiagnosticCodes.Sha256Invalid,
                $"\"{text}\" is not a SHA-256 digest.",
                "a digest is 64 hexadecimal digits in lowercase",
                lowered != text && IsDigest(lowered)
                    ? $"write it in lowercase: \"{lowered}\""
                    : "write the 64 hexadecimal digits of the archive's SHA-256");
            return false;
        }

        private static bool IsDigest(string text) => text.Length == Length && text.All(Ascii.IsLowerHexDigit);
    }
}
