using System.Globalization;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// How a time is written in a registry's file: in UTC, to the second, as <c>2026-10-11T15:02:11Z</c>.
    /// There is one spelling, so that the same registry is the same bytes whoever writes it.
    /// </summary>
    internal static class RegistryTime
    {
        public const string Example = "2026-10-11T15:02:11Z";

        private const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static string Write(DateTimeOffset time) => time.UtcDateTime.ToString(Format, CultureInfo.InvariantCulture);

        public static bool TryParse(string text, out DateTimeOffset time) =>
            DateTimeOffset.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out time);
    }
}
