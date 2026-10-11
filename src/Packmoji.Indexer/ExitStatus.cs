namespace Packmoji.Indexer
{
    /// <summary>How a command of the indexer ends. A workflow reads this and nothing else to know whether to go on.</summary>
    internal static class ExitStatus
    {
        public const int Success = 0;

        /// <summary>The command could not do what it was asked, or what it was asked to hold does not hold.</summary>
        public const int Failure = 1;

        /// <summary>The command was not asked as it is written.</summary>
        public const int Misused = 2;
    }
}
