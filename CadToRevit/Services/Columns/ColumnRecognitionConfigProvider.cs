using CadToRevit.Models.Mapping;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services.Columns
{
    [DataContract]
    public sealed class ColumnRecognitionConfig
    {
        [DataMember(Name = "version")]
        public string Version { get; set; } = "10.5";

        [DataMember(Name = "defaults")]
        public ColumnRecognitionDefaults Defaults { get; set; } = new ColumnRecognitionDefaults();

        [DataMember(Name = "overridesByLayer")]
        public Dictionary<string, ColumnRecognitionLayerOverride> OverridesByLayer { get; set; }
            = new Dictionary<string, ColumnRecognitionLayerOverride>(StringComparer.OrdinalIgnoreCase);
    }

    [DataContract]
    public sealed class ColumnRecognitionDefaults
    {
        [DataMember(Name = "cluster")]
        public ColumnClusterSettings Cluster { get; set; } = new ColumnClusterSettings();

        [DataMember(Name = "sizeFilter")]
        public ColumnSizeFilterSettings SizeFilter { get; set; } = new ColumnSizeFilterSettings();

        [DataMember(Name = "lineFilter")]
        public ColumnLineFilterSettings LineFilter { get; set; } = new ColumnLineFilterSettings();

        [DataMember(Name = "merge")]
        public ColumnMergeSettings Merge { get; set; } = new ColumnMergeSettings();

        [DataMember(Name = "score")]
        public ColumnScoreSettings Score { get; set; } = new ColumnScoreSettings();

        [DataMember(Name = "attachToWall")]
        public ColumnAttachToWallSettings AttachToWall { get; set; } = new ColumnAttachToWallSettings();

        [DataMember(Name = "debug")]
        public ColumnDebugSettings Debug { get; set; } = new ColumnDebugSettings();

        [DataMember(Name = "orientation")]
        public ColumnOrientationSettings Orientation { get; set; } = new ColumnOrientationSettings();

        [DataMember(Name = "irregular")]
        public ColumnIrregularSettings Irregular { get; set; } = new ColumnIrregularSettings();
    }

    [DataContract]
    public sealed class ColumnRecognitionLayerOverride
    {
        [DataMember(Name = "cluster")]
        public ColumnClusterSettings Cluster { get; set; }

        [DataMember(Name = "sizeFilter")]
        public ColumnSizeFilterSettings SizeFilter { get; set; }

        [DataMember(Name = "lineFilter")]
        public ColumnLineFilterSettings LineFilter { get; set; }

        [DataMember(Name = "merge")]
        public ColumnMergeSettings Merge { get; set; }

        [DataMember(Name = "score")]
        public ColumnScoreSettings Score { get; set; }

        [DataMember(Name = "attachToWall")]
        public ColumnAttachToWallSettings AttachToWall { get; set; }

        [DataMember(Name = "debug")]
        public ColumnDebugSettings Debug { get; set; }

        [DataMember(Name = "orientation")]
        public ColumnOrientationSettings Orientation { get; set; }

        [DataMember(Name = "irregular")]
        public ColumnIrregularSettings Irregular { get; set; }
    }

    [DataContract]
    public sealed class ColumnClusterSettings
    {
        [DataMember(Name = "algorithm")]
        public string Algorithm { get; set; } = "MidpointBFS";

        [DataMember(Name = "clusterTolMm")]
        public double ClusterTolMm { get; set; } = 350.0;

        [DataMember(Name = "endpointTolMm")]
        public double EndpointTolMm { get; set; } = 30.0;

        [DataMember(Name = "gapTolMm")]
        public double GapTolMm { get; set; } = 50.0;

        [DataMember(Name = "minGroupSegments")]
        public int MinGroupSegments { get; set; } = 8;
    }

    [DataContract]
    public sealed class ColumnSizeFilterSettings
    {
        [DataMember(Name = "minSizeMm")]
        public double MinSizeMm { get; set; } = 200.0;

        [DataMember(Name = "maxSizeMm")]
        public double MaxSizeMm { get; set; } = 1200.0;

        [DataMember(Name = "minAreaM2")]
        public double MinAreaM2 { get; set; } = 0.04;

        [DataMember(Name = "maxAspectRatio")]
        public double MaxAspectRatio { get; set; } = 4.0;

        [DataMember(Name = "minFillRatio")]
        public double MinFillRatio { get; set; } = 0.25;
    }

    [DataContract]
    public sealed class ColumnLineFilterSettings
    {
        [DataMember(Name = "enable")]
        public bool Enable { get; set; } = true;

        [DataMember(Name = "maxSegmentLengthMm")]
        public double MaxSegmentLengthMm { get; set; } = 2000.0;
    }

    [DataContract]
    public sealed class ColumnMergeSettings
    {
        [DataMember(Name = "enable")]
        public bool Enable { get; set; } = true;

        [DataMember(Name = "mergeTolMm")]
        public double MergeTolMm { get; set; } = 300.0;

        [DataMember(Name = "strategy")]
        public string Strategy { get; set; } = "KeepBest";

        [DataMember(Name = "dedupePlacedTolMm")]
        public double DedupePlacedTolMm { get; set; } = 150.0;
    }

    [DataContract]
    public sealed class ColumnScoreSettings
    {
        [DataMember(Name = "areaWeight")]
        public double AreaWeight { get; set; } = 1.0;

        [DataMember(Name = "segmentCountWeight")]
        public double SegmentCountWeight { get; set; } = 0.6;

        [DataMember(Name = "rectnessWeight")]
        public double RectnessWeight { get; set; } = 0.8;

        [DataMember(Name = "longLinePenalty")]
        public double LongLinePenalty { get; set; } = 1.2;
    }

    [DataContract]
    public sealed class ColumnAttachToWallSettings
    {
        [DataMember(Name = "enable")]
        public bool Enable { get; set; } = true;

        [DataMember(Name = "snapTolMm")]
        public double SnapTolMm { get; set; } = 250.0;

        [DataMember(Name = "target")]
        public string Target { get; set; } = "WallCenterline";

        [DataMember(Name = "allowOverlap")]
        public bool AllowOverlap { get; set; }
    }

    [DataContract]
    public sealed class ColumnDebugSettings
    {
        [DataMember(Name = "drawCandidates")]
        public bool DrawCandidates { get; set; }

        [DataMember(Name = "drawClusterId")]
        public bool DrawClusterId { get; set; }

        [DataMember(Name = "drawRejectReason")]
        public bool DrawRejectReason { get; set; }

        [DataMember(Name = "exportReport")]
        public bool ExportReport { get; set; }
    }

    [DataContract]
    public sealed class ColumnOrientationSettings
    {
        [DataMember(Name = "enableAutoRotate")]
        public bool EnableAutoRotate { get; set; } = true;

        [DataMember(Name = "snapToOrthoEnable")]
        public bool SnapToOrthoEnable { get; set; } = true;

        [DataMember(Name = "snapToOrthoThresholdDeg")]
        public double SnapToOrthoThresholdDeg { get; set; } = 10.0;

        [DataMember(Name = "wallDirSearchRadiusMm")]
        public double WallDirSearchRadiusMm { get; set; } = 2000.0;

        [DataMember(Name = "minDominantDirConfidence")]
        public double MinDominantDirConfidence { get; set; } = 0.55;
    }

    [DataContract]
    public sealed class ColumnIrregularSettings
    {
        [DataMember(Name = "enable")]
        public bool Enable { get; set; } = true;

        [DataMember(Name = "maxSizeMm")]
        public double MaxSizeMm { get; set; } = 1800.0;

        [DataMember(Name = "requireClosedLoop")]
        public bool RequireClosedLoop { get; set; } = true;

        [DataMember(Name = "minAreaM2")]
        public double MinAreaM2 { get; set; } = 0.03;

        [DataMember(Name = "minGroupSegments")]
        public int MinGroupSegments { get; set; } = 4;

        [DataMember(Name = "maxAreaM2")]
        public double MaxAreaM2 { get; set; } = 2.0;

        [DataMember(Name = "minFillRatio")]
        public double MinFillRatio { get; set; } = 0.15;

        [DataMember(Name = "maxAspectRatio")]
        public double MaxAspectRatio { get; set; } = 6.0;

        [DataMember(Name = "enableHelperEdges")]
        public bool EnableHelperEdges { get; set; } = true;

        [DataMember(Name = "helperLayerKeywords")]
        public List<string> HelperLayerKeywords { get; set; } = new List<string> { "WALL", "A-WALL", "S-WALL", "E-WALL" };

        [DataMember(Name = "gapTolMm")]
        public double GapTolMm { get; set; } = 30.0;

        [DataMember(Name = "fragmentMergeTolMm")]
        public double FragmentMergeTolMm { get; set; } = 600.0;

        [DataMember(Name = "maxVirtualEdgeLenMm")]
        public double MaxVirtualEdgeLenMm { get; set; } = 300.0;

        [DataMember(Name = "maxHelperEdgeLenMm")]
        public double MaxHelperEdgeLenMm { get; set; } = 1500.0;

        [DataMember(Name = "maxHelperEdges")]
        public int MaxHelperEdges { get; set; } = 2;

        [DataMember(Name = "maxFragmentsPerGroup")]
        public int MaxFragmentsPerGroup { get; set; } = 3;
    }

    public static class ColumnRecognitionConfigProvider
    {
        public static ColumnRecognitionConfig Load()
        {
            ColumnRecognitionConfig fallback = new ColumnRecognitionConfig();
            string dllDir = null;
            try
            {
                dllDir = Path.GetDirectoryName(typeof(ColumnRecognitionConfigProvider).Assembly.Location);
            }
            catch
            {
                dllDir = null;
            }

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EMSD",
                "CadToRevit");

            string[] candidates =
            {
                string.IsNullOrWhiteSpace(dllDir) ? null : Path.Combine(dllDir, "ColumnRecognitionConfig.json"),
                Path.Combine(appDataDir, "ColumnRecognitionConfig.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColumnRecognitionConfig.json")
            };

            string path = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
            if (string.IsNullOrWhiteSpace(path))
            {
                return fallback;
            }

            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ColumnRecognitionConfig));
                    ColumnRecognitionConfig loaded = serializer.ReadObject(fs) as ColumnRecognitionConfig;
                    return loaded ?? fallback;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ColumnConfig] load failed: " + ex.Message);
                return fallback;
            }
        }

        public static ColumnRecognitionDefaults ResolveForLayer(string rawLayerName, AdvancedSettingsRow rowSettings)
        {
            ColumnRecognitionConfig config = Load();
            ColumnRecognitionDefaults resolved = CloneDefaults(config.Defaults ?? new ColumnRecognitionDefaults());

            ColumnRecognitionLayerOverride layerOverride;
            if (!string.IsNullOrWhiteSpace(rawLayerName) &&
                config.OverridesByLayer != null &&
                config.OverridesByLayer.TryGetValue(rawLayerName, out layerOverride) &&
                layerOverride != null)
            {
                ApplyLayerOverride(resolved, layerOverride);
            }

            ApplyRowOverride(resolved, rowSettings);
            return resolved;
        }

        private static void ApplyLayerOverride(ColumnRecognitionDefaults target, ColumnRecognitionLayerOverride source)
        {
            if (target == null || source == null)
            {
                return;
            }

            if (source.Cluster != null)
            {
                ApplyCluster(target.Cluster, source.Cluster);
            }

            if (source.SizeFilter != null)
            {
                ApplySizeFilter(target.SizeFilter, source.SizeFilter);
            }

            if (source.LineFilter != null)
            {
                ApplyLineFilter(target.LineFilter, source.LineFilter);
            }

            if (source.Merge != null)
            {
                ApplyMerge(target.Merge, source.Merge);
            }

            if (source.Score != null)
            {
                ApplyScore(target.Score, source.Score);
            }

            if (source.AttachToWall != null)
            {
                ApplyAttachToWall(target.AttachToWall, source.AttachToWall);
            }

            if (source.Debug != null)
            {
                ApplyDebug(target.Debug, source.Debug);
            }

            if (source.Orientation != null)
            {
                ApplyOrientation(target.Orientation, source.Orientation);
            }

            if (source.Irregular != null)
            {
                ApplyIrregular(target.Irregular, source.Irregular);
            }
        }

        private static void ApplyRowOverride(ColumnRecognitionDefaults target, AdvancedSettingsRow row)
        {
            if (target == null || row == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(row.ColumnClusterAlgorithm)) target.Cluster.Algorithm = row.ColumnClusterAlgorithm;
            if (row.ColumnClusterTolMm.HasValue) target.Cluster.ClusterTolMm = row.ColumnClusterTolMm.Value;
            if (row.ColumnEndpointTolMm.HasValue) target.Cluster.EndpointTolMm = row.ColumnEndpointTolMm.Value;
            if (row.ColumnGapTolMm.HasValue) target.Cluster.GapTolMm = row.ColumnGapTolMm.Value;
            if (row.ColumnMinGroupSegments.HasValue) target.Cluster.MinGroupSegments = Math.Max(1, row.ColumnMinGroupSegments.Value);

            if (row.ColumnMinSizeMm.HasValue) target.SizeFilter.MinSizeMm = row.ColumnMinSizeMm.Value;
            if (row.ColumnMaxSizeMm.HasValue) target.SizeFilter.MaxSizeMm = row.ColumnMaxSizeMm.Value;
            if (row.ColumnMinAreaM2.HasValue) target.SizeFilter.MinAreaM2 = row.ColumnMinAreaM2.Value;
            if (row.ColumnMaxAspectRatio.HasValue) target.SizeFilter.MaxAspectRatio = row.ColumnMaxAspectRatio.Value;
            if (row.ColumnMinFillRatio.HasValue) target.SizeFilter.MinFillRatio = row.ColumnMinFillRatio.Value;
            if (row.ColumnIrregularEnable.HasValue) target.Irregular.Enable = row.ColumnIrregularEnable.Value;
            if (row.ColumnIrregularMaxSizeMm.HasValue) target.Irregular.MaxSizeMm = row.ColumnIrregularMaxSizeMm.Value;
            if (row.ColumnIrregularGapTolMm.HasValue) target.Irregular.GapTolMm = row.ColumnIrregularGapTolMm.Value;
            if (row.ColumnIrregularMinAreaM2.HasValue) target.Irregular.MinAreaM2 = row.ColumnIrregularMinAreaM2.Value;

            if (row.ColumnEnableLongLineFilter.HasValue) target.LineFilter.Enable = row.ColumnEnableLongLineFilter.Value;
            if (row.ColumnMaxSegmentLengthMm.HasValue) target.LineFilter.MaxSegmentLengthMm = row.ColumnMaxSegmentLengthMm.Value;

            if (row.ColumnEnableMerge.HasValue) target.Merge.Enable = row.ColumnEnableMerge.Value;
            if (row.ColumnMergeTolMm.HasValue) target.Merge.MergeTolMm = row.ColumnMergeTolMm.Value;
            if (!string.IsNullOrWhiteSpace(row.ColumnMergeStrategy)) target.Merge.Strategy = row.ColumnMergeStrategy;
            if (row.ColumnDedupePlacedTolMm.HasValue) target.Merge.DedupePlacedTolMm = row.ColumnDedupePlacedTolMm.Value;

            if (row.ColumnAreaWeight.HasValue) target.Score.AreaWeight = row.ColumnAreaWeight.Value;
            if (row.ColumnSegmentCountWeight.HasValue) target.Score.SegmentCountWeight = row.ColumnSegmentCountWeight.Value;
            if (row.ColumnRectnessWeight.HasValue) target.Score.RectnessWeight = row.ColumnRectnessWeight.Value;
            if (row.ColumnLongLinePenalty.HasValue) target.Score.LongLinePenalty = row.ColumnLongLinePenalty.Value;

            if (row.ColumnAttachToWallEnable.HasValue) target.AttachToWall.Enable = row.ColumnAttachToWallEnable.Value;
            if (row.ColumnAttachToWallSnapTolMm.HasValue) target.AttachToWall.SnapTolMm = row.ColumnAttachToWallSnapTolMm.Value;
            if (!string.IsNullOrWhiteSpace(row.ColumnAttachToWallTarget)) target.AttachToWall.Target = row.ColumnAttachToWallTarget;
            if (row.ColumnAttachToWallAllowOverlap.HasValue) target.AttachToWall.AllowOverlap = row.ColumnAttachToWallAllowOverlap.Value;

            if (row.ColumnDebugDrawCandidates.HasValue) target.Debug.DrawCandidates = row.ColumnDebugDrawCandidates.Value;
            if (row.ColumnDebugDrawClusterId.HasValue) target.Debug.DrawClusterId = row.ColumnDebugDrawClusterId.Value;
            if (row.ColumnDebugDrawRejectReason.HasValue) target.Debug.DrawRejectReason = row.ColumnDebugDrawRejectReason.Value;
            if (row.ColumnDebugExportReport.HasValue) target.Debug.ExportReport = row.ColumnDebugExportReport.Value;
        }

        private static ColumnRecognitionDefaults CloneDefaults(ColumnRecognitionDefaults source)
        {
            ColumnOrientationSettings orientation = source.Orientation ?? new ColumnOrientationSettings();
            ColumnIrregularSettings irregular = source.Irregular ?? new ColumnIrregularSettings();
            return new ColumnRecognitionDefaults
            {
                Cluster = new ColumnClusterSettings
                {
                    Algorithm = source.Cluster.Algorithm,
                    ClusterTolMm = source.Cluster.ClusterTolMm,
                    EndpointTolMm = source.Cluster.EndpointTolMm,
                    GapTolMm = source.Cluster.GapTolMm,
                    MinGroupSegments = source.Cluster.MinGroupSegments
                },
                SizeFilter = new ColumnSizeFilterSettings
                {
                    MinSizeMm = source.SizeFilter.MinSizeMm,
                    MaxSizeMm = source.SizeFilter.MaxSizeMm,
                    MinAreaM2 = source.SizeFilter.MinAreaM2,
                    MaxAspectRatio = source.SizeFilter.MaxAspectRatio,
                    MinFillRatio = source.SizeFilter.MinFillRatio
                },
                LineFilter = new ColumnLineFilterSettings
                {
                    Enable = source.LineFilter.Enable,
                    MaxSegmentLengthMm = source.LineFilter.MaxSegmentLengthMm
                },
                Merge = new ColumnMergeSettings
                {
                    Enable = source.Merge.Enable,
                    MergeTolMm = source.Merge.MergeTolMm,
                    Strategy = source.Merge.Strategy,
                    DedupePlacedTolMm = source.Merge.DedupePlacedTolMm
                },
                Score = new ColumnScoreSettings
                {
                    AreaWeight = source.Score.AreaWeight,
                    SegmentCountWeight = source.Score.SegmentCountWeight,
                    RectnessWeight = source.Score.RectnessWeight,
                    LongLinePenalty = source.Score.LongLinePenalty
                },
                AttachToWall = new ColumnAttachToWallSettings
                {
                    Enable = source.AttachToWall.Enable,
                    SnapTolMm = source.AttachToWall.SnapTolMm,
                    Target = source.AttachToWall.Target,
                    AllowOverlap = source.AttachToWall.AllowOverlap
                },
                Debug = new ColumnDebugSettings
                {
                    DrawCandidates = source.Debug.DrawCandidates,
                    DrawClusterId = source.Debug.DrawClusterId,
                    DrawRejectReason = source.Debug.DrawRejectReason,
                    ExportReport = source.Debug.ExportReport
                },
                Orientation = new ColumnOrientationSettings
                {
                    EnableAutoRotate = orientation.EnableAutoRotate,
                    SnapToOrthoEnable = orientation.SnapToOrthoEnable,
                    SnapToOrthoThresholdDeg = orientation.SnapToOrthoThresholdDeg,
                    WallDirSearchRadiusMm = orientation.WallDirSearchRadiusMm,
                    MinDominantDirConfidence = orientation.MinDominantDirConfidence
                },
                Irregular = new ColumnIrregularSettings
                {
                    Enable = irregular.Enable,
                    MaxSizeMm = irregular.MaxSizeMm,
                    RequireClosedLoop = irregular.RequireClosedLoop,
                    MinAreaM2 = irregular.MinAreaM2,
                    MinGroupSegments = irregular.MinGroupSegments,
                    MaxAreaM2 = irregular.MaxAreaM2,
                    MinFillRatio = irregular.MinFillRatio,
                    MaxAspectRatio = irregular.MaxAspectRatio,
                    EnableHelperEdges = irregular.EnableHelperEdges,
                    HelperLayerKeywords = irregular.HelperLayerKeywords == null
                        ? new List<string>()
                        : new List<string>(irregular.HelperLayerKeywords.Where(x => !string.IsNullOrWhiteSpace(x))),
                    GapTolMm = irregular.GapTolMm,
                    FragmentMergeTolMm = irregular.FragmentMergeTolMm,
                    MaxVirtualEdgeLenMm = irregular.MaxVirtualEdgeLenMm,
                    MaxHelperEdgeLenMm = irregular.MaxHelperEdgeLenMm,
                    MaxHelperEdges = irregular.MaxHelperEdges,
                    MaxFragmentsPerGroup = irregular.MaxFragmentsPerGroup
                }
            };
        }

        private static void ApplyCluster(ColumnClusterSettings target, ColumnClusterSettings source)
        {
            if (!string.IsNullOrWhiteSpace(source.Algorithm)) target.Algorithm = source.Algorithm;
            target.ClusterTolMm = source.ClusterTolMm;
            target.EndpointTolMm = source.EndpointTolMm;
            target.GapTolMm = source.GapTolMm;
            target.MinGroupSegments = source.MinGroupSegments;
        }

        private static void ApplySizeFilter(ColumnSizeFilterSettings target, ColumnSizeFilterSettings source)
        {
            target.MinSizeMm = source.MinSizeMm;
            target.MaxSizeMm = source.MaxSizeMm;
            target.MinAreaM2 = source.MinAreaM2;
            target.MaxAspectRatio = source.MaxAspectRatio;
            target.MinFillRatio = source.MinFillRatio;
        }

        private static void ApplyLineFilter(ColumnLineFilterSettings target, ColumnLineFilterSettings source)
        {
            target.Enable = source.Enable;
            target.MaxSegmentLengthMm = source.MaxSegmentLengthMm;
        }

        private static void ApplyMerge(ColumnMergeSettings target, ColumnMergeSettings source)
        {
            target.Enable = source.Enable;
            target.MergeTolMm = source.MergeTolMm;
            if (!string.IsNullOrWhiteSpace(source.Strategy)) target.Strategy = source.Strategy;
            target.DedupePlacedTolMm = source.DedupePlacedTolMm;
        }

        private static void ApplyScore(ColumnScoreSettings target, ColumnScoreSettings source)
        {
            target.AreaWeight = source.AreaWeight;
            target.SegmentCountWeight = source.SegmentCountWeight;
            target.RectnessWeight = source.RectnessWeight;
            target.LongLinePenalty = source.LongLinePenalty;
        }

        private static void ApplyAttachToWall(ColumnAttachToWallSettings target, ColumnAttachToWallSettings source)
        {
            target.Enable = source.Enable;
            target.SnapTolMm = source.SnapTolMm;
            if (!string.IsNullOrWhiteSpace(source.Target)) target.Target = source.Target;
            target.AllowOverlap = source.AllowOverlap;
        }

        private static void ApplyDebug(ColumnDebugSettings target, ColumnDebugSettings source)
        {
            target.DrawCandidates = source.DrawCandidates;
            target.DrawClusterId = source.DrawClusterId;
            target.DrawRejectReason = source.DrawRejectReason;
            target.ExportReport = source.ExportReport;
        }

        private static void ApplyOrientation(ColumnOrientationSettings target, ColumnOrientationSettings source)
        {
            target.EnableAutoRotate = source.EnableAutoRotate;
            target.SnapToOrthoEnable = source.SnapToOrthoEnable;
            target.SnapToOrthoThresholdDeg = source.SnapToOrthoThresholdDeg;
            target.WallDirSearchRadiusMm = source.WallDirSearchRadiusMm;
            target.MinDominantDirConfidence = source.MinDominantDirConfidence;
        }

        private static void ApplyIrregular(ColumnIrregularSettings target, ColumnIrregularSettings source)
        {
            target.Enable = source.Enable;
            target.MaxSizeMm = source.MaxSizeMm;
            target.RequireClosedLoop = source.RequireClosedLoop;
            target.MinAreaM2 = source.MinAreaM2;
            target.MinGroupSegments = source.MinGroupSegments;
            target.MaxAreaM2 = source.MaxAreaM2;
            target.MinFillRatio = source.MinFillRatio;
            target.MaxAspectRatio = source.MaxAspectRatio;
            target.EnableHelperEdges = source.EnableHelperEdges;
            target.HelperLayerKeywords = source.HelperLayerKeywords == null
                ? new List<string>()
                : new List<string>(source.HelperLayerKeywords.Where(x => !string.IsNullOrWhiteSpace(x)));
            target.GapTolMm = source.GapTolMm;
            target.FragmentMergeTolMm = source.FragmentMergeTolMm;
            target.MaxVirtualEdgeLenMm = source.MaxVirtualEdgeLenMm;
            target.MaxHelperEdgeLenMm = source.MaxHelperEdgeLenMm;
            target.MaxHelperEdges = source.MaxHelperEdges;
            target.MaxFragmentsPerGroup = source.MaxFragmentsPerGroup;
        }
    }
}
