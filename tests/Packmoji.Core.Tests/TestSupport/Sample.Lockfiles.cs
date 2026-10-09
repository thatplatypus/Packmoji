using Packmoji.Core.Lockfiles;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        /// <summary>A digest of one hexadecimal digit repeated, which is enough to tell two apart.</summary>
        public static Sha256Digest Sha(char fill)
        {
            Sha256Digest.TryParse(new string(fill, Sha256Digest.Length), out var digest, out var error).ShouldBeTrue(error?.Reason);
            return digest!;
        }
    }
}
