using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services
{
    public static class LayerNameMapper
    {
        private static readonly object SyncRoot = new object();
        private static Dictionary<string, HashSet<string>> _mapping;

        public static string Map(string normalizedLayerName)
        {
            EnsureLoaded();

            string layer = string.IsNullOrWhiteSpace(normalizedLayerName)
                ? "UNKNOWN"
                : normalizedLayerName.Trim().ToUpperInvariant();

            foreach (KeyValuePair<string, HashSet<string>> kv in _mapping)
            {
                if (kv.Value.Contains(layer))
                {
                    return kv.Key;
                }
            }

            return "UNKNOWN";
        }

        public static Dictionary<string, List<string>> Snapshot()
        {
            EnsureLoaded();
            return _mapping.ToDictionary(
                x => x.Key,
                x => x.Value.OrderBy(v => v).ToList(),
                StringComparer.OrdinalIgnoreCase);
        }

        private static void EnsureLoaded()
        {
            if (_mapping != null)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_mapping != null)
                {
                    return;
                }

                _mapping = GetDefaultMapping();
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layer-mapping.json");
                if (!File.Exists(path))
                {
                    return;
                }

                try
                {
                    using (FileStream fs = File.OpenRead(path))
                    {
                        DataContractJsonSerializer serializer =
                            new DataContractJsonSerializer(typeof(LayerMappingConfig));
                        LayerMappingConfig cfg = serializer.ReadObject(fs) as LayerMappingConfig;
                        if (cfg == null)
                        {
                            return;
                        }

                        Dictionary<string, HashSet<string>> loaded = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
                        {
                            { "WALL", ToSet(cfg.WALL) },
                            { "DOOR", ToSet(cfg.DOOR) },
                            { "WINDOW", ToSet(cfg.WINDOW) },
                            { "GRID", ToSet(cfg.GRID) }
                        };
                        _mapping = loaded;
                    }
                }
                catch
                {
                    _mapping = GetDefaultMapping();
                }
            }
        }

        private static Dictionary<string, HashSet<string>> GetDefaultMapping()
        {
            return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "WALL", ToSet(new[] { "STRUCTURE", "WALL", "A-WALL" }) },
                { "DOOR", ToSet(new[] { "DOOR", "DR", "D" }) },
                { "WINDOW", ToSet(new[] { "GLASS", "WINDOW", "WIN", "WD" }) },
                { "GRID", ToSet(new[] { "GRID", "AXIS" }) }
            };
        }

        private static HashSet<string> ToSet(IEnumerable<string> items)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (items == null)
            {
                return set;
            }

            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                set.Add(item.Trim().ToUpperInvariant());
            }

            return set;
        }

        [DataContract]
        private class LayerMappingConfig
        {
            [DataMember(Name = "WALL")]
            public List<string> WALL { get; set; }

            [DataMember(Name = "DOOR")]
            public List<string> DOOR { get; set; }

            [DataMember(Name = "WINDOW")]
            public List<string> WINDOW { get; set; }

            [DataMember(Name = "GRID")]
            public List<string> GRID { get; set; }
        }
    }
}
