using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CadToRevit.Services
{
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

            // 新增
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

            // 新增
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
            List<string> layers = (rawLayerNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            LayerStandardAnalyzeResult result = new LayerStandardAnalyzeResult
            {
                TotalLayers = layers.Count
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
                    MatchedStandard = valid ? matched.Label : "Unmatched Standard"
                };

                result.Matches.Add(item);
                if (valid)
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

            return result;
        }

        public static List<string> BuildRuleDescriptions()
        {
            return RuleRanges
                .Select(x => x.Min.ToString("D3") + "-" + x.Max.ToString("D3") + ": " + x.Label)
                .ToList();
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
    }
}
