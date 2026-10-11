using Packmoji.Core.Resolution;

namespace Packmoji.Core.Registry
{
    /// <summary>
    /// How a status is spelled in a registry's file, in the one place its reader and its writer both
    /// use. These three are all the first format will ever hold: a pmj that met a fourth would not
    /// know whether the version may be used.
    /// </summary>
    internal static class RegistryStatuses
    {
        public const string Active = "active";
        public const string Yanked = "yanked";
        public const string Quarantined = "quarantined";

        public static string Name(VersionStatus status) => status switch
        {
            VersionStatus.Yanked => Yanked,
            VersionStatus.Quarantined => Quarantined,
            _ => Active,
        };

        public static bool TryParse(string text, out VersionStatus status)
        {
            status = text switch
            {
                Yanked => VersionStatus.Yanked,
                Quarantined => VersionStatus.Quarantined,
                _ => VersionStatus.Active,
            };
            return text is Active or Yanked or Quarantined;
        }
    }
}
