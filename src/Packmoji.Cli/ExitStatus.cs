namespace Packmoji.Cli
{
    /// <summary>
    /// How pmj ends. These are part of what pmj promises to scripts and to other tools, so they stay
    /// few, and none of them changes its meaning.
    /// </summary>
    public static class ExitStatus
    {
        /// <summary>What was asked for was done. A warning does not change this.</summary>
        public const int Success = 0;

        /// <summary>A problem was found and reported, and nothing was left half done.</summary>
        public const int Problem = 1;

        /// <summary>The command line could not be read: a command or an option pmj does not have, or an argument that is missing.</summary>
        public const int Usage = 2;

        /// <summary>pmj itself failed. This is a fault in pmj and is worth reporting.</summary>
        public const int InternalError = 70;

        /// <summary>pmj was stopped before it had finished, as by Ctrl+C. It is the status a shell gives a program that an interrupt ended.</summary>
        public const int Interrupted = 130;
    }
}
