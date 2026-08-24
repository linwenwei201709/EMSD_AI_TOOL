using Autodesk.Revit.ApplicationServices;

namespace CadToRevit.Infrastructure.Localization
{
    /// <summary>
    /// Maps Revit language values to .NET culture names.
    /// </summary>
    public static class RevitLanguageMapper
    {
        public static string MapToCultureName(LanguageType language)
        {
            switch (language)
            {
                case LanguageType.English_USA:
                    return "en";
                case LanguageType.Chinese_Simplified:
                    return "zh-Hans";
                case LanguageType.Chinese_Traditional:
                    return "zh-Hant";
                default:
                    return "en";
            }
        }
    }
}
