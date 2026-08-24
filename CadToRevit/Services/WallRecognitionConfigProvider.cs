using CadToRevit.Models.Topology;
using CadToRevit.Services.Config;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services
{
    [DataContract]
    public class WallRecognitionConfig
    {
        [DataMember(Name = "MinWallLengthMm")]
        public double MinWallLengthMm { get; set; } = 1500.0;

        [DataMember(Name = "ParallelAngleTolDeg")]
        public double ParallelAngleTolDeg { get; set; } = 2.0;

        [DataMember(Name = "WallThicknessTolMm")]
        public double WallThicknessTolMm { get; set; } = 20.0;

        [DataMember(Name = "EndpointMergeTolMm")]
        public double EndpointMergeTolMm { get; set; } = 50.0;

        [DataMember(Name = "EnableMergeCollinear")]
        public bool EnableMergeCollinear { get; set; } = false;

        [DataMember(Name = "ArcThicknessTolMm")]
        public double ArcThicknessTolMm { get; set; } = 20.0;

        [DataMember(Name = "MaxWallThicknessMm")]
        public double MaxWallThicknessMm { get; set; } = 500.0;

        [DataMember(Name = "DefaultSingleWallThicknessMm")]
        public double DefaultSingleWallThicknessMm { get; set; } = 200.0;

        [DataMember(Name = "Topology")]
        public TopologySettings Topology { get; set; } = new TopologySettings();
    }

    public static class WallRecognitionConfigProvider
    {
        private const double MmPerFoot = 304.8;
        public static string LastLoadedPath { get; private set; }
        public static string LastLoadMessage { get; private set; }

        public static WallRecognitionConfig Load()
        {
            WallRecognitionConfig fallback = new WallRecognitionConfig();
            string dllDir = null;
            try
            {
                dllDir = Path.GetDirectoryName(typeof(WallRecognitionConfigProvider).Assembly.Location);
            }
            catch
            {
                dllDir = null;
            }

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EMSD",
                "CadToRevit");

            string[] candidates = new[]
            {
                string.IsNullOrWhiteSpace(dllDir) ? null : Path.Combine(dllDir, "WallRecognitionConfig.json"),
                Path.Combine(appDataDir, "WallRecognitionConfig.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WallRecognitionConfig.json")
            };

            string path = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
            if (string.IsNullOrWhiteSpace(path))
            {
                LastLoadedPath = "(fallback)";
                LastLoadMessage = "Config not found. CurrentDirectory=" + Environment.CurrentDirectory;
                DiagnosticRecorder.AppendDebug(
                    "[Config] fallback(no file). CurrentDirectory=" + Environment.CurrentDirectory +
                    ", Candidates=" + string.Join(" | ", candidates.Where(x => !string.IsNullOrWhiteSpace(x))));
                return fallback;
            }

            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WallRecognitionConfig));
                    WallRecognitionConfig cfg = serializer.ReadObject(fs) as WallRecognitionConfig;
                    WallRecognitionConfig loaded = cfg ?? fallback;
                    LastLoadedPath = path;
                    LastLoadMessage =
                        "Path=" + path +
                        ", DefaultSingleWallThicknessMm=" + loaded.DefaultSingleWallThicknessMm.ToString("F2") +
                        ", WallThicknessTolMm=" + loaded.WallThicknessTolMm.ToString("F2") +
                        ", CurrentDirectory=" + Environment.CurrentDirectory;
                    DiagnosticRecorder.AppendDebug("[Config] " + LastLoadMessage);
                    return loaded;
                }
            }
            catch
            {
                LastLoadedPath = path;
                LastLoadMessage = "Config read failed, fallback used. Path=" + path + ", CurrentDirectory=" + Environment.CurrentDirectory;
                DiagnosticRecorder.AppendDebug("[Config] " + LastLoadMessage);
                return fallback;
            }
        }

        public static WallRecognitionConfigDerived LoadDerived()
        {
            return LoadDerived(null);
        }

        public static WallRecognitionConfigDerived LoadDerived(ISet<string> selectedRawLayers)
        {
            WallRecognitionConfig cfg = Load();
            ApplyLayerOverrides(cfg, selectedRawLayers);
            TopologySettings topo = cfg.Topology ?? new TopologySettings();
            return new WallRecognitionConfigDerived
            {
                MinWallLengthFt = MmToFt(cfg.MinWallLengthMm),
                ParallelAngleTolDeg = cfg.ParallelAngleTolDeg,
                WallThicknessTolFt = MmToFt(cfg.WallThicknessTolMm),
                EndpointMergeTolFt = MmToFt(cfg.EndpointMergeTolMm),
                EnableMergeCollinear = cfg.EnableMergeCollinear,
                ArcThicknessTolFt = MmToFt(cfg.ArcThicknessTolMm),
                MaxWallThicknessFt = MmToFt(cfg.MaxWallThicknessMm),
                DefaultSingleWallThicknessFt = MmToFt(cfg.DefaultSingleWallThicknessMm),
                TopologyEndpointClusterTolFt = MmToFt(topo.EndpointClusterTolMm),
                TopologyExtendSearchTolFt = MmToFt(topo.ExtendSearchTolMm),
                TopologyDuplicateTolFt = MmToFt(topo.DuplicateTolMm),
                TopologyAngleSnapDeg = topo.AngleSnapDeg,
                EnableTopologyOrthogonalSnap = topo.EnableOrthogonalSnap,
                EnableTopologyExtendToIntersection = topo.EnableExtendToIntersection,
                EnableTopologyEndpointClustering = topo.EnableEndpointClustering,
                EnableTopologyDuplicateRemoval = topo.EnableDuplicateRemoval,
                EnableTopologyExtendCollinear = topo.EnableExtendCollinear,
                TopologyExtendCollinearTolFt = MmToFt(topo.ExtendCollinearTolMm),
                TopologyCollinearOffsetTolFt = MmToFt(topo.CollinearOffsetTolMm),
                TopologyExtendProjectionTolFt = MmToFt(topo.ExtendProjectionTolMm),
                TopologyUseDirectionalClustering = topo.UseDirectionalClustering,
                TopologyIgnoreSmallerThanFt = 0.0,
                TopologyMinJunctureWidthFt = 0.0,
                TopologyIgnoreLargerThanFt = 0.0,
                TopologyMaxJunctureWidthFt = 0.0
            };
        }

        private static void ApplyLayerOverrides(WallRecognitionConfig cfg, ISet<string> selectedRawLayers)
        {
            if (cfg == null || selectedRawLayers == null || selectedRawLayers.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[Override] Skip apply: cfg or selectedRawLayers is empty.");
                return;
            }

            LayerOverrideConfig overrides = LoadLayerOverrideConfig();
            if (overrides == null || overrides.Layers == null || overrides.Layers.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[Override] No override entries loaded.");
                return;
            }

            HashSet<string> selected = new HashSet<string>(selectedRawLayers.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[Override] Skip apply: selectedRawLayers has no valid names.");
                return;
            }

            bool applied = false;
            foreach (LayerOverrideEntry entry in overrides.Layers)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.RawLayerName))
                {
                    continue;
                }

                if (!selected.Contains(entry.RawLayerName))
                {
                    continue;
                }
                applied = true;
                DiagnosticRecorder.AppendDebug("[Override] Applying layer override: " + entry.RawLayerName);

                if (entry.MinWallLengthMm.HasValue)
                {
                    cfg.MinWallLengthMm = entry.MinWallLengthMm.Value;
                }

                if (entry.ParallelAngleTolDeg.HasValue)
                {
                    cfg.ParallelAngleTolDeg = entry.ParallelAngleTolDeg.Value;
                }

                if (entry.WallThicknessTolMm.HasValue)
                {
                    cfg.WallThicknessTolMm = entry.WallThicknessTolMm.Value;
                }

                if (entry.EndpointMergeTolMm.HasValue)
                {
                    cfg.EndpointMergeTolMm = entry.EndpointMergeTolMm.Value;
                }

                if (entry.EnableMergeCollinear.HasValue)
                {
                    cfg.EnableMergeCollinear = entry.EnableMergeCollinear.Value;
                }

                ApplyTopologyOverride(cfg.Topology, entry.Topology);
            }

            if (!applied)
            {
                DiagnosticRecorder.AppendDebug("[Override] No matching layer override found. Selected=" + string.Join(",", selected));
            }
        }

        private static void ApplyTopologyOverride(TopologySettings target, TopologySettingsOverride source)
        {
            if (target == null || source == null)
            {
                return;
            }

            if (source.EndpointClusterTolMm.HasValue) target.EndpointClusterTolMm = source.EndpointClusterTolMm.Value;
            if (source.ExtendSearchTolMm.HasValue) target.ExtendSearchTolMm = source.ExtendSearchTolMm.Value;
            if (source.DuplicateTolMm.HasValue) target.DuplicateTolMm = source.DuplicateTolMm.Value;
            if (source.AngleSnapDeg.HasValue) target.AngleSnapDeg = source.AngleSnapDeg.Value;
            if (source.EnableOrthogonalSnap.HasValue) target.EnableOrthogonalSnap = source.EnableOrthogonalSnap.Value;
            if (source.EnableExtendToIntersection.HasValue) target.EnableExtendToIntersection = source.EnableExtendToIntersection.Value;
            if (source.EnableEndpointClustering.HasValue) target.EnableEndpointClustering = source.EnableEndpointClustering.Value;
            if (source.EnableDuplicateRemoval.HasValue) target.EnableDuplicateRemoval = source.EnableDuplicateRemoval.Value;
            if (source.EnableExtendCollinear.HasValue) target.EnableExtendCollinear = source.EnableExtendCollinear.Value;
            if (source.ExtendCollinearTolMm.HasValue) target.ExtendCollinearTolMm = source.ExtendCollinearTolMm.Value;
            if (source.CollinearOffsetTolMm.HasValue) target.CollinearOffsetTolMm = source.CollinearOffsetTolMm.Value;
            if (source.ExtendProjectionTolMm.HasValue) target.ExtendProjectionTolMm = source.ExtendProjectionTolMm.Value;
            if (source.UseDirectionalClustering.HasValue) target.UseDirectionalClustering = source.UseDirectionalClustering.Value;
        }

        private static LayerOverrideConfig LoadLayerOverrideConfig()
        {
            string dllDir = null;
            try
            {
                dllDir = Path.GetDirectoryName(typeof(WallRecognitionConfigProvider).Assembly.Location);
            }
            catch
            {
                dllDir = null;
            }

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EMSD",
                "CadToRevit");

            string[] candidates = new[]
            {
                string.IsNullOrWhiteSpace(dllDir) ? null : Path.Combine(dllDir, "WallRecognitionLayerOverrides.json"),
                Path.Combine(appDataDir, "WallRecognitionLayerOverrides.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WallRecognitionLayerOverrides.json")
            };

            string path = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
            if (string.IsNullOrWhiteSpace(path))
            {
                DiagnosticRecorder.AppendDebug("[Override] Override file not found.");
                return null;
            }

            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerOverrideConfig));
                    LayerOverrideConfig loaded = serializer.ReadObject(fs) as LayerOverrideConfig;
                    int count = loaded?.Layers?.Count ?? 0;
                    DiagnosticRecorder.AppendDebug("[Override] Loaded override file: " + path + ", layerCount=" + count);
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Override] Failed to load override file: " + path + ", ex=" + ex.Message);
                return null;
            }
        }

        private static double MmToFt(double mm)
        {
            return mm / MmPerFoot;
        }
    }

    [DataContract]
    public sealed class LayerOverrideConfig
    {
        [DataMember(Name = "Layers")]
        public List<LayerOverrideEntry> Layers { get; set; } = new List<LayerOverrideEntry>();
    }

    [DataContract]
    public sealed class LayerOverrideEntry
    {
        [DataMember(Name = "RawLayerName")]
        public string RawLayerName { get; set; }

        [DataMember(Name = "MinWallLengthMm")]
        public double? MinWallLengthMm { get; set; }

        [DataMember(Name = "ParallelAngleTolDeg")]
        public double? ParallelAngleTolDeg { get; set; }

        [DataMember(Name = "WallThicknessTolMm")]
        public double? WallThicknessTolMm { get; set; }

        [DataMember(Name = "EndpointMergeTolMm")]
        public double? EndpointMergeTolMm { get; set; }

        [DataMember(Name = "EnableMergeCollinear")]
        public bool? EnableMergeCollinear { get; set; }

        [DataMember(Name = "Topology")]
        public TopologySettingsOverride Topology { get; set; }
    }

    [DataContract]
    public sealed class TopologySettingsOverride
    {
        [DataMember(Name = "EndpointClusterTolMm")]
        public double? EndpointClusterTolMm { get; set; }

        [DataMember(Name = "ExtendSearchTolMm")]
        public double? ExtendSearchTolMm { get; set; }

        [DataMember(Name = "DuplicateTolMm")]
        public double? DuplicateTolMm { get; set; }

        [DataMember(Name = "AngleSnapDeg")]
        public double? AngleSnapDeg { get; set; }

        [DataMember(Name = "EnableOrthogonalSnap")]
        public bool? EnableOrthogonalSnap { get; set; }

        [DataMember(Name = "EnableExtendToIntersection")]
        public bool? EnableExtendToIntersection { get; set; }

        [DataMember(Name = "EnableEndpointClustering")]
        public bool? EnableEndpointClustering { get; set; }

        [DataMember(Name = "EnableDuplicateRemoval")]
        public bool? EnableDuplicateRemoval { get; set; }

        [DataMember(Name = "EnableExtendCollinear")]
        public bool? EnableExtendCollinear { get; set; }

        [DataMember(Name = "ExtendCollinearTolMm")]
        public double? ExtendCollinearTolMm { get; set; }

        [DataMember(Name = "CollinearOffsetTolMm")]
        public double? CollinearOffsetTolMm { get; set; }

        [DataMember(Name = "ExtendProjectionTolMm")]
        public double? ExtendProjectionTolMm { get; set; }

        [DataMember(Name = "UseDirectionalClustering")]
        public bool? UseDirectionalClustering { get; set; }
    }
}
