namespace CadToRevit.Infrastructure.Localization
{
    /// <summary>
    /// Short alias for localization access.
    /// </summary>
    public static class Loc
    {
        public static string T(string key)
        {
            return LocalizationService.T(key);
        }

        public static string T(string key, params object[] args)
        {
            return LocalizationService.T(key, args);
        }
    }
}
