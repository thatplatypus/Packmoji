namespace Packmoji.Core.Building
{
    /// <summary>
    /// Says which language a native file is in, by the end of its name alone. A package cannot say it
    /// any other way: a manifest gives a build no flag.
    /// </summary>
    public static class NativeSource
    {
        private static readonly string[] CppEndings = [".cpp", ".cc", ".cxx"];

        /// <summary>The language of a file, or null when its name ends as neither C nor C++ does. Case counts: <c>.C</c> is not <c>.c</c>.</summary>
        public static NativeLanguage? LanguageOf(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            if (path.EndsWith(".c", StringComparison.Ordinal))
            {
                return NativeLanguage.C;
            }

            return CppEndings.Any(ending => path.EndsWith(ending, StringComparison.Ordinal)) ? NativeLanguage.Cpp : null;
        }
    }
}
