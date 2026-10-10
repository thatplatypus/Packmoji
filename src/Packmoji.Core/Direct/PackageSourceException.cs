using Packmoji.Core.Diagnostics;

namespace Packmoji.Core.Direct
{
    /// <summary>
    /// What a package source throws when it could not find out: the network failed, GitHub refused,
    /// or a release is there and is not what it claims to be. It carries the diagnostic to show, so
    /// that the one who catches it has nothing to work out.
    /// </summary>
    public sealed class PackageSourceException : Exception
    {
        public PackageSourceException(Diagnostic diagnostic)
            : base(diagnostic?.Message)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            Diagnostic = diagnostic;
        }

        public Diagnostic Diagnostic { get; }
    }
}
