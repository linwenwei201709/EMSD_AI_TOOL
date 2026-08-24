using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadToRevit.Models;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class DoorCreatorService
    {
        private const double MaxWallDistanceMm = 500.0;
        private const double DuplicateDoorTolMm = 150.0;
        private const double R3BDefaultPatchWallHeightMm = 4000.0;
        private const double R3BPatchSafetyMarginPerSideMm = 120.0;
        private const double R3BPatchMinimumDoorEdgeClearanceMm = 120.0;
        private const double R3BMinPatchWallThicknessMm = 115.0;
        private const double DoorTypeReuseToleranceMm = 5.0;
        private const double DoorTypeHeightToleranceMm = 1.0;
        private const double R3DStableWidthStepMm = 10.0;

        public sealed class DoorWidthResolveOptions
        {
            public bool PreferGeometryOpeningWidth { get; set; } = true;
            public bool UseFixedDoorWidth { get; set; } = false;
            public double? FixedDoorWidthMm { get; set; }
            public double MinDoorWidthMm { get; set; } = 600.0;
            public double MaxDoorWidthMm { get; set; } = 3000.0;
        }

        public static DoorCreateResult CreateDoors(
            Document doc,
            IList<DoorCandidate> doorCandidates)
        {
            return CreateDoors(doc, doorCandidates, null, true);
        }

        public static DoorCreateResult CreateDoors(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            FamilySymbol forcedDoorSymbol,
            IList<Wall> hostWalls,
            bool useTransaction)
        {
            return CreateDoorsCoreEntry(doc, doorCandidates, forcedDoorSymbol, hostWalls, useTransaction, null, null);
        }

        public static DoorCreateResult CreateDoors(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            FamilySymbol forcedDoorSymbol,
            IList<Wall> hostWalls,
            bool useTransaction,
            VerticalDimensionSettings vertical)
        {
            return CreateDoorsCoreEntry(doc, doorCandidates, forcedDoorSymbol, hostWalls, useTransaction, vertical, null);
        }

        public static DoorCreateResult CreateDoors(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            FamilySymbol forcedDoorSymbol,
            IList<Wall> hostWalls,
            bool useTransaction,
            VerticalDimensionSettings vertical,
            AdvancedSettingsRow settings)
        {
            return CreateDoorsCoreEntry(doc, doorCandidates, forcedDoorSymbol, hostWalls, useTransaction, vertical, BuildWidthOptions(settings));
        }

        public static DoorCreateResult CreateDoors(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            IList<Wall> hostWalls,
            bool useTransaction)
        {
            return CreateDoorsCoreEntry(doc, doorCandidates, null, hostWalls, useTransaction, null, null);
        }

        private static DoorCreateResult CreateDoorsCoreEntry(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            FamilySymbol forcedDoorSymbol,
            IList<Wall> hostWalls,
            bool useTransaction,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions)
        {
            DoorCreateResult result = new DoorCreateResult();
            if (doc == null || doorCandidates == null || doorCandidates.Count == 0)
            {
                return result;
            }

            result.DoorCandidates = doorCandidates.Count;

            List<Wall> walls = hostWalls == null ? GetWalls(doc) : hostWalls.Where(x => x != null).Distinct().ToList();
            if (walls.Count == 0)
            {
                result.SkipReasons.Add("No host walls found.");
                result.SkippedDoors = result.DoorCandidates;
                return result;
            }

            GlobalGenerationSettings globalSettings = LoadGlobalGenerationSettings(doc);
            if (globalSettings.CreateDoorOpeningOnly)
            {
                result.DoorSymbolName = "Wall Opening Only";

                // Current EMSD path-recognition workflow does not need Door Family semantics.
                // Doors are generated as real rectangular wall openings only, so IFC consumers can
                // read the wall geometry with an actual passage instead of an IfcDoor object.
                CreateDoorOpeningsCore(doc, doorCandidates, walls, result, vertical, widthOptions, useTransaction);
                return result;
            }

            FamilySymbol doorSymbol = forcedDoorSymbol ?? FindDoorSymbol(doc);
            if (doorSymbol == null)
            {
                result.DoorSymbolName = "Door Family";
                result.SkipReasons.Add("No Door FamilySymbol found. Enable wall-opening mode or load a Door family/type.");
                result.SkippedDoors = result.DoorCandidates;
                return result;
            }

            result.DoorSymbolName = doorSymbol.FamilyName + " : " + doorSymbol.Name;
            CreateDoorsCore(doc, doorCandidates, walls, doorSymbol, result, vertical, widthOptions, useTransaction);
            return result;
        }


        private static void CreateDoorOpeningsCore(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            List<Wall> walls,
            DoorCreateResult result,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            bool useTransaction)
        {
            Dictionary<int, List<XYZ>> placedByWall = new Dictionary<int, List<XYZ>>();
            List<DoorCandidate> orderedCandidates = (doorCandidates ?? new List<DoorCandidate>())
                .Where(x => x != null)
                .OrderByDescending(CandidatePriority)
                .ToList();

            foreach (DoorCandidate candidate in orderedCandidates)
            {
                XYZ hostMatchPoint = ResolveHostMatchPoint(candidate);
                if (candidate == null || hostMatchPoint == null)
                {
                    result.SkippedDoors++;
                    AddReason(result, "Candidate center is null.");
                    continue;
                }

                Wall nearestWall;
                XYZ projectedPoint;
                double distMm;
                if (!TryResolveHostWall(walls, candidate, hostMatchPoint, out nearestWall, out projectedPoint, out distMm))
                {
                    result.SkippedDoors++;
                    AddReason(result, "No valid linear wall for candidate.");
                    continue;
                }

                if (distMm > MaxWallDistanceMm)
                {
                    result.SkippedDoors++;
                    AddReason(result, "Nearest wall too far: " + distMm.ToString("F1") + " mm.");
                    continue;
                }

                Level hostLevel = ResolveHostLevel(doc, nearestWall);
                if (hostLevel == null)
                {
                    result.SkippedDoors++;
                    AddReason(result, "No host level for wall " + nearestWall.Id.IntegerValue + ".");
                    continue;
                }

                try
                {
                    int createdOpeningId;
                    XYZ placementPoint;
                    string skipReason;
                    bool created = useTransaction
                        ? TryCreateSingleDoorOpeningWithOwnTransaction(
                            doc,
                            candidate,
                            nearestWall,
                            projectedPoint,
                            hostLevel,
                            vertical,
                            widthOptions,
                            walls,
                            result,
                            placedByWall,
                            out createdOpeningId,
                            out placementPoint,
                            out skipReason)
                        : TryCreateSingleDoorOpeningCore(
                            doc,
                            candidate,
                            nearestWall,
                            projectedPoint,
                            hostLevel,
                            vertical,
                            widthOptions,
                            walls,
                            result,
                            placedByWall,
                            out createdOpeningId,
                            out placementPoint,
                            out skipReason);

                    if (!created)
                    {
                        result.SkippedDoors++;
                        AddReason(result, skipReason ?? "Door opening creation failed.");
                        continue;
                    }

                    AddPlacedPoint(placedByWall, nearestWall, placementPoint);
                    result.CreatedDoors++;
                    RegisterCreatedElementId(result.CreatedElementIds, createdOpeningId);
                }
                catch (Exception ex)
                {
                    result.SkippedDoors++;
                    AddReason(result, ex.Message);
                }
            }
        }

        private static bool TryCreateSingleDoorOpeningWithOwnTransaction(
            Document doc,
            DoorCandidate candidate,
            Wall nearestWall,
            XYZ projectedPoint,
            Level hostLevel,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            IEnumerable<Wall> allHostWalls,
            DoorCreateResult result,
            Dictionary<int, List<XYZ>> placedByWall,
            out int createdOpeningId,
            out XYZ placementPoint,
            out string skipReason)
        {
            createdOpeningId = -1;
            placementPoint = null;
            skipReason = null;
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Door Opening"))
                {
                    tx.Start();
                    FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(new DoorBatchFailuresPreprocessor(doc));
                    fho.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(fho);

                    int tempOpeningId;
                    XYZ tempPlacementPoint;
                    string tempReason;
                    bool created = TryCreateSingleDoorOpeningCore(
                        doc,
                        candidate,
                        nearestWall,
                        projectedPoint,
                        hostLevel,
                        vertical,
                        widthOptions,
                        allHostWalls,
                        result,
                        placedByWall,
                        out tempOpeningId,
                        out tempPlacementPoint,
                        out tempReason);

                    if (!created)
                    {
                        skipReason = tempReason;
                        tx.RollBack();
                        return false;
                    }

                    TransactionStatus status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        skipReason = "Door opening transaction failed: " + status + ".";
                        return false;
                    }

                    if (tempOpeningId <= 0 || doc.GetElement(new ElementId(tempOpeningId)) == null)
                    {
                        skipReason = "Door opening transaction committed but opening was not found.";
                        return false;
                    }

                    createdOpeningId = tempOpeningId;
                    placementPoint = tempPlacementPoint;
                    return true;
                }
            }
            catch (Exception ex)
            {
                skipReason = ex.Message;
                DiagnosticRecorder.AppendDebug("[DoorOpeningSingleTx] Failed: " + ex.Message);
                return false;
            }
        }

        private static bool TryCreateSingleDoorOpeningCore(
            Document doc,
            DoorCandidate candidate,
            Wall nearestWall,
            XYZ projectedPoint,
            Level hostLevel,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            IEnumerable<Wall> allHostWalls,
            DoorCreateResult result,
            Dictionary<int, List<XYZ>> placedByWall,
            out int createdOpeningId,
            out XYZ placementPoint,
            out string skipReason)
        {
            createdOpeningId = -1;
            placementPoint = null;
            skipReason = null;

            string widthSource;
            double widthMm = ResolveDoorWidthMm(doc, candidate, nearestWall, allHostWalls, null, widthOptions, out widthSource);
            if (widthMm <= 1e-6)
            {
                widthMm = ResolveCandidateOpeningWidthMm(candidate);
                widthSource = "CandidateFallback";
            }

            if (widthMm <= 1e-6)
            {
                skipReason = "Door opening width not resolved for candidate " + (candidate == null ? 0 : candidate.CandidateId) + ".";
                return false;
            }

            double heightMm = ResolveOpeningHeightMm(vertical);
            double sillMm = ResolveOpeningSillHeightMm(vertical);

            string placementSource;
            placementPoint = ResolvePlacementPointOnWall(candidate, nearestWall, projectedPoint, null, out placementSource);
            if (placementPoint == null)
            {
                skipReason = "Door opening placement point not resolved.";
                return false;
            }

            if (IsDuplicatePlacement(placedByWall, nearestWall, placementPoint, candidate))
            {
                skipReason = "Duplicate door opening placement skipped.";
                return false;
            }

            Line wallLine;
            if (!TryGetWallLine(nearestWall, out wallLine))
            {
                skipReason = "Host wall is not a linear wall.";
                return false;
            }

            XYZ wallDir = wallLine.Direction.Normalize();
            double halfWidthFt = UnitUtils.ConvertToInternalUnits(widthMm * 0.5, UnitTypeId.Millimeters);
            double bottomZ = hostLevel.Elevation + UnitUtils.ConvertToInternalUnits(sillMm, UnitTypeId.Millimeters);
            double topZ = bottomZ + UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
            if (topZ <= bottomZ + 1e-9)
            {
                skipReason = "Door opening height is invalid.";
                return false;
            }

            XYZ center = new XYZ(placementPoint.X, placementPoint.Y, 0);
            XYZ leftPlan = center - wallDir.Multiply(halfWidthFt);
            XYZ rightPlan = center + wallDir.Multiply(halfWidthFt);
            XYZ p1 = new XYZ(leftPlan.X, leftPlan.Y, bottomZ);
            XYZ p2 = new XYZ(rightPlan.X, rightPlan.Y, topZ);

            Opening opening = doc.Create.NewOpening(nearestWall, p1, p2);
            if (opening == null)
            {
                skipReason = "Revit returned null opening.";
                return false;
            }

            string mark = "DOOR_OPENING_" + (result.CreatedDoors + 1).ToString("0000");
            TrySetTextParameter(opening, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "CadToRevit_DoorOpening");
            TrySetTextParameter(opening, BuiltInParameter.ALL_MODEL_MARK, mark);
            TrySetTextParameterByName(opening, "Comments", "CadToRevit_DoorOpening");
            TrySetTextParameterByName(opening, "Mark", mark);

            if (candidate != null)
            {
                candidate.FinalWidthMmApplied = widthMm;
                candidate.FinalHeightMmApplied = heightMm;
            }

            result.WidthSetSuccessCount++;
            result.HeightSetSuccessCount++;

            DiagnosticRecorder.AppendDebug(
                "[DoorOpeningCreate] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", Rule=" + (candidate == null ? string.Empty : (candidate.RuleSource ?? string.Empty)) +
                ", HostWallId=" + nearestWall.Id.IntegerValue +
                ", OpeningId=" + opening.Id.IntegerValue +
                ", WidthMm=" + widthMm.ToString("F1") +
                ", HeightMm=" + heightMm.ToString("F1") +
                ", SillMm=" + sillMm.ToString("F1") +
                ", WidthSource=" + (widthSource ?? string.Empty) +
                ", PlacementSource=" + (placementSource ?? string.Empty) +
                ", P1=" + FormatPointForLog(p1) +
                ", P2=" + FormatPointForLog(p2));

            createdOpeningId = opening.Id.IntegerValue;
            return true;
        }

        private static double ResolveCandidateOpeningWidthMm(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 0.0;
            }

            double combinedWidthMm = ResolveCombinedWidthMm(candidate);
            if (candidate.IsDoubleDoor && combinedWidthMm > 1e-6)
            {
                return combinedWidthMm;
            }

            if (candidate.VirtualOpeningWidthMm > 1e-6)
            {
                return candidate.VirtualOpeningWidthMm;
            }

            if (candidate.OpeningWidthMm > 1e-6)
            {
                return candidate.OpeningWidthMm;
            }

            if (candidate.WidthMm > 1e-6)
            {
                return candidate.WidthMm;
            }

            if (candidate.ArcRadiusMm > 1e-6)
            {
                return candidate.ArcRadiusMm;
            }

            return 0.0;
        }

        private static double ResolveOpeningHeightMm(VerticalDimensionSettings vertical)
        {
            return vertical != null && vertical.DoorHeightMm > 0 ? vertical.DoorHeightMm : 2100.0;
        }

        private static double ResolveOpeningSillHeightMm(VerticalDimensionSettings vertical)
        {
            return vertical != null && vertical.DoorSillHeightMm >= 0 ? vertical.DoorSillHeightMm : 0.0;
        }

        private static void TrySetTextParameter(Element element, BuiltInParameter builtInParameter, string value)
        {
            if (element == null)
            {
                return;
            }

            Parameter parameter = element.get_Parameter(builtInParameter);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
            {
                parameter.Set(value ?? string.Empty);
            }
        }

        private static void TrySetTextParameterByName(Element element, string parameterName, string value)
        {
            if (element == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            Parameter parameter = element.LookupParameter(parameterName);
            if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
            {
                parameter.Set(value ?? string.Empty);
            }
        }

        private static void CreateDoorsCore(
            Document doc,
            IList<DoorCandidate> doorCandidates,
            List<Wall> walls,
            FamilySymbol doorSymbol,
            DoorCreateResult result,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            bool useTransaction)
        {
            if (!useTransaction && !doorSymbol.IsActive)
            {
                doorSymbol.Activate();
                doc.Regenerate();
            }

            Dictionary<int, List<XYZ>> placedByWall = new Dictionary<int, List<XYZ>>();
            Dictionary<string, FamilySymbol> finalTypeCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FamilySymbol> baseSymbolCache = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            List<DoorCandidate> orderedCandidates = (doorCandidates ?? new List<DoorCandidate>())
                .Where(x => x != null)
                .OrderByDescending(CandidatePriority)
                .ToList();
            foreach (DoorCandidate candidate in orderedCandidates)
            {
                XYZ hostMatchPoint = ResolveHostMatchPoint(candidate);
                if (candidate == null || hostMatchPoint == null)
                {
                    result.SkippedDoors++;
                    AddReason(result, "Candidate center is null.");
                    continue;
                }

                if (IsR3BDedicatedCandidate(candidate))
                {
                    int createdDoorId;
                    XYZ placementPoint;
                    string skipReason;
                    bool created = TryCreateR3BDedicatedPipeline(
                        doc,
                        candidate,
                        walls,
                        doorSymbol,
                        vertical,
                        widthOptions,
                        result,
                        finalTypeCache,
                        baseSymbolCache,
                        placedByWall,
                        useTransaction,
                        out createdDoorId,
                        out placementPoint,
                        out skipReason);

                    if (!created)
                    {
                        result.SkippedDoors++;
                        AddReason(result, skipReason ?? "R3B dedicated pipeline failed.");
                        continue;
                    }

                    result.CreatedDoors++;
                    continue;
                }

                if (candidate.SymbolFamilyKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing ||
                    string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase))
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DoorR3DDirectHostPath] CandidateId=" + candidate.CandidateId +
                        ", Rule=" + (candidate.RuleSource ?? string.Empty) +
                        ", PreferredHostWallId=" + (candidate.PreferredHostWallId == null || candidate.PreferredHostWallId == ElementId.InvalidElementId ? 0 : candidate.PreferredHostWallId.IntegerValue));
                }

                Wall nearestWall;
                XYZ projectedPoint;
                double distMm;
                if (!TryResolveHostWall(walls, candidate, hostMatchPoint, out nearestWall, out projectedPoint, out distMm))
                {
                    result.SkippedDoors++;
                    AddReason(result, "No valid linear wall for candidate.");
                    continue;
                }

                if (distMm > MaxWallDistanceMm)
                {
                    result.SkippedDoors++;
                    AddReason(result, "Nearest wall too far: " + distMm.ToString("F1") + " mm.");
                    continue;
                }

                Level hostLevel = ResolveHostLevel(doc, nearestWall);
                if (hostLevel == null)
                {
                    result.SkippedDoors++;
                    AddReason(result, "No host level for wall " + nearestWall.Id.IntegerValue + ".");
                    continue;
                }

                try
                {
                    int createdDoorId;
                    XYZ placementPoint;
                    string skipReason;
                    bool created = useTransaction
                        ? TryCreateSingleDoorWithOwnTransaction(
                            doc,
                            candidate,
                            nearestWall,
                            projectedPoint,
                            hostLevel,
                            doorSymbol,
                            walls,
                            vertical,
                            widthOptions,
                            result,
                            finalTypeCache,
                            baseSymbolCache,
                            placedByWall,
                            out createdDoorId,
                            out placementPoint,
                            out skipReason)
                        : TryCreateSingleDoorCore(
                            doc,
                            candidate,
                            nearestWall,
                            projectedPoint,
                            hostLevel,
                            doorSymbol,
                            walls,
                            vertical,
                            widthOptions,
                            result,
                            finalTypeCache,
                            baseSymbolCache,
                            placedByWall,
                            out createdDoorId,
                            out placementPoint,
                            out skipReason);

                    if (!created)
                    {
                        result.SkippedDoors++;
                        AddReason(result, skipReason ?? "Door transaction failed or was auto-deleted by failure processor.");
                        continue;
                    }

                    AddPlacedPoint(placedByWall, nearestWall, placementPoint);
                    result.CreatedDoors++;
                    if (createdDoorId > 0)
                    {
                        RegisterCreatedElementId(result.CreatedElementIds, createdDoorId);
                    }
                }
                catch (Exception ex)
                {
                    result.SkippedDoors++;
                    AddReason(result, ex.Message);
                }
            }
        }

        private static bool TryCreateSingleDoorWithOwnTransaction(
            Document doc,
            DoorCandidate candidate,
            Wall nearestWall,
            XYZ projectedPoint,
            Level hostLevel,
            FamilySymbol doorSymbol,
            List<Wall> walls,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            DoorCreateResult result,
            Dictionary<string, FamilySymbol> finalTypeCache,
            Dictionary<string, FamilySymbol> baseSymbolCache,
            Dictionary<int, List<XYZ>> placedByWall,
            out int createdDoorId,
            out XYZ placementPoint,
            out string skipReason)
        {
            createdDoorId = -1;
            placementPoint = null;
            skipReason = null;

            bool retryEligible;
            string firstAttemptReason;
            bool created = TryCreateSingleDoorTransactionAttempt(
                doc,
                candidate,
                nearestWall,
                projectedPoint,
                hostLevel,
                doorSymbol,
                walls,
                vertical,
                widthOptions,
                result,
                finalTypeCache,
                baseSymbolCache,
                placedByWall,
                false,
                "Primary",
                out createdDoorId,
                out placementPoint,
                out firstAttemptReason,
                out retryEligible);

            if (created)
            {
                return true;
            }

            if (!IsR3DStableWidthCandidate(candidate) || !retryEligible)
            {
                skipReason = firstAttemptReason;
                return false;
            }

            // A small subset of rotated R3D wall-hosted double doors can be created with
            // a host-derived hand orientation that makes the family regeneration fail at
            // transaction commit (for example, "Profile sketch is empty"). Keep all
            // successful doors unchanged and retry only the failed R3D candidate in a new
            // transaction after flipping its hand orientation once.
            SanitizeDoorSymbolCache(doc, finalTypeCache, "BeforeR3DHandFlipRetry");
            DiagnosticRecorder.AppendDebug(
                "[R3DHandFlipRetry] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", FirstAttemptStatus=Failed" +
                ", FirstReason=" + (firstAttemptReason ?? string.Empty) +
                ", RetryStarted=True");

            int retryDoorId;
            XYZ retryPlacementPoint;
            string retryReason;
            bool retryIgnored;
            bool retryCreated = TryCreateSingleDoorTransactionAttempt(
                doc,
                candidate,
                nearestWall,
                projectedPoint,
                hostLevel,
                doorSymbol,
                walls,
                vertical,
                widthOptions,
                result,
                finalTypeCache,
                baseSymbolCache,
                placedByWall,
                true,
                "R3DHandFlipRetry",
                out retryDoorId,
                out retryPlacementPoint,
                out retryReason,
                out retryIgnored);

            if (retryCreated)
            {
                createdDoorId = retryDoorId;
                placementPoint = retryPlacementPoint;
                DiagnosticRecorder.AppendDebug(
                    "[R3DHandFlipRetry] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", RetryStatus=Committed" +
                    ", DoorId=" + retryDoorId);
                return true;
            }

            skipReason =
                "Initial R3D door creation failed: " + (firstAttemptReason ?? "unknown") +
                " Hand-flip retry failed: " + (retryReason ?? "unknown");
            DiagnosticRecorder.AppendDebug(
                "[R3DHandFlipRetry] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", RetryStatus=Failed" +
                ", RetryReason=" + (retryReason ?? string.Empty));
            return false;
        }

        private static bool TryCreateSingleDoorTransactionAttempt(
            Document doc,
            DoorCandidate candidate,
            Wall nearestWall,
            XYZ projectedPoint,
            Level hostLevel,
            FamilySymbol doorSymbol,
            List<Wall> walls,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            DoorCreateResult result,
            Dictionary<string, FamilySymbol> finalTypeCache,
            Dictionary<string, FamilySymbol> baseSymbolCache,
            Dictionary<int, List<XYZ>> placedByWall,
            bool flipR3DHandBeforeCommit,
            string attemptName,
            out int createdDoorId,
            out XYZ placementPoint,
            out string skipReason,
            out bool retryEligible)
        {
            createdDoorId = -1;
            placementPoint = null;
            skipReason = null;
            retryEligible = false;

            try
            {
                using (Transaction tx = new Transaction(doc, flipR3DHandBeforeCommit ? "Create Door - R3D Hand Flip Retry" : "Create Door"))
                {
                    tx.Start();
                    FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(new DoorBatchFailuresPreprocessor(doc));
                    fho.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(fho);

                    int tempDoorId;
                    XYZ tempPlacementPoint;
                    string tempReason;
                    bool created = TryCreateSingleDoorCore(
                        doc,
                        candidate,
                        nearestWall,
                        projectedPoint,
                        hostLevel,
                        doorSymbol,
                        walls,
                        vertical,
                        widthOptions,
                        result,
                        finalTypeCache,
                        baseSymbolCache,
                        placedByWall,
                        out tempDoorId,
                        out tempPlacementPoint,
                        out tempReason);

                    if (!created)
                    {
                        skipReason = tempReason;
                        tx.RollBack();
                        SanitizeDoorSymbolCache(doc, finalTypeCache, attemptName + ":CreateCoreReturnedFalse");
                        return false;
                    }

                    if (flipR3DHandBeforeCommit)
                    {
                        FamilyInstance retryDoor = tempDoorId > 0
                            ? doc.GetElement(new ElementId(tempDoorId)) as FamilyInstance
                            : null;
                        if (retryDoor == null)
                        {
                            skipReason = "R3D hand-flip retry could not resolve the created door instance.";
                            tx.RollBack();
                            SanitizeDoorSymbolCache(doc, finalTypeCache, attemptName + ":CreatedDoorMissingBeforeFlip");
                            return false;
                        }

                        LogR3DHandFlipRetryState(retryDoor, nearestWall, candidate, "BeforeFlip");
                        if (!retryDoor.CanFlipHand)
                        {
                            skipReason = "R3D door family does not support hand flipping.";
                            tx.RollBack();
                            SanitizeDoorSymbolCache(doc, finalTypeCache, attemptName + ":CanFlipHandFalse");
                            DiagnosticRecorder.AppendDebug(
                                "[R3DHandFlipRetry] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                                ", CanFlipHand=False" +
                                ", RetryStatus=Aborted");
                            return false;
                        }

                        retryDoor.flipHand();
                        doc.Regenerate();
                        LogR3DHandFlipRetryState(retryDoor, nearestWall, candidate, "AfterFlip");
                    }

                    TransactionStatus status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        retryEligible = !flipR3DHandBeforeCommit;
                        SanitizeDoorSymbolCache(doc, finalTypeCache, attemptName + ":CommitStatus=" + status);
                        skipReason = "Door transaction failed: " + status + ".";
                        return false;
                    }

                    if (tempDoorId <= 0 || doc.GetElement(new ElementId(tempDoorId)) == null)
                    {
                        retryEligible = !flipR3DHandBeforeCommit;
                        SanitizeDoorSymbolCache(doc, finalTypeCache, attemptName + ":CommittedInstanceMissing");
                        skipReason = "Door transaction committed but instance was removed by failure processor.";
                        return false;
                    }

                    createdDoorId = tempDoorId;
                    placementPoint = tempPlacementPoint;
                    return true;
                }
            }
            catch (Exception ex)
            {
                retryEligible = !flipR3DHandBeforeCommit;
                SanitizeDoorSymbolCache(doc, finalTypeCache, attemptName + ":Exception=" + ex.GetType().Name);
                skipReason = ex.Message;
                DiagnosticRecorder.AppendDebug(
                    "[DoorCreateSingleTx] Attempt=" + (attemptName ?? string.Empty) +
                    ", CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", Failed=" + ex.Message);
                return false;
            }
        }

        private static void LogR3DHandFlipRetryState(
            FamilyInstance door,
            Wall hostWall,
            DoorCandidate candidate,
            string stage)
        {
            if (door == null)
            {
                return;
            }

            XYZ wallDirection = null;
            LocationCurve wallLocation = hostWall == null ? null : hostWall.Location as LocationCurve;
            Line wallLine = wallLocation == null ? null : wallLocation.Curve as Line;
            if (wallLine != null)
            {
                wallDirection = wallLine.Direction;
            }

            DiagnosticRecorder.AppendDebug(
                "[R3DHandFlipRetryState] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", Stage=" + (stage ?? string.Empty) +
                ", DoorId=" + door.Id.IntegerValue +
                ", HostWallId=" + (hostWall == null ? 0 : hostWall.Id.IntegerValue) +
                ", CanFlipHand=" + door.CanFlipHand +
                ", CanFlipFacing=" + door.CanFlipFacing +
                ", HandFlipped=" + door.HandFlipped +
                ", FacingFlipped=" + door.FacingFlipped +
                ", Mirrored=" + door.Mirrored +
                ", HandOrientation=" + FormatPointForLog(door.HandOrientation) +
                ", FacingOrientation=" + FormatPointForLog(door.FacingOrientation) +
                ", WallDirection=" + FormatPointForLog(wallDirection) +
                ", WallOrientation=" + FormatPointForLog(hostWall == null ? null : hostWall.Orientation) +
                ", WallFlipped=" + (hostWall != null && hostWall.Flipped));
        }

        private static bool TryCreateSingleDoorCore(
            Document doc,
            DoorCandidate candidate,
            Wall nearestWall,
            XYZ projectedPoint,
            Level hostLevel,
            FamilySymbol doorSymbol,
            List<Wall> walls,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            DoorCreateResult result,
            Dictionary<string, FamilySymbol> finalTypeCache,
            Dictionary<string, FamilySymbol> baseSymbolCache,
            Dictionary<int, List<XYZ>> placedByWall,
            out int createdDoorId,
            out XYZ placementPoint,
            out string skipReason)
        {
            createdDoorId = -1;
            placementPoint = null;
            skipReason = null;

            FamilySymbol baseSymbol = ResolveBaseDoorSymbolForCandidate(doc, doorSymbol, candidate, baseSymbolCache) ?? doorSymbol;
            string widthSource;
            double widthMm = ResolveDoorWidthMm(doc, candidate, nearestWall, walls, baseSymbol, widthOptions, out widthSource);
            double targetHeightMm = ResolveTargetDoorHeightMm(vertical, baseSymbol);
            FamilySymbol finalSymbol = ResolveFinalDoorSymbol(doc, baseSymbol, widthMm, targetHeightMm, candidate, result, finalTypeCache);
            if (finalSymbol == null)
            {
                skipReason = "Final door type not resolved strictly for candidate " + candidate.CandidateId + ".";
                return false;
            }

            EnsureSymbolActivated(doc, finalSymbol);

            string placementSource;
            placementPoint = ResolvePlacementPointOnWall(candidate, nearestWall, projectedPoint, finalSymbol, out placementSource);
            if (IsDuplicatePlacement(placedByWall, nearestWall, placementPoint, candidate))
            {
                skipReason = "Duplicate door placement skipped.";
                return false;
            }

            FamilyInstance door = doc.Create.NewFamilyInstance(
                placementPoint,
                finalSymbol,
                nearestWall,
                hostLevel,
                StructuralType.NonStructural);
            doc.Regenerate();
            DiagnosticRecorder.AppendDebug("[DoorRegen] AfterInstanceCreated=True");

            const string widthWriteTarget = "FinalTypePreResolved";
            bool widthOk = true;
            if (candidate != null)
            {
                double actualTypeWidthMm;
                candidate.FinalWidthMmApplied = TryGetTypeLengthMm(
                    finalSymbol,
                    new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" },
                    out actualTypeWidthMm)
                    ? actualTypeWidthMm
                    : widthMm;
            }

            result.WidthSetSuccessCount++;
            TryApplyDoorVerticalDimensions(doc, finalSymbol, door, vertical, candidate, result);
            doc.Regenerate();
            DiagnosticRecorder.AppendDebug("[DoorRegen] AfterInstanceParams=True");

            // R3D double doors are already oriented by their resolved host wall when the
            // hosted family instance is created. Do not run the legacy facing correction
            // for this branch: it compares the wall tangent with the door facing normal,
            // which can produce an unstable sign on rotated walls and trigger an invalid
            // flipFacing() regeneration for otherwise valid hosted double doors.
            if (IsR3DStableWidthCandidate(candidate))
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorFacingAlign] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", Rule=" + (candidate == null ? string.Empty : (candidate.RuleSource ?? string.Empty)) +
                    ", Skipped=True, Reason=R3DHostedDoorUsesHostWallOrientation");
            }
            else
            {
                TryAlignFacing(nearestWall, door);
            }

            LogFinalDoorState(door, nearestWall, finalSymbol, candidate);
            DiagnosticRecorder.AppendDebug(
                "[DoorCreate] Layer=" + (candidate == null ? string.Empty : (candidate.UnmatchedReason ?? string.Empty)) +
                ", CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", DoorKind=" + ((candidate != null && candidate.IsDoubleDoor) ? "Double" : "Single") +
                ", CombinedWidthMm=" + (candidate == null ? 0.0 : candidate.CombinedWidthMm).ToString("F1") +
                ", LeftEdgePoint=" + FormatPointForLog(candidate == null ? null : candidate.LeftEdgePoint) +
                ", RightEdgePoint=" + FormatPointForLog(candidate == null ? null : candidate.RightEdgePoint) +
                ", HostWallId=" + nearestWall.Id.IntegerValue +
                ", WidthSource=" + (widthSource ?? string.Empty) +
                ", WidthWriteTarget=" + (widthWriteTarget ?? string.Empty) +
                ", OpeningWidthMm=" + (candidate == null ? 0.0 : candidate.OpeningWidthMm).ToString("F1") +
                ", OpeningCenter=" + FormatPointForLog(candidate == null ? null : candidate.OpeningCenterPoint) +
                ", PlacementSource=" + (placementSource ?? string.Empty) +
                ", FinalPlacementPoint=" + FormatPointForLog(candidate == null ? null : candidate.FinalPlacementPoint) +
                ", FinalWidthMmRequested=" + widthMm.ToString("F1") +
                ", FinalWidthMmApplied=" + (candidate == null ? 0.0 : candidate.FinalWidthMmApplied).ToString("F1") +
                ", WidthWriteSuccess=" + widthOk +
                ", HeightMmRequested=" + (vertical == null ? 0.0 : vertical.DoorHeightMm).ToString("F1") +
                ", DoorSillHeightMm=" + (vertical == null ? 0.0 : vertical.DoorSillHeightMm).ToString("F1") +
                ", DoorHeadHeightMmNotUsed=True" +
                ", FinalHeightMmApplied=" + (candidate == null ? 0.0 : candidate.FinalHeightMmApplied).ToString("F1"));

            createdDoorId = door.Id.IntegerValue;
            return true;
        }


        private static GlobalGenerationSettings LoadGlobalGenerationSettings(Document doc)
        {
            try
            {
                LayerOverrideStoreData store = LayerOverrideStoreService.Load(doc);
                return GlobalGenerationSettings.Clone(store != null ? store.GlobalGenerationSettings : null);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DoorGenerationMode] Failed to load global settings, fallback to opening-only mode: " + ex.Message);
                return GlobalGenerationSettings.CreateDefault();
            }
        }

        private static FamilySymbol FindDoorSymbol(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();
        }

        private static List<Wall> GetWalls(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();
        }

        private static bool TryFindNearestWall(
            List<Wall> walls,
            XYZ point,
            out Wall nearestWall,
            out XYZ projectedPoint,
            out double distMm)
        {
            nearestWall = null;
            projectedPoint = null;
            distMm = double.MaxValue;

            foreach (Wall wall in walls)
            {
                XYZ projected;
                double dMm;
                if (!TryProjectPointToWallSegment(wall, point, out projected, out dMm))
                {
                    continue;
                }

                if (dMm < distMm)
                {
                    distMm = dMm;
                    nearestWall = wall;
                    projectedPoint = projected;
                }
            }

            return nearestWall != null && projectedPoint != null;
        }

        private static bool TryResolveHostWall(
            List<Wall> walls,
            DoorCandidate candidate,
            XYZ hostMatchPoint,
            out Wall nearestWall,
            out XYZ projectedPoint,
            out double distMm)
        {
            nearestWall = null;
            projectedPoint = null;
            distMm = double.MaxValue;
            if (candidate == null || hostMatchPoint == null || walls == null || walls.Count == 0)
            {
                return false;
            }

            if (IsOpeningBasePreferredCandidate(candidate))
            {
                // For opening-base driven doors (especially R3), prefer the wall that best supports
                // the opening base. When detector already bound a preferred host wall, try it first
                // before running generic geometric competition.
                Wall preferredWall = null;
                if (candidate.PreferredHostWallId != null && candidate.PreferredHostWallId != ElementId.InvalidElementId)
                {
                    preferredWall = walls.FirstOrDefault(x => x != null && x.Id.IntegerValue == candidate.PreferredHostWallId.IntegerValue);
                }

                if (preferredWall != null)
                {
                    XYZ preferredProjected;
                    double preferredDistMm;
                    if (TryMatchOpeningBaseToSpecificWall(preferredWall, candidate, out preferredProjected, out preferredDistMm))
                    {
                        nearestWall = preferredWall;
                        projectedPoint = preferredProjected;
                        distMm = preferredDistMm;
                        DiagnosticRecorder.AppendDebug(
                            "[DoorPreferredHostWallMatched] CandidateId=" + candidate.CandidateId +
                            ", Rule=" + (candidate.RuleSource ?? string.Empty) +
                            ", PreferredWallId=" + preferredWall.Id.IntegerValue +
                            ", PreferredPoint=" + FormatPointForLog(ResolvePreferredHostPoint(candidate, hostMatchPoint)) +
                            ", ProjectedPoint=" + FormatPointForLog(preferredProjected) +
                            ", DistMm=" + preferredDistMm.ToString("F1"));
                        return true;
                    }
                }

                Wall openingBaseWall;
                XYZ openingBaseProjected;
                double openingBaseDistMm;
                if (TryFindBestOpeningBaseHostWall(walls, candidate, out openingBaseWall, out openingBaseProjected, out openingBaseDistMm))
                {
                    nearestWall = openingBaseWall;
                    projectedPoint = openingBaseProjected;
                    distMm = openingBaseDistMm;
                    DiagnosticRecorder.AppendDebug(
                        "[DoorOpeningBaseHostMatched] CandidateId=" + candidate.CandidateId +
                        ", Rule=" + (candidate.RuleSource ?? string.Empty) +
                        ", WallId=" + openingBaseWall.Id.IntegerValue +
                        ", ProjectedPoint=" + FormatPointForLog(openingBaseProjected) +
                        ", DistMm=" + openingBaseDistMm.ToString("F1"));
                    return true;
                }
            }

            if (candidate.MatchedWallId != null && candidate.MatchedWallId != ElementId.InvalidElementId)
            {
                Wall matched = walls.FirstOrDefault(x => x != null && x.Id.IntegerValue == candidate.MatchedWallId.IntegerValue);
                if (matched != null)
                {
                    XYZ p = candidate.ProjectedPointOnWall ?? hostMatchPoint;
                    XYZ projected;
                    double dMm;
                    if (TryProjectPointToWallSegment(matched, p, out projected, out dMm))
                    {
                        nearestWall = matched;
                        projectedPoint = projected;
                        distMm = UnitUtils.ConvertFromInternalUnits(hostMatchPoint.DistanceTo(projected), UnitTypeId.Millimeters);
                        return true;
                    }
                }
            }

            return TryFindNearestWall(walls, hostMatchPoint, out nearestWall, out projectedPoint, out distMm);
        }

        private static bool TryFindBestOpeningBaseHostWall(
            List<Wall> walls,
            DoorCandidate candidate,
            out Wall bestWall,
            out XYZ projectedPoint,
            out double distMm)
        {
            bestWall = null;
            projectedPoint = null;
            distMm = double.MaxValue;
            if (walls == null || candidate == null)
            {
                return false;
            }

            XYZ s = candidate.VirtualOpeningBaseStart ?? candidate.OpeningBaseStartPoint;
            XYZ e = candidate.VirtualOpeningBaseEnd ?? candidate.OpeningBaseEndPoint;
            if (s == null || e == null)
            {
                return false;
            }

            XYZ d = e - s;
            double baseLenFt = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
            if (baseLenFt < 1e-9)
            {
                return false;
            }

            XYZ openingDir = new XYZ(d.X / baseLenFt, d.Y / baseLenFt, 0);
            XYZ openingCenter = ResolveOpeningBaseCenter(candidate) ?? candidate.OpeningCenterPoint ?? MidPoint(s, e);

            const double minParallelCos = 0.965925826; // 15 degrees.
            const double centerTolMm = 120.0;
            const double looseEndTolMm = 180.0;
            const double minOverlapRatio = 0.55;
            const double maxUncoveredMm = 260.0;

            int preferredWallId = candidate.PreferredHostWallId != null && candidate.PreferredHostWallId != ElementId.InvalidElementId
                ? candidate.PreferredHostWallId.IntegerValue
                : 0;

            double bestScore = double.MaxValue;

            foreach (Wall wall in walls)
            {
                if (wall == null)
                {
                    continue;
                }

                XYZ centerOnWall;
                double avgDistMm;
                double overlapRatio;
                double uncoveredMm;
                double centerDistMm;
                double score;
                if (!TryScoreOpeningBaseWall(
                        wall,
                        s,
                        e,
                        openingCenter,
                        openingDir,
                        baseLenFt,
                        preferredWallId,
                        minParallelCos,
                        centerTolMm,
                        looseEndTolMm,
                        minOverlapRatio,
                        maxUncoveredMm,
                        out centerOnWall,
                        out avgDistMm,
                        out overlapRatio,
                        out uncoveredMm,
                        out centerDistMm,
                        out score))
                {
                    continue;
                }

                DiagnosticRecorder.AppendDebug(
                    "[DoorOpeningBaseHostCandidate] CandidateId=" + candidate.CandidateId +
                    ", Rule=" + (candidate.RuleSource ?? string.Empty) +
                    ", WallId=" + wall.Id.IntegerValue +
                    ", OverlapRatio=" + overlapRatio.ToString("F3") +
                    ", UncoveredMm=" + uncoveredMm.ToString("F1") +
                    ", AvgDistMm=" + avgDistMm.ToString("F1") +
                    ", CenterDistMm=" + centerDistMm.ToString("F1") +
                    ", Score=" + score.ToString("F3"));

                if (score < bestScore)
                {
                    bestScore = score;
                    bestWall = wall;
                    projectedPoint = centerOnWall;
                    distMm = avgDistMm;
                }
            }

            return bestWall != null && projectedPoint != null;
        }

        private static bool TryMatchOpeningBaseToSpecificWall(
            Wall wall,
            DoorCandidate candidate,
            out XYZ projectedPoint,
            out double distMm)
        {
            projectedPoint = null;
            distMm = double.MaxValue;
            if (wall == null || candidate == null)
            {
                return false;
            }

            XYZ s = candidate.VirtualOpeningBaseStart ?? candidate.OpeningBaseStartPoint;
            XYZ e = candidate.VirtualOpeningBaseEnd ?? candidate.OpeningBaseEndPoint;
            if (s == null || e == null)
            {
                return false;
            }

            XYZ d = e - s;
            double baseLenFt = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
            if (baseLenFt < 1e-9)
            {
                return false;
            }

            XYZ openingDir = new XYZ(d.X / baseLenFt, d.Y / baseLenFt, 0);
            XYZ openingCenter = ResolveOpeningBaseCenter(candidate) ?? candidate.OpeningCenterPoint ?? MidPoint(s, e);

            XYZ centerOnWall;
            double avgDistMm;
            double overlapRatio;
            double uncoveredMm;
            double centerDistMm;
            double score;
            if (!TryScoreOpeningBaseWall(
                    wall,
                    s,
                    e,
                    openingCenter,
                    openingDir,
                    baseLenFt,
                    wall.Id.IntegerValue,
                    0.965925826,
                    120.0,
                    180.0,
                    0.40,
                    320.0,
                    out centerOnWall,
                    out avgDistMm,
                    out overlapRatio,
                    out uncoveredMm,
                    out centerDistMm,
                    out score))
            {
                return false;
            }

            projectedPoint = centerOnWall;
            distMm = avgDistMm;
            return true;
        }

        private static bool TryScoreOpeningBaseWall(
            Wall wall,
            XYZ openingStart,
            XYZ openingEnd,
            XYZ openingCenter,
            XYZ openingDir,
            double baseLenFt,
            int preferredWallId,
            double minParallelCos,
            double centerTolMm,
            double looseEndTolMm,
            double minOverlapRatio,
            double maxUncoveredMm,
            out XYZ centerOnWall,
            out double avgDistMm,
            out double overlapRatio,
            out double uncoveredMm,
            out double centerDistMm,
            out double score)
        {
            centerOnWall = null;
            avgDistMm = double.MaxValue;
            overlapRatio = 0.0;
            uncoveredMm = double.MaxValue;
            centerDistMm = double.MaxValue;
            score = double.MaxValue;

            if (wall == null || openingStart == null || openingEnd == null || openingCenter == null || openingDir == null)
            {
                return false;
            }

            Line wallLine;
            if (!TryGetWallLine(wall, out wallLine) || wallLine == null)
            {
                return false;
            }

            XYZ wallDirRaw = wallLine.Direction;
            double wallLen = Math.Sqrt((wallDirRaw.X * wallDirRaw.X) + (wallDirRaw.Y * wallDirRaw.Y));
            if (wallLen < 1e-9)
            {
                return false;
            }

            XYZ wallDir = new XYZ(wallDirRaw.X / wallLen, wallDirRaw.Y / wallLen, 0);
            double parallelAbs = Math.Abs(Dot(openingDir, wallDir));
            if (parallelAbs < minParallelCos)
            {
                return false;
            }

            ProjectionData pc = ProjectPointToLineSegment(openingCenter, wallLine, centerTolMm);
            if (!pc.IsInsideSegment)
            {
                return false;
            }

            ProjectionData ps = ProjectPointToLineSegment(openingStart, wallLine, looseEndTolMm);
            ProjectionData pe = ProjectPointToLineSegment(openingEnd, wallLine, looseEndTolMm);

            double dsMm = UnitUtils.ConvertFromInternalUnits(ps.DistanceFeet, UnitTypeId.Millimeters);
            double deMm = UnitUtils.ConvertFromInternalUnits(pe.DistanceFeet, UnitTypeId.Millimeters);
            avgDistMm = (dsMm + deMm) * 0.5;
            if (avgDistMm > MaxWallDistanceMm)
            {
                return false;
            }

            double wallA = Dot(wallLine.GetEndPoint(0) - openingStart, openingDir);
            double wallB = Dot(wallLine.GetEndPoint(1) - openingStart, openingDir);
            double wallMin = Math.Min(wallA, wallB);
            double wallMax = Math.Max(wallA, wallB);
            double overlapFt = Math.Max(0.0, Math.Min(baseLenFt, wallMax) - Math.Max(0.0, wallMin));
            overlapRatio = baseLenFt > 1e-9 ? overlapFt / baseLenFt : 0.0;
            double uncoveredBeforeFt = Math.Max(0.0, wallMin);
            double uncoveredAfterFt = Math.Max(0.0, baseLenFt - wallMax);
            uncoveredMm = UnitUtils.ConvertFromInternalUnits(uncoveredBeforeFt + uncoveredAfterFt, UnitTypeId.Millimeters);

            bool preferredMatch = preferredWallId > 0 && wall.Id.IntegerValue == preferredWallId;
            if (!preferredMatch)
            {
                if (overlapRatio < minOverlapRatio)
                {
                    return false;
                }

                if (uncoveredMm > maxUncoveredMm)
                {
                    return false;
                }
            }

            centerOnWall = pc.ProjectedPoint;
            centerDistMm = UnitUtils.ConvertFromInternalUnits(openingCenter.DistanceTo(centerOnWall), UnitTypeId.Millimeters);

            double scoreLocal = 0.0;
            scoreLocal += uncoveredMm * 6.0;
            scoreLocal += avgDistMm * 3.0;
            scoreLocal += centerDistMm * 1.5;
            scoreLocal += (1.0 - parallelAbs) * 2000.0;
            scoreLocal -= overlapRatio * 1000.0;
            if (ps.IsInsideSegment) scoreLocal -= 80.0;
            if (pe.IsInsideSegment) scoreLocal -= 80.0;
            if (preferredMatch) scoreLocal -= 10000.0;

            score = scoreLocal;
            return true;
        }

        private static XYZ MidPoint(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return null;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static ProjectionData ProjectPointToLineSegment(XYZ point, Line line, double endTolMm)
        {
            if (point == null || line == null)
            {
                return new ProjectionData();
            }

            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            XYZ ab = b - a;
            double len2 = Dot(ab, ab);
            if (len2 < 1e-12)
            {
                return new ProjectionData { ProjectedPoint = a, DistanceFeet = point.DistanceTo(a), IsInsideSegment = false };
            }

            double t = Dot(point - a, ab) / len2;
            double lenFt = Math.Sqrt(len2);
            double endTolFt = UnitUtils.ConvertToInternalUnits(endTolMm, UnitTypeId.Millimeters);
            double tTol = lenFt < 1e-9 ? 0.0 : endTolFt / lenFt;
            double clamped = Math.Max(0.0, Math.Min(1.0, t));
            XYZ projected = a + ab.Multiply(clamped);
            return new ProjectionData
            {
                ProjectedPoint = projected,
                DistanceFeet = point.DistanceTo(projected),
                IsInsideSegment = t >= -tTol && t <= 1.0 + tTol
            };
        }

        private sealed class ProjectionData
        {
            public XYZ ProjectedPoint { get; set; }
            public double DistanceFeet { get; set; }
            public bool IsInsideSegment { get; set; }
        }

        private static bool TryProjectPointToWallSegment(
            Wall wall,
            XYZ point,
            out XYZ projectedPoint,
            out double distMm)
        {
            projectedPoint = null;
            distMm = double.MaxValue;
            if (wall == null || point == null)
            {
                return false;
            }

            LocationCurve loc = wall.Location as LocationCurve;
            Line line = loc?.Curve as Line;
            if (line == null)
            {
                return false;
            }

            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            XYZ ab = b - a;
            double len2 = Dot(ab, ab);
            if (len2 < 1e-12)
            {
                return false;
            }

            double t = Dot(point - a, ab) / len2;
            double tTol = 0.02;
            if (t < -tTol || t > 1.0 + tTol)
            {
                return false;
            }

            XYZ projected = a + ab.Multiply(t);
            double dFeet = point.DistanceTo(projected);
            projectedPoint = projected;
            distMm = UnitUtils.ConvertFromInternalUnits(dFeet, UnitTypeId.Millimeters);
            return true;
        }

        private static double Dot(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        private static bool IsDuplicatePlacement(
            Dictionary<int, List<XYZ>> placedByWall,
            Wall wall,
            XYZ point,
            DoorCandidate candidate)
        {
            if (placedByWall == null || wall == null || point == null)
            {
                return false;
            }

            int key = wall.Id.IntegerValue;
            List<XYZ> existing;
            if (!placedByWall.TryGetValue(key, out existing) || existing == null || existing.Count == 0)
            {
                return false;
            }

            double tolMm = ResolveDuplicateTolMm(candidate);
            double tolFeet = UnitUtils.ConvertToInternalUnits(tolMm, UnitTypeId.Millimeters);
            return existing.Any(x => x != null && x.DistanceTo(point) <= tolFeet);
        }

        private static double ResolveDuplicateTolMm(DoorCandidate candidate)
        {
            double widthMm = candidate?.WidthMm ?? 0.0;
            if (widthMm <= 1e-6)
            {
                return DuplicateDoorTolMm;
            }

            double dynamicTol = widthMm * 0.9;
            return Math.Max(DuplicateDoorTolMm, Math.Min(dynamicTol, 600.0));
        }

        private static int CandidatePriority(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 0;
            }

            int score = 0;
            if (IsOpeningBasePreferredCandidate(candidate)) score += 120;
            if (candidate.PreferVirtualOpeningHost && candidate.PreferredHostWallId != null && candidate.PreferredHostWallId != ElementId.InvalidElementId) score += 300;
            if (string.Equals(candidate.RuleSource, "R3T", StringComparison.OrdinalIgnoreCase)) score += 110;
            if (string.Equals(candidate.RuleSource, "R3", StringComparison.OrdinalIgnoreCase)) score += 100;
            if (string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase)) score += 85;
            if (string.Equals(candidate.RuleSource, "R3BD", StringComparison.OrdinalIgnoreCase)) score += 85;
            if (string.Equals(candidate.RuleSource, "R3C", StringComparison.OrdinalIgnoreCase)) score += 80;
            if (string.Equals(candidate.RuleSource, "R3CD", StringComparison.OrdinalIgnoreCase)) score += 80;
            if (string.Equals(candidate.RuleSource, "R3B", StringComparison.OrdinalIgnoreCase)) score += 70;
            if (string.Equals(candidate.RuleSource, "R1", StringComparison.OrdinalIgnoreCase)) score += 60;
            if (candidate.WallDirHint != null) score += 30;
            if (candidate.OpeningCenterPoint != null) score += 20;
            if (candidate.HingePoint != null) score += 10;
            return score;
        }

        private static bool IsR3BDedicatedCandidate(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            // Patch-wall dedicated pipeline is ONLY for no-wall families.
            // R3 / R3D are wall-crossing branches and must never enter this path.
            return candidate.SymbolFamilyKind == DoorSymbolFamilyKind.MinimalArcDoorNoWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.MinimalDoubleArcDoorNoWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.ComplexStandardDoorNoWallCrossingR3CD ||
                   string.Equals(candidate.RuleSource, "R3BD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3C", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3CD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3B", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryCreateR3BDedicatedPipeline(
            Document doc,
            DoorCandidate candidate,
            List<Wall> walls,
            FamilySymbol doorSymbol,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            DoorCreateResult result,
            Dictionary<string, FamilySymbol> finalTypeCache,
            Dictionary<string, FamilySymbol> baseSymbolCache,
            Dictionary<int, List<XYZ>> placedByWall,
            bool useTransaction,
            out int createdDoorId,
            out XYZ placementPoint,
            out string skipReason)
        {
            createdDoorId = -1;
            placementPoint = null;
            skipReason = null;
            if (doc == null || candidate == null || walls == null || walls.Count == 0)
            {
                skipReason = "R3B patch failed: missing document/candidate/walls.";
                return false;
            }

            XYZ openingCenter = ResolveOpeningBaseCenter(candidate) ?? candidate.OpeningCenterPoint ?? candidate.CenterPoint;
            if (openingCenter == null)
            {
                skipReason = "R3B patch failed: opening center is null.";
                return false;
            }

            XYZ openingDir = ResolveR3BOpeningDirection(candidate);
            if (openingDir == null)
            {
                skipReason = "R3B patch failed: opening direction is null.";
                return false;
            }

            double openingWidthMm = ResolveR3BOpeningWidthMm(candidate);
            if (openingWidthMm <= 1e-6)
            {
                skipReason = "R3B patch failed: opening width is invalid.";
                return false;
            }

            R3BPatchContext patchContext;
            bool hasAlignedContext = TryResolveR3BPatchContextFromOpeningEnds(candidate, walls, out patchContext);

            XYZ patchCenter = openingCenter;
            XYZ patchStart = null;
            XYZ patchEnd = null;
            double patchWallLengthMm = openingWidthMm + (R3BPatchSafetyMarginPerSideMm * 2.0);
            EnsurePatchWallSafetyForDoorWidth(openingWidthMm, ref patchWallLengthMm);

            if (hasAlignedContext && patchContext != null)
            {
                openingDir = Normalize2D(patchContext.PatchDirection) ?? openingDir;
                patchCenter = patchContext.PatchCenter ?? patchContext.OpeningCenter ?? openingCenter;
                patchStart = patchContext.PatchStart;
                patchEnd = patchContext.PatchEnd;

                double alignedSpanMm = (patchStart != null && patchEnd != null)
                    ? UnitUtils.ConvertFromInternalUnits(patchStart.DistanceTo(patchEnd), UnitTypeId.Millimeters)
                    : 0.0;
                if (alignedSpanMm > patchWallLengthMm)
                {
                    patchWallLengthMm = alignedSpanMm;
                }

                EnsurePatchWallSafetyForDoorWidth(openingWidthMm, ref patchWallLengthMm);
                EnsureR3BPatchLineAroundCenter(ref patchStart, ref patchEnd, patchCenter, openingDir, patchWallLengthMm);

                DiagnosticRecorder.AppendDebug(
                    "[R3BAlignedPatch] CandidateId=" + candidate.CandidateId +
                    ", OpeningCenter=(" + openingCenter.X.ToString("F3") + "," + openingCenter.Y.ToString("F3") + "," + openingCenter.Z.ToString("F3") + ")" +
                    ", PatchCenter=(" + patchCenter.X.ToString("F3") + "," + patchCenter.Y.ToString("F3") + "," + patchCenter.Z.ToString("F3") + ")" +
                    ", OpeningDir=(" + openingDir.X.ToString("F4") + "," + openingDir.Y.ToString("F4") + ",0.0000)" +
                    ", OpeningWidthMm=" + openingWidthMm.ToString("F1") +
                    ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                    ", ReferenceWallId=" + (patchContext.ReferenceWall != null ? patchContext.ReferenceWall.Id.IntegerValue.ToString() : "-1") +
                    ", LeftWallId=" + (patchContext.LeftWall != null ? patchContext.LeftWall.Id.IntegerValue.ToString() : "-1") +
                    ", RightWallId=" + (patchContext.RightWall != null ? patchContext.RightWall.Id.IntegerValue.ToString() : "-1") +
                    ", LeftWallLengthMm=" + patchContext.LeftWallLengthMm.ToString("F1") +
                    ", RightWallLengthMm=" + patchContext.RightWallLengthMm.ToString("F1") +
                    ", PairSpanMm=" + patchContext.PairSpanMm.ToString("F1") +
                    ", PatchStart=(" + patchStart.X.ToString("F3") + "," + patchStart.Y.ToString("F3") + "," + patchStart.Z.ToString("F3") + ")" +
                    ", PatchEnd=(" + patchEnd.X.ToString("F3") + "," + patchEnd.Y.ToString("F3") + "," + patchEnd.Z.ToString("F3") + ")");
            }
            else
            {
                double patchHalfLengthFt = UnitUtils.ConvertToInternalUnits(patchWallLengthMm * 0.5, UnitTypeId.Millimeters);
                patchStart = patchCenter - openingDir.Multiply(patchHalfLengthFt);
                patchEnd = patchCenter + openingDir.Multiply(patchHalfLengthFt);

                DiagnosticRecorder.AppendDebug(
                    "[R3BMinimalPatch] CandidateId=" + candidate.CandidateId +
                    ", OpeningCenter=(" + openingCenter.X.ToString("F3") + "," + openingCenter.Y.ToString("F3") + "," + openingCenter.Z.ToString("F3") + ")" +
                    ", PatchCenter=(" + patchCenter.X.ToString("F3") + "," + patchCenter.Y.ToString("F3") + "," + patchCenter.Z.ToString("F3") + ")" +
                    ", OpeningDir=(" + openingDir.X.ToString("F4") + "," + openingDir.Y.ToString("F4") + ",0.0000)" +
                    ", OpeningWidthMm=" + openingWidthMm.ToString("F1") +
                    ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                    ", PatchStart=(" + patchStart.X.ToString("F3") + "," + patchStart.Y.ToString("F3") + "," + patchStart.Z.ToString("F3") + ")" +
                    ", PatchEnd=(" + patchEnd.X.ToString("F3") + "," + patchEnd.Y.ToString("F3") + "," + patchEnd.Z.ToString("F3") + ")");
            }

            if (patchStart == null || patchEnd == null || patchStart.DistanceTo(patchEnd) < 1e-9)
            {
                skipReason = "R3B patch failed: invalid minimal patch baseline.";
                return false;
            }

            Line patchLine = Line.CreateBound(patchStart, patchEnd);
            Wall templateWall = ResolveR3BPatchTemplateWall(walls, openingCenter, patchContext);
            if (templateWall == null)
            {
                skipReason = "R3B patch failed: no template wall.";
                return false;
            }

            double templateWallThicknessMm = UnitUtils.ConvertFromInternalUnits(templateWall.Width, UnitTypeId.Millimeters);
            ElementId wallTypeId = templateWall.GetTypeId();
            bool usedMinPatchRule = templateWallThicknessMm < R3BMinPatchWallThicknessMm;
            string originalWallTypeName = ResolveWallTypeName(doc, wallTypeId);
            string finalPatchWallTypeName = originalWallTypeName;
            double finalPatchWallThicknessMm = templateWallThicknessMm;

            if (usedMinPatchRule)
            {
                wallTypeId = ResolveOrCreateR3BPatchWallTypeId(doc, templateWall, R3BMinPatchWallThicknessMm);
                finalPatchWallTypeName = ResolveWallTypeName(doc, wallTypeId);
                finalPatchWallThicknessMm = ResolveWallTypeWidthMm(doc, wallTypeId, templateWallThicknessMm);
            }

            DiagnosticRecorder.AppendDebug(
                "[R3BPatchWallType] RuleSource=" + (candidate.RuleSource ?? string.Empty) +
                ", TemplateWallId=" + templateWall.Id.IntegerValue +
                ", TemplateWallThicknessMm=" + templateWallThicknessMm.ToString("F1") +
                ", OriginalWallTypeName=" + (originalWallTypeName ?? string.Empty) +
                ", FinalPatchWallTypeName=" + (finalPatchWallTypeName ?? string.Empty) +
                ", FinalPatchWallThicknessMm=" + finalPatchWallThicknessMm.ToString("F1") +
                ", UsedMinPatchRule=" + usedMinPatchRule);
            Level hostLevel = ResolveHostLevel(doc, templateWall);
            if (hostLevel == null)
            {
                skipReason = "R3B patch failed: host level not resolved.";
                return false;
            }

            double patchHeightFt = ResolvePatchWallHeightFeet(templateWall);
            double patchBaseOffsetFt = ResolvePatchWallBaseOffsetFeet(templateWall);

            Wall patchWall = null;
            if (useTransaction)
            {
                using (Transaction txPatch = new Transaction(doc, "R3B Create Minimal Patch Wall"))
                {
                    txPatch.Start();
                    // Keep the batch flow running by suppressing non-critical patch-wall warnings.
                    FailureHandlingOptions patchFho = txPatch.GetFailureHandlingOptions();
                    patchFho.SetFailuresPreprocessor(new NonCriticalWarningsPreprocessor("R3BPatchCreate"));
                    patchFho.SetClearAfterRollback(true);
                    txPatch.SetFailureHandlingOptions(patchFho);

                    patchWall = Wall.Create(doc, patchLine, wallTypeId, hostLevel.Id, patchHeightFt, patchBaseOffsetFt, false, false);
                    TransactionStatus patchStatus = txPatch.Commit();

                    if (patchStatus != TransactionStatus.Committed || patchWall == null)
                    {
                        skipReason = "R3B patch failed: patch wall create transaction failed.";
                        return false;
                    }
                }

                ApplyR3BPatchWallPostCreateSafety(
                    doc,
                    candidate,
                    patchWall,
                    openingWidthMm,
                    patchWallLengthMm,
                    finalPatchWallTypeName,
                    finalPatchWallThicknessMm);

                using (Transaction txDoor = new Transaction(doc, "R3B Create Door On Minimal Patch"))
                {
                    txDoor.Start();
                    FailureHandlingOptions fho = txDoor.GetFailureHandlingOptions();
                    fho.SetFailuresPreprocessor(new DoorBatchFailuresPreprocessor(doc));
                    fho.SetClearAfterRollback(true);
                    txDoor.SetFailureHandlingOptions(fho);

                    List<Wall> contextWalls = BuildPatchContextWalls(walls, patchWall);
                    int tempDoorId;
                    XYZ tempPlacement;
                    string tempReason;
                    bool created = TryCreateR3BDoorWithPlacementRetries(
                        doc,
                        candidate,
                        patchWall,
                        patchCenter,
                        openingDir,
                        patchStart,
                        patchEnd,
                        patchWallLengthMm,
                        openingWidthMm,
                        hostLevel,
                        doorSymbol,
                        contextWalls,
                        vertical,
                        widthOptions,
                        result,
                        finalTypeCache,
                        baseSymbolCache,
                        placedByWall,
                        out tempDoorId,
                        out tempPlacement,
                        out tempReason);

                    if (!created)
                    {
                        txDoor.RollBack();
                        skipReason = "R3B create failed after minimal patch (patch retained): " + (tempReason ?? "unknown reason");
                        DiagnosticRecorder.AppendDebug("[R3BMinimalPatch] DoorCreateFailed PatchRetained=True, PatchWallId=" + patchWall.Id.IntegerValue);
                        return false;
                    }

                    bool existsBeforeCommit = tempDoorId > 0 && doc.GetElement(new ElementId(tempDoorId)) != null;
                    DiagnosticRecorder.AppendDebug(
                        "[R3BMinimalPatchDoorState] Stage=BeforeCommit" +
                        ", CandidateId=" + candidate.CandidateId +
                        ", DoorId=" + tempDoorId +
                        ", Exists=" + existsBeforeCommit +
                        ", PlacementPoint=(" + tempPlacement.X.ToString("F3") + "," + tempPlacement.Y.ToString("F3") + "," + tempPlacement.Z.ToString("F3") + ")" +
                        ", CombinedWidthMm=" + candidate.CombinedWidthMm.ToString("F1") +
                        ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                        ", DistToPatchStartMm=" + UnitUtils.ConvertFromInternalUnits(tempPlacement.DistanceTo(patchStart), UnitTypeId.Millimeters).ToString("F1") +
                        ", DistToPatchEndMm=" + UnitUtils.ConvertFromInternalUnits(tempPlacement.DistanceTo(patchEnd), UnitTypeId.Millimeters).ToString("F1"));

                    TransactionStatus doorCommit = txDoor.Commit();
                    bool existsAfterCommit = tempDoorId > 0 && doc.GetElement(new ElementId(tempDoorId)) != null;
                    DiagnosticRecorder.AppendDebug(
                        "[R3BMinimalPatchDoorState] Stage=AfterCommit" +
                        ", CandidateId=" + candidate.CandidateId +
                        ", DoorId=" + tempDoorId +
                        ", DoorTxStatus=" + doorCommit +
                        ", Exists=" + existsAfterCommit +
                        ", PlacementPoint=(" + tempPlacement.X.ToString("F3") + "," + tempPlacement.Y.ToString("F3") + "," + tempPlacement.Z.ToString("F3") + ")" +
                        ", CombinedWidthMm=" + candidate.CombinedWidthMm.ToString("F1") +
                        ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                        ", DistToPatchStartMm=" + UnitUtils.ConvertFromInternalUnits(tempPlacement.DistanceTo(patchStart), UnitTypeId.Millimeters).ToString("F1") +
                        ", DistToPatchEndMm=" + UnitUtils.ConvertFromInternalUnits(tempPlacement.DistanceTo(patchEnd), UnitTypeId.Millimeters).ToString("F1"));

                    if (doorCommit != TransactionStatus.Committed)
                    {
                        skipReason = "R3B create failed after minimal patch (patch retained): door transaction failed.";
                        DiagnosticRecorder.AppendDebug("[R3BMinimalPatch] DoorCreateFailed PatchRetained=True, PatchWallId=" + patchWall.Id.IntegerValue);
                        return false;
                    }

                    createdDoorId = tempDoorId;
                    placementPoint = tempPlacement;
                }
            }
            else
            {
                patchWall = Wall.Create(doc, patchLine, wallTypeId, hostLevel.Id, patchHeightFt, patchBaseOffsetFt, false, false);
                if (patchWall == null)
                {
                    skipReason = "R3B patch failed: patch wall create returned null.";
                    return false;
                }

                ApplyR3BPatchWallPostCreateSafety(
                    doc,
                    candidate,
                    patchWall,
                    openingWidthMm,
                    patchWallLengthMm,
                    finalPatchWallTypeName,
                    finalPatchWallThicknessMm);

                List<Wall> contextWalls = BuildPatchContextWalls(walls, patchWall);
                bool created = TryCreateR3BDoorWithPlacementRetries(
                    doc,
                    candidate,
                    patchWall,
                    patchCenter,
                    openingDir,
                    patchStart,
                    patchEnd,
                    patchWallLengthMm,
                    openingWidthMm,
                    hostLevel,
                    doorSymbol,
                    contextWalls,
                    vertical,
                    widthOptions,
                    result,
                    finalTypeCache,
                    baseSymbolCache,
                    placedByWall,
                    out createdDoorId,
                    out placementPoint,
                    out skipReason);

                if (!created)
                {
                    skipReason = "R3B create failed after minimal patch (patch retained): " + (skipReason ?? "unknown reason");
                    return false;
                }
            }

            RegisterDedicatedPipelineElements(result, candidate, patchWall, createdDoorId);
            return true;
        }

        private static void RegisterDedicatedPipelineElements(
            DoorCreateResult result,
            DoorCandidate candidate,
            Wall patchWall,
            int createdDoorId)
        {
            if (result == null)
            {
                return;
            }

            int patchWallId = patchWall != null ? patchWall.Id.IntegerValue : -1;
            if (patchWallId > 0)
            {
                RegisterCreatedElementId(result.CreatedAuxWallElementIds, patchWallId);
                RegisterCreatedElementId(result.CreatedElementIds, patchWallId);
            }

            if (createdDoorId > 0)
            {
                RegisterCreatedElementId(result.CreatedElementIds, createdDoorId);
            }

            DiagnosticRecorder.AppendDebug(
                "[R3BTrackingLink] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", RuleSource=" + (candidate == null ? string.Empty : (candidate.RuleSource ?? string.Empty)) +
                ", PatchWallId=" + patchWallId +
                ", DoorId=" + createdDoorId);
        }

        private static void RegisterCreatedElementId(List<int> target, int elementId)
        {
            if (target == null || elementId <= 0 || target.Contains(elementId))
            {
                return;
            }

            target.Add(elementId);
        }

        private static bool TryCreateR3BDoorWithPlacementRetries(
            Document doc,
            DoorCandidate candidate,
            Wall patchWall,
            XYZ patchCenter,
            XYZ openingDir,
            XYZ patchStart,
            XYZ patchEnd,
            double patchWallLengthMm,
            double openingWidthMm,
            Level hostLevel,
            FamilySymbol doorSymbol,
            List<Wall> contextWalls,
            VerticalDimensionSettings vertical,
            DoorWidthResolveOptions widthOptions,
            DoorCreateResult result,
            Dictionary<string, FamilySymbol> finalTypeCache,
            Dictionary<string, FamilySymbol> baseSymbolCache,
            Dictionary<int, List<XYZ>> placedByWall,
            out int createdDoorId,
            out XYZ placementPoint,
            out string skipReason)
        {
            createdDoorId = -1;
            placementPoint = patchCenter;
            skipReason = null;
            XYZ normalizedDir = Normalize2D(openingDir) ?? XYZ.BasisX;
            double[] offsetsMm = new[] { 0.0, 30.0, -30.0, 60.0, -60.0 };
            List<string> failures = new List<string>();

            foreach (double offsetMm in offsetsMm)
            {
                XYZ placementAnchor = patchCenter;
                if (Math.Abs(offsetMm) > 1e-6)
                {
                    double offsetFt = UnitUtils.ConvertToInternalUnits(offsetMm, UnitTypeId.Millimeters);
                    placementAnchor = patchCenter + normalizedDir.Multiply(offsetFt);
                }

                placementPoint = placementAnchor;
                double distToStartMm = UnitUtils.ConvertFromInternalUnits(placementAnchor.DistanceTo(patchStart), UnitTypeId.Millimeters);
                double distToEndMm = UnitUtils.ConvertFromInternalUnits(placementAnchor.DistanceTo(patchEnd), UnitTypeId.Millimeters);
                DiagnosticRecorder.AppendDebug(
                    "[R3BMinimalPatchDoorAttempt] CandidateId=" + candidate.CandidateId +
                    ", PatchWallId=" + patchWall.Id.IntegerValue +
                    ", RetryOffsetMm=" + offsetMm.ToString("F1") +
                    ", PlacementPoint=(" + placementAnchor.X.ToString("F3") + "," + placementAnchor.Y.ToString("F3") + "," + placementAnchor.Z.ToString("F3") + ")" +
                    ", RequestedDoorWidthMm=" + openingWidthMm.ToString("F1") +
                    ", CombinedWidthMm=" + candidate.CombinedWidthMm.ToString("F1") +
                    ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                    ", RequestedDoorHeightMm=" + ResolveTargetDoorHeightMm(vertical, doorSymbol).ToString("F1") +
                    ", DistToPatchStartMm=" + distToStartMm.ToString("F1") +
                    ", DistToPatchEndMm=" + distToEndMm.ToString("F1"));

                int tempDoorId;
                XYZ tempPlacement;
                string tempReason;
                bool created = TryCreateSingleDoorCore(
                    doc,
                    candidate,
                    patchWall,
                    placementAnchor,
                    hostLevel,
                    doorSymbol,
                    contextWalls,
                    vertical,
                    widthOptions,
                    result,
                    finalTypeCache,
                    baseSymbolCache,
                    placedByWall,
                    out tempDoorId,
                    out tempPlacement,
                    out tempReason);
                if (created)
                {
                    createdDoorId = tempDoorId;
                    placementPoint = tempPlacement ?? placementAnchor;
                    return true;
                }

                failures.Add("OffsetMm=" + offsetMm.ToString("F1") + ", Reason=" + (tempReason ?? "unknown"));
            }

            skipReason = "R3B create failed after minimal patch (patch retained): all placement retries failed.";
            DiagnosticRecorder.AppendDebug(
                "[R3BMinimalPatchRetryFailed] CandidateId=" + candidate.CandidateId +
                ", OpeningWidthMm=" + openingWidthMm.ToString("F1") +
                ", OpeningCenter=(" + patchCenter.X.ToString("F3") + "," + patchCenter.Y.ToString("F3") + "," + patchCenter.Z.ToString("F3") + ")" +
                ", OpeningDir=(" + normalizedDir.X.ToString("F4") + "," + normalizedDir.Y.ToString("F4") + ",0.0000)" +
                ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                ", PatchStart=(" + patchStart.X.ToString("F3") + "," + patchStart.Y.ToString("F3") + "," + patchStart.Z.ToString("F3") + ")" +
                ", PatchEnd=(" + patchEnd.X.ToString("F3") + "," + patchEnd.Y.ToString("F3") + "," + patchEnd.Z.ToString("F3") + ")" +
                ", Attempts=" + string.Join(" | ", failures));
            return false;
        }

        private static void EnsurePatchWallSafetyForDoorWidth(double requestedDoorWidthMm, ref double patchWallLengthMm)
        {
            if (requestedDoorWidthMm <= 1e-6)
            {
                return;
            }

            // Keep enough clearance on both wall ends so cutting range does not clip outside patch wall.
            double requiredLengthMm = requestedDoorWidthMm + (R3BPatchMinimumDoorEdgeClearanceMm * 2.0);
            if (patchWallLengthMm < requiredLengthMm)
            {
                patchWallLengthMm = requiredLengthMm;
            }
        }

        private static void ApplyR3BPatchWallPostCreateSafety(
            Document doc,
            DoorCandidate candidate,
            Wall patchWall,
            double openingWidthMm,
            double patchWallLengthMm,
            string patchWallTypeName,
            double patchWallThicknessMm)
        {
            if (doc == null || patchWall == null)
            {
                return;
            }

            bool joinEnd0Disabled = false;
            bool joinEnd1Disabled = false;

            try
            {
                WallUtils.DisallowWallJoinAtEnd(patchWall, 0);
                joinEnd0Disabled = !WallUtils.IsWallJoinAllowedAtEnd(patchWall, 0);
            }
            catch
            {
                joinEnd0Disabled = false;
            }

            try
            {
                WallUtils.DisallowWallJoinAtEnd(patchWall, 1);
                joinEnd1Disabled = !WallUtils.IsWallJoinAllowedAtEnd(patchWall, 1);
            }
            catch
            {
                joinEnd1Disabled = false;
            }

            DiagnosticRecorder.AppendDebug(
                "[R3BPatchWallSafety] RuleSource=" + (candidate != null ? (candidate.RuleSource ?? string.Empty) : string.Empty) +
                ", CandidateId=" + (candidate != null ? candidate.CandidateId.ToString() : "-1") +
                ", OpeningWidthMm=" + openingWidthMm.ToString("F1") +
                ", PatchWallLengthMm=" + patchWallLengthMm.ToString("F1") +
                ", PatchSafetyMarginPerSideMm=" + R3BPatchSafetyMarginPerSideMm.ToString("F1") +
                ", PatchMinimumDoorEdgeClearanceMm=" + R3BPatchMinimumDoorEdgeClearanceMm.ToString("F1") +
                ", PatchWallId=" + patchWall.Id.IntegerValue +
                ", PatchWallTypeName=" + (patchWallTypeName ?? string.Empty) +
                ", PatchWallThicknessMm=" + patchWallThicknessMm.ToString("F1"));

            DiagnosticRecorder.AppendDebug(
                "[R3BPatchWallJoinControl] RuleSource=" + (candidate != null ? (candidate.RuleSource ?? string.Empty) : string.Empty) +
                ", CandidateId=" + (candidate != null ? candidate.CandidateId.ToString() : "-1") +
                ", PatchWallId=" + patchWall.Id.IntegerValue +
                ", JoinEnd0Disabled=" + joinEnd0Disabled +
                ", JoinEnd1Disabled=" + joinEnd1Disabled);
        }

        private static XYZ ResolveR3BOpeningDirection(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            XYZ vStart = candidate.VirtualOpeningBaseStart;
            XYZ vEnd = candidate.VirtualOpeningBaseEnd;
            XYZ dir = (vStart != null && vEnd != null) ? Normalize2D(vEnd - vStart) : null;
            if (dir != null)
            {
                return dir;
            }

            XYZ s = candidate.OpeningBaseStartPoint;
            XYZ e = candidate.OpeningBaseEndPoint;
            dir = (s != null && e != null) ? Normalize2D(e - s) : null;
            if (dir != null)
            {
                return dir;
            }

            return Normalize2D(candidate.WallDirHint);
        }

        private static Wall ResolveR3BPatchTemplateWall(
            IReadOnlyList<Wall> walls,
            XYZ openingCenter,
            R3BPatchContext patchContext)
        {
            if (patchContext != null)
            {
                if (patchContext.ReferenceWall != null)
                {
                    return patchContext.ReferenceWall;
                }

                if (patchContext.LeftWall != null && patchContext.RightWall != null)
                {
                    return patchContext.LeftWallLengthMm >= patchContext.RightWallLengthMm
                        ? patchContext.LeftWall
                        : patchContext.RightWall;
                }

                if (patchContext.LeftWall != null)
                {
                    return patchContext.LeftWall;
                }

                if (patchContext.RightWall != null)
                {
                    return patchContext.RightWall;
                }
            }

            if (walls == null || walls.Count == 0)
            {
                return null;
            }

            Wall first = walls.FirstOrDefault(x => x != null);
            if (openingCenter == null)
            {
                return first;
            }

            Wall best = null;
            double bestDist = double.MaxValue;
            foreach (Wall wall in walls)
            {
                Line line;
                if (!TryGetWallLine(wall, out line))
                {
                    continue;
                }

                XYZ projected = line.Project(openingCenter)?.XYZPoint;
                if (projected == null)
                {
                    continue;
                }

                double dist = openingCenter.DistanceTo(projected);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = wall;
                }
            }

            return best ?? first;
        }

        private sealed class R3BPatchContext
        {
            public Wall LeftWall { get; set; }
            public Wall RightWall { get; set; }
            public Wall ReferenceWall { get; set; }
            public XYZ LeftSnapPoint { get; set; }
            public XYZ RightSnapPoint { get; set; }
            public XYZ PatchDirection { get; set; }
            public XYZ PatchStart { get; set; }
            public XYZ PatchEnd { get; set; }
            public XYZ PatchCenter { get; set; }
            public XYZ OpeningCenter { get; set; }
            public double LeftWallLengthMm { get; set; }
            public double RightWallLengthMm { get; set; }
            public double PairSpanMm { get; set; }
        }

        private static bool TryResolveR3BPatchContextFromOpeningEnds(
            DoorCandidate candidate,
            IReadOnlyList<Wall> walls,
            out R3BPatchContext context)
        {
            context = null;
            if (candidate == null || walls == null || walls.Count == 0)
            {
                return false;
            }

            XYZ start = candidate.VirtualOpeningBaseStart ?? candidate.OpeningBaseStartPoint;
            XYZ end = candidate.VirtualOpeningBaseEnd ?? candidate.OpeningBaseEndPoint;
            if (start == null || end == null)
            {
                return false;
            }

            XYZ openingDir = Normalize2D(end - start);
            if (openingDir == null)
            {
                return false;
            }

            XYZ openingCenter = new XYZ((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5, (start.Z + end.Z) * 0.5);
            XYZ hintDir = Normalize2D(candidate.WallDirHint);
            XYZ expectedDir = hintDir ?? openingDir;

            WallEndpointHit leftHit = FindBestWallEndpointHitForR3B(start, expectedDir, walls);
            WallEndpointHit rightHit = FindBestWallEndpointHitForR3B(end, expectedDir, walls);
            if (leftHit == null || rightHit == null)
            {
                return false;
            }

            if (leftHit.Wall.Id.IntegerValue == rightHit.Wall.Id.IntegerValue)
            {
                return false;
            }

            XYZ leftDir = Normalize2D(leftHit.WallDirection);
            XYZ rightDir = Normalize2D(rightHit.WallDirection);
            if (leftDir == null || rightDir == null)
            {
                return false;
            }

            double parallelAbs = Math.Abs(Dot(leftDir, rightDir));
            if (parallelAbs < 0.90)
            {
                return false;
            }

            double minPairSpanMm = Math.Max(300.0, ResolveR3BOpeningWidthMm(candidate) * 0.35);
            double maxPairSpanMm = Math.Max(6000.0, ResolveR3BOpeningWidthMm(candidate) * 4.0);
            double pairSpanMm = UnitUtils.ConvertFromInternalUnits(leftHit.SnapPoint.DistanceTo(rightHit.SnapPoint), UnitTypeId.Millimeters);
            if (pairSpanMm < minPairSpanMm || pairSpanMm > maxPairSpanMm)
            {
                return false;
            }

            WallEndpointHit referenceHit = SelectReferenceWallHitForR3B(leftHit, rightHit, openingCenter, expectedDir);
            if (referenceHit == null || referenceHit.WallLine == null)
            {
                return false;
            }

            XYZ referenceDir = Normalize2D(referenceHit.WallDirection) ?? Normalize2D(referenceHit.WallLine.GetEndPoint(1) - referenceHit.WallLine.GetEndPoint(0));
            if (referenceDir == null)
            {
                referenceDir = Normalize2D((leftDir + rightDir) * 0.5) ?? openingDir;
            }

            if (Dot(referenceDir, openingDir) < 0.0)
            {
                referenceDir = referenceDir.Multiply(-1.0);
            }

            XYZ refA = referenceHit.WallLine.GetEndPoint(0);
            XYZ refB = referenceHit.WallLine.GetEndPoint(1);
            XYZ projectedPatchCenter = ProjectPointToInfiniteLine(openingCenter, refA, refB) ?? openingCenter;
            double alignmentShiftMm = UnitUtils.ConvertFromInternalUnits(openingCenter.DistanceTo(projectedPatchCenter), UnitTypeId.Millimeters);
            if (alignmentShiftMm > 1200.0)
            {
                return false;
            }

            double maxCenterSnapMm = Math.Min(80.0, Math.Max(40.0, ResolveR3BOpeningWidthMm(candidate) * 0.12));
            bool useProjectedCenter = alignmentShiftMm <= maxCenterSnapMm;
            XYZ patchCenter = useProjectedCenter ? projectedPatchCenter : openingCenter;

            double axisHalfLengthFt = UnitUtils.ConvertToInternalUnits(1000.0, UnitTypeId.Millimeters);
            XYZ axisA = patchCenter - referenceDir.Multiply(axisHalfLengthFt);
            XYZ axisB = patchCenter + referenceDir.Multiply(axisHalfLengthFt);

            XYZ leftAligned = ProjectPointToInfiniteLine(leftHit.SnapPoint, axisA, axisB) ?? leftHit.ProjectedPoint ?? leftHit.SnapPoint;
            XYZ rightAligned = ProjectPointToInfiniteLine(rightHit.SnapPoint, axisA, axisB) ?? rightHit.ProjectedPoint ?? rightHit.SnapPoint;
            if (leftAligned == null || rightAligned == null)
            {
                return false;
            }

            double leftAlongFt = Dot(leftAligned - patchCenter, referenceDir);
            double rightAlongFt = Dot(rightAligned - patchCenter, referenceDir);
            double minAlongFt = Math.Min(leftAlongFt, rightAlongFt);
            double maxAlongFt = Math.Max(leftAlongFt, rightAlongFt);
            if ((maxAlongFt - minAlongFt) < 1e-9)
            {
                double halfOpeningFt = UnitUtils.ConvertToInternalUnits(ResolveR3BOpeningWidthMm(candidate) * 0.5, UnitTypeId.Millimeters);
                minAlongFt = -halfOpeningFt;
                maxAlongFt = halfOpeningFt;
            }

            XYZ patchStart = patchCenter + referenceDir.Multiply(minAlongFt);
            XYZ patchEnd = patchCenter + referenceDir.Multiply(maxAlongFt);
            if (patchStart == null || patchEnd == null || patchStart.DistanceTo(patchEnd) < 1e-9)
            {
                return false;
            }

            context = new R3BPatchContext
            {
                LeftWall = leftHit.Wall,
                RightWall = rightHit.Wall,
                ReferenceWall = referenceHit.Wall,
                LeftSnapPoint = leftHit.SnapPoint,
                RightSnapPoint = rightHit.SnapPoint,
                PatchDirection = referenceDir,
                PatchStart = patchStart,
                PatchEnd = patchEnd,
                PatchCenter = patchCenter,
                OpeningCenter = openingCenter,
                LeftWallLengthMm = leftHit.WallLengthMm,
                RightWallLengthMm = rightHit.WallLengthMm,
                PairSpanMm = pairSpanMm
            };

            DiagnosticRecorder.AppendDebug(
                "[R3BPatchContextResolved] CandidateId=" + candidate.CandidateId +
                ", LeftWallId=" + leftHit.Wall.Id.IntegerValue +
                ", RightWallId=" + rightHit.Wall.Id.IntegerValue +
                ", ReferenceWallId=" + referenceHit.Wall.Id.IntegerValue +
                ", LeftWallLengthMm=" + leftHit.WallLengthMm.ToString("F1") +
                ", RightWallLengthMm=" + rightHit.WallLengthMm.ToString("F1") +
                ", LeftEndDistMm=" + leftHit.EndDistanceMm.ToString("F1") +
                ", RightEndDistMm=" + rightHit.EndDistanceMm.ToString("F1") +
                ", LeftPerpDistMm=" + leftHit.PerpDistanceMm.ToString("F1") +
                ", RightPerpDistMm=" + rightHit.PerpDistanceMm.ToString("F1") +
                ", PairSpanMm=" + pairSpanMm.ToString("F1") +
                ", AlignmentShiftMm=" + alignmentShiftMm.ToString("F1") +
                ", CenterSnapLimitMm=" + maxCenterSnapMm.ToString("F1") +
                ", UsedProjectedCenter=" + useProjectedCenter);

            return true;
        }

        private sealed class WallEndpointHit
        {
            public Wall Wall { get; set; }
            public Line WallLine { get; set; }
            public XYZ SnapPoint { get; set; }
            public XYZ ProjectedPoint { get; set; }
            public XYZ WallDirection { get; set; }
            public double Score { get; set; }
            public double EndDistanceMm { get; set; }
            public double PerpDistanceMm { get; set; }
            public double WallLengthMm { get; set; }
        }

        private static WallEndpointHit FindBestWallEndpointHitForR3B(
            XYZ openingEnd,
            XYZ expectedDir,
            IReadOnlyList<Wall> walls)
        {
            if (openingEnd == null || expectedDir == null || walls == null)
            {
                return null;
            }

            double maxEndpointDistMm = 2500.0;
            double maxEndpointDistFt = UnitUtils.ConvertToInternalUnits(maxEndpointDistMm, UnitTypeId.Millimeters);
            double maxPerpDistMm = 800.0;
            double maxPerpDistFt = UnitUtils.ConvertToInternalUnits(maxPerpDistMm, UnitTypeId.Millimeters);
            double minParallelAbs = 0.85;

            WallEndpointHit best = null;
            foreach (Wall wall in walls)
            {
                Line line;
                if (!TryGetWallLine(wall, out line))
                {
                    continue;
                }

                XYZ a = line.GetEndPoint(0);
                XYZ b = line.GetEndPoint(1);
                XYZ wallDir = Normalize2D(b - a);
                if (wallDir == null)
                {
                    continue;
                }

                double parallelAbs = Math.Abs(Dot(wallDir, expectedDir));
                if (parallelAbs < minParallelAbs)
                {
                    continue;
                }

                XYZ nearestEnd = a.DistanceTo(openingEnd) <= b.DistanceTo(openingEnd) ? a : b;
                double endDistFt = nearestEnd.DistanceTo(openingEnd);
                if (endDistFt > maxEndpointDistFt)
                {
                    continue;
                }

                XYZ projected = ProjectPointToInfiniteLine(openingEnd, a, b);
                if (projected == null)
                {
                    continue;
                }

                double perpDistFt = openingEnd.DistanceTo(projected);
                if (perpDistFt > maxPerpDistFt)
                {
                    continue;
                }

                double wallLengthFt = a.DistanceTo(b);
                double lengthBonusFt = Math.Min(wallLengthFt, UnitUtils.ConvertToInternalUnits(2500.0, UnitTypeId.Millimeters)) * 0.03;
                double score = endDistFt + (perpDistFt * 0.4) + ((1.0 - parallelAbs) * 10.0) - lengthBonusFt;
                if (best == null || score < best.Score)
                {
                    best = new WallEndpointHit
                    {
                        Wall = wall,
                        WallLine = line,
                        SnapPoint = nearestEnd,
                        ProjectedPoint = projected,
                        WallDirection = wallDir,
                        Score = score,
                        EndDistanceMm = UnitUtils.ConvertFromInternalUnits(endDistFt, UnitTypeId.Millimeters),
                        PerpDistanceMm = UnitUtils.ConvertFromInternalUnits(perpDistFt, UnitTypeId.Millimeters),
                        WallLengthMm = UnitUtils.ConvertFromInternalUnits(wallLengthFt, UnitTypeId.Millimeters)
                    };
                }
            }

            return best;
        }

        private static WallEndpointHit SelectReferenceWallHitForR3B(
            WallEndpointHit leftHit,
            WallEndpointHit rightHit,
            XYZ openingCenter,
            XYZ expectedDir)
        {
            if (leftHit == null)
            {
                return rightHit;
            }

            if (rightHit == null)
            {
                return leftHit;
            }

            double leftCenterPerpMm = ResolvePointToWallPerpDistanceMm(openingCenter, leftHit.WallLine);
            double rightCenterPerpMm = ResolvePointToWallPerpDistanceMm(openingCenter, rightHit.WallLine);
            double centerPerpPreferToleranceMm = 80.0;
            if (leftCenterPerpMm + centerPerpPreferToleranceMm < rightCenterPerpMm)
            {
                return leftHit;
            }

            if (rightCenterPerpMm + centerPerpPreferToleranceMm < leftCenterPerpMm)
            {
                return rightHit;
            }

            double lengthToleranceMm = 100.0;
            if (leftHit.WallLengthMm > rightHit.WallLengthMm + lengthToleranceMm)
            {
                return leftHit;
            }

            if (rightHit.WallLengthMm > leftHit.WallLengthMm + lengthToleranceMm)
            {
                return rightHit;
            }

            double leftParallelAbs = Math.Abs(Dot(Normalize2D(leftHit.WallDirection) ?? XYZ.BasisX, expectedDir));
            double rightParallelAbs = Math.Abs(Dot(Normalize2D(rightHit.WallDirection) ?? XYZ.BasisX, expectedDir));
            if (leftParallelAbs > rightParallelAbs + 0.01)
            {
                return leftHit;
            }

            if (rightParallelAbs > leftParallelAbs + 0.01)
            {
                return rightHit;
            }

            if (leftHit.EndDistanceMm <= rightHit.EndDistanceMm)
            {
                return leftHit;
            }

            return rightHit;
        }

        private static double ResolvePointToWallPerpDistanceMm(XYZ point, Line wallLine)
        {
            if (point == null || wallLine == null)
            {
                return double.MaxValue;
            }

            XYZ a = wallLine.GetEndPoint(0);
            XYZ b = wallLine.GetEndPoint(1);
            XYZ projected = ProjectPointToInfiniteLine(point, a, b);
            if (projected == null)
            {
                return double.MaxValue;
            }

            return UnitUtils.ConvertFromInternalUnits(point.DistanceTo(projected), UnitTypeId.Millimeters);
        }

        private static void EnsureR3BPatchLineAroundCenter(
            ref XYZ patchStart,
            ref XYZ patchEnd,
            XYZ patchCenter,
            XYZ patchDir,
            double requiredLengthMm)
        {
            XYZ normalizedDir = Normalize2D(patchDir) ?? XYZ.BasisX;
            if (patchCenter == null)
            {
                patchCenter = patchStart ?? patchEnd;
            }

            if (patchCenter == null)
            {
                return;
            }

            double requiredLengthFt = UnitUtils.ConvertToInternalUnits(requiredLengthMm, UnitTypeId.Millimeters);
            double halfRequiredFt = requiredLengthFt * 0.5;

            if (patchStart == null || patchEnd == null)
            {
                patchStart = patchCenter - normalizedDir.Multiply(halfRequiredFt);
                patchEnd = patchCenter + normalizedDir.Multiply(halfRequiredFt);
                return;
            }

            double startAlongFt = Dot(patchStart - patchCenter, normalizedDir);
            double endAlongFt = Dot(patchEnd - patchCenter, normalizedDir);
            double minAlongFt = Math.Min(startAlongFt, endAlongFt);
            double maxAlongFt = Math.Max(startAlongFt, endAlongFt);
            double currentLengthFt = maxAlongFt - minAlongFt;

            if (currentLengthFt < requiredLengthFt)
            {
                double expandFt = (requiredLengthFt - currentLengthFt) * 0.5;
                minAlongFt -= expandFt;
                maxAlongFt += expandFt;
            }

            patchStart = patchCenter + normalizedDir.Multiply(minAlongFt);
            patchEnd = patchCenter + normalizedDir.Multiply(maxAlongFt);
        }

        private static XYZ ProjectPointToInfiniteLine(XYZ point, XYZ lineStart, XYZ lineEnd)
        {
            if (point == null || lineStart == null || lineEnd == null)
            {
                return null;
            }

            XYZ v = lineEnd - lineStart;
            double vv = Dot(v, v);
            if (vv < 1e-12)
            {
                return lineStart;
            }

            double t = Dot(point - lineStart, v) / vv;
            return lineStart + v.Multiply(t);
        }

        private static XYZ Normalize2D(XYZ v)
        {
            if (v == null)
            {
                return null;
            }

            double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
            if (len < 1e-9)
            {
                return null;
            }

            return new XYZ(v.X / len, v.Y / len, 0);
        }

        private static List<Wall> BuildPatchContextWalls(List<Wall> walls, Wall patchWall)
        {
            List<Wall> context = (walls ?? new List<Wall>()).Where(x => x != null).ToList();
            if (patchWall != null && !context.Any(x => x.Id.IntegerValue == patchWall.Id.IntegerValue))
            {
                context.Add(patchWall);
            }
            return context;
        }

        private static double ResolveR3BOpeningWidthMm(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 900.0;
            }

            if (candidate.VirtualOpeningWidthMm > 1e-6)
            {
                return candidate.VirtualOpeningWidthMm;
            }
            if (candidate.OpeningWidthMm > 1e-6)
            {
                return candidate.OpeningWidthMm;
            }
            if (candidate.WidthMm > 1e-6)
            {
                return candidate.WidthMm;
            }
            return 900.0;
        }

        private static ElementId ResolveOrCreateR3BPatchWallTypeId(
            Document doc,
            Wall templateWall,
            double thicknessMm)
        {
            if (doc == null || templateWall == null)
            {
                return templateWall != null ? templateWall.GetTypeId() : ElementId.InvalidElementId;
            }

            WallType templateWallType = doc.GetElement(templateWall.GetTypeId()) as WallType;
            if (templateWallType == null)
            {
                return templateWall.GetTypeId();
            }

            Func<ElementId> resolve = () =>
            {
                try
                {
                    string typeName = (templateWallType.Name ?? "BasicWall") + "_R3BPatch_" + ((int)Math.Round(thicknessMm)).ToString() + "mm";
                    WallType existingType = new FilteredElementCollector(doc)
                        .OfClass(typeof(WallType))
                        .Cast<WallType>()
                        .FirstOrDefault(x => x != null && string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
                    if (existingType != null)
                    {
                        return existingType.Id;
                    }

                    WallType newType = templateWallType.Duplicate(typeName) as WallType;
                    if (newType == null)
                    {
                        return templateWall.GetTypeId();
                    }

                    CompoundStructure structure = newType.GetCompoundStructure();
                    if (structure == null)
                    {
                        return templateWall.GetTypeId();
                    }

                    IList<CompoundStructureLayer> layers = structure.GetLayers();
                    if (layers == null || layers.Count == 0)
                    {
                        return templateWall.GetTypeId();
                    }

                    double targetThicknessFt = UnitUtils.ConvertToInternalUnits(thicknessMm, UnitTypeId.Millimeters);
                    if (layers.Count == 1)
                    {
                        structure.SetLayerWidth(0, targetThicknessFt);
                    }
                    else
                    {
                        double currentTotalFt = 0.0;
                        for (int i = 0; i < layers.Count; i++)
                        {
                            currentTotalFt += layers[i].Width;
                        }

                        double deltaFt = targetThicknessFt - currentTotalFt;
                        if (deltaFt > 1e-9)
                        {
                            int targetLayerIndex = ResolveR3BPatchTargetLayerIndex(structure, layers);
                            double updatedWidthFt = layers[targetLayerIndex].Width + deltaFt;
                            structure.SetLayerWidth(targetLayerIndex, updatedWidthFt);
                        }
                    }

                    newType.SetCompoundStructure(structure);
                    return newType.Id;
                }
                catch
                {
                    return templateWall.GetTypeId();
                }
            };

            if (doc.IsModifiable)
            {
                return resolve();
            }

            using (Transaction tx = new Transaction(doc, "Resolve R3B Patch Wall Type"))
            {
                tx.Start();
                ElementId resolvedTypeId = resolve();
                tx.Commit();
                return resolvedTypeId;
            }
        }

        private static int ResolveR3BPatchTargetLayerIndex(
            CompoundStructure structure,
            IList<CompoundStructureLayer> layers)
        {
            if (structure != null)
            {
                int firstCore = structure.GetFirstCoreLayerIndex();
                int lastCore = structure.GetLastCoreLayerIndex();
                if (firstCore >= 0 && lastCore >= firstCore && firstCore < layers.Count)
                {
                    return firstCore;
                }
            }

            return 0;
        }

        private static string ResolveWallTypeName(Document doc, ElementId wallTypeId)
        {
            WallType wallType = doc != null && wallTypeId != null && wallTypeId != ElementId.InvalidElementId
                ? doc.GetElement(wallTypeId) as WallType
                : null;
            return wallType != null ? wallType.Name : string.Empty;
        }

        private static double ResolveWallTypeWidthMm(Document doc, ElementId wallTypeId, double fallbackMm)
        {
            WallType wallType = doc != null && wallTypeId != null && wallTypeId != ElementId.InvalidElementId
                ? doc.GetElement(wallTypeId) as WallType
                : null;
            if (wallType == null)
            {
                return fallbackMm;
            }

            try
            {
                return UnitUtils.ConvertFromInternalUnits(wallType.Width, UnitTypeId.Millimeters);
            }
            catch
            {
                return fallbackMm;
            }
        }

        private static double ResolvePatchWallHeightFeet(Wall referenceWall)
        {
            double defaultFt = UnitUtils.ConvertToInternalUnits(R3BDefaultPatchWallHeightMm, UnitTypeId.Millimeters);
            if (referenceWall == null)
            {
                return defaultFt;
            }

            Parameter p = referenceWall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            if (p != null && p.StorageType == StorageType.Double)
            {
                double value = p.AsDouble();
                if (value > 1e-6)
                {
                    return value;
                }
            }

            return defaultFt;
        }

        private static double ResolvePatchWallBaseOffsetFeet(Wall referenceWall)
        {
            if (referenceWall == null)
            {
                return 0.0;
            }

            Parameter p = referenceWall.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            if (p != null && p.StorageType == StorageType.Double)
            {
                return p.AsDouble();
            }

            return 0.0;
        }

        private static void AddPlacedPoint(
            Dictionary<int, List<XYZ>> placedByWall,
            Wall wall,
            XYZ point)
        {
            if (placedByWall == null || wall == null || point == null)
            {
                return;
            }

            int key = wall.Id.IntegerValue;
            List<XYZ> list;
            if (!placedByWall.TryGetValue(key, out list))
            {
                list = new List<XYZ>();
                placedByWall[key] = list;
            }

            list.Add(point);
        }

        private static Level ResolveHostLevel(Document doc, Wall wall)
        {
            Parameter p = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (p != null && p.AsElementId() != ElementId.InvalidElementId)
            {
                Level fromWall = doc.GetElement(p.AsElementId()) as Level;
                if (fromWall != null)
                {
                    return fromWall;
                }
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static XYZ ResolveHostMatchPoint(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return null;
            }

            if (string.Equals(candidate.RuleSource, "R3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.RuleSource, "R3T", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveOpeningBaseCenter(candidate) ??
                       candidate.OpeningCenterPoint ??
                       candidate.HingePoint ??
                       candidate.ArcMidPoint ??
                       candidate.CenterPoint;
            }

            return candidate.OpeningCenterPoint ?? candidate.CenterPoint;
        }

        private static XYZ ResolvePlacementPointOnWall(
            DoorCandidate candidate,
            Wall wall,
            XYZ fallbackProjected,
            FamilySymbol doorSymbol,
            out string placementSource)
        {
            placementSource = "FallbackProjected";
            if (wall == null)
            {
                return fallbackProjected;
            }

            Line wallLine;
            if (!TryGetWallLine(wall, out wallLine))
            {
                return fallbackProjected;
            }

            XYZ placement = null;
            if (IsOpeningBasePreferredCandidate(candidate))
            {
                XYZ openingBaseCenter = ResolveOpeningBaseCenter(candidate);
                if (openingBaseCenter != null)
                {
                    placement = wallLine.Project(openingBaseCenter)?.XYZPoint;
                    if (placement != null)
                    {
                        placementSource = "OpeningBaseCenter";
                    }
                }
            }

            XYZ openingCenter = candidate == null ? null : candidate.OpeningCenterPoint;
            if (placement == null && openingCenter != null)
            {
                placement = wallLine.Project(openingCenter)?.XYZPoint;
                if (placement != null)
                {
                    placementSource = "OpeningCenter";
                }
            }

            XYZ hinge = candidate == null ? null : candidate.HingePoint;
            if (placement == null && hinge != null && !IsOpeningBasePreferredCandidate(candidate))
            {
                double widthMm = ResolveDoorWidthMm(candidate, doorSymbol);
                if (widthMm > 1e-6)
                {
                    XYZ wallDir = wallLine.Direction.Normalize();
                    double sign = ResolveFallbackSign(candidate, wallDir);
                    double halfWidthFt = UnitUtils.ConvertToInternalUnits(widthMm * 0.5, UnitTypeId.Millimeters);
                    XYZ targetCenter = hinge + wallDir.Multiply(sign * halfWidthFt);
                    placement = wallLine.Project(targetCenter)?.XYZPoint;
                    if (placement != null)
                    {
                        placementSource = "HingeHalfWidth";
                    }
                }
            }

            if (placement == null)
            {
                placement = fallbackProjected;
                placementSource = "FallbackProjected";
            }

            UpdatePlacementDiagnostics(candidate, wallLine, placement, placementSource);
            return placement;
        }

        private static bool IsOpeningBasePreferredCandidate(DoorCandidate candidate)
        {
            if (candidate == null || !(candidate.PreferOpeningBaseHost || candidate.PreferVirtualOpeningHost))
            {
                return false;
            }

            // Opening-base / preferred-host behavior is broader than patch-wall behavior.
            // R3D must keep preferred-host matching, but must NOT enter the patch-wall pipeline.
            return IsR3BDedicatedCandidate(candidate) ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing ||
                   candidate.SymbolFamilyKind == DoorSymbolFamilyKind.TripleArcDoorWithWallCrossing ||
                   string.Equals(candidate.RuleSource, "R3T", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(candidate.RuleSource, "AltArc", StringComparison.OrdinalIgnoreCase);
        }

        private static XYZ ResolveOpeningBaseCenter(DoorCandidate candidate)
        {
            if (candidate?.VirtualOpeningBaseCenter != null)
            {
                return candidate.VirtualOpeningBaseCenter;
            }

            if (candidate?.VirtualOpeningBaseStart != null && candidate.VirtualOpeningBaseEnd != null)
            {
                XYZ vs = candidate.VirtualOpeningBaseStart;
                XYZ ve = candidate.VirtualOpeningBaseEnd;
                return new XYZ((vs.X + ve.X) * 0.5, (vs.Y + ve.Y) * 0.5, (vs.Z + ve.Z) * 0.5);
            }

            if (candidate?.OpeningBaseStartPoint == null || candidate.OpeningBaseEndPoint == null)
            {
                return null;
            }

            XYZ s = candidate.OpeningBaseStartPoint;
            XYZ e = candidate.OpeningBaseEndPoint;
            return new XYZ((s.X + e.X) * 0.5, (s.Y + e.Y) * 0.5, (s.Z + e.Z) * 0.5);
        }

        private static XYZ ResolvePreferredHostPoint(DoorCandidate candidate, XYZ fallback)
        {
            return candidate?.PreferredHostPoint ??
                   ResolveOpeningBaseCenter(candidate) ??
                   candidate?.OpeningCenterPoint ??
                   fallback;
        }

        private static double ResolveFallbackSign(DoorCandidate candidate, XYZ wallDir)
        {
            if (candidate == null || candidate.HingePoint == null || wallDir == null)
            {
                return 1.0;
            }

            XYZ refPoint = candidate.ArcMidPoint ?? candidate.CenterPoint ?? candidate.OpeningCenterPoint;
            if (refPoint == null)
            {
                return 1.0;
            }

            XYZ v = refPoint - candidate.HingePoint;
            double dot = v.DotProduct(wallDir);
            if (Math.Abs(dot) < 1e-9)
            {
                return 1.0;
            }

            return dot >= 0.0 ? 1.0 : -1.0;
        }

        private static bool TryGetWallLine(Wall wall, out Line line)
        {
            line = null;
            if (wall == null)
            {
                return false;
            }

            LocationCurve loc = wall.Location as LocationCurve;
            line = loc?.Curve as Line;
            return line != null;
        }

        private static double ResolveDoorWidthMm(
            DoorCandidate candidate,
            FamilySymbol doorSymbol)
        {
            // Keep legacy fallback behavior for placement-point estimation.
            if (candidate != null && candidate.WidthMm > 1e-6)
            {
                return candidate.WidthMm;
            }

            if (doorSymbol == null)
            {
                return 0.0;
            }

            Parameter width = doorSymbol.LookupParameter("Width") ?? doorSymbol.LookupParameter("宽度") ?? doorSymbol.LookupParameter("寬度");
            if (width == null)
            {
                return 0.0;
            }

            return UnitUtils.ConvertFromInternalUnits(width.AsDouble(), UnitTypeId.Millimeters);
        }

        private static double ResolveDoorWidthMm(
            Document doc,
            DoorCandidate candidate,
            Wall hostWall,
            IEnumerable<Wall> hostWalls,
            FamilySymbol doorSymbol,
            DoorWidthResolveOptions options,
            out string source)
        {
            source = "FamilyDefault";
            DoorWidthResolveOptions effective = options ?? new DoorWidthResolveOptions();
            double minWidth = effective.MinDoorWidthMm > 0 ? effective.MinDoorWidthMm : 600.0;
            double maxWidth = effective.MaxDoorWidthMm > 0 ? effective.MaxDoorWidthMm : 3000.0;
            if (candidate != null &&
                string.Equals(candidate.RuleSource, "R3T", StringComparison.OrdinalIgnoreCase) &&
                maxWidth < 3600.0)
            {
                maxWidth = 3600.0;
            }
            double combinedWidthMm = ResolveCombinedWidthMm(candidate);
            if (candidate != null && candidate.IsDoubleDoor && combinedWidthMm > 1e-6)
            {
                source = "Combined";
                candidate.CombinedWidthMm = combinedWidthMm;
                candidate.WidthMm = combinedWidthMm;
                candidate.OpeningWidthMm = combinedWidthMm;
                candidate.WidthSource = source;
                if (candidate.CombinedCenter != null)
                {
                    candidate.OpeningCenterPoint = candidate.CombinedCenter;
                }

                return combinedWidthMm;
            }

            if (IsOpeningBasePreferredCandidate(candidate))
            {
                double virtualWidthMm = candidate.VirtualOpeningWidthMm > 1e-6 ? candidate.VirtualOpeningWidthMm : candidate.WidthMm;
                if (virtualWidthMm >= minWidth && virtualWidthMm <= maxWidth)
                {
                    source = "VirtualOpening";
                    candidate.WidthMm = virtualWidthMm;
                    candidate.OpeningWidthMm = virtualWidthMm;
                    candidate.WidthSource = source;
                    if (candidate.VirtualOpeningBaseCenter != null)
                    {
                        candidate.OpeningCenterPoint = candidate.VirtualOpeningBaseCenter;
                    }

                    return virtualWidthMm;
                }
            }

            // For R3 single-door candidates, prioritize CAD candidate width before geometry opening width.
            bool isR3SingleDoor = candidate != null &&
                                  string.Equals(candidate.RuleSource, "R3", StringComparison.OrdinalIgnoreCase) &&
                                  !candidate.IsDoubleDoor;
            if (isR3SingleDoor && candidate.WidthMm > 1e-6 && candidate.WidthMm >= minWidth && candidate.WidthMm <= maxWidth)
            {
                source = "ArcCandidate";
                candidate.WidthSource = source;
                return candidate.WidthMm;
            }

            double openingWidthMm;
            XYZ openingCenter;
            string reason;
            bool geometryOk = DoorOpeningWidthResolver.TryResolveOpeningWidthMm(doc, candidate, hostWall, hostWalls, out openingWidthMm, out openingCenter, out reason);
            bool geometryInRange = geometryOk && openingWidthMm >= minWidth && openingWidthMm <= maxWidth;
            if (candidate != null)
            {
                candidate.OpeningWidthMm = geometryOk ? openingWidthMm : 0.0;
                if (openingCenter != null)
                {
                    candidate.OpeningCenterPoint = openingCenter;
                }
            }
            if (effective.PreferGeometryOpeningWidth && geometryInRange)
            {
                source = "GeometryOpening";
                if (candidate != null)
                {
                    candidate.WidthMm = openingWidthMm;
                    candidate.WidthSource = source;
                }

                return openingWidthMm;
            }

            if (effective.UseFixedDoorWidth && effective.FixedDoorWidthMm.HasValue && effective.FixedDoorWidthMm.Value > 0)
            {
                source = "FixedExpectedWidth";
                if (candidate != null)
                {
                    candidate.WidthMm = effective.FixedDoorWidthMm.Value;
                    candidate.WidthSource = source;
                }

                return effective.FixedDoorWidthMm.Value;
            }

            if (!effective.PreferGeometryOpeningWidth && geometryInRange)
            {
                source = "GeometryOpening";
                if (candidate != null)
                {
                    candidate.WidthMm = openingWidthMm;
                    candidate.WidthSource = source;
                }

                return openingWidthMm;
            }

            if (doorSymbol == null)
            {
                return 0.0;
            }

            Parameter width = doorSymbol.LookupParameter("Width") ?? doorSymbol.LookupParameter("宽度") ?? doorSymbol.LookupParameter("寬度");
            if (width == null)
            {
                return 0.0;
            }

            double value = UnitUtils.ConvertFromInternalUnits(width.AsDouble(), UnitTypeId.Millimeters);
            if (candidate != null)
            {
                candidate.WidthSource = "FamilyDefault";
                candidate.WidthMm = value;
            }

            return value;
        }

        private static void UpdatePlacementDiagnostics(DoorCandidate candidate, Line wallLine, XYZ placementPoint, string placementSource)
        {
            if (candidate == null)
            {
                return;
            }

            candidate.FinalPlacementPoint = placementPoint;
            candidate.PlacementSource = placementSource;
            if (candidate.HingePoint == null || wallLine == null || placementPoint == null)
            {
                return;
            }

            XYZ wallDir = wallLine.Direction.Normalize();
            double deltaFeet = (placementPoint - candidate.HingePoint).DotProduct(wallDir);
            candidate.DeltaAlongWallMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(deltaFeet), UnitTypeId.Millimeters);
            DiagnosticRecorder.AppendDebug(
                "[DoorPlacement] CandidateId=" + candidate.CandidateId +
                ", Rule=" + candidate.RuleSource +
                ", WidthSource=" + (candidate.WidthSource ?? string.Empty) +
                ", PlacementSource=" + (candidate.PlacementSource ?? string.Empty) +
                ", WidthMm=" + candidate.WidthMm.ToString("F1") +
                ", DeltaAlongWallMm=" + candidate.DeltaAlongWallMm.ToString("F1"));
        }

        // Try instance parameter first, then fallback to type parameter.
        private static bool TrySetDoorWidth(FamilyInstance door, double widthMm, bool allowTypeFallback, out string writeTarget)
        {
            writeTarget = "None";
            if (door == null || widthMm <= 1e-6)
            {
                return false;
            }

            double widthFeet = UnitUtils.ConvertToInternalUnits(widthMm, UnitTypeId.Millimeters);
            if (TrySetWidthParameter(door.LookupParameter("Width"), widthFeet) ||
                TrySetWidthParameter(door.LookupParameter("宽度"), widthFeet) ||
                TrySetWidthParameter(door.LookupParameter("寬度"), widthFeet))
            {
                writeTarget = "Instance";
                return true;
            }

            FamilySymbol symbol = door.Symbol;
            if (symbol == null)
            {
                return false;
            }

            if (!allowTypeFallback)
            {
                if (HasWidthValue(symbol, widthMm))
                {
                    writeTarget = "TypePreset";
                    return true;
                }

                return false;
            }

            if (TrySetWidthParameter(symbol.LookupParameter("Width"), widthFeet) ||
                TrySetWidthParameter(symbol.LookupParameter("宽度"), widthFeet) ||
                TrySetWidthParameter(symbol.LookupParameter("寬度"), widthFeet))
            {
                writeTarget = "Type";
                return true;
            }

            return false;
        }

        private static bool TrySetWidthParameter(Parameter parameter, double widthFeet)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            {
                return false;
            }

            return parameter.Set(widthFeet);
        }

        private static double ResolveCombinedWidthMm(DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return 0.0;
            }

            if (candidate.LeftEdgePoint != null && candidate.RightEdgePoint != null)
            {
                return UnitUtils.ConvertFromInternalUnits(
                    candidate.LeftEdgePoint.DistanceTo(candidate.RightEdgePoint),
                    UnitTypeId.Millimeters);
            }

            return candidate.CombinedWidthMm > 1e-6 ? candidate.CombinedWidthMm : 0.0;
        }

        private static string FormatPointForLog(XYZ point)
        {
            if (point == null)
            {
                return string.Empty;
            }

            double x = UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters);
            double z = UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters);
            return "(" + x.ToString("F1") + "," + y.ToString("F1") + "," + z.ToString("F1") + ")";
        }

        private static void TryApplyDoorVerticalDimensions(
            Document doc,
            FamilySymbol symbol,
            FamilyInstance inst,
            VerticalDimensionSettings vertical,
            DoorCandidate candidate,
            DoorCreateResult result)
        {
            if (doc == null || inst == null || symbol == null || vertical == null)
            {
                return;
            }

            // Head height is intentionally not used in the current mode.
            bool ok = vertical.DoorHeightMm > 0;
            bool sillOk = true;
            if (vertical.DoorSillHeightMm >= 0)
            {
                sillOk = RevitParameterSetters.TrySetInstanceLength(inst, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM, vertical.DoorSillHeightMm) ||
                         RevitParameterSetters.TrySetByNames(inst, vertical.DoorSillHeightMm, "Sill Height", "Door Sill Height");
            }
            DiagnosticRecorder.AppendDebug(
                "[DoorVertical] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", DoorHeightMm=" + vertical.DoorHeightMm.ToString("F1") +
                ", DoorSillHeightMm=" + vertical.DoorSillHeightMm.ToString("F1") +
                ", SillHeightApplied=" + sillOk +
                ", HeadHeightApplied=False" +
                ", DoorHeadHeightMm not used in current mode");

            if (ok)
            {
                result.HeightSetSuccessCount++;
                if (candidate != null)
                {
                    candidate.FinalHeightMmApplied = vertical.DoorHeightMm;
                }
            }
            else
            {
                result.HeightSetFailedCount++;
                AddReason(result, "DoorHeightSetFailed: Candidate " + (candidate == null ? 0 : candidate.CandidateId));
            }
        }

        private static double ResolveTargetDoorHeightMm(VerticalDimensionSettings vertical, FamilySymbol fallbackSymbol)
        {
            if (vertical != null && vertical.DoorHeightMm > 0)
            {
                return vertical.DoorHeightMm;
            }

            double fallbackMm;
            if (TryGetTypeLengthMm(fallbackSymbol, new[] { "Height", "Rough Height", "Door Height", "\u9AD8\u5EA6" }, out fallbackMm) && fallbackMm > 0)
            {
                return fallbackMm;
            }

            return 2100.0;
        }

        private static FamilySymbol ResolveBaseDoorSymbolForCandidate(
            Document doc,
            FamilySymbol configuredSymbol,
            DoorCandidate candidate,
            Dictionary<string, FamilySymbol> cache)
        {
            if (configuredSymbol == null)
            {
                return null;
            }

            bool needsDoubleDoor = candidate != null && candidate.IsDoubleDoor;
            string cacheKey = "configured";
            if (cache != null && cache.TryGetValue(cacheKey, out FamilySymbol cached) && cached != null)
            {
                return cached;
            }

            // Business rule: double-arc candidates represent a wide single opening.
            // Keep using configured single-door symbol; width is resolved separately.
            FamilySymbol resolved = configuredSymbol;

            DiagnosticRecorder.AppendDebug(
                "[DoorBaseSymbolSelect] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", NeedsDoubleDoor=" + needsDoubleDoor +
                ", ConfiguredSymbol=" + configuredSymbol.Name +
                ", SelectedSymbol=" + (resolved == null ? string.Empty : resolved.Name));

            if (cache != null && resolved != null)
            {
                cache[cacheKey] = resolved;
            }

            return resolved;
        }

        private static FamilySymbol FindDoorSymbolByLeafMode(Document doc, bool needDouble)
        {
            if (doc == null)
            {
                return null;
            }

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x != null)
                .FirstOrDefault(x => IsDoubleDoorSymbol(x) == needDouble);
        }

        private static bool IsDoubleDoorSymbol(FamilySymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            string token = ((symbol.FamilyName ?? string.Empty) + "|" + (symbol.Name ?? string.Empty)).ToLowerInvariant();
            return token.Contains("double") ||
                   token.Contains("double leaf") ||
                   token.Contains("two leaf") ||
                   token.Contains("2 leaf") ||
                   token.Contains("双") ||
                   token.Contains("双开") ||
                   token.Contains("双扇");
        }

        private static FamilySymbol ResolveFinalDoorSymbol(
            Document doc,
            FamilySymbol baseSymbol,
            double targetWidthMm,
            double targetHeightMm,
            DoorCandidate candidate,
            DoorCreateResult result,
            Dictionary<string, FamilySymbol> cache)
        {
            if (doc == null || baseSymbol == null)
            {
                return baseSymbol;
            }

            double stableWidthMm = NormalizeDoorTypeWidthMm(targetWidthMm, candidate);
            int w = (int)Math.Round(Math.Max(stableWidthMm, 1.0));
            int h = (int)Math.Round(Math.Max(targetHeightMm, 1.0));
            string cacheKey = BuildDoorTypeCacheKey(baseSymbol, w, h);

            FamilySymbol cached;
            if (cache != null && cache.TryGetValue(cacheKey, out cached))
            {
                if (IsUsableDoorSymbol(doc, cached))
                {
                    ApplyResolvedTypeDimensionsToCandidate(cached, stableWidthMm, targetHeightMm, candidate);
                    return cached;
                }

                cache.Remove(cacheKey);
                DiagnosticRecorder.AppendDebug(
                    "[DoorTypeCacheInvalidated] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", CacheKey=" + cacheKey +
                    ", Reason=Cached symbol is invalid or was rolled back.");
            }

            string typeName = baseSymbol.Name + "_W" + w + "_H" + h;
            FamilySymbol finalSymbol = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => IsUsableDoorSymbol(doc, x) &&
                                     x.FamilyName == baseSymbol.FamilyName &&
                                     string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));

            bool foundExactExistingType = finalSymbol != null;
            bool reusedNearbyType = false;
            if (finalSymbol == null && IsR3DStableWidthCandidate(candidate))
            {
                finalSymbol = FindReusableNearbyDoorSymbol(
                    doc,
                    baseSymbol,
                    targetWidthMm,
                    targetHeightMm,
                    DoorTypeReuseToleranceMm);
                reusedNearbyType = finalSymbol != null;

                if (reusedNearbyType)
                {
                    double nearbyWidthMm;
                    TryGetTypeLengthMm(
                        finalSymbol,
                        new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" },
                        out nearbyWidthMm);
                    DiagnosticRecorder.AppendDebug(
                        "[DoorTypeNearMatch] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                        ", RequestedWidthMm=" + targetWidthMm.ToString("F1") +
                        ", ReusedWidthMm=" + nearbyWidthMm.ToString("F1") +
                        ", ToleranceMm=" + DoorTypeReuseToleranceMm.ToString("F1") +
                        ", SymbolId=" + finalSymbol.Id.IntegerValue +
                        ", SymbolName=" + finalSymbol.Name);
                }
            }

            bool createdNewType = false;
            bool widthSet = false;
            bool heightSet = false;
            if (finalSymbol == null)
            {
                FamilySymbol dup = baseSymbol.Duplicate(typeName) as FamilySymbol;
                if (dup == null)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[DoorTypeCreateFailed] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                        ", TypeName=" + typeName +
                        ", Reason=FamilySymbol.Duplicate returned null.");
                    return null;
                }

                finalSymbol = dup;
                createdNewType = true;
            }

            // IMPORTANT:
            // Do not write Width/Height back to an existing exact or nearby type. Revit can
            // regenerate a loaded door family even when Parameter.Set writes the same value,
            // and some rotated R3D instances then fail with "Profile sketch is empty".
            // Only a newly duplicated type requires its dimensions to be initialized.
            if (createdNewType)
            {
                widthSet = RevitParameterSetters.TrySetTypeByNames(
                    finalSymbol,
                    stableWidthMm,
                    "Width", "Rough Width", "Door Width", "宽度", "寬度");
                heightSet = RevitParameterSetters.TrySetTypeByNames(
                    finalSymbol,
                    targetHeightMm,
                    "Height", "Rough Height", "Door Height", "高度");

                DiagnosticRecorder.AppendDebug(
                    "[DoorTypeCreatedNew] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", SymbolId=" + finalSymbol.Id.IntegerValue +
                    ", SymbolName=" + finalSymbol.Name +
                    ", WidthSet=" + widthSet +
                    ", HeightSet=" + heightSet);
            }
            else
            {
                double existingWidthMm;
                double existingHeightMm;
                bool hasExistingWidth = TryGetTypeLengthMm(
                    finalSymbol,
                    new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" },
                    out existingWidthMm);
                bool hasExistingHeight = TryGetTypeLengthMm(
                    finalSymbol,
                    new[] { "Height", "Rough Height", "Door Height", "高度" },
                    out existingHeightMm);

                DiagnosticRecorder.AppendDebug(
                    "[DoorTypeParameterWriteSkipped] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", SymbolId=" + finalSymbol.Id.IntegerValue +
                    ", SymbolName=" + finalSymbol.Name +
                    ", Reason=" + (foundExactExistingType ? "ExactExistingType" : "NearbyExistingType") +
                    ", ExistingWidthMm=" + (hasExistingWidth ? existingWidthMm.ToString("F1") : "Unknown") +
                    ", ExistingHeightMm=" + (hasExistingHeight ? existingHeightMm.ToString("F1") : "Unknown") +
                    ", RequestedStableWidthMm=" + stableWidthMm.ToString("F1") +
                    ", RequestedHeightMm=" + targetHeightMm.ToString("F1"));
            }

            if (createdNewType || widthSet || heightSet)
            {
                doc.Regenerate();
                DiagnosticRecorder.AppendDebug("[DoorRegen] AfterTypePrepared=True");
            }

            if (!IsUsableDoorSymbol(doc, finalSymbol))
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorTypePreparedInvalid] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", RequestedWidthMm=" + targetWidthMm.ToString("F1") +
                    ", StableWidthMm=" + stableWidthMm.ToString("F1") +
                    ", TargetHeightMm=" + targetHeightMm.ToString("F1"));
                return null;
            }

            DiagnosticRecorder.AppendDebug(
                "[DoorTypePrepared] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", BaseSymbolName=" + baseSymbol.Name +
                ", TargetWidthMm=" + targetWidthMm.ToString("F1") +
                ", StableWidthMm=" + stableWidthMm.ToString("F1") +
                ", TargetHeightMm=" + targetHeightMm.ToString("F1") +
                ", FoundExactExistingType=" + foundExactExistingType +
                ", ReusedNearbyType=" + reusedNearbyType +
                ", CreatedNewType=" + createdNewType +
                ", FinalSymbolName=" + finalSymbol.Name +
                ", FinalSymbolId=" + finalSymbol.Id.IntegerValue);

            if (cache != null)
            {
                cache[cacheKey] = finalSymbol;
            }

            ApplyResolvedTypeDimensionsToCandidate(finalSymbol, stableWidthMm, targetHeightMm, candidate);
            return finalSymbol;
        }

        private static double NormalizeDoorTypeWidthMm(double targetWidthMm, DoorCandidate candidate)
        {
            double safeWidthMm = Math.Max(targetWidthMm, 1.0);
            if (!IsR3DStableWidthCandidate(candidate))
            {
                return safeWidthMm;
            }

            // Rotated copies of the same DWG double door can differ by a few millimetres
            // because the opening width is reconstructed from transformed line/arc endpoints.
            // Normalizing only R3D wall-crossing doors to 10 mm prevents equivalent copies
            // from producing unstable W2061/W2065 family types.
            double normalizedWidthMm = Math.Round(
                safeWidthMm / R3DStableWidthStepMm,
                MidpointRounding.ToEven) * R3DStableWidthStepMm;

            if (normalizedWidthMm < 1.0)
            {
                normalizedWidthMm = safeWidthMm;
            }

            if (Math.Abs(normalizedWidthMm - safeWidthMm) > 0.01)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DoorTypeWidthNormalized] CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                    ", Rule=" + (candidate == null ? string.Empty : (candidate.RuleSource ?? string.Empty)) +
                    ", OriginalWidthMm=" + safeWidthMm.ToString("F1") +
                    ", StableWidthMm=" + normalizedWidthMm.ToString("F1") +
                    ", StepMm=" + R3DStableWidthStepMm.ToString("F1"));
            }

            return normalizedWidthMm;
        }

        private static bool IsR3DStableWidthCandidate(DoorCandidate candidate)
        {
            return candidate != null &&
                   (candidate.SymbolFamilyKind == DoorSymbolFamilyKind.DoubleArcDoorWithWallCrossing ||
                    string.Equals(candidate.RuleSource, "R3D", StringComparison.OrdinalIgnoreCase));
        }

        private static FamilySymbol FindReusableNearbyDoorSymbol(
            Document doc,
            FamilySymbol baseSymbol,
            double targetWidthMm,
            double targetHeightMm,
            double toleranceMm)
        {
            if (doc == null || baseSymbol == null)
            {
                return null;
            }

            FamilySymbol best = null;
            double bestWidthDifferenceMm = double.MaxValue;
            IEnumerable<FamilySymbol> symbols = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => IsUsableDoorSymbol(doc, x) && x.FamilyName == baseSymbol.FamilyName);

            foreach (FamilySymbol symbol in symbols)
            {
                double symbolWidthMm;
                if (!TryGetTypeLengthMm(
                    symbol,
                    new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" },
                    out symbolWidthMm))
                {
                    continue;
                }

                double symbolHeightMm;
                if (TryGetTypeLengthMm(
                    symbol,
                    new[] { "Height", "Rough Height", "Door Height", "高度" },
                    out symbolHeightMm) &&
                    Math.Abs(symbolHeightMm - targetHeightMm) > DoorTypeHeightToleranceMm)
                {
                    continue;
                }

                double widthDifferenceMm = Math.Abs(symbolWidthMm - targetWidthMm);
                if (widthDifferenceMm > toleranceMm || widthDifferenceMm >= bestWidthDifferenceMm)
                {
                    continue;
                }

                best = symbol;
                bestWidthDifferenceMm = widthDifferenceMm;
            }

            return best;
        }

        private static string BuildDoorTypeCacheKey(FamilySymbol baseSymbol, int widthMm, int heightMm)
        {
            return (baseSymbol == null ? string.Empty : (baseSymbol.FamilyName ?? string.Empty)) +
                   "|W" + widthMm +
                   "|H" + heightMm;
        }

        private static bool IsUsableDoorSymbol(Document doc, FamilySymbol symbol)
        {
            try
            {
                if (doc == null || symbol == null || !symbol.IsValidObject ||
                    symbol.Id == null || symbol.Id == ElementId.InvalidElementId)
                {
                    return false;
                }

                FamilySymbol current = doc.GetElement(symbol.Id) as FamilySymbol;
                return current != null && current.IsValidObject;
            }
            catch
            {
                return false;
            }
        }

        private static void SanitizeDoorSymbolCache(
            Document doc,
            Dictionary<string, FamilySymbol> cache,
            string reason)
        {
            if (cache == null || cache.Count == 0)
            {
                return;
            }

            List<string> invalidKeys = cache
                .Where(x => !IsUsableDoorSymbol(doc, x.Value))
                .Select(x => x.Key)
                .ToList();

            foreach (string key in invalidKeys)
            {
                cache.Remove(key);
                DiagnosticRecorder.AppendDebug(
                    "[DoorTypeCacheInvalidated] CacheKey=" + key +
                    ", Reason=" + (reason ?? string.Empty));
            }
        }

        private static void ApplyResolvedTypeDimensionsToCandidate(
            FamilySymbol finalSymbol,
            double fallbackWidthMm,
            double fallbackHeightMm,
            DoorCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            double actualWidthMm;
            candidate.FinalWidthMmApplied = TryGetTypeLengthMm(
                finalSymbol,
                new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" },
                out actualWidthMm)
                ? actualWidthMm
                : fallbackWidthMm;

            double actualHeightMm;
            candidate.FinalHeightMmApplied = TryGetTypeLengthMm(
                finalSymbol,
                new[] { "Height", "Rough Height", "Door Height", "高度" },
                out actualHeightMm)
                ? actualHeightMm
                : fallbackHeightMm;
        }

        private static void EnsureSymbolActivated(Document doc, FamilySymbol symbol)
        {
            if (doc == null || symbol == null)
            {
                return;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
                DiagnosticRecorder.AppendDebug("[DoorRegen] AfterFinalSymbolActivated=True");
            }
        }

        private static bool TryGetTypeLengthMm(FamilySymbol symbol, IEnumerable<string> names, out double valueMm)
        {
            valueMm = 0.0;
            if (symbol == null || names == null)
            {
                return false;
            }

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Parameter p = symbol.LookupParameter(name);
                if (p == null || p.StorageType != StorageType.Double)
                {
                    continue;
                }

                valueMm = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
                return true;
            }

            return false;
        }

        private static void LogFinalDoorState(FamilyInstance door, Wall hostWall, FamilySymbol finalSymbol, DoorCandidate candidate)
        {
            if (door == null)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[DoorFinalState] DoorId=" + door.Id.IntegerValue +
                ", CandidateId=" + (candidate == null ? 0 : candidate.CandidateId) +
                ", HostWallId=" + (hostWall == null ? 0 : hostWall.Id.IntegerValue) +
                ", HostIsWall=" + (door.Host is Wall) +
                ", FinalSymbolName=" + (finalSymbol == null ? string.Empty : finalSymbol.Name) +
                ", FinalWidthMm=" + (candidate == null ? 0.0 : candidate.FinalWidthMmApplied).ToString("F1") +
                ", FinalHeightMm=" + (candidate == null ? 0.0 : candidate.FinalHeightMmApplied).ToString("F1"));
        }

        private static FamilySymbol EnsureDoorTypeWithHeight(Document doc, FamilySymbol source, double targetHeightMm)
        {
            if (doc == null || source == null || targetHeightMm <= 0)
            {
                return source;
            }

            if (HasHeightValue(source, targetHeightMm))
            {
                return source;
            }

            string suffix = "_H" + ((int)Math.Round(targetHeightMm)).ToString();
            string typeName = source.Name + suffix;
            FamilySymbol existing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x != null &&
                                     x.FamilyName == source.FamilyName &&
                                     string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            try
            {
                FamilySymbol dup = source.Duplicate(typeName) as FamilySymbol;
                if (dup == null)
                {
                    return source;
                }

                bool setOk = RevitParameterSetters.TrySetTypeByNames(dup, targetHeightMm, "Height", "Rough Height", "Door Height", "\u9AD8\u5EA6");
                return setOk ? dup : source;
            }
            catch
            {
                return source;
            }
        }

        private static bool HasHeightValue(FamilySymbol symbol, double targetHeightMm)
        {
            if (symbol == null || targetHeightMm <= 0)
            {
                return false;
            }

            Parameter p = symbol.LookupParameter("Height") ?? symbol.LookupParameter("Rough Height") ?? symbol.LookupParameter("Door Height") ?? symbol.LookupParameter("\u9AD8\u5EA6");
            if (p == null || p.StorageType != StorageType.Double)
            {
                return false;
            }

            double mm = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            return Math.Abs(mm - targetHeightMm) <= 1.0;
        }

        private static FamilySymbol ResolveDoorSymbolForWidth(
            Document doc,
            FamilySymbol baseSymbol,
            double targetWidthMm,
            Dictionary<string, FamilySymbol> cache)
        {
            if (doc == null || baseSymbol == null || targetWidthMm <= 1e-6)
            {
                return baseSymbol;
            }

            int rounded = (int)Math.Round(targetWidthMm);
            string cacheKey = (baseSymbol.FamilyName ?? string.Empty) + "|" + rounded;
            if (cache != null && cache.TryGetValue(cacheKey, out FamilySymbol cached) && cached != null)
            {
                return cached;
            }

            if (HasWidthValue(baseSymbol, targetWidthMm))
            {
                if (cache != null) cache[cacheKey] = baseSymbol;
                return baseSymbol;
            }

            string typeName = baseSymbol.Name + "_W" + rounded;
            FamilySymbol existing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x != null &&
                                     x.FamilyName == baseSymbol.FamilyName &&
                                     string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!HasWidthValue(existing, targetWidthMm))
                {
                    RevitParameterSetters.TrySetTypeByNames(existing, targetWidthMm, "Width", "Rough Width", "Door Width", "宽度", "寬度");
                }

                SyncDoorTypeParameters(baseSymbol, existing);
                if (cache != null) cache[cacheKey] = existing;
                return existing;
            }

            try
            {
                FamilySymbol dup = baseSymbol.Duplicate(typeName) as FamilySymbol;
                if (dup == null)
                {
                    return baseSymbol;
                }

                RevitParameterSetters.TrySetTypeByNames(dup, targetWidthMm, "Width", "Rough Width", "Door Width", "宽度", "寬度");
                SyncDoorTypeParameters(baseSymbol, dup);
                FamilySymbol result = dup;
                if (cache != null) cache[cacheKey] = result;
                return result;
            }
            catch
            {
                return baseSymbol;
            }
        }

        private static bool HasWidthValue(FamilySymbol symbol, double targetWidthMm)
        {
            if (symbol == null || targetWidthMm <= 0)
            {
                return false;
            }

            Parameter p = symbol.LookupParameter("Width")
                          ?? symbol.LookupParameter("Rough Width")
                          ?? symbol.LookupParameter("Door Width")
                          ?? symbol.LookupParameter("宽度")
                          ?? symbol.LookupParameter("寬度");
            if (p == null || p.StorageType != StorageType.Double)
            {
                return false;
            }

            double mm = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            return Math.Abs(mm - targetWidthMm) <= 1.0;
        }

        private static void SyncDoorTypeParameters(FamilySymbol source, FamilySymbol target)
        {
            if (source == null || target == null || source.Id.IntegerValue == target.Id.IntegerValue)
            {
                return;
            }

            try
            {
                int applied = 0;
                foreach (Parameter srcParam in source.Parameters)
                {
                    if (!CanCopyTypeParameter(srcParam))
                    {
                        continue;
                    }

                    string name = srcParam.Definition == null ? null : srcParam.Definition.Name;
                    if (string.IsNullOrWhiteSpace(name) || IsWidthOrHeightName(name))
                    {
                        continue;
                    }

                    Parameter dstParam = target.LookupParameter(name);
                    if (!CanCopyTypeParameter(dstParam) || dstParam.StorageType != srcParam.StorageType)
                    {
                        continue;
                    }

                    if (TryCopyParameterValue(srcParam, dstParam))
                    {
                        applied++;
                    }
                }

                // Log synced parameter count to diagnose style/material consistency on generated door types.
                DiagnosticRecorder.AppendDebug(
                    "[DoorTypeSync] BaseType=" + source.Name +
                    ", TargetType=" + target.Name +
                    ", CopiedParams=" + applied);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DoorTypeSync] Failed=" + ex.Message);
            }
        }

        private static bool CanCopyTypeParameter(Parameter p)
        {
            return p != null &&
                   !p.IsReadOnly &&
                   p.Definition != null &&
                   p.StorageType != StorageType.None;
        }

        private static bool IsWidthOrHeightName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            string n = name.Trim();
            return n.Equals("Width", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("Door Width", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("Rough Width", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("宽度", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("寬度", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("Height", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("Door Height", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("Rough Height", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("\u9AD8\u5EA6", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("高度", StringComparison.OrdinalIgnoreCase) ||
                   n.Equals("高度", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryCopyParameterValue(Parameter src, Parameter dst)
        {
            try
            {
                switch (src.StorageType)
                {
                    case StorageType.Double:
                        dst.Set(src.AsDouble());
                        return true;
                    case StorageType.Integer:
                        dst.Set(src.AsInteger());
                        return true;
                    case StorageType.String:
                        dst.Set(src.AsString() ?? string.Empty);
                        return true;
                    case StorageType.ElementId:
                        dst.Set(src.AsElementId());
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TryAlignFacing(Wall wall, FamilyInstance door)
        {
            LocationCurve loc = wall.Location as LocationCurve;
            Line line = loc?.Curve as Line;
            if (line == null || door == null)
            {
                return;
            }

            XYZ wallDir = line.Direction.Normalize();
            XYZ doorFacing = door.FacingOrientation.Normalize();
            if (wallDir.DotProduct(doorFacing) < 0)
            {
                door.flipFacing();
            }
        }

        private static void AddReason(DoorCreateResult result, string reason)
        {
            if (result.SkipReasons.Count < 8)
            {
                result.SkipReasons.Add(reason);
            }
        }

        private static DoorWidthResolveOptions BuildWidthOptions(AdvancedSettingsRow settings)
        {
            DoorWidthResolveOptions options = new DoorWidthResolveOptions();
            if (settings == null)
            {
                return options;
            }

            options.UseFixedDoorWidth = settings.UseFixedDoorWidth.HasValue && settings.UseFixedDoorWidth.Value;
            options.FixedDoorWidthMm = settings.DoorExpectedWidthMm;
            options.PreferGeometryOpeningWidth = !settings.PreferGeometryOpeningWidth.HasValue || settings.PreferGeometryOpeningWidth.Value;
            if (settings.MinDoorWidthMm.HasValue && settings.MinDoorWidthMm.Value > 0)
            {
                options.MinDoorWidthMm = settings.MinDoorWidthMm.Value;
            }

            if (settings.MaxDoorWidthMm.HasValue && settings.MaxDoorWidthMm.Value > 0)
            {
                options.MaxDoorWidthMm = settings.MaxDoorWidthMm.Value;
            }

            return options;
        }
    }
}
