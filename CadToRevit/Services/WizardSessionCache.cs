using Autodesk.Revit.DB;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class WizardSessionCache
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, SessionEntry> Entries = new Dictionary<string, SessionEntry>(StringComparer.OrdinalIgnoreCase);

        public static string BuildContextSignature(ElementId dwgId, ElementId levelId, SourceUnit unit)
        {
            int dwg = dwgId != null ? dwgId.IntegerValue : -1;
            int level = levelId != null ? levelId.IntegerValue : -1;
            return "dwg=" + dwg + ";level=" + level + ";unit=" + unit;
        }

        public static bool TryLoad(Document doc, string contextSignature, out List<MapRow> mapRows)
        {
            mapRows = new List<MapRow>();
            string docKey = BuildDocKey(doc);
            if (string.IsNullOrWhiteSpace(docKey))
            {
                return false;
            }

            lock (SyncRoot)
            {
                SessionEntry entry;
                if (!Entries.TryGetValue(docKey, out entry) || entry == null)
                {
                    return false;
                }

                if (!string.Equals(entry.ContextSignature ?? string.Empty, contextSignature ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                mapRows = CloneMapRows(entry.MapRows);
                return true;
            }
        }

        public static void Save(Document doc, string contextSignature, IEnumerable<MapRow> mapRows)
        {
            string docKey = BuildDocKey(doc);
            if (string.IsNullOrWhiteSpace(docKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                Entries[docKey] = new SessionEntry
                {
                    ContextSignature = contextSignature ?? string.Empty,
                    MapRows = CloneMapRows(mapRows)
                };
            }
        }

        public static void Clear(Document doc)
        {
            string docKey = BuildDocKey(doc);
            if (string.IsNullOrWhiteSpace(docKey))
            {
                return;
            }

            lock (SyncRoot)
            {
                Entries.Remove(docKey);
            }
        }

        private static string BuildDocKey(Document doc)
        {
            if (doc == null)
            {
                return string.Empty;
            }

            string path = doc.PathName ?? string.Empty;
            string title = doc.Title ?? string.Empty;
            int hash = doc.GetHashCode();
            return path + "|" + title + "|" + hash.ToString();
        }

        private static List<MapRow> CloneMapRows(IEnumerable<MapRow> mapRows)
        {
            List<MapRow> source = mapRows == null ? new List<MapRow>() : mapRows.Where(x => x != null).ToList();
            List<MapRow> result = new List<MapRow>(source.Count);
            foreach (MapRow row in source)
            {
                result.Add(new MapRow
                {
                    RawLayerName = row.RawLayerName,
                    Category = row.Category,
                    RevitTypeId = row.RevitTypeId,
                    RevitTypeName = row.RevitTypeName,
                    ExpectedWidthMm = row.ExpectedWidthMm,
                    Settings = CloneSettings(row.Settings)
                });
            }

            return result;
        }

        private static AdvancedSettingsRow CloneSettings(AdvancedSettingsRow source)
        {
            AdvancedSettingsRow target = new AdvancedSettingsRow();
            if (source == null)
            {
                return target;
            }

            JunctureSettings juncture = source.Juncture ?? new JunctureSettings();
            target.EnableLayerOverride = source.EnableLayerOverride;
            target.ApplyAsCategoryDefault = source.ApplyAsCategoryDefault;
            target.DoorExpectedWidthMm = source.DoorExpectedWidthMm;
            target.MinDoorWidthMm = source.MinDoorWidthMm;
            target.MaxDoorWidthMm = source.MaxDoorWidthMm;
            target.DoorWallMatchTolMm = source.DoorWallMatchTolMm;
            target.WallMinWallLengthMm = source.WallMinWallLengthMm;
            target.WallThicknessTolMm = source.WallThicknessTolMm;
            target.WallMaxWallThicknessMm = source.WallMaxWallThicknessMm;
            target.WallDefaultSingleWallThicknessMm = source.WallDefaultSingleWallThicknessMm;
            target.WallParallelAngleTolDeg = source.WallParallelAngleTolDeg;
            target.WallEndpointMergeTolMm = source.WallEndpointMergeTolMm;
            target.WallArcThicknessTolMm = source.WallArcThicknessTolMm;
            target.WallEndpointClusterTolMm = source.WallEndpointClusterTolMm;
            target.WallExtendSearchTolMm = source.WallExtendSearchTolMm;
            target.WallDuplicateTolMm = source.WallDuplicateTolMm;
            target.WallAngleSnapDeg = source.WallAngleSnapDeg;
            target.WallEnableOrthogonalSnap = source.WallEnableOrthogonalSnap;
            target.WallEnableExtendToIntersection = source.WallEnableExtendToIntersection;
            target.WallEnableEndpointClustering = source.WallEnableEndpointClustering;
            target.WallEnableDuplicateRemoval = source.WallEnableDuplicateRemoval;
            target.WallEnableExtendCollinear = source.WallEnableExtendCollinear;
            target.WallEnableMergeCollinear = source.WallEnableMergeCollinear;
            target.WallExtendCollinearTolMm = source.WallExtendCollinearTolMm;
            target.WallCollinearOffsetTolMm = source.WallCollinearOffsetTolMm;
            target.WallExtendProjectionTolMm = source.WallExtendProjectionTolMm;
            target.WallUseDirectionalClustering = source.WallUseDirectionalClustering;
            target.WallEnableAutoDoubleLineThickness = source.WallEnableAutoDoubleLineThickness;
            target.WallAutoThicknessTopK = source.WallAutoThicknessTopK;
            target.WallAutoThicknessBinMm = source.WallAutoThicknessBinMm;
            target.WallMinDoubleLineThicknessMm = source.WallMinDoubleLineThicknessMm;
            target.WallMinDoubleLineOverlapLenMm = source.WallMinDoubleLineOverlapLenMm;
            target.WallForceSingleLineMode = source.WallForceSingleLineMode;
            target.WallDoubleLineSingleWallPlaceMode = source.WallDoubleLineSingleWallPlaceMode;
            target.WallDoubleLineLengthPolicy = source.WallDoubleLineLengthPolicy;
            target.WallDoubleLineAdaptiveContainTolMm = source.WallDoubleLineAdaptiveContainTolMm;
            target.WallDoubleLineAdaptiveExtendMaxMm = source.WallDoubleLineAdaptiveExtendMaxMm;
            target.WallHeightMm = source.WallHeightMm;
            target.WallBaseOffsetMm = source.WallBaseOffsetMm;
            target.DoorHeightMm = source.DoorHeightMm;
            target.DoorSillHeightMm = source.DoorSillHeightMm;
            target.UseFixedDoorWidth = source.UseFixedDoorWidth;
            target.PreferGeometryOpeningWidth = source.PreferGeometryOpeningWidth;
            target.BeamMinLengthMm = source.BeamMinLengthMm;
            target.BeamElevationOffsetMm = source.BeamElevationOffsetMm;
            target.BeamEnableMergeCollinear = source.BeamEnableMergeCollinear;
            target.BeamEndpointMergeTolMm = source.BeamEndpointMergeTolMm;
            target.BeamParallelAngleTolDeg = source.BeamParallelAngleTolDeg;
            target.BeamAllowArc = source.BeamAllowArc;
            target.WindowHeightMm = source.WindowHeightMm;
            target.WindowSillHeightMm = source.WindowSillHeightMm;
            target.WindowUseSillPlusHeight = source.WindowUseSillPlusHeight;
            target.ColumnHeightMm = source.ColumnHeightMm;
            target.ColumnClusterAlgorithm = source.ColumnClusterAlgorithm;
            target.ColumnClusterTolMm = source.ColumnClusterTolMm;
            target.ColumnEndpointTolMm = source.ColumnEndpointTolMm;
            target.ColumnGapTolMm = source.ColumnGapTolMm;
            target.ColumnMinGroupSegments = source.ColumnMinGroupSegments;
            target.ColumnMinSizeMm = source.ColumnMinSizeMm;
            target.ColumnMaxSizeMm = source.ColumnMaxSizeMm;
            target.ColumnMinAreaM2 = source.ColumnMinAreaM2;
            target.ColumnMaxAspectRatio = source.ColumnMaxAspectRatio;
            target.ColumnMinFillRatio = source.ColumnMinFillRatio;
            target.ColumnEnableLongLineFilter = source.ColumnEnableLongLineFilter;
            target.ColumnMaxSegmentLengthMm = source.ColumnMaxSegmentLengthMm;
            target.ColumnEnableMerge = source.ColumnEnableMerge;
            target.ColumnMergeTolMm = source.ColumnMergeTolMm;
            target.ColumnMergeStrategy = source.ColumnMergeStrategy;
            target.ColumnDedupePlacedTolMm = source.ColumnDedupePlacedTolMm;
            target.ColumnAreaWeight = source.ColumnAreaWeight;
            target.ColumnSegmentCountWeight = source.ColumnSegmentCountWeight;
            target.ColumnRectnessWeight = source.ColumnRectnessWeight;
            target.ColumnLongLinePenalty = source.ColumnLongLinePenalty;
            target.ColumnIrregularEnable = source.ColumnIrregularEnable;
            target.ColumnIrregularMaxSizeMm = source.ColumnIrregularMaxSizeMm;
            target.ColumnIrregularGapTolMm = source.ColumnIrregularGapTolMm;
            target.ColumnIrregularMinAreaM2 = source.ColumnIrregularMinAreaM2;
            target.ColumnAttachToWallEnable = source.ColumnAttachToWallEnable;
            target.ColumnAttachToWallSnapTolMm = source.ColumnAttachToWallSnapTolMm;
            target.ColumnAttachToWallTarget = source.ColumnAttachToWallTarget;
            target.ColumnAttachToWallAllowOverlap = source.ColumnAttachToWallAllowOverlap;
            target.ColumnDebugDrawCandidates = source.ColumnDebugDrawCandidates;
            target.ColumnDebugDrawClusterId = source.ColumnDebugDrawClusterId;
            target.ColumnDebugDrawRejectReason = source.ColumnDebugDrawRejectReason;
            target.ColumnDebugExportReport = source.ColumnDebugExportReport;
            target.Juncture = new JunctureSettings
            {
                IgnoreSmallerThanMm = juncture.IgnoreSmallerThanMm,
                MinJunctureWidthMm = juncture.MinJunctureWidthMm,
                IgnoreLargerThanMm = juncture.IgnoreLargerThanMm,
                MaxJunctureWidthMm = juncture.MaxJunctureWidthMm
            };

            if (source.ParameterMappings != null)
            {
                foreach (ParameterMapping mapping in source.ParameterMappings)
                {
                    if (mapping == null)
                    {
                        continue;
                    }

                    target.ParameterMappings.Add(new ParameterMapping
                    {
                        ParameterName = mapping.ParameterName,
                        StorageType = mapping.StorageType,
                        Value = mapping.Value
                    });
                }
            }

            return target;
        }

        private sealed class SessionEntry
        {
            public string ContextSignature { get; set; }

            public List<MapRow> MapRows { get; set; }
        }
    }
}



