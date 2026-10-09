namespace Packmoji.Core
{
    /// <summary>
    /// Character tests that mean ASCII and nothing else. <c>char.IsDigit</c> and <c>char.IsLetter</c>
    /// accept the digits and letters of every script, which would let a name or a version be spelled
    /// with characters that only look like the ones its grammar names.
    /// </summary>
    internal static class Ascii
    {
        public static bool IsDigit(char c) => c is >= '0' and <= '9';

        public static bool IsLowerLetter(char c) => c is >= 'a' and <= 'z';

        public static bool IsUpperLetter(char c) => c is >= 'A' and <= 'Z';

        public static bool IsLetter(char c) => IsLowerLetter(c) || IsUpperLetter(c);

        public static bool IsLowerHexDigit(char c) => IsDigit(c) || c is >= 'a' and <= 'f';
    }
}
