using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomPointProbeService
    {
        private const string ProbePreviewApplicationId = "CadToRevit.ProbeRoomPreview";
        private static readonly Dictionary<string, List<int>> PreviousPreviewIdsByDocument = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        internal static RoomPointProbeResult Probe(Document doc, XYZ pickPoint, View activeView)
        {
            ClearPreviousProbePreview(doc);

            RoomPointProbeResult invalid = BuildFailedResult("NoRecognizableClosedSpace", "No pick point was provided.");
            invalid.PickPoint = pickPoint;

            if (doc == null || pickPoint == null)
            {
                return invalid;
            }

            Level analysisLevel = ResolveAnalysisLevel(doc, activeView, pickPoint);
            if (analysisLevel == null)
            {
                RoomPointProbeResult failedLevel = BuildFailedResult("NoRecognizableClosedSpace", "No valid analysis level was found for the selected point.");
                failedLevel.PickPoint = pickPoint;
                return failedLevel;
            }

            Room nativeRoom = TryFindNativeRoom(doc, analysisLevel, pickPoint);
            if (nativeRoom != null)
            {
                return BuildResultFromNativeRoom(doc, nativeRoom, analysisLevel, pickPoint, activeView);
            }

            ModelRoomSeedRecognitionResult modelResult = TryRecognizeClosedModelRoom(doc, analysisLevel, pickPoint);
            if (modelResult != null && modelResult.Success && modelResult.Record != null)
            {
                return BuildResultFromRecord(doc, modelResult.Record, analysisLevel, pickPoint, activeView);
            }

            RoomPointProbeResult failed = BuildFailedResult("NoRecognizableClosedSpace", "The selected point is not inside any recognizable closed room space.");
            failed.PickPoint = pickPoint;
            failed.LevelId = analysisLevel.Id;
            failed.LevelName = analysisLevel.Name ?? string.Empty;
            if (modelResult != null && !string.IsNullOrWhiteSpace(modelResult.FailureReason))
            {
                failed.Warnings.Add(modelResult.FailureReason);
            }

            return failed;
        }

        // Resolve the 2D analysis level conservatively from the active view or picked Z value.
        private static Level ResolveAnalysisLevel(Document doc, View activeView, XYZ pickPoint)
        {
            if (doc == null)
            {
                return null;
            }

            if (activeView != null && !(activeView is View3D) && activeView.GenLevel != null)
            {
                Level viewLevel = doc.GetElement(activeView.GenLevel.Id) as Level;
                if (viewLevel != null)
                {
                    return viewLevel;
                }
            }

            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(x => x != null)
                .OrderBy(x => x.Elevation)
                .ToList();
            if (levels.Count == 0 || pickPoint == null)
            {
                return null;
            }

            return levels
                .OrderBy(x => Math.Abs(x.Elevation - pickPoint.Z))
                .FirstOrDefault();
        }

        private static Room TryFindNativeRoom(Document doc, Level level, XYZ pickPoint)
        {
            if (doc == null || level == null || pickPoint == null)
            {
                return null;
            }

            List<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(x => x != null &&
                            x.LevelId == level.Id &&
                            x.Area > 0.0 &&
                            x.Location != null)
                .ToList();

            return rooms.FirstOrDefault(x => x.IsPointInRoom(pickPoint));
        }

        private static ModelRoomSeedRecognitionResult TryRecognizeClosedModelRoom(Document doc, Level level, XYZ pickPoint)
        {
            TargetRoomSeed seed = new TargetRoomSeed
            {
                Key = "probe_" + Guid.NewGuid().ToString("N"),
                RoomName = "Point Probe",
                TargetRoomType = string.Empty,
                Position = pickPoint,
                LevelId = level != null ? level.Id : ElementId.InvalidElementId,
                SourceLayer = "PointProbe",
                RawText = "PointProbe"
            };

            DiagnosticRecorder.AppendDebug(
                "[ProbeRoom] Using dedicated point-probe recognition flow. WindowMm=" +
                ModelRoomSeedRecognitionService.ResolveProbeRecognitionWindowSizeMm().ToString("F0") +
                ", ReuseProjectWindowSettings=False");
            return ModelRoomSeedRecognitionService.RecognizeProbeSeed(doc, seed);
        }

        private static RoomPointProbeResult BuildResultFromNativeRoom(Document doc, Room room, Level level, XYZ pickPoint, View activeView)
        {
            string roomName = room != null ? (room.Name ?? string.Empty) : string.Empty;
            string roomNumber = room != null ? (room.Number ?? string.Empty) : string.Empty;
            List<XYZ> loopPoints = ExtractNativeRoomLoopPoints(room, pickPoint);
            string stableRoomKey = BuildStableRoomKey(level != null ? level.Id : ElementId.InvalidElementId, loopPoints, true, room != null ? room.Id : ElementId.InvalidElementId);
            List<ElementId> previewIds = CreateProbePreviewElements(doc, activeView, loopPoints);
            RoomSemanticRecord record = new RoomSemanticRecord
            {
                Key = stableRoomKey,
                RoomName = roomName,
                RoomNumber = roomNumber,
                TargetRoomType = "NativeRoom",
                Status = "Matched-NativeRoom",
                AreaM2 = room != null ? UnitUtils.ConvertFromInternalUnits(room.Area, UnitTypeId.SquareMeters) : 0.0,
                CloseGapMm = 0.0,
                BoundaryLayers = "NativeRoom",
                LoopPoints = loopPoints
            };

            return new RoomPointProbeResult
            {
                Success = true,
                HitNativeRoom = true,
                Status = "HitNativeRoom",
                Message = "The selected point is inside a native Revit Room.",
                RoomName = roomName,
                RoomNumber = roomNumber,
                LevelName = level != null ? (level.Name ?? string.Empty) : string.Empty,
                AreaM2 = record.AreaM2,
                PickPoint = pickPoint,
                LevelId = level != null ? level.Id : ElementId.InvalidElementId,
                StableRoomKey = stableRoomKey,
                LoopPoints = loopPoints,
                BoundaryElementIds = previewIds,
                SemanticRecord = record
            };
        }

        private static RoomPointProbeResult BuildResultFromRecord(Document doc, RoomSemanticRecord record, Level level, XYZ pickPoint, View activeView)
        {
            List<XYZ> loopPoints = NormalizeLoopPoints(record != null ? record.LoopPoints : null);
            string stableRoomKey = BuildStableRoomKey(level != null ? level.Id : ElementId.InvalidElementId, loopPoints, false, ElementId.InvalidElementId);
            List<ElementId> previewIds = CreateProbePreviewElements(doc, activeView, loopPoints);

            if (record != null)
            {
                record.Key = stableRoomKey;
                record.LoopPoints = loopPoints;
            }

            return new RoomPointProbeResult
            {
                Success = true,
                HitNativeRoom = false,
                Status = "HitModelClosedSpace",
                Message = "The selected point is inside a model-recognized closed space.",
                RoomName = record != null ? (record.RoomName ?? string.Empty) : string.Empty,
                RoomNumber = record != null ? (record.RoomNumber ?? string.Empty) : string.Empty,
                LevelName = level != null ? (level.Name ?? string.Empty) : string.Empty,
                AreaM2 = record != null ? record.AreaM2 : 0.0,
                PickPoint = pickPoint,
                LevelId = level != null ? level.Id : ElementId.InvalidElementId,
                StableRoomKey = stableRoomKey,
                LoopPoints = loopPoints,
                BoundaryElementIds = previewIds,
                SemanticRecord = record
            };
        }

        internal static void ClearProbePreview(Document doc)
        {
            ClearPreviousProbePreview(doc);
        }

        internal static List<ElementId> RecreatePreviewFromLoopPoints(Document doc, View activeView, List<XYZ> loopPoints)
        {
            ClearPreviousProbePreview(doc);
            return CreateProbePreviewElements(doc, activeView, loopPoints);
        }

        internal static string BuildStableRoomKey(ElementId levelId, List<XYZ> loopPoints, bool hitNativeRoom, ElementId nativeRoomId)
        {
            if (hitNativeRoom && nativeRoomId != null && nativeRoomId != ElementId.InvalidElementId)
            {
                return "native_room_" + nativeRoomId.IntegerValue;
            }

            List<XYZ> normalized = NormalizeLoopPoints(loopPoints);
            string levelPart = levelId != null && levelId != ElementId.InvalidElementId
                ? levelId.IntegerValue.ToString()
                : "0";
            if (normalized.Count == 0)
            {
                return "model_loop_" + levelPart + "_empty";
            }

            string canonicalLoop = BuildCanonicalLoopSignature(normalized);
            string payload = levelPart + "|" + canonicalLoop;
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                byte[] hash = sha1.ComputeHash(bytes);
                string hashText = BitConverter.ToString(hash).Replace("-", string.Empty).Substring(0, 16).ToLowerInvariant();
                return "model_loop_" + levelPart + "_" + hashText;
            }
        }

        private static List<XYZ> ExtractNativeRoomLoopPoints(Room room, XYZ pickPoint)
        {
            List<XYZ> bestLoop = new List<XYZ>();
            if (room == null)
            {
                return bestLoop;
            }

            SpatialElementBoundaryOptions options = new SpatialElementBoundaryOptions();
            IList<IList<BoundarySegment>> loops = room.GetBoundarySegments(options);
            foreach (IList<BoundarySegment> loop in loops ?? new List<IList<BoundarySegment>>())
            {
                List<XYZ> points = new List<XYZ>();
                foreach (BoundarySegment segment in loop ?? new List<BoundarySegment>())
                {
                    Curve curve = segment != null ? segment.GetCurve() : null;
                    if (curve == null)
                    {
                        continue;
                    }

                    XYZ start = curve.GetEndPoint(0);
                    if (start != null)
                    {
                        points.Add(start);
                    }
                }

                points = NormalizeLoopPoints(points);
                if (points.Count < 3)
                {
                    continue;
                }

                if (pickPoint != null && PointInPolygon.ContainsPointXY(points, pickPoint))
                {
                    return points;
                }

                if (bestLoop.Count == 0)
                {
                    bestLoop = points;
                }
            }

            return bestLoop;
        }

        private static void ClearPreviousProbePreview(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            string key = GetDocumentKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (!PreviousPreviewIdsByDocument.TryGetValue(key, out List<int> storedIds) || storedIds == null || storedIds.Count == 0)
            {
                return;
            }

            List<ElementId> ids = storedIds
                .Distinct()
                .Select(x => new ElementId(x))
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .ToList();

            ExecuteDocumentMutation(doc, "Clear Probe Room Preview", () =>
            {
                foreach (ElementId id in ids)
                {
                    if (doc.GetElement(id) != null)
                    {
                        try
                        {
                            doc.Delete(id);
                        }
                        catch
                        {
                        }
                    }
                }
            });

            PreviousPreviewIdsByDocument.Remove(key);
        }

        private static List<ElementId> CreateProbePreviewElements(Document doc, View activeView, List<XYZ> loopPoints)
        {
            List<ElementId> ids = new List<ElementId>();
            if (doc == null)
            {
                return ids;
            }

            List<XYZ> normalized = NormalizeLoopPoints(loopPoints);
            if (normalized.Count < 3)
            {
                return ids;
            }

            string key = GetDocumentKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return ids;
            }

            ExecuteDocumentMutation(doc, "Create Probe Room Preview", () =>
            {
                DirectShape shape = BuildProbePreviewShape(doc, normalized);
                if (shape != null)
                {
                    ids.Add(shape.Id);
                }

                if (ids.Count > 0)
                {
                    ApplyProbePreviewOverrides(doc, activeView, ids);
                }
            });

            if (ids.Count > 0)
            {
                PreviousPreviewIdsByDocument[key] = ids.Select(x => x.IntegerValue).ToList();
            }

            return ids;
        }

        private static DirectShape BuildProbePreviewShape(Document doc, List<XYZ> loopPoints)
        {
            try
            {
                CurveLoop loop = new CurveLoop();
                for (int i = 0; i < loopPoints.Count; i++)
                {
                    XYZ start = loopPoints[i];
                    XYZ end = loopPoints[(i + 1) % loopPoints.Count];
                    if (start == null || end == null || start.DistanceTo(end) <= 1e-6)
                    {
                        continue;
                    }

                    loop.Append(Line.CreateBound(start, end));
                }

                if (loop.Count() < 3)
                {
                    return null;
                }

                double thicknessFt = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                ElementId materialId = GetOrCreateProbePreviewMaterialId(doc);
                SolidOptions solidOptions = new SolidOptions(
                    materialId != ElementId.InvalidElementId ? materialId : ElementId.InvalidElementId,
                    ElementId.InvalidElementId);
                Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop },
                    XYZ.BasisZ,
                    thicknessFt,
                    solidOptions);
                if (solid == null)
                {
                    return null;
                }

                DirectShape shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                shape.ApplicationId = ProbePreviewApplicationId;
                shape.ApplicationDataId = Guid.NewGuid().ToString("N");
                shape.SetShape(new List<GeometryObject> { solid });
                return shape;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyProbePreviewOverrides(Document doc, View activeView, List<ElementId> ids)
        {
            if (doc == null || activeView == null || ids == null || ids.Count == 0)
            {
                return;
            }

            ElementId solidFillId = GetSolidFillPatternId(doc);
            Color fillColor = new Color(255, 165, 0);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetProjectionLineColor(fillColor);
            ogs.SetProjectionLineWeight(6);

            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceForegroundPatternColor(fillColor);
                ogs.SetSurfaceBackgroundPatternVisible(true);
                ogs.SetSurfaceBackgroundPatternId(solidFillId);
                ogs.SetSurfaceBackgroundPatternColor(fillColor);
            }

            ogs.SetSurfaceTransparency(0);

            foreach (ElementId id in ids)
            {
                if (id != null && id != ElementId.InvalidElementId && doc.GetElement(id) != null)
                {
                    activeView.SetElementOverrides(id, ogs);
                }
            }
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);

            return solidFill != null ? solidFill.Id : ElementId.InvalidElementId;
        }

        private static ElementId GetOrCreateProbePreviewMaterialId(Document doc)
        {
            if (doc == null)
            {
                return ElementId.InvalidElementId;
            }

            const string materialName = "CadToRevit_ProbeRoomPreview_Orange";
            Material material = new FilteredElementCollector(doc)
                .OfClass(typeof(Material))
                .Cast<Material>()
                .FirstOrDefault(x => string.Equals(x.Name, materialName, StringComparison.OrdinalIgnoreCase));

            if (material == null)
            {
                ElementId materialId = Material.Create(doc, materialName);
                material = doc.GetElement(materialId) as Material;
            }

            if (material == null)
            {
                return ElementId.InvalidElementId;
            }

            material.Color = new Color(255, 165, 0);
            material.Transparency = 0;
            return material.Id;
        }

        private static string GetDocumentKey(Document doc)
        {
            if (doc == null)
            {
                return string.Empty;
            }

            string path = doc.PathName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string title = doc.Title ?? string.Empty;
            return string.IsNullOrWhiteSpace(title) ? "InMemoryDocument" : title;
        }

        private static void ExecuteDocumentMutation(Document doc, string transactionName, Action action)
        {
            if (doc == null || action == null)
            {
                return;
            }

            if (doc.IsModifiable)
            {
                action();
                return;
            }

            using (Transaction tx = new Transaction(doc, transactionName))
            {
                tx.Start();
                action();
                tx.Commit();
            }
        }

        private static List<XYZ> NormalizeLoopPoints(List<XYZ> loopPoints)
        {
            List<XYZ> normalized = new List<XYZ>();
            foreach (XYZ point in loopPoints ?? new List<XYZ>())
            {
                if (point != null)
                {
                    normalized.Add(point);
                }
            }

            if (normalized.Count > 1 && normalized[0].DistanceTo(normalized[normalized.Count - 1]) <= 1e-6)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            return normalized;
        }

        // Canonicalize loop ordering so the same room shape resolves to a stable key across repeated probes.
        private static string BuildCanonicalLoopSignature(List<XYZ> loopPoints)
        {
            List<string> forward = loopPoints
                .Select(ToRoundedLoopPointToken)
                .ToList();
            List<string> reversed = new List<string>(forward);
            reversed.Reverse();
            return string.CompareOrdinal(BuildBestRotationSignature(forward), BuildBestRotationSignature(reversed)) <= 0
                ? BuildBestRotationSignature(forward)
                : BuildBestRotationSignature(reversed);
        }

        private static string BuildBestRotationSignature(List<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
            {
                return string.Empty;
            }

            string best = null;
            for (int i = 0; i < tokens.Count; i++)
            {
                IEnumerable<string> ordered = tokens.Skip(i).Concat(tokens.Take(i));
                string candidate = string.Join(";", ordered);
                if (best == null || string.CompareOrdinal(candidate, best) < 0)
                {
                    best = candidate;
                }
            }

            return best ?? string.Empty;
        }

        private static string ToRoundedLoopPointToken(XYZ point)
        {
            if (point == null)
            {
                return "null";
            }

            double xMm = UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters);
            double yMm = UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters);
            return Math.Round(xMm).ToString("F0") + "," + Math.Round(yMm).ToString("F0");
        }

        private static Outline BuildLoopOutline(List<XYZ> loopPoints, double paddingMm)
        {
            if (loopPoints == null || loopPoints.Count == 0)
            {
                return null;
            }

            double paddingFt = UnitUtils.ConvertToInternalUnits(Math.Max(0.0, paddingMm), UnitTypeId.Millimeters);
            double minX = loopPoints.Min(x => x.X) - paddingFt;
            double minY = loopPoints.Min(x => x.Y) - paddingFt;
            double minZ = loopPoints.Min(x => x.Z) - paddingFt;
            double maxX = loopPoints.Max(x => x.X) + paddingFt;
            double maxY = loopPoints.Max(x => x.Y) + paddingFt;
            double maxZ = loopPoints.Max(x => x.Z) + paddingFt;
            return new Outline(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
        }

        private static bool IsWallOnLevel(Wall wall, ElementId levelId)
        {
            if (wall == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return true;
            }

            Parameter parameter = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            ElementId wallLevelId = parameter != null ? parameter.AsElementId() : ElementId.InvalidElementId;
            return wallLevelId != null && wallLevelId.IntegerValue == levelId.IntegerValue;
        }

        private static bool DoesWallMatchLoop(Wall wall, List<XYZ> loopPoints, double toleranceFt)
        {
            if (wall == null || loopPoints == null || loopPoints.Count < 3)
            {
                return false;
            }

            LocationCurve locationCurve = wall.Location as LocationCurve;
            Line hostLine = locationCurve != null ? locationCurve.Curve as Line : null;
            if (hostLine == null)
            {
                BoundingBoxXYZ box = wall.get_BoundingBox(null);
                return box != null && DoesBoundingBoxTouchLoop(box, loopPoints, toleranceFt);
            }

            XYZ p0 = hostLine.GetEndPoint(0);
            XYZ p1 = hostLine.GetEndPoint(1);
            XYZ dir = (p1 - p0).Normalize();
            XYZ normal = new XYZ(-dir.Y, dir.X, 0.0);
            double halfWidth = Math.Max(wall.Width * 0.5, toleranceFt * 0.5);

            XYZ a = p0 + normal.Multiply(halfWidth);
            XYZ b = p1 + normal.Multiply(halfWidth);
            XYZ c = p1 - normal.Multiply(halfWidth);
            XYZ d = p0 - normal.Multiply(halfWidth);

            return DoesBoundaryLineMatchLoop(a, b, loopPoints, toleranceFt) ||
                   DoesBoundaryLineMatchLoop(b, c, loopPoints, toleranceFt) ||
                   DoesBoundaryLineMatchLoop(c, d, loopPoints, toleranceFt) ||
                   DoesBoundaryLineMatchLoop(d, a, loopPoints, toleranceFt);
        }

        private static bool DoesBoundingBoxTouchLoop(BoundingBoxXYZ box, List<XYZ> loopPoints, double toleranceFt)
        {
            if (box == null || box.Min == null || box.Max == null)
            {
                return false;
            }

            XYZ center = new XYZ(
                (box.Min.X + box.Max.X) * 0.5,
                (box.Min.Y + box.Max.Y) * 0.5,
                (box.Min.Z + box.Max.Z) * 0.5);
            return IsPointNearLoopBoundary(center, loopPoints, toleranceFt);
        }

        private static bool DoesBoundaryLineMatchLoop(XYZ start, XYZ end, List<XYZ> loopPoints, double toleranceFt)
        {
            if (start == null || end == null || loopPoints == null || loopPoints.Count < 3)
            {
                return false;
            }

            XYZ midpoint = new XYZ((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5, (start.Z + end.Z) * 0.5);
            if (IsPointNearLoopBoundary(start, loopPoints, toleranceFt) ||
                IsPointNearLoopBoundary(end, loopPoints, toleranceFt) ||
                IsPointNearLoopBoundary(midpoint, loopPoints, toleranceFt))
            {
                return true;
            }

            for (int i = 0; i < loopPoints.Count; i++)
            {
                XYZ edgeStart = loopPoints[i];
                XYZ edgeEnd = loopPoints[(i + 1) % loopPoints.Count];
                if (TrySegmentsIntersectXY(start, end, edgeStart, edgeEnd))
                {
                    return true;
                }

                if (ComputeSegmentDistanceXY(start, end, edgeStart, edgeEnd) <= toleranceFt)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointNearLoopBoundary(XYZ point, List<XYZ> loopPoints, double toleranceFt)
        {
            if (point == null || loopPoints == null || loopPoints.Count < 2)
            {
                return false;
            }

            for (int i = 0; i < loopPoints.Count; i++)
            {
                XYZ start = loopPoints[i];
                XYZ end = loopPoints[(i + 1) % loopPoints.Count];
                if (ComputePointToSegmentDistanceXY(point, start, end) <= toleranceFt)
                {
                    return true;
                }
            }

            return false;
        }

        private static double ComputeSegmentDistanceXY(XYZ a0, XYZ a1, XYZ b0, XYZ b1)
        {
            if (TrySegmentsIntersectXY(a0, a1, b0, b1))
            {
                return 0.0;
            }

            return new[]
            {
                ComputePointToSegmentDistanceXY(a0, b0, b1),
                ComputePointToSegmentDistanceXY(a1, b0, b1),
                ComputePointToSegmentDistanceXY(b0, a0, a1),
                ComputePointToSegmentDistanceXY(b1, a0, a1)
            }.Min();
        }

        private static double ComputePointToSegmentDistanceXY(XYZ point, XYZ start, XYZ end)
        {
            if (point == null || start == null || end == null)
            {
                return double.MaxValue;
            }

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared <= 1e-12)
            {
                return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
            }

            double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double projX = start.X + (dx * t);
            double projY = start.Y + (dy * t);
            double deltaX = point.X - projX;
            double deltaY = point.Y - projY;
            return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        private static bool TrySegmentsIntersectXY(XYZ a0, XYZ a1, XYZ b0, XYZ b1)
        {
            if (a0 == null || a1 == null || b0 == null || b1 == null)
            {
                return false;
            }

            double o1 = ComputeOrientation(a0, a1, b0);
            double o2 = ComputeOrientation(a0, a1, b1);
            double o3 = ComputeOrientation(b0, b1, a0);
            double o4 = ComputeOrientation(b0, b1, a1);
            double epsilon = 1e-9;

            if ((o1 > epsilon && o2 < -epsilon || o1 < -epsilon && o2 > epsilon) &&
                (o3 > epsilon && o4 < -epsilon || o3 < -epsilon && o4 > epsilon))
            {
                return true;
            }

            return IsPointOnSegmentXY(b0, a0, a1, epsilon) ||
                   IsPointOnSegmentXY(b1, a0, a1, epsilon) ||
                   IsPointOnSegmentXY(a0, b0, b1, epsilon) ||
                   IsPointOnSegmentXY(a1, b0, b1, epsilon);
        }

        private static double ComputeOrientation(XYZ a, XYZ b, XYZ c)
        {
            return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        }

        private static bool IsPointOnSegmentXY(XYZ point, XYZ start, XYZ end, double epsilon)
        {
            if (Math.Abs(ComputeOrientation(start, end, point)) > epsilon)
            {
                return false;
            }

            return point.X <= Math.Max(start.X, end.X) + epsilon &&
                   point.X >= Math.Min(start.X, end.X) - epsilon &&
                   point.Y <= Math.Max(start.Y, end.Y) + epsilon &&
                   point.Y >= Math.Min(start.Y, end.Y) - epsilon;
        }

        private static RoomPointProbeResult BuildFailedResult(string status, string message)
        {
            return new RoomPointProbeResult
            {
                Success = false,
                Status = status,
                Message = message ?? string.Empty
            };
        }
    }
}
