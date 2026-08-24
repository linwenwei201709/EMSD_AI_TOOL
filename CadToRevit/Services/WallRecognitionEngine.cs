using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Models.Mapping;
using CadToRevit.Services.Config;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Topology;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class WallRecognitionEngine
    {
        // Hard rule threshold for true short single-line walls using side-biased placement.
        private const double SingleLineShortWallBiasMaxLenMm = 1000.0;
        // Accept small geometric conversion loss around the configured overlap threshold.
        private static readonly double OverlapThresholdToleranceFt = UnitUtils.ConvertToInternalUnits(1.5, UnitTypeId.Millimeters);
        // Guard Union mode so a single tiny stub cannot inflate into a full-length thick wall.
        private const double UnionGuardShortSideRatio = 0.65;
        private const double UnionGuardInflationRatio = 1.80;
        private const double UnionGuardMinCoverageRatio = 0.18;
        private const double UnionGuardMinOverlapMultiple = 2.50;
        private const int UnionGuardMinSupportingSegments = 3;

        public static WallRecognitionResult RecognizeWalls(
            List<CadSegment> segments,
            ISet<string> selectedRawLayers)
        {
            return RecognizeWalls(segments, selectedRawLayers, null);
        }


        public static WallRecognitionResult RecognizeWalls(
            List<CadSegment> segments,
            ISet<string> selectedRawLayers,
            AdvancedSettingsRow rowSettings)
        {
            return RecognizeWalls(segments, selectedRawLayers, rowSettings, null);
        }


        public static WallRecognitionResult RecognizeWalls(
            List<CadSegment> segments,
            ISet<string> selectedRawLayers,
            AdvancedSettingsRow rowSettings,
            double? expectedWidthMm)
        {
            if (selectedRawLayers == null || selectedRawLayers.Count == 0)
            {
                return RecognizeWalls(segments);
            }

            List<CadSegment> filtered = segments == null
                ? new List<CadSegment>()
                : segments.Where(s => s != null &&
                                      !s.IsArc &&
                                      !string.IsNullOrWhiteSpace(s.RawLayerName) &&
                                      selectedRawLayers.Contains(s.RawLayerName))
                          .ToList();

            List<string> rawLayerCounts = filtered
                .GroupBy(s => s.RawLayerName)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Select(x => x.Name + ":" + x.Count)
                .ToList();
            DiagnosticRecorder.AppendDebug(
                "[WallRecognize] SelectedLayers=" + string.Join(",", selectedRawLayers) +
                ", SegmentCount=" + filtered.Count +
                ", RawLayerNameCounts=" + string.Join(", ", rawLayerCounts));

            return RecognizeFromWallSegments(filtered, rowSettings, expectedWidthMm, selectedRawLayers);
        }


        public static WallRecognitionResult RecognizeWalls(List<CadSegment> segments)
        {
            WallRecognitionResult result = new WallRecognitionResult();
            if (segments == null)
            {
                return result;
            }

            List<CadSegment> wallSegments = segments
                .Where(x => !x.IsArc)
                .Where(x => string.Equals(x.SemanticLayer, "WALL", StringComparison.OrdinalIgnoreCase))
                .Where(IsValidLineSegment)
                .ToList();
            return RecognizeFromWallSegments(wallSegments, null, null, null);
        }


        private static WallRecognitionResult RecognizeFromWallSegments(
            List<CadSegment> wallSegments,
            AdvancedSettingsRow rowSettings,
            double? expectedWidthMm,
            ISet<string> selectedRawLayers)
        {
            WallRecognitionResult result = new WallRecognitionResult();
            WallRecognitionConfig cfg = WallRecognitionConfigProvider.Load();
            WallRecognitionConfigDerived cfgFt = WallRecognitionConfigProvider.LoadDerived(selectedRawLayers);


            ApplyRowOverrides(cfgFt, rowSettings);
            ApplyJunctureOverrides(cfgFt, rowSettings != null ? rowSettings.Juncture : null);
            wallSegments = (wallSegments ?? new List<CadSegment>())
                .Where(IsValidLineSegment)
                .ToList();
            result.TotalWallSegments = wallSegments.Count;
            if (wallSegments.Count == 0)
            {
                return result;
            }

            double targetThicknessMm = expectedWidthMm.HasValue && expectedWidthMm.Value > 0
                ? expectedWidthMm.Value
                : (rowSettings != null && rowSettings.WallDefaultSingleWallThicknessMm.HasValue && rowSettings.WallDefaultSingleWallThicknessMm.Value > 0
                    ? rowSettings.WallDefaultSingleWallThicknessMm.Value
                    : cfg.DefaultSingleWallThicknessMm);

            double minOverlapMmDefault = 300.0;
            bool enableAutoThickness = rowSettings == null || !rowSettings.WallEnableAutoDoubleLineThickness.HasValue
                ? true
                : rowSettings.WallEnableAutoDoubleLineThickness.Value;
            int autoTopK = Math.Max(1, rowSettings != null && rowSettings.WallAutoThicknessTopK.HasValue ? rowSettings.WallAutoThicknessTopK.Value : 3);
            double autoBinMm = Math.Max(1.0, rowSettings != null && rowSettings.WallAutoThicknessBinMm.HasValue ? rowSettings.WallAutoThicknessBinMm.Value : 10.0);
            double minDoubleThicknessMm = Math.Max(1.0, rowSettings != null && rowSettings.WallMinDoubleLineThicknessMm.HasValue ? rowSettings.WallMinDoubleLineThicknessMm.Value : 60.0);
            double minDoubleOverlapMm = Math.Max(1.0, rowSettings != null && rowSettings.WallMinDoubleLineOverlapLenMm.HasValue ? rowSettings.WallMinDoubleLineOverlapLenMm.Value : minOverlapMmDefault);
            double adaptiveContainTolMm = Math.Max(1.0, rowSettings != null && rowSettings.WallDoubleLineAdaptiveContainTolMm.HasValue ? rowSettings.WallDoubleLineAdaptiveContainTolMm.Value : 100.0);
            double adaptiveExtendMaxMm = Math.Max(1.0, rowSettings != null && rowSettings.WallDoubleLineAdaptiveExtendMaxMm.HasValue ? rowSettings.WallDoubleLineAdaptiveExtendMaxMm.Value : 600.0);
            WallDoubleLineLengthPolicy lengthPolicy = ResolveDoubleLineLengthPolicy(rowSettings != null ? rowSettings.WallDoubleLineLengthPolicy : null);
            double maxDoubleThicknessMm = UnitUtils.ConvertFromInternalUnits(cfgFt.MaxWallThicknessFt, UnitTypeId.Millimeters);
            if (maxDoubleThicknessMm < minDoubleThicknessMm)
            {
                maxDoubleThicknessMm = minDoubleThicknessMm;
            }
            // Force single-line mode bypasses double-line centerline generation.
            bool forceSingleLineMode = rowSettings != null && rowSettings.WallForceSingleLineMode == true;
            bool useInsideFacePlacement = string.Equals(
                rowSettings != null ? rowSettings.WallDoubleLineSingleWallPlaceMode : null,
                AdvancedSettingsRow.WallPlaceModeInsideFaceOnCadLine,
                StringComparison.OrdinalIgnoreCase);

            // Build pair tags when inside-face mode is enabled so degraded single walls can be offset safely.
            Dictionary<int, PairTag> pairTags = useInsideFacePlacement
                ? BuildSingleLinePairTags(
                    wallSegments,
                    cfgFt.ParallelAngleTolDeg,
                    MmToFt(minDoubleThicknessMm),
                    MmToFt(maxDoubleThicknessMm),
                    MmToFt(minDoubleOverlapMm))
                : new Dictionary<int, PairTag>();

            List<WallCenterlineCandidate> stage1All = new List<WallCenterlineCandidate>();
            if (!forceSingleLineMode && enableAutoThickness)
            {
                List<WallCenterlineDetector.PairMeasurement> measurements = WallCenterlineDetector.ScanPairMeasurements(
                    wallSegments,
                    cfgFt.ParallelAngleTolDeg,
                    MmToFt(minDoubleThicknessMm),
                    MmToFt(maxDoubleThicknessMm),
                    MmToFt(minDoubleOverlapMm));
                List<double> peaks = BuildThicknessPeaks(measurements, autoBinMm, autoTopK);
                if (peaks.Count == 0)
                {
                    peaks.Add(targetThicknessMm);
                }

                DiagnosticRecorder.AppendDebug("[WallRecognize] AutoThicknessPeaks=[" + string.Join(", ", peaks.Select(x => x.ToString("F1"))) + "]");
                foreach (double peak in peaks)
                {
                    WallDetectSettings autoSettings = new WallDetectSettings
                    {
                        TargetThicknessFt = MmToFt(peak),
                        ThicknessTolFt = cfgFt.WallThicknessTolFt,
                        ParallelAngleTolDeg = cfgFt.ParallelAngleTolDeg,
                        MinOverlapFt = MmToFt(minDoubleOverlapMm),
                        DoubleLineLengthPolicy = lengthPolicy,
                        AdaptiveContainTolFt = MmToFt(adaptiveContainTolMm),
                        AdaptiveExtendMaxFt = MmToFt(adaptiveExtendMaxMm)
                    };
                    WallDetectResult stageAuto = WallCenterlineDetector.Detect(wallSegments, autoSettings);
                    stage1All.AddRange(stageAuto.Centerlines);
                    DiagnosticRecorder.AppendDebug("[WallRecognize] Peak=" + peak.ToString("F1") + "mm, Centerlines=" + stageAuto.Centerlines.Count);
                }
            }
            else if (!forceSingleLineMode)
            {
                WallDetectSettings s1 = new WallDetectSettings
                {
                    TargetThicknessFt = MmToFt(targetThicknessMm),
                    ThicknessTolFt = cfgFt.WallThicknessTolFt,
                    ParallelAngleTolDeg = cfgFt.ParallelAngleTolDeg,
                    MinOverlapFt = MmToFt(minDoubleOverlapMm),
                    DoubleLineLengthPolicy = lengthPolicy,
                    AdaptiveContainTolFt = MmToFt(adaptiveContainTolMm),
                    AdaptiveExtendMaxFt = MmToFt(adaptiveExtendMaxMm)
                };
                WallDetectResult stage1 = WallCenterlineDetector.Detect(wallSegments, s1);
                stage1All.AddRange(stage1.Centerlines);
            }

            if (lengthPolicy == WallDoubleLineLengthPolicy.Union)
            {
                stage1All = FilterUnionModeCenterlines(
                    stage1All,
                    wallSegments,
                    cfgFt.ParallelAngleTolDeg,
                    cfgFt.WallThicknessTolFt,
                    MmToFt(minDoubleOverlapMm),
                    MmToFt(adaptiveExtendMaxMm));
            }

            HashSet<int> extraConsumedSegmentIds = lengthPolicy == WallDoubleLineLengthPolicy.Union
                ? CollectUnionModeExtraConsumedSegmentIds(
                    stage1All,
                    wallSegments,
                    cfgFt.ParallelAngleTolDeg,
                    cfgFt.WallThicknessTolFt,
                    MmToFt(minDoubleOverlapMm),
                    MmToFt(adaptiveExtendMaxMm))
                : new HashSet<int>();

            result.TypeADoubleLineWalls = stage1All.Count;

            HashSet<int> usedSegmentIds = new HashSet<int>();
            foreach (WallCenterlineCandidate c in stage1All)
            {
                if (c.SideA != null)
                {
                    usedSegmentIds.Add(c.SideA.SegmentId);
                }

                if (c.SideB != null)
                {
                    usedSegmentIds.Add(c.SideB.SegmentId);
                }
            }

            foreach (int id in extraConsumedSegmentIds)
            {
                usedSegmentIds.Add(id);
            }

            HashSet<int> suspiciousSegmentIds = forceSingleLineMode
                ? new HashSet<int>()
                : FindSuspiciousDoubleLineSegments(
                    wallSegments,
                    usedSegmentIds,
                    cfgFt.ParallelAngleTolDeg,
                    MmToFt(minDoubleThicknessMm),
                    MmToFt(maxDoubleThicknessMm),
                    MmToFt(minDoubleOverlapMm));
            List<WallCenterlineCandidate> stage2 = BuildSingleLineCandidates(
                wallSegments,
                usedSegmentIds,
                suspiciousSegmentIds,
                cfgFt.MinWallLengthFt,
                targetThicknessMm,
                pairTags,
                useInsideFacePlacement,
                cfgFt.ParallelAngleTolDeg,
                cfgFt.EndpointMergeTolFt);
            int filteredByMinLength = wallSegments.Count(s =>
                s != null &&
                !usedSegmentIds.Contains(s.SegmentId) &&
                s.P0 != null &&
                s.P1 != null &&
                s.P0.DistanceTo(s.P1) < cfgFt.MinWallLengthFt);
            result.TypeBSingleLineWalls = stage2.Count;
            DiagnosticRecorder.AppendDebug(
                "[WallRecognize] Stage1DoubleLine=" + result.TypeADoubleLineWalls +
                ", Stage2SingleLine=" + result.TypeBSingleLineWalls +
                ", SuspiciousDoubleLineEdges=" + suspiciousSegmentIds.Count +
                ", FilteredByMinLength=" + filteredByMinLength +
                ", TargetThicknessMm=" + targetThicknessMm.ToString("F2") +
                ", TolMm=" + cfg.WallThicknessTolMm.ToString("F2"));

            List<WallCenterlineCandidate> all = new List<WallCenterlineCandidate>();
            all.AddRange(stage1All);
            all.AddRange(stage2);


            List<WallCenterlineCandidate> merged = cfgFt.EnableMergeCollinear
                ? MergeCenterlines(all, cfgFt.EndpointMergeTolFt, cfgFt.ParallelAngleTolDeg, cfg.WallThicknessTolMm)
                : CloneCenterlineCandidates(all);
            WallTopologyRefineResult refined = WallTopologyRefiner.Refine(merged, cfgFt);
            result.MergedWalls = merged.Count;
            result.RefinedWalls = refined.Centerlines.Count;
            result.ClusteredEndpointCount = refined.ClusteredEndpointCount;
            result.ExtendedEndpointCount = refined.ExtendedEndpointCount;
            result.DuplicateRemovedCount = refined.DuplicateRemovedCount;
            result.OffAxisSnappedCount = refined.OffAxisSnappedCount;
            result.CollinearMergedCount = refined.CollinearMergedCount;
            result.Centerlines = refined.Centerlines;
            DiagnosticRecorder.AppendDebug(
                "[WallStats] Merged=" + result.MergedWalls +
                ", Refined=" + result.RefinedWalls +
                ", Clustered=" + result.ClusteredEndpointCount +
                ", ExtendedTotal=" + result.ExtendedEndpointCount +
                ", CollinearMerged=" + result.CollinearMergedCount +
                ", DuplicateRemoved=" + result.DuplicateRemovedCount);
            return result;
        }


        private static void ApplyJunctureOverrides(
            WallRecognitionConfigDerived cfgFt,
            JunctureSettings juncture)
        {
            if (cfgFt == null || juncture == null)
            {
                return;
            }

            double ignoreSmall = MmToFt(juncture.IgnoreSmallerThanMm);
            double minWidth = MmToFt(juncture.MinJunctureWidthMm);
            double ignoreLarge = MmToFt(juncture.IgnoreLargerThanMm);
            double maxWidth = MmToFt(juncture.MaxJunctureWidthMm);

            if (ignoreSmall > 0)
            {
                cfgFt.TopologyIgnoreSmallerThanFt = ignoreSmall;
            }

            if (minWidth > 0)
            {
                cfgFt.TopologyMinJunctureWidthFt = minWidth;
                cfgFt.TopologyEndpointClusterTolFt = minWidth;
            }

            if (ignoreLarge > 0)
            {
                cfgFt.TopologyIgnoreLargerThanFt = ignoreLarge;
            }

            if (maxWidth > 0)
            {
                cfgFt.TopologyMaxJunctureWidthFt = maxWidth;
                cfgFt.TopologyExtendSearchTolFt = maxWidth;
            }
        }


        private static void ApplyRowOverrides(
            WallRecognitionConfigDerived cfgFt,
            AdvancedSettingsRow rowSettings)
        {

            if (cfgFt == null || rowSettings == null || !rowSettings.EnableLayerOverride)
            {
                return;
            }

            if (rowSettings.WallMinWallLengthMm.HasValue && rowSettings.WallMinWallLengthMm.Value > 0)
            {
                cfgFt.MinWallLengthFt = MmToFt(rowSettings.WallMinWallLengthMm.Value);
            }

            if (rowSettings.WallThicknessTolMm.HasValue && rowSettings.WallThicknessTolMm.Value > 0)
            {
                cfgFt.WallThicknessTolFt = MmToFt(rowSettings.WallThicknessTolMm.Value);
            }

            if (rowSettings.WallMaxWallThicknessMm.HasValue && rowSettings.WallMaxWallThicknessMm.Value > 0)
            {
                cfgFt.MaxWallThicknessFt = MmToFt(rowSettings.WallMaxWallThicknessMm.Value);
            }

            if (rowSettings.WallDefaultSingleWallThicknessMm.HasValue && rowSettings.WallDefaultSingleWallThicknessMm.Value > 0)
            {
                cfgFt.DefaultSingleWallThicknessFt = MmToFt(rowSettings.WallDefaultSingleWallThicknessMm.Value);
            }

            if (rowSettings.WallParallelAngleTolDeg.HasValue && rowSettings.WallParallelAngleTolDeg.Value > 0)
            {
                cfgFt.ParallelAngleTolDeg = rowSettings.WallParallelAngleTolDeg.Value;
            }

            if (rowSettings.WallEndpointMergeTolMm.HasValue && rowSettings.WallEndpointMergeTolMm.Value > 0)
            {
                cfgFt.EndpointMergeTolFt = MmToFt(rowSettings.WallEndpointMergeTolMm.Value);
            }

            if (rowSettings.WallArcThicknessTolMm.HasValue && rowSettings.WallArcThicknessTolMm.Value > 0)
            {
                cfgFt.ArcThicknessTolFt = MmToFt(rowSettings.WallArcThicknessTolMm.Value);
            }

            if (rowSettings.WallEndpointClusterTolMm.HasValue && rowSettings.WallEndpointClusterTolMm.Value > 0)
            {
                cfgFt.TopologyEndpointClusterTolFt = MmToFt(rowSettings.WallEndpointClusterTolMm.Value);
            }

            if (rowSettings.WallExtendSearchTolMm.HasValue && rowSettings.WallExtendSearchTolMm.Value > 0)
            {
                cfgFt.TopologyExtendSearchTolFt = MmToFt(rowSettings.WallExtendSearchTolMm.Value);
            }

            if (rowSettings.WallDuplicateTolMm.HasValue && rowSettings.WallDuplicateTolMm.Value > 0)
            {
                cfgFt.TopologyDuplicateTolFt = MmToFt(rowSettings.WallDuplicateTolMm.Value);
            }

            if (rowSettings.WallAngleSnapDeg.HasValue && rowSettings.WallAngleSnapDeg.Value > 0)
            {
                cfgFt.TopologyAngleSnapDeg = rowSettings.WallAngleSnapDeg.Value;
            }

            if (rowSettings.WallEnableOrthogonalSnap.HasValue)
            {
                cfgFt.EnableTopologyOrthogonalSnap = rowSettings.WallEnableOrthogonalSnap.Value;
            }

            if (rowSettings.WallEnableExtendToIntersection.HasValue)
            {
                cfgFt.EnableTopologyExtendToIntersection = rowSettings.WallEnableExtendToIntersection.Value;
            }

            if (rowSettings.WallEnableEndpointClustering.HasValue)
            {
                cfgFt.EnableTopologyEndpointClustering = rowSettings.WallEnableEndpointClustering.Value;
            }

            if (rowSettings.WallEnableDuplicateRemoval.HasValue)
            {
                cfgFt.EnableTopologyDuplicateRemoval = rowSettings.WallEnableDuplicateRemoval.Value;
            }

            if (rowSettings.WallEnableExtendCollinear.HasValue)
            {
                cfgFt.EnableTopologyExtendCollinear = rowSettings.WallEnableExtendCollinear.Value;
            }

            if (rowSettings.WallEnableMergeCollinear.HasValue)
            {
                cfgFt.EnableMergeCollinear = rowSettings.WallEnableMergeCollinear.Value;
            }

            if (rowSettings.WallExtendCollinearTolMm.HasValue && rowSettings.WallExtendCollinearTolMm.Value > 0)
            {
                cfgFt.TopologyExtendCollinearTolFt = MmToFt(rowSettings.WallExtendCollinearTolMm.Value);
            }

            if (rowSettings.WallCollinearOffsetTolMm.HasValue && rowSettings.WallCollinearOffsetTolMm.Value > 0)
            {
                cfgFt.TopologyCollinearOffsetTolFt = MmToFt(rowSettings.WallCollinearOffsetTolMm.Value);
            }

            if (rowSettings.WallExtendProjectionTolMm.HasValue && rowSettings.WallExtendProjectionTolMm.Value > 0)
            {
                cfgFt.TopologyExtendProjectionTolFt = MmToFt(rowSettings.WallExtendProjectionTolMm.Value);
            }

            if (rowSettings.WallUseDirectionalClustering.HasValue)
            {
                cfgFt.TopologyUseDirectionalClustering = rowSettings.WallUseDirectionalClustering.Value;
            }
        }


        private static double MmToFt(double mm)
        {
            return mm > 0 ? mm / 304.8 : 0.0;
        }

        private static WallDoubleLineLengthPolicy ResolveDoubleLineLengthPolicy(string policy)
        {
            if (string.Equals(policy, AdvancedSettingsRow.WallDoubleLineLengthPolicyOverlap, StringComparison.OrdinalIgnoreCase))
            {
                return WallDoubleLineLengthPolicy.Overlap;
            }

            if (string.Equals(policy, AdvancedSettingsRow.WallDoubleLineLengthPolicyLongerSide, StringComparison.OrdinalIgnoreCase))
            {
                return WallDoubleLineLengthPolicy.LongerSide;
            }

            if (string.Equals(policy, AdvancedSettingsRow.WallDoubleLineLengthPolicyUnion, StringComparison.OrdinalIgnoreCase))
            {
                return WallDoubleLineLengthPolicy.Union;
            }

            return WallDoubleLineLengthPolicy.Union;
        }

        private static List<WallCenterlineCandidate> FilterUnionModeCenterlines(
            List<WallCenterlineCandidate> stage1All,
            List<CadSegment> wallSegments,
            double parallelAngleTolDeg,
            double wallThicknessTolFt,
            double minOverlapFt,
            double maxGapFt)
        {
            List<WallCenterlineCandidate> result = new List<WallCenterlineCandidate>();
            if (stage1All == null || stage1All.Count == 0 || wallSegments == null || wallSegments.Count == 0)
            {
                return stage1All ?? result;
            }

            HashSet<int> stage1UsedIds = new HashSet<int>();
            foreach (WallCenterlineCandidate c in stage1All)
            {
                if (c?.SideA != null)
                {
                    stage1UsedIds.Add(c.SideA.SegmentId);
                }

                if (c?.SideB != null)
                {
                    stage1UsedIds.Add(c.SideB.SegmentId);
                }
            }

            double cosTol = Math.Cos(parallelAngleTolDeg * Math.PI / 180.0);
            foreach (WallCenterlineCandidate c in stage1All)
            {
                if (!RequiresUnionCoverageGuard(c))
                {
                    result.Add(c);
                    continue;
                }

                CadSegment dominant;
                CadSegment mate;
                if (!TryGetDominantAndMate(c, out dominant, out mate))
                {
                    result.Add(c);
                    continue;
                }

                XYZ dominantDir = Normalize(dominant.P1 - dominant.P0);
                XYZ mateDir = Normalize(mate.P1 - mate.P0);
                if (Math.Abs(Dot(dominantDir, mateDir)) < cosTol)
                {
                    result.Add(c);
                    continue;
                }

                ProjectedInterval candidateSpan = ComputeProjectedInterval(c.CenterLine.GetEndPoint(0), c.CenterLine.GetEndPoint(1), dominantDir);
                if (!candidateSpan.IsValid || candidateSpan.LengthFt <= 1e-6)
                {
                    result.Add(c);
                    continue;
                }

                double mateSideDistanceFt = ComputeSignedDistanceToLine(dominant, mate.P0);
                if (Math.Abs(mateSideDistanceFt) <= 1e-6)
                {
                    result.Add(c);
                    continue;
                }

                double targetThicknessFt = MmToFt(c.ThicknessMm);
                HashSet<int> excludedIds = new HashSet<int>(stage1UsedIds);
                excludedIds.Remove(dominant.SegmentId);
                excludedIds.Remove(mate.SegmentId);
                List<SegmentInterval> siblingIntervals = CollectSiblingIntervalsOnMateSide(
                    wallSegments,
                    excludedIds,
                    dominant,
                    dominantDir,
                    mateSideDistanceFt,
                    targetThicknessFt,
                    wallThicknessTolFt,
                    candidateSpan,
                    minOverlapFt,
                    cosTol);
                SegmentInterval mateInterval = CreateSegmentInterval(mate, dominantDir, candidateSpan);
                if (mateInterval == null)
                {
                    continue;
                }

                List<SegmentInterval> coverageIntervals = new List<SegmentInterval> { mateInterval };
                coverageIntervals.AddRange(siblingIntervals);
                CoverageSummary coverage = ComputeMergedCoverageAndMaxGap(coverageIntervals, candidateSpan);
                int supportSegmentCount = coverageIntervals.Count(x => x != null && x.OverlapFt > 1e-6);
                double requiredCoveredFt = Math.Max(
                    minOverlapFt * UnionGuardMinOverlapMultiple,
                    candidateSpan.LengthFt * UnionGuardMinCoverageRatio);
                double allowedGapFt = Math.Max(maxGapFt * 4.0, candidateSpan.LengthFt * 0.45);
                bool accepted = supportSegmentCount >= UnionGuardMinSupportingSegments &&
                                coverage.CoveredLengthFt + OverlapThresholdToleranceFt >= requiredCoveredFt &&
                                coverage.MaxInternalGapFt <= allowedGapFt;

                DiagnosticRecorder.AppendDebug(
                    "[UnionGuard] CandidateThicknessMm=" + c.ThicknessMm.ToString("F1") +
                    ", DominantLenMm=" + UnitUtils.ConvertFromInternalUnits(dominant.P0.DistanceTo(dominant.P1), UnitTypeId.Millimeters).ToString("F1") +
                    ", MateLenMm=" + UnitUtils.ConvertFromInternalUnits(mate.P0.DistanceTo(mate.P1), UnitTypeId.Millimeters).ToString("F1") +
                    ", SupportSegments=" + supportSegmentCount +
                    ", CoveredMm=" + UnitUtils.ConvertFromInternalUnits(coverage.CoveredLengthFt, UnitTypeId.Millimeters).ToString("F1") +
                    ", RequiredCoveredMm=" + UnitUtils.ConvertFromInternalUnits(requiredCoveredFt, UnitTypeId.Millimeters).ToString("F1") +
                    ", MaxGapMm=" + UnitUtils.ConvertFromInternalUnits(coverage.MaxInternalGapFt, UnitTypeId.Millimeters).ToString("F1") +
                    ", Accepted=" + accepted);

                if (accepted)
                {
                    result.Add(c);
                }
            }

            return result;
        }

        private static bool RequiresUnionCoverageGuard(WallCenterlineCandidate candidate)
        {
            if (candidate?.SideA == null || candidate.SideB == null || candidate.CenterLine == null)
            {
                return false;
            }

            double lenA = candidate.SideA.P0.DistanceTo(candidate.SideA.P1);
            double lenB = candidate.SideB.P0.DistanceTo(candidate.SideB.P1);
            double shorter = Math.Min(lenA, lenB);
            double longer = Math.Max(lenA, lenB);
            if (shorter <= 1e-6 || longer <= 1e-6)
            {
                return false;
            }

            double ratio = shorter / longer;
            double spanLen = candidate.CenterLine.Length;
            double overlapFt = UnitUtils.ConvertToInternalUnits(candidate.OverlapLengthMm, UnitTypeId.Millimeters);
            double inflationRatio = overlapFt > 1e-6 ? spanLen / overlapFt : double.MaxValue;
            return ratio < UnionGuardShortSideRatio && inflationRatio > UnionGuardInflationRatio;
        }

        private static HashSet<int> CollectUnionModeExtraConsumedSegmentIds(
            List<WallCenterlineCandidate> stage1All,
            List<CadSegment> wallSegments,
            double parallelAngleTolDeg,
            double wallThicknessTolFt,
            double minOverlapFt,
            double maxGapFt)
        {
            HashSet<int> result = new HashSet<int>();
            if (stage1All == null || wallSegments == null || stage1All.Count == 0 || wallSegments.Count == 0)
            {
                return result;
            }

            HashSet<int> stage1UsedIds = new HashSet<int>();
            foreach (WallCenterlineCandidate c in stage1All)
            {
                if (c?.SideA != null)
                {
                    stage1UsedIds.Add(c.SideA.SegmentId);
                }

                if (c?.SideB != null)
                {
                    stage1UsedIds.Add(c.SideB.SegmentId);
                }
            }

            double cosTol = Math.Cos(parallelAngleTolDeg * Math.PI / 180.0);
            foreach (WallCenterlineCandidate c in stage1All)
            {
                if (c?.SideA == null || c.SideB == null || c.CenterLine == null || c.CenterLine.Length <= 1e-6)
                {
                    continue;
                }

                CadSegment dominant;
                CadSegment mate;
                if (!TryGetDominantAndMate(c, out dominant, out mate))
                {
                    continue;
                }

                XYZ dominantDir = Normalize(dominant.P1 - dominant.P0);
                XYZ mateDir = Normalize(mate.P1 - mate.P0);
                if (Math.Abs(Dot(dominantDir, mateDir)) < cosTol)
                {
                    continue;
                }

                ProjectedInterval candidateSpan = ComputeProjectedInterval(c.CenterLine.GetEndPoint(0), c.CenterLine.GetEndPoint(1), dominantDir);
                if (!candidateSpan.IsValid || candidateSpan.LengthFt <= 1e-6)
                {
                    continue;
                }

                double mateSideDistanceFt = ComputeSignedDistanceToLine(dominant, mate.P0);
                if (Math.Abs(mateSideDistanceFt) <= 1e-6)
                {
                    continue;
                }

                double targetThicknessFt = MmToFt(c.ThicknessMm);
                List<SegmentInterval> siblingIntervals = CollectSiblingIntervalsOnMateSide(
                    wallSegments,
                    stage1UsedIds,
                    dominant,
                    dominantDir,
                    mateSideDistanceFt,
                    targetThicknessFt,
                    wallThicknessTolFt,
                    candidateSpan,
                    minOverlapFt,
                    cosTol);
                if (siblingIntervals.Count == 0)
                {
                    continue;
                }

                SegmentInterval mateInterval = CreateSegmentInterval(mate, dominantDir, candidateSpan);
                if (mateInterval == null)
                {
                    continue;
                }

                List<SegmentInterval> coverageIntervals = new List<SegmentInterval> { mateInterval };
                coverageIntervals.AddRange(siblingIntervals);

                CoverageSummary coverage = ComputeMergedCoverageAndMaxGap(coverageIntervals, candidateSpan);
                if (coverage.CoverageRatio < 0.80 || coverage.MaxInternalGapFt > maxGapFt)
                {
                    continue;
                }

                foreach (SegmentInterval sibling in siblingIntervals)
                {
                    result.Add(sibling.Segment.SegmentId);
                }
            }

            return result;
        }

        private static bool TryGetDominantAndMate(WallCenterlineCandidate candidate, out CadSegment dominant, out CadSegment mate)
        {
            dominant = null;
            mate = null;
            if (candidate?.SideA == null || candidate.SideB == null)
            {
                return false;
            }

            double lenA = candidate.SideA.P0.DistanceTo(candidate.SideA.P1);
            double lenB = candidate.SideB.P0.DistanceTo(candidate.SideB.P1);
            if (lenA >= lenB)
            {
                dominant = candidate.SideA;
                mate = candidate.SideB;
            }
            else
            {
                dominant = candidate.SideB;
                mate = candidate.SideA;
            }

            return IsValidLineSegment(dominant) && IsValidLineSegment(mate);
        }

        private static ProjectedInterval ComputeProjectedInterval(XYZ p0, XYZ p1, XYZ direction)
        {
            double s0 = Dot(p0, direction);
            double s1 = Dot(p1, direction);
            if (s0 > s1)
            {
                Swap(ref s0, ref s1);
            }

            return new ProjectedInterval
            {
                StartFt = s0,
                EndFt = s1
            };
        }

        private static List<SegmentInterval> CollectSiblingIntervalsOnMateSide(
            List<CadSegment> wallSegments,
            HashSet<int> stage1UsedIds,
            CadSegment dominant,
            XYZ dominantDir,
            double mateSideDistanceFt,
            double targetThicknessFt,
            double wallThicknessTolFt,
            ProjectedInterval candidateSpan,
            double minOverlapFt,
            double cosTol)
        {
            List<SegmentInterval> result = new List<SegmentInterval>();
            foreach (CadSegment s in wallSegments)
            {
                if (!IsValidLineSegment(s) || stage1UsedIds.Contains(s.SegmentId))
                {
                    continue;
                }

                XYZ sDir = Normalize(s.P1 - s.P0);
                if (Math.Abs(Dot(dominantDir, sDir)) < cosTol)
                {
                    continue;
                }

                double signedDistanceFt = ComputeSignedDistanceToLine(dominant, s.P0);
                if (mateSideDistanceFt * signedDistanceFt <= 1e-6)
                {
                    continue;
                }

                if (Math.Abs(Math.Abs(signedDistanceFt) - targetThicknessFt) > wallThicknessTolFt)
                {
                    continue;
                }

                SegmentInterval interval = CreateSegmentInterval(s, dominantDir, candidateSpan);
                if (interval == null || interval.OverlapFt + OverlapThresholdToleranceFt < minOverlapFt)
                {
                    continue;
                }

                result.Add(interval);
            }

            return result;
        }

        private static CoverageSummary ComputeMergedCoverageAndMaxGap(
            List<SegmentInterval> intervals,
            ProjectedInterval candidateSpan)
        {
            CoverageSummary result = new CoverageSummary();
            if (intervals == null || intervals.Count == 0 || !candidateSpan.IsValid || candidateSpan.LengthFt <= 1e-6)
            {
                return result;
            }

            List<ProjectedInterval> ordered = intervals
                .Where(x => x != null && x.Interval != null && x.Interval.IsValid && x.Interval.OverlapLengthIn(candidateSpan) > 1e-6)
                .Select(x => new ProjectedInterval
                {
                    StartFt = Math.Max(candidateSpan.StartFt, x.Interval.StartFt),
                    EndFt = Math.Min(candidateSpan.EndFt, x.Interval.EndFt)
                })
                .OrderBy(x => x.StartFt)
                .ToList();
            if (ordered.Count == 0)
            {
                return result;
            }

            double mergedLengthFt = 0.0;
            double maxGapFt = 0.0;
            double currentStart = ordered[0].StartFt;
            double currentEnd = ordered[0].EndFt;
            for (int i = 1; i < ordered.Count; i++)
            {
                ProjectedInterval next = ordered[i];
                if (next.StartFt <= currentEnd + 1e-6)
                {
                    currentEnd = Math.Max(currentEnd, next.EndFt);
                    continue;
                }

                mergedLengthFt += Math.Max(0.0, currentEnd - currentStart);
                maxGapFt = Math.Max(maxGapFt, next.StartFt - currentEnd);
                currentStart = next.StartFt;
                currentEnd = next.EndFt;
            }

            mergedLengthFt += Math.Max(0.0, currentEnd - currentStart);
            result.CoveredLengthFt = mergedLengthFt;
            result.CoverageRatio = mergedLengthFt / candidateSpan.LengthFt;
            result.MaxInternalGapFt = maxGapFt;
            return result;
        }


        private static bool IsValidLineSegment(CadSegment s)
        {
            if (s == null || s.P0 == null || s.P1 == null)
            {
                return false;
            }

            return s.P0.DistanceTo(s.P1) > 1e-6;
        }

        private static SegmentInterval CreateSegmentInterval(CadSegment segment, XYZ direction, ProjectedInterval candidateSpan)
        {
            if (!IsValidLineSegment(segment))
            {
                return null;
            }

            ProjectedInterval interval = ComputeProjectedInterval(segment.P0, segment.P1, direction);
            if (!interval.IsValid)
            {
                return null;
            }

            return new SegmentInterval
            {
                Segment = segment,
                Interval = interval,
                OverlapFt = interval.OverlapLengthIn(candidateSpan)
            };
        }

        private static double ComputeSignedDistanceToLine(CadSegment line, XYZ point)
        {
            XYZ dir = Normalize(line.P1 - line.P0);
            XYZ normal = new XYZ(-dir.Y, dir.X, 0);
            return Dot(point - line.P0, normal);
        }

        private sealed class SegmentInterval
        {
            public CadSegment Segment { get; set; }
            public ProjectedInterval Interval { get; set; }
            public double OverlapFt { get; set; }
        }

        private sealed class CoverageSummary
        {
            public double CoveredLengthFt { get; set; }
            public double CoverageRatio { get; set; }
            public double MaxInternalGapFt { get; set; }
        }

        private sealed class ProjectedInterval
        {
            public double StartFt { get; set; }
            public double EndFt { get; set; }

            public bool IsValid
            {
                get { return EndFt > StartFt + 1e-6; }
            }

            public double LengthFt
            {
                get { return Math.Max(0.0, EndFt - StartFt); }
            }

            public double OverlapLengthIn(ProjectedInterval other)
            {
                if (other == null || !IsValid || !other.IsValid)
                {
                    return 0.0;
                }

                double start = Math.Max(StartFt, other.StartFt);
                double end = Math.Min(EndFt, other.EndFt);
                return Math.Max(0.0, end - start);
            }
        }


        private static List<WallCenterlineCandidate> BuildSingleLineCandidates(
            List<CadSegment> wallSegments,
            HashSet<int> usedSegmentIds,
            HashSet<int> suspiciousSegmentIds,
            double minWallLengthFt,
            double defaultThicknessMm,
            Dictionary<int, PairTag> pairTags,
            bool useInsideFacePlacement,
            double parallelAngleTolDeg,
            double endpointTolFt)
        {

            List<WallCenterlineCandidate> result = new List<WallCenterlineCandidate>();
            Dictionary<int, CadSegment> segmentById = (wallSegments ?? new List<CadSegment>())
                .Where(x => x != null)
                .GroupBy(x => x.SegmentId)
                .ToDictionary(g => g.Key, g => g.First());
            foreach (CadSegment s in wallSegments)
            {
                if (usedSegmentIds.Contains(s.SegmentId))
                {
                    continue;
                }

                if (suspiciousSegmentIds != null && suspiciousSegmentIds.Contains(s.SegmentId))
                {
                    continue;
                }

                double lenFt = s.P0.DistanceTo(s.P1);
                if (lenFt < minWallLengthFt)
                {
                    continue;
                }

                PairTag tag = null;
                if (pairTags != null)
                {
                    pairTags.TryGetValue(s.SegmentId, out tag);
                }

                CadSegment mate = null;
                if (tag != null && tag.MateSegmentId.HasValue)
                {
                    segmentById.TryGetValue(tag.MateSegmentId.Value, out mate);
                }

                Line centerLine = Line.CreateBound(s.P0, s.P1);
                // Apply the short true-single hard rule before generic centerline placement.
                XYZ hardRuleNormal = tag == null
                    ? TryResolveShortSingleWallBiasNormal(s, wallSegments, parallelAngleTolDeg, endpointTolFt)
                    : null;
                if (hardRuleNormal != null)
                {
                    XYZ offsetDir = new XYZ(hardRuleNormal.X, hardRuleNormal.Y, 0);
                    if (offsetDir.GetLength() > 1e-9)
                    {
                        double halfThicknessFt = UnitUtils.ConvertToInternalUnits(defaultThicknessMm, UnitTypeId.Millimeters) * 0.5;
                        XYZ offset = offsetDir.Normalize().Multiply(halfThicknessFt);
                        centerLine = Line.CreateBound(s.P0 + offset, s.P1 + offset);
                    }
                }
                // Only derived-single candidates in inside-face mode are shifted to keep original CAD line on inner face.
                else if (useInsideFacePlacement && tag != null && tag.InsideNormal != null)
                {
                    XYZ inside2d = new XYZ(tag.InsideNormal.X, tag.InsideNormal.Y, 0);
                    if (inside2d.GetLength() > 1e-9)
                    {
                        XYZ offsetDir = inside2d.Normalize();
                        double halfThicknessFt = UnitUtils.ConvertToInternalUnits(defaultThicknessMm, UnitTypeId.Millimeters) * 0.5;
                        XYZ offset = offsetDir.Multiply(halfThicknessFt);
                        centerLine = Line.CreateBound(s.P0 + offset, s.P1 + offset);
                    }
                }

                result.Add(new WallCenterlineCandidate
                {
                    CenterLine = centerLine,
                    ThicknessMm = defaultThicknessMm,
                    SideA = s,
                    SideB = mate,
                    OverlapLengthMm = UnitUtils.ConvertFromInternalUnits(lenFt, UnitTypeId.Millimeters),
                    IsDoubleLinePairedSingleWall = tag != null,
                    MateSegmentId = tag != null ? tag.MateSegmentId : null,
                    InsideNormal = tag != null ? tag.InsideNormal : null
                });
            }

            return result;
        }

        // Stores endpoint-attached branch info used by the short single-line hard rule.
        private sealed class EndpointSideConnection
        {
            public CadSegment Segment { get; set; }
            public XYZ OutwardDirection { get; set; }
            public double EndpointDistanceFt { get; set; }
            public double SideScore { get; set; }
        }

        // Pair info used by force-single placement to keep wall growth inside CAD double lines.
        private sealed class PairTag
        {
            public int? MateSegmentId { get; set; }
            public XYZ InsideNormal { get; set; }
        }

        private sealed class PairEval
        {
            public CadSegment A { get; set; }
            public CadSegment B { get; set; }
            public double OverlapFt { get; set; }
            public XYZ InsideNormalA { get; set; }
        }

        private static Dictionary<int, PairTag> BuildSingleLinePairTags(
            List<CadSegment> wallSegments,
            double parallelAngleTolDeg,
            double minThicknessFt,
            double maxThicknessFt,
            double minOverlapFt)
        {
            Dictionary<int, PairTag> tags = new Dictionary<int, PairTag>();
            List<CadSegment> valid = (wallSegments ?? new List<CadSegment>())
                .Where(IsValidLineSegment)
                .ToList();
            if (valid.Count < 2)
            {
                return tags;
            }

            double cosTol = Math.Cos(parallelAngleTolDeg * Math.PI / 180.0);
            List<PairEval> evals = new List<PairEval>();
            for (int i = 0; i < valid.Count; i++)
            {
                CadSegment a = valid[i];
                XYZ da = Normalize(a.P1 - a.P0);
                XYZ na = new XYZ(-da.Y, da.X, 0);
                for (int j = i + 1; j < valid.Count; j++)
                {
                    CadSegment b = valid[j];
                    XYZ db = Normalize(b.P1 - b.P0);
                    if (Math.Abs(Dot(da, db)) < cosTol)
                    {
                        continue;
                    }

                    double signedDistanceFt = Dot(b.P0 - a.P0, na);
                    double thicknessFt = Math.Abs(signedDistanceFt);
                    if (thicknessFt < minThicknessFt || thicknessFt > maxThicknessFt)
                    {
                        continue;
                    }

                    double overlapFt = ComputeOverlapLength2D(a.P0, a.P1, b.P0, b.P1, da);
                    // Tag only degraded short double-lines:
                    // 0 < overlap < minOverlapFt (boundary with formal double-line stage).
                    if (overlapFt <= 1e-6 || overlapFt + OverlapThresholdToleranceFt >= minOverlapFt)
                    {
                        continue;
                    }

                    XYZ insideA = signedDistanceFt >= 0 ? na : na.Negate();
                    evals.Add(new PairEval
                    {
                        A = a,
                        B = b,
                        OverlapFt = overlapFt,
                        InsideNormalA = Normalize(insideA)
                    });
                }
            }

            HashSet<int> used = new HashSet<int>();
            foreach (PairEval pair in evals.OrderByDescending(x => x.OverlapFt))
            {
                if (pair == null || pair.A == null || pair.B == null)
                {
                    continue;
                }

                if (used.Contains(pair.A.SegmentId) || used.Contains(pair.B.SegmentId))
                {
                    continue;
                }

                used.Add(pair.A.SegmentId);
                used.Add(pair.B.SegmentId);
                tags[pair.A.SegmentId] = new PairTag
                {
                    MateSegmentId = pair.B.SegmentId,
                    InsideNormal = pair.InsideNormalA
                };
                tags[pair.B.SegmentId] = new PairTag
                {
                    MateSegmentId = pair.A.SegmentId,
                    InsideNormal = pair.InsideNormalA.Negate()
                };
            }

            DiagnosticRecorder.AppendDebug(
                "[PairTags] TaggedSegments=" + tags.Count +
                ", MinOverlapMm=" + UnitUtils.ConvertFromInternalUnits(minOverlapFt, UnitTypeId.Millimeters).ToString("F1"));

            return tags;
        }

        private static List<double> BuildThicknessPeaks(
            List<WallCenterlineDetector.PairMeasurement> measurements,
            double binMm,
            int topK)
        {
            Dictionary<double, double> bins = new Dictionary<double, double>();
            foreach (WallCenterlineDetector.PairMeasurement item in measurements ?? new List<WallCenterlineDetector.PairMeasurement>())
            {
                if (item == null || item.ThicknessMm <= 0 || item.OverlapMm <= 0)
                {
                    continue;
                }

                double key = Math.Round(item.ThicknessMm / binMm) * binMm;
                if (!bins.ContainsKey(key))
                {
                    bins[key] = 0.0;
                }

                bins[key] += item.OverlapMm;
            }

            return bins
                .OrderByDescending(x => x.Value)
                .Take(topK)
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToList();
        }

        // Returns the offset normal for the hard rule when a short true single-line wall
        // is connected by two near-perpendicular branches on the same side.
        private static XYZ TryResolveShortSingleWallBiasNormal(
            CadSegment shortSegment,
            List<CadSegment> wallSegments,
            double angleTolDeg,
            double endpointTolFt)
        {
            if (!IsValidLineSegment(shortSegment))
            {
                return null;
            }

            double lengthMm = UnitUtils.ConvertFromInternalUnits(shortSegment.P0.DistanceTo(shortSegment.P1), UnitTypeId.Millimeters);
            if (lengthMm > SingleLineShortWallBiasMaxLenMm + 1.0)
            {
                return null;
            }

            XYZ wallDir = Normalize2D(shortSegment.P1 - shortSegment.P0);
            XYZ wallNormal = Normalize2D(new XYZ(-wallDir.Y, wallDir.X, 0));
            EndpointSideConnection startConnection = FindEndpointSideConnection(shortSegment, shortSegment.P0, wallSegments, wallDir, wallNormal, angleTolDeg, endpointTolFt);
            EndpointSideConnection endConnection = FindEndpointSideConnection(shortSegment, shortSegment.P1, wallSegments, wallDir, wallNormal, angleTolDeg, endpointTolFt);
            if (startConnection == null || endConnection == null)
            {
                return null;
            }

            if (startConnection.Segment == null || endConnection.Segment == null)
            {
                return null;
            }

            if (startConnection.Segment.SegmentId == endConnection.Segment.SegmentId)
            {
                return null;
            }

            double parallelCosTol = Math.Cos(angleTolDeg * Math.PI / 180.0);
            XYZ startDir = Normalize2D(startConnection.OutwardDirection);
            XYZ endDir = Normalize2D(endConnection.OutwardDirection);
            if (Math.Abs(Dot(startDir, endDir)) < parallelCosTol)
            {
                return null;
            }

            double side0 = Dot(startDir, wallNormal);
            double side1 = Dot(endDir, wallNormal);
            if (Math.Abs(side0) < 1e-6 || Math.Abs(side1) < 1e-6)
            {
                return null;
            }

            if ((side0 > 0 && side1 < 0) || (side0 < 0 && side1 > 0))
            {
                return null;
            }

            return side0 > 0 ? wallNormal : wallNormal.Negate();
        }

        // Finds the best endpoint-attached branch that extends away from the short wall endpoint.
        private static EndpointSideConnection FindEndpointSideConnection(
            CadSegment shortSegment,
            XYZ endpoint,
            List<CadSegment> wallSegments,
            XYZ wallDir,
            XYZ wallNormal,
            double angleTolDeg,
            double endpointTolFt)
        {
            double perpendicularDotTol = Math.Sin(angleTolDeg * Math.PI / 180.0);
            EndpointSideConnection best = null;
            foreach (CadSegment candidate in wallSegments ?? new List<CadSegment>())
            {
                if (!IsValidLineSegment(candidate) || candidate.SegmentId == shortSegment.SegmentId)
                {
                    continue;
                }

                double distToP0 = candidate.P0.DistanceTo(endpoint);
                double distToP1 = candidate.P1.DistanceTo(endpoint);
                bool touchP0 = distToP0 <= endpointTolFt;
                bool touchP1 = distToP1 <= endpointTolFt;
                if (!touchP0 && !touchP1)
                {
                    continue;
                }

                XYZ outward;
                double endpointDistanceFt;
                if (touchP0 && (!touchP1 || distToP0 <= distToP1))
                {
                    outward = candidate.P1 - candidate.P0;
                    endpointDistanceFt = distToP0;
                }
                else
                {
                    outward = candidate.P0 - candidate.P1;
                    endpointDistanceFt = distToP1;
                }

                XYZ outward2d = Normalize2D(outward);
                if (outward2d.GetLength() <= 1e-9)
                {
                    continue;
                }

                // Reuse the existing wall angle tolerance for near-perpendicular checks.
                if (Math.Abs(Dot(outward2d, wallDir)) > perpendicularDotTol)
                {
                    continue;
                }

                double sideScore = Math.Abs(Dot(outward2d, wallNormal));
                if (sideScore <= perpendicularDotTol)
                {
                    continue;
                }

                if (best == null ||
                    endpointDistanceFt < best.EndpointDistanceFt - 1e-9 ||
                    (Math.Abs(endpointDistanceFt - best.EndpointDistanceFt) <= 1e-9 && sideScore > best.SideScore))
                {
                    best = new EndpointSideConnection
                    {
                        Segment = candidate,
                        OutwardDirection = outward2d,
                        EndpointDistanceFt = endpointDistanceFt,
                        SideScore = sideScore
                    };
                }
            }

            return best;
        }

        private static HashSet<int> FindSuspiciousDoubleLineSegments(
            List<CadSegment> wallSegments,
            HashSet<int> usedSegmentIds,
            double parallelAngleTolDeg,
            double minThicknessFt,
            double maxThicknessFt,
            double minOverlapFt)
        {
            HashSet<int> suspicious = new HashSet<int>();
            if (wallSegments == null || wallSegments.Count == 0)
            {
                return suspicious;
            }

            double cosTol = Math.Cos(parallelAngleTolDeg * Math.PI / 180.0);
            List<CadSegment> remaining = wallSegments
                .Where(IsValidLineSegment)
                .Where(x => !usedSegmentIds.Contains(x.SegmentId))
                .ToList();
            for (int i = 0; i < remaining.Count; i++)
            {
                CadSegment a = remaining[i];
                XYZ da = Normalize(a.P1 - a.P0);
                for (int j = 0; j < remaining.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    CadSegment b = remaining[j];
                    XYZ db = Normalize(b.P1 - b.P0);
                    if (Math.Abs(Dot(da, db)) < cosTol)
                    {
                        continue;
                    }

                    double thicknessFt = DistancePointToLine2D(a.P0, a.P1, b.P0);
                    if (thicknessFt < minThicknessFt || thicknessFt > maxThicknessFt)
                    {
                        continue;
                    }

                    double overlapFt = ComputeOverlapLength2D(a.P0, a.P1, b.P0, b.P1, da);
                    if (overlapFt + OverlapThresholdToleranceFt < minOverlapFt)
                    {
                        continue;
                    }

                    suspicious.Add(a.SegmentId);
                    break;
                }
            }

            return suspicious;
        }

        private static double DistancePointToLine2D(XYZ lineP0, XYZ lineP1, XYZ p)
        {
            XYZ d = Normalize(lineP1 - lineP0);
            XYZ n = new XYZ(-d.Y, d.X, 0);
            return Math.Abs(Dot(p - lineP0, n));
        }

        private static double ComputeOverlapLength2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1, XYZ aDir)
        {
            double aa0 = Dot(a0, aDir);
            double aa1 = Dot(a1, aDir);
            if (aa0 > aa1)
            {
                Swap(ref aa0, ref aa1);
            }

            double bb0 = Dot(b0, aDir);
            double bb1 = Dot(b1, aDir);
            if (bb0 > bb1)
            {
                Swap(ref bb0, ref bb1);
            }

            double start = Math.Max(aa0, bb0);
            double end = Math.Min(aa1, bb1);
            return Math.Max(0.0, end - start);
        }


        private static List<WallCenterlineCandidate> MergeCenterlines(
            List<WallCenterlineCandidate> input,
            double endpointTolFeet,
            double parallelAngleTolDeg,
            double thicknessTolMm)
        {
            if (input == null || input.Count == 0)
            {
                return new List<WallCenterlineCandidate>();
            }

            double cosTol = Math.Cos(parallelAngleTolDeg * Math.PI / 180.0);

            List<WallCenterlineCandidate> work = input
                .Where(x => x.CenterLine != null && x.CenterLine.Length > 1e-6)
                .Select(x => new WallCenterlineCandidate
                {
                    CenterLine = x.CenterLine,
                    ThicknessMm = x.ThicknessMm,
                    SideA = x.SideA,
                    SideB = x.SideB,
                    OverlapLengthMm = x.OverlapLengthMm,
                    IsDoubleLinePairedSingleWall = x.IsDoubleLinePairedSingleWall,
                    MateSegmentId = x.MateSegmentId,
                    InsideNormal = x.InsideNormal
                })
                .ToList();

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < work.Count; i++)
                {
                    for (int j = i + 1; j < work.Count; j++)
                    {
                        Line a = work[i].CenterLine;
                        Line b = work[j].CenterLine;
                        XYZ da = Normalize(a.Direction);
                        XYZ db = Normalize(b.Direction);
                        if (Math.Abs(Dot(da, db)) < cosTol)
                        {
                            continue;
                        }

                        if (!IsCollinear2D(a, b, endpointTolFeet))
                        {
                            continue;
                        }

                        if (!HasEndpointTouch(a, b, endpointTolFeet))
                        {
                            continue;
                        }

                        Line merged = MergeTwoLines2D(a, b);
                        if (merged == null)
                        {
                            continue;
                        }

                        work[i].CenterLine = merged;
                        work[i].ThicknessMm = MergeThickness(work[i].ThicknessMm, work[j].ThicknessMm, a.Length, b.Length, thicknessTolMm);
                        work[i].OverlapLengthMm += work[j].OverlapLengthMm;
                        if (!work[i].IsDoubleLinePairedSingleWall && work[j].IsDoubleLinePairedSingleWall)
                        {
                            work[i].IsDoubleLinePairedSingleWall = true;
                            work[i].MateSegmentId = work[j].MateSegmentId;
                            work[i].InsideNormal = work[j].InsideNormal;
                        }
                        work.RemoveAt(j);
                        changed = true;
                        goto NextRound;
                    }
                }

            NextRound:
                ;
            }

            return work;
        }

        private static List<WallCenterlineCandidate> CloneCenterlineCandidates(IEnumerable<WallCenterlineCandidate> input)
        {
            if (input == null)
            {
                return new List<WallCenterlineCandidate>();
            }

            return input
                .Where(x => x != null && x.CenterLine != null && x.CenterLine.Length > 1e-6)
                .Select(CloneCenterlineCandidate)
                .Where(x => x != null)
                .ToList();
        }

        private static WallCenterlineCandidate CloneCenterlineCandidate(WallCenterlineCandidate source)
        {
            if (source == null || source.CenterLine == null)
            {
                return null;
            }

            return new WallCenterlineCandidate
            {
                CenterLine = Line.CreateBound(source.CenterLine.GetEndPoint(0), source.CenterLine.GetEndPoint(1)),
                ThicknessMm = source.ThicknessMm,
                SideA = source.SideA,
                SideB = source.SideB,
                OverlapLengthMm = source.OverlapLengthMm,
                IsDoubleLinePairedSingleWall = source.IsDoubleLinePairedSingleWall,
                MateSegmentId = source.MateSegmentId,
                InsideNormal = source.InsideNormal
            };
        }


        private static double MergeThickness(double aMm, double bMm, double lenAFt, double lenBFt, double tolMm)
        {
            if (Math.Abs(aMm - bMm) <= tolMm)
            {
                return (aMm + bMm) * 0.5;
            }

            return lenAFt >= lenBFt ? aMm : bMm;
        }


        private static bool HasEndpointTouch(Line a, Line b, double tol)
        {
            XYZ[] pa = { a.GetEndPoint(0), a.GetEndPoint(1) };
            XYZ[] pb = { b.GetEndPoint(0), b.GetEndPoint(1) };
            foreach (XYZ p in pa)
            {
                foreach (XYZ q in pb)
                {
                    if (p.DistanceTo(q) <= tol)
                    {
                        return true;
                    }
                }
            }

            return false;
        }


        private static bool IsCollinear2D(Line a, Line b, double tol)
        {
            XYZ ap0 = a.GetEndPoint(0);
            XYZ ap1 = a.GetEndPoint(1);
            XYZ bp0 = b.GetEndPoint(0);
            XYZ da = Normalize(ap1 - ap0);
            XYZ n = new XYZ(-da.Y, da.X, 0);
            double dist = Math.Abs(Dot(bp0 - ap0, n));
            return dist <= tol;
        }


        private static Line MergeTwoLines2D(Line a, Line b)
        {
            XYZ p0 = a.GetEndPoint(0);
            XYZ p1 = a.GetEndPoint(1);
            XYZ u = Normalize(p1 - p0);
            XYZ[] pts = { a.GetEndPoint(0), a.GetEndPoint(1), b.GetEndPoint(0), b.GetEndPoint(1) };
            double minS = double.MaxValue;
            double maxS = double.MinValue;
            foreach (XYZ p in pts)
            {
                double s = Dot(p, u);
                if (s < minS)
                {
                    minS = s;
                }

                if (s > maxS)
                {
                    maxS = s;
                }
            }

            XYZ c0 = PointAtProjection(p0, u, minS);
            XYZ c1 = PointAtProjection(p0, u, maxS);
            if (c0.DistanceTo(c1) <= 1e-6)
            {
                return null;
            }

            return Line.CreateBound(c0, c1);
        }


        private static XYZ PointAtProjection(XYZ anchor, XYZ u, double targetProj)
        {
            double anchorProj = Dot(anchor, u);
            double d = targetProj - anchorProj;
            return anchor + u.Multiply(d);
        }


        private static XYZ Normalize(XYZ v)
        {
            double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
            if (len < 1e-9)
            {
                return new XYZ(1, 0, 0);
            }

            return new XYZ(v.X / len, v.Y / len, v.Z / len);
        }

        // Normalizes vectors in the XY plane to keep the rule axis-agnostic.
        private static XYZ Normalize2D(XYZ v)
        {
            double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
            if (len < 1e-9)
            {
                return new XYZ(0, 0, 0);
            }

            return new XYZ(v.X / len, v.Y / len, 0);
        }


        private static double Dot(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        private static void Swap(ref double a, ref double b)
        {
            double tmp = a;
            a = b;
            b = tmp;
        }
    }
}
