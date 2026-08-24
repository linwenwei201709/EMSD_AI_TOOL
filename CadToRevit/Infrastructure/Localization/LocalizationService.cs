using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Xml.Linq;

namespace CadToRevit.Infrastructure.Localization
{
    /// <summary>
    /// Provides global localization access backed by .resx resources.
    /// </summary>
    public static class LocalizationService
    {
        private static readonly object SyncRoot = new object();
        private static readonly HashSet<string> MissingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, string>> ResxCache =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ResourceManager ResourceManager =
            new ResourceManager("CadToRevit.Resources.Strings", Assembly.GetExecutingAssembly());

        public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("en");

        public static void Initialize(string cultureName)
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(cultureName) ? "en" : cultureName);
            }
            catch
            {
                culture = CultureInfo.GetCultureInfo("en");
            }

            CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        public static string T(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string value = null;
            try
            {
                value = ResourceManager.GetString(key, CurrentCulture);
                if (!string.IsNullOrEmpty(value))
                {
                    return NormalizeResourceText(value);
                }

                value = ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"));
                if (!string.IsNullOrEmpty(value))
                {
                    return NormalizeResourceText(value);
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Loc] Resource lookup failed. Key=" + key + ", Error=" + ex.Message);
            }

            value = TryGetFromResxFiles(key, CurrentCulture);
            if (!string.IsNullOrEmpty(value))
            {
                return NormalizeResourceText(value);
            }

            value = TryGetFromResxFiles(key, CultureInfo.GetCultureInfo("en"));
            if (!string.IsNullOrEmpty(value))
            {
                return NormalizeResourceText(value);
            }

            RecordMissingKey(key);
            return key;
        }

        public static string T(string key, params object[] args)
        {
            string format = T(key);
            if (args == null || args.Length == 0)
            {
                return format;
            }

            try
            {
                return string.Format(CurrentCulture, format, args);
            }
            catch
            {
                return format;
            }
        }

        private static void RecordMissingKey(string key)
        {
            lock (SyncRoot)
            {
                if (MissingKeys.Contains(key))
                {
                    return;
                }

                MissingKeys.Add(key);
            }

            DiagnosticRecorder.AppendDebug("[Loc] Missing key: " + key);
        }

        private static string NormalizeResourceText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Replace("\\n", Environment.NewLine);
        }

        private static string TryGetFromResxFiles(string key, CultureInfo culture)
        {
            foreach (string path in GetFallbackResxPaths(culture))
            {
                Dictionary<string, string> map = LoadResxMap(path);
                if (map != null && map.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetFallbackResxPaths(CultureInfo culture)
        {
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string resourcesFolder = Path.Combine(assemblyFolder, "Resources");
            string cultureName = culture != null ? (culture.Name ?? string.Empty) : string.Empty;

            if (!string.IsNullOrWhiteSpace(cultureName))
            {
                yield return Path.Combine(resourcesFolder, "Strings." + cultureName + ".resx");
            }

            if (culture != null && !string.IsNullOrWhiteSpace(culture.TwoLetterISOLanguageName))
            {
                yield return Path.Combine(resourcesFolder, "Strings." + culture.TwoLetterISOLanguageName + ".resx");
            }

            yield return Path.Combine(resourcesFolder, "Strings.resx");
        }

        private static Dictionary<string, string> LoadResxMap(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            lock (SyncRoot)
            {
                if (ResxCache.TryGetValue(path, out Dictionary<string, string> cached))
                {
                    return cached;
                }
            }

            try
            {
                XDocument doc = XDocument.Load(path);
                Dictionary<string, string> result = doc
                    .Root?
                    .Elements("data")
                    .Where(x => x.Attribute("name") != null)
                    .GroupBy(x => (string)x.Attribute("name"), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => (string)x.Element("value")).FirstOrDefault() ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                lock (SyncRoot)
                {
                    ResxCache[path] = result;
                }

                return result;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Loc] Resx fallback load failed. Path=" + path + ", Error=" + ex.Message);
                return null;
            }
        }
    }
}
