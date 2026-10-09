using Packmoji.Core.Lockfiles;
using Shouldly;

namespace Packmoji.Core.Tests.TestSupport
{
    internal static partial class Sample
    {
        public static Sha256Digest ShaOf(string hex)
        {
            Sha256Digest.TryParse(hex, out var digest, out var error).ShouldBeTrue(error?.Reason);
            return digest!;
        }
    }
}
