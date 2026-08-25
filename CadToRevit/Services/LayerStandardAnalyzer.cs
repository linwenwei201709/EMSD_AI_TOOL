using CadToRevit.Models.Mapping;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace CadToRevit.Services
{
    /// <summary>
    /// CAD layer standard analyzer.
    /// The original EMSD rule remains the immutable built-in default. When a custom
    /// profile is active, exact layer-name mappings from the user profile are used.
    /// </summary>
    public static class LayerStandardAnalyzer
    {
        private static readonly Regex ThreeDigitCodeRegex = new Regex(@"\d{3}", RegexOptions.Compiled);

        private sealed class RuleRange
        {
            public int Min { get; set; }
            public int Max { get; set; }
            public string Label { get; set; }
        }

        private static readonly List<RuleRange> RuleRanges = new List<RuleRange>
        {
            new RuleRange { Min = 20, Max = 29, Label = "Grids" },
            new RuleRange { Min = 30, Max = 39, Label = "Dimensions" },
            new RuleRange { Min = 40, Max = 49, Label = "Text" },
            new RuleRange { Min = 50, Max = 59, Label = "General Symbols" },
            new RuleRange { Min = 70, Max = 79, Label = "Revisions" },
            new RuleRange { Min = 120, Max = 129, Label = "Earthworks" },
            new RuleRange { Min = 210, Max = 229, Label = "Walls / Partitions" },
            new RuleRange { Min = 240, Max = 249, Label = "Stairs / Ramps" },
            new RuleRange { Min = 280, Max = 289, Label = "Structural Elements" },
            new RuleRange { Min = 290, Max = 299, Label = "Parts & Accessories" },
            new RuleRange { Min = 340, Max = 349, Label = "Secondary Elements to Stairs & Ramps" },
            new RuleRange { Min = 350, Max = 359, Label = "Suspended Ceiling" },
            new RuleRange { Min = 370, Max = 379, Label = "Secondary Elements to Roof" },
            new RuleRange { Min = 400, Max = 409, Label = "Vacant" },
            new RuleRange { Min = 410, Max = 429, Label = "Finishes to External Wall/Internal Walls" },
            new RuleRange { Min = 430, Max = 439, Label = "Finishes to Floors" },
            new RuleRange { Min = 450, Max = 459, Label = "Finishes to Ceilings" },
            new RuleRange { Min = 500, Max = 509, Label = "Building Services" },
            new RuleRange { Min = 520, Max = 529, Label = "Drainage" },
            new RuleRange { Min = 530, Max = 539, Label = "Water Supply" },
            new RuleRange { Min = 580, Max = 589, Label = "Fire Services" },
            new RuleRange { Min = 630, Max = 639, Label = "Lighting" },
            new RuleRange { Min = 660, Max = 669, Label = "Transportation" },
            new RuleRange { Min = 710, Max = 719, Label = "Circulation FFE" },
            new RuleRange { Min = 760, Max = 769, Label = "Storage / Screening FFE" },
            new RuleRange { Min = 830, Max = 839, Label = "Traffic Aids & Markings" },
            new RuleRange { Min = 910, Max = 919, Label = "Boundaries" },
            new RuleRange { Min = 920, Max = 929, Label = "Surface Drainage" },
            new RuleRange { Min = 930, Max = 939, Label = "Sewage" },
            new RuleRange { Min = 970, Max = 979, Label = "External Structures" },
            new RuleRange { Min = 980, Max = 989, Label = "Landscape" },
            new RuleRange { Min = 990, Max = 999, Label = "External Accessories" }
        };

        public static LayerStandardAnalyzeResult AnalyzeLayers(IEnumerable<string> rawLayerNames)
        {
            LayerRuleProfile activeRule = LayerRuleProfileStoreService.GetActiveRule();
            if (activeRule != null && !activeRule.IsBuiltIn)
            {
                return AnalyzeCustomRule(rawLayerNames, activeRule);
            }

            return AnalyzeBuiltInRule(rawLayerNames);
        }

        public static List<string> BuildRuleDescriptions()
        {
            LayerRuleProfile activeRule = LayerRuleProfileStoreService.GetActiveRule();
            if (activeRule != null && !activeRule.IsBuiltIn)
            {
                return BuildCustomRuleDescriptions(activeRule);
            }

            return BuildBuiltInRuleDescriptions();
        }

        public static List<string> BuildBuiltInRuleDescriptions()
        {
            return RuleRanges
                .Select(x => x.Min.ToString("D3") + "-" + x.Max.ToString("D3") + ": " + x.Label)
                .ToList();
        }

        private static LayerStandardAnalyzeResult AnalyzeBuiltInRule(IEnumerable<string> rawLayerNames)
        {
            List<string> layers = NormalizeLayers(rawLayerNames);
            LayerStandardAnalyzeResult result = new LayerStandardAnalyzeResult
            {
                TotalLayers = layers.Count,
                RuleKey = LayerRuleProfileStoreService.BuiltInRuleKey,
                RuleName = "Default Rule - EMSD CAD Layer Code Standard",
                RuleDescriptions = BuildBuiltInRuleDescriptions()
            };

            foreach (string layer in layers)
            {
                string code = ExtractThreeDigitCode(layer);
                int numericCode = 0;
                bool hasCode = !string.IsNullOrWhiteSpace(code) && int.TryParse(code, out numericCode);
                RuleRange matched = hasCode ? RuleRanges.FirstOrDefault(x => numericCode >= x.Min && numericCode <= x.Max) : null;
                bool valid = matched != null;

                LayerStandardMatchItem item = new LayerStandardMatchItem
                {
                    LayerName = layer,
                    Code = hasCode ? code : string.Empty,
                    IsValid = valid,
                    MatchedStandard = valid ? matched.Label : "Unmatched Standard",
                    SuggestedMapCategory = valid
                        ? InferBuiltInMapCategory(layer, matched.Label)
                        : MapCategory.Unknown
                };

                AppendMatch(result, item);
            }

            return result;
        }

        private static LayerStandardAnalyzeResult AnalyzeCustomRule(IEnumerable<string> rawLayerNames, LayerRuleProfile rule)
        {
            List<string> layers = NormalizeLayers(rawLayerNames);
            LayerStandardAnalyzeResult result = new LayerStandardAnalyzeResult
            {
                TotalLayers = layers.Count,
                RuleKey = rule.Key ?? string.Empty,
                RuleName = string.IsNullOrWhiteSpace(rule.Name) ? "Custom Rule" : rule.Name.Trim(),
                RuleDescriptions = BuildCustomRuleDescriptions(rule)
            };

            Dictionary<string, LayerRuleCategoryMapping> exactMap = new Dictionary<string, LayerRuleCategoryMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (LayerRuleCategoryMapping mapping in rule.Mappings ?? new List<LayerRuleCategoryMapping>())
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.Category))
                {
                    continue;
                }

                foreach (string rawName in mapping.LayerNames ?? new List<string>())
                {
                    string name = (rawName ?? string.Empty).Trim();
                    if (name.Length == 0 || exactMap.ContainsKey(name))
                    {
                        continue;
                    }

                    exactMap[name] = mapping;
                }
            }

            foreach (string layer in layers)
            {
                LayerRuleCategoryMapping mapping;
                bool valid = exactMap.TryGetValue(layer, out mapping);
                LayerStandardMatchItem item = new LayerStandardMatchItem
                {
                    LayerName = layer,
                    Code = string.Empty,
                    IsValid = valid,
                    MatchedStandard = valid ? LayerRuleProfileStoreService.GetCategoryDisplayName(mapping.Category) : "Unmatched Standard",
                    SuggestedMapCategory = valid
                        ? LayerRuleProfileStoreService.GetSuggestedMapCategory(mapping.Category)
                        : MapCategory.Unknown
                };

                AppendMatch(result, item);
            }

            return result;
        }

        private static List<string> BuildCustomRuleDescriptions(LayerRuleProfile rule)
        {
            List<string> lines = new List<string>();
            foreach (LayerRuleEditableCategoryDefinition definition in LayerRuleProfileStoreService.EditableCategories)
            {
                LayerRuleCategoryMapping mapping = (rule.Mappings ?? new List<LayerRuleCategoryMapping>())
                    .FirstOrDefault(x => x != null && string.Equals(x.Category, definition.Key, StringComparison.OrdinalIgnoreCase));
                List<string> names = LayerRuleProfileStoreService.NormalizeLayerNames(mapping != null ? mapping.LayerNames : null);
                if (names.Count > 0)
                {
                    lines.Add(definition.DisplayName + ": " + string.Join(", ", names));
                }
            }

            if (lines.Count == 0)
            {
                lines.Add("No layer mappings configured.");
            }

            return lines;
        }

        private static List<string> NormalizeLayers(IEnumerable<string> rawLayerNames)
        {
            return (rawLayerNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AppendMatch(LayerStandardAnalyzeResult result, LayerStandardMatchItem item)
        {
            result.Matches.Add(item);
            if (item.IsValid)
            {
                result.ValidLayers++;
                result.ValidLayerNames.Add(item.LayerName);
            }
            else
            {
                result.InvalidLayers++;
                result.InvalidLayerNames.Add(item.LayerName);
            }
        }

        private static MapCategory InferBuiltInMapCategory(string rawLayerName, string matchedStandard)
        {
            if (ContainsIgnoreCase(rawLayerName, "Text") ||
                ContainsIgnoreCase(rawLayerName, "Grid") ||
                ContainsIgnoreCase(rawLayerName, "Dimension") ||
                ContainsIgnoreCase(rawLayerName, "Axis") ||
                ContainsIgnoreCase(rawLayerName, "Stair") ||
                ContainsIgnoreCase(rawLayerName, "Ramp") ||
                ContainsIgnoreCase(matchedStandard, "Text") ||
                ContainsIgnoreCase(matchedStandard, "Grid") ||
                ContainsIgnoreCase(matchedStandard, "Dimension") ||
                ContainsIgnoreCase(matchedStandard, "Axis") ||
                ContainsIgnoreCase(matchedStandard, "Stair") ||
                ContainsIgnoreCase(matchedStandard, "Ramp"))
            {
                return MapCategory.NotForBuild;
            }

            if (ContainsIgnoreCase(rawLayerName, "Walls") ||
                ContainsIgnoreCase(rawLayerName, "Wall") ||
                ContainsIgnoreCase(matchedStandard, "Walls") ||
                ContainsIgnoreCase(matchedStandard, "Wall"))
            {
                return MapCategory.Walls;
            }

            if (ContainsIgnoreCase(rawLayerName, "Structural") ||
                ContainsIgnoreCase(matchedStandard, "Structural"))
            {
                return MapCategory.Columns;
            }

            return MapCategory.NotForBuild;
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   !string.IsNullOrWhiteSpace(value) &&
                   text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractThreeDigitCode(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return string.Empty;
            }

            Match match = ThreeDigitCodeRegex.Match(layerName);
            return match.Success ? match.Value : string.Empty;
        }
    }

    public sealed class LayerStandardAnalyzeResult
    {
        public int TotalLayers { get; set; }
        public int ValidLayers { get; set; }
        public int InvalidLayers { get; set; }
        public string RuleKey { get; set; }
        public string RuleName { get; set; }
        public List<string> RuleDescriptions { get; set; } = new List<string>();
        public List<string> ValidLayerNames { get; set; } = new List<string>();
        public List<string> InvalidLayerNames { get; set; } = new List<string>();
        public List<LayerStandardMatchItem> Matches { get; set; } = new List<LayerStandardMatchItem>();
    }

    public sealed class LayerStandardMatchItem
    {
        public string LayerName { get; set; }
        public string Code { get; set; }
        public bool IsValid { get; set; }
        public string MatchedStandard { get; set; }
        public MapCategory SuggestedMapCategory { get; set; } = MapCategory.Unknown;
    }

    [DataContract]
    public sealed class LayerRuleProfileStoreData
    {
        [DataMember(Name = "activeRuleKey")]
        public string ActiveRuleKey { get; set; }

        [DataMember(Name = "rules")]
        public List<LayerRuleProfile> Rules { get; set; } = new List<LayerRuleProfile>();
    }

    [DataContract]
    public sealed class LayerRuleProfile
    {
        [DataMember(Name = "key")]
        public string Key { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "mappings")]
        public List<LayerRuleCategoryMapping> Mappings { get; set; } = new List<LayerRuleCategoryMapping>();

        [IgnoreDataMember]
        public bool IsBuiltIn { get; set; }
    }

    [DataContract]
    public sealed class LayerRuleCategoryMapping
    {
        [DataMember(Name = "category")]
        public string Category { get; set; }

        [DataMember(Name = "layerNames")]
        public List<string> LayerNames { get; set; } = new List<string>();
    }

    public sealed class LayerRuleEditableCategoryDefinition
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public MapCategory SuggestedMapCategory { get; set; }
    }

    /// <summary>
    /// Stores user-created layer rules under LocalAppData so recompiling or updating the
    /// plug-in does not overwrite department-specific mappings. The EMSD default rule is
    /// built into code and is never written to or edited through this store.
    /// </summary>
    public static class LayerRuleProfileStoreService
    {
        public const string BuiltInRuleKey = "builtin_emsd";
        public const string BuiltInRuleDisplayName = "Default Rule";

        public static readonly IReadOnlyList<LayerRuleEditableCategoryDefinition> EditableCategories =
            new List<LayerRuleEditableCategoryDefinition>
            {
                new LayerRuleEditableCategoryDefinition { Key = "Walls", DisplayName = "Walls", SuggestedMapCategory = MapCategory.Walls },
                new LayerRuleEditableCategoryDefinition { Key = "Columns", DisplayName = "Columns", SuggestedMapCategory = MapCategory.Columns },
                new LayerRuleEditableCategoryDefinition { Key = "Windows", DisplayName = "Windows", SuggestedMapCategory = MapCategory.Windows },
                new LayerRuleEditableCategoryDefinition { Key = "Doors", DisplayName = "Doors", SuggestedMapCategory = MapCategory.Doors },
                new LayerRuleEditableCategoryDefinition { Key = "Beams", DisplayName = "Beams", SuggestedMapCategory = MapCategory.Beams },
                new LayerRuleEditableCategoryDefinition { Key = "Text", DisplayName = "Text", SuggestedMapCategory = MapCategory.NotForBuild },
                new LayerRuleEditableCategoryDefinition { Key = "Grids", DisplayName = "Grids", SuggestedMapCategory = MapCategory.NotForBuild },
                new LayerRuleEditableCategoryDefinition { Key = "Dimensions", DisplayName = "Dimensions", SuggestedMapCategory = MapCategory.NotForBuild },
                new LayerRuleEditableCategoryDefinition { Key = "Lines", DisplayName = "Lines", SuggestedMapCategory = MapCategory.NotForBuild }
            };

        private static readonly object SyncRoot = new object();
        private static LayerRuleProfileStoreData _cachedStore;
        private static DateTime _cachedFileWriteUtc = DateTime.MinValue;

        public static LayerRuleProfileStoreData Load()
        {
            lock (SyncRoot)
            {
                string path = GetStorePath();
                DateTime writeUtc = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_cachedStore != null && writeUtc == _cachedFileWriteUtc)
                {
                    return CloneStore(_cachedStore);
                }

                LayerRuleProfileStoreData store = TryRead(path) ?? new LayerRuleProfileStoreData();
                NormalizeStore(store);
                _cachedStore = CloneStore(store);
                _cachedFileWriteUtc = writeUtc;
                return CloneStore(store);
            }
        }

        public static void Save(LayerRuleProfileStoreData store)
        {
            LayerRuleProfileStoreData normalized = CloneStore(store ?? new LayerRuleProfileStoreData());
            NormalizeStore(normalized);

            lock (SyncRoot)
            {
                string path = GetStorePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerRuleProfileStoreData));
                string tempPath = path + ".tmp";
                using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.WriteObject(stream, normalized);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tempPath, path);

                _cachedStore = CloneStore(normalized);
                _cachedFileWriteUtc = File.GetLastWriteTimeUtc(path);
            }
        }

        public static LayerRuleProfile GetActiveRule()
        {
            LayerRuleProfileStoreData store = Load();
            if (!string.IsNullOrWhiteSpace(store.ActiveRuleKey) &&
                !string.Equals(store.ActiveRuleKey, BuiltInRuleKey, StringComparison.OrdinalIgnoreCase))
            {
                LayerRuleProfile custom = (store.Rules ?? new List<LayerRuleProfile>())
                    .FirstOrDefault(x => x != null && string.Equals(x.Key, store.ActiveRuleKey, StringComparison.OrdinalIgnoreCase));
                if (custom != null)
                {
                    custom.IsBuiltIn = false;
                    return custom;
                }
            }

            return CreateBuiltInRule();
        }

        public static List<LayerRuleProfile> GetProfilesIncludingBuiltIn(LayerRuleProfileStoreData store)
        {
            LayerRuleProfileStoreData safe = CloneStore(store ?? new LayerRuleProfileStoreData());
            NormalizeStore(safe);
            List<LayerRuleProfile> result = new List<LayerRuleProfile> { CreateBuiltInRule() };
            result.AddRange((safe.Rules ?? new List<LayerRuleProfile>()).Select(CloneProfile));
            return result;
        }

        public static LayerRuleProfile CreateBuiltInRule()
        {
            return new LayerRuleProfile
            {
                Key = BuiltInRuleKey,
                Name = BuiltInRuleDisplayName,
                IsBuiltIn = true,
                Mappings = new List<LayerRuleCategoryMapping>()
            };
        }

        public static MapCategory GetSuggestedMapCategory(string categoryKey)
        {
            LayerRuleEditableCategoryDefinition definition = EditableCategories
                .FirstOrDefault(x => string.Equals(x.Key, categoryKey, StringComparison.OrdinalIgnoreCase));
            return definition != null ? definition.SuggestedMapCategory : MapCategory.NotForBuild;
        }

        public static string GetCategoryDisplayName(string categoryKey)
        {
            LayerRuleEditableCategoryDefinition definition = EditableCategories
                .FirstOrDefault(x => string.Equals(x.Key, categoryKey, StringComparison.OrdinalIgnoreCase));
            return definition != null ? definition.DisplayName : (categoryKey ?? string.Empty);
        }

        public static List<string> NormalizeLayerNames(IEnumerable<string> names)
        {
            return (names ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static LayerRuleProfile CloneProfile(LayerRuleProfile source)
        {
            if (source == null)
            {
                return null;
            }

            return new LayerRuleProfile
            {
                Key = source.Key,
                Name = source.Name,
                IsBuiltIn = source.IsBuiltIn,
                Mappings = (source.Mappings ?? new List<LayerRuleCategoryMapping>())
                    .Where(x => x != null)
                    .Select(x => new LayerRuleCategoryMapping
                    {
                        Category = x.Category,
                        LayerNames = NormalizeLayerNames(x.LayerNames)
                    })
                    .ToList()
            };
        }

        public static LayerRuleProfileStoreData CloneStore(LayerRuleProfileStoreData source)
        {
            LayerRuleProfileStoreData safe = source ?? new LayerRuleProfileStoreData();
            return new LayerRuleProfileStoreData
            {
                ActiveRuleKey = safe.ActiveRuleKey,
                Rules = (safe.Rules ?? new List<LayerRuleProfile>())
                    .Where(x => x != null)
                    .Select(CloneProfile)
                    .ToList()
            };
        }

        public static string GetStorePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "EMSD AI Tool", "LayerRules", "layer-rules.json");
        }

        private static LayerRuleProfileStoreData TryRead(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerRuleProfileStoreData));
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return serializer.ReadObject(stream) as LayerRuleProfileStoreData;
                }
            }
            catch
            {
                return null;
            }
        }

        private static void NormalizeStore(LayerRuleProfileStoreData store)
        {
            if (store == null)
            {
                return;
            }

            store.Rules = (store.Rules ?? new List<LayerRuleProfile>())
                .Where(x => x != null && !string.Equals(x.Key, BuiltInRuleKey, StringComparison.OrdinalIgnoreCase))
                .Select(CloneProfile)
                .Where(x => x != null)
                .ToList();

            foreach (LayerRuleProfile rule in store.Rules)
            {
                rule.IsBuiltIn = false;
                if (string.IsNullOrWhiteSpace(rule.Key))
                {
                    rule.Key = "rule_" + Guid.NewGuid().ToString("N");
                }
                rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "Custom Rule" : rule.Name.Trim();
                rule.Mappings = (rule.Mappings ?? new List<LayerRuleCategoryMapping>())
                    .Where(x => x != null && EditableCategories.Any(d => string.Equals(d.Key, x.Category, StringComparison.OrdinalIgnoreCase)))
                    .Select(x => new LayerRuleCategoryMapping
                    {
                        Category = EditableCategories.First(d => string.Equals(d.Key, x.Category, StringComparison.OrdinalIgnoreCase)).Key,
                        LayerNames = NormalizeLayerNames(x.LayerNames)
                    })
                    .ToList();
            }

            bool activeExists = string.Equals(store.ActiveRuleKey, BuiltInRuleKey, StringComparison.OrdinalIgnoreCase) ||
                store.Rules.Any(x => string.Equals(x.Key, store.ActiveRuleKey, StringComparison.OrdinalIgnoreCase));
            if (!activeExists)
            {
                store.ActiveRuleKey = BuiltInRuleKey;
            }
        }
    }
}
