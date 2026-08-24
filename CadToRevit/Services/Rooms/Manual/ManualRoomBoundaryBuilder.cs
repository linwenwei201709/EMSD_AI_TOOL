using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms.Manual
{
    public sealed class ManualRoomBoundaryBuildResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public ManualRoomRecord Record { get; set; }

        public bool UsedVirtualOpening { get; set; }

        public XYZ VirtualOpeningStart { get; set; }

        public XYZ VirtualOpeningEnd { get; set; }

        public double VirtualOpeningWidthMm { get; set; }

        public string Rule { get; set; }
    }

    public static class ManualRoomBoundaryBuilder
    {
        private const double SnapToleranceMm = 500.0;
        private const double MinAreaM2 = 1.0;
        private const double MinSegmentMm = 50.0;
        private const double LiftOpeningMinMm = 1500.0;
        private const double LiftOpeningMaxMm = 3000.0;
        private const double CollinearToleranceMm = 120.0;

        public static ManualRoomBoundaryBuildResult Build(Document doc, View activeView, IList<Wall> walls)
        {
            return Build(doc, activeView, (walls ?? new List<Wall>()).Cast<Element>().ToList());
        }

        public static ManualRoomBoundaryBuildResult Build(Document doc, View activeView, IList<Element> boundaryElements)
        {
            if (doc == null || boundaryElements == null || boundaryElements.Count == 0)
            {
                return Fail("Please select boundary walls before creating a manual room.");
            }

            List<Element> elements = boundaryElements.Where(IsSupportedBoundaryElement).ToList();
            if (elements.Count == 0)
            {
                return Fail("Please select boundary walls before creating a manual room.");
            }

            ElementId levelId = ResolveLevelId(activeView, elements);
            double baseZ = ResolveBaseZ(doc, levelId, elements);
            List<BoundarySegment> sourceSegments = BuildSourceSegments(elements, activeView, baseZ);
            if (sourceSegments.Count < 3)
            {
                return Fail("The selected elements do not form a closed room boundary. Please select more boundary walls.");
            }

            double snapTolerance = UnitUtils.ConvertToInternalUnits(SnapToleranceMm, UnitTypeId.Millimeters);
            double minSegment = UnitUtils.ConvertToInternalUnits(MinSegmentMm, UnitTypeId.Millimeters);
            GraphBuildResult graph = BuildIntersectionGraph(sourceSegments, snapTolerance, minSegment);
            List<XYZ> loopPoints = FindBestClosedLoop(graph);
            if (loopPoints.Count < 3)
            {
                return Fail("The selected elements do not form a closed room boundary. Please select more boundary walls.");
            }

            double signedArea = ComputeSignedArea(loopPoints);
            double areaM2 = UnitUtils.ConvertFromInternalUnits(Math.Abs(signedArea), UnitTypeId.SquareMeters);
            if (areaM2 < MinAreaM2)
            {
                return Fail("The selected boundary is too small to create a room.");
            }

            if (signedArea < 0.0)
            {
                loopPoints.Reverse();
            }

            InteriorBoundaryResult interior = ResolveInteriorBoundary(doc, levelId, loopPoints, elements, baseZ);
            if (interior != null && interior.Success && interior.LoopPoints != null && interior.LoopPoints.Count >= 3)
            {
                loopPoints = interior.LoopPoints;
                signedArea = ComputeSignedArea(loopPoints);
                if (signedArea < 0.0)
                {
                    loopPoints.Reverse();
                }
            }

            XYZ centroid = ComputeCentroid(loopPoints);
            BoundingBoxXYZ bbox = BuildBoundingBox(loopPoints, elements, baseZ);
            double finalAreaM2 = interior != null && interior.Success && interior.AreaM2 > 0.0
                ? interior.AreaM2
                : UnitUtils.ConvertFromInternalUnits(Math.Abs(ComputeSignedArea(loopPoints)), UnitTypeId.SquareMeters);
            ManualRoomRecord record = new ManualRoomRecord
            {
                Key = string.Empty,
                SourceType = "Manual",
                LevelIdValue = levelId != null ? levelId.IntegerValue : -1,
                LevelName = ResolveLevelName(doc, levelId),
                BoundarySignature = BuildBoundarySignature(elements),
                AreaM2 = finalAreaM2,
                Centroid = interior != null && interior.Success && interior.Centroid != null ? interior.Centroid : centroid,
                BBox = interior != null && interior.Success && interior.BBox != null ? ExtendBoundingBoxZ(interior.BBox, bbox) : bbox,
                LoopPoints = loopPoints,
                BoundaryWalls = BuildBoundaryWallReferences(elements.OfType<Wall>().ToList())
            };

            return new ManualRoomBoundaryBuildResult
            {
                Success = true,
                Record = record
            };
        }


        /// <summary>
        /// Builds a manual lift boundary from an otherwise open wall chain.
        /// The selected boundary must become a simple four-sided shaft after
        /// adding exactly one virtual opening between 1500 mm and 3000 mm.
        /// This rule is intended for manual lift creation only.
        /// </summary>
        public static ManualRoomBoundaryBuildResult BuildOpenGapLiftBoundary(
            Document doc,
            View activeView,
            IList<Element> boundaryElements)
        {
            if (doc == null || boundaryElements == null || boundaryElements.Count == 0)
            {
                return FailOpenGap("Please select the wall / column elements that form the lift shaft.");
            }

            List<Element> elements = boundaryElements.Where(IsSupportedBoundaryElement).ToList();
            if (elements.Count == 0)
            {
                return FailOpenGap("Please select the wall / column elements that form the lift shaft.");
            }

            ElementId levelId = ResolveLevelId(activeView, elements);
            double baseZ = ResolveBaseZ(doc, levelId, elements);
            List<BoundarySegment> sourceSegments = BuildSourceSegments(elements, activeView, baseZ);
            if (sourceSegments.Count < 3)
            {
                return FailOpenGap(
                    "The selected lift boundary must form a four-sided shaft with one opening between 1500 mm and 3000 mm.");
            }

            double snapTolerance = UnitUtils.ConvertToInternalUnits(SnapToleranceMm, UnitTypeId.Millimeters);
            double minSegment = UnitUtils.ConvertToInternalUnits(MinSegmentMm, UnitTypeId.Millimeters);
            double minOpening = UnitUtils.ConvertToInternalUnits(LiftOpeningMinMm, UnitTypeId.Millimeters);
            double maxOpening = UnitUtils.ConvertToInternalUnits(LiftOpeningMaxMm, UnitTypeId.Millimeters);
            double collinearTolerance = UnitUtils.ConvertToInternalUnits(CollinearToleranceMm, UnitTypeId.Millimeters);

            GraphBuildResult graph = BuildIntersectionGraph(sourceSegments, snapTolerance, minSegment);
            Dictionary<int, List<int>> adjacency = BuildAdjacency(graph.Edges);
            List<int> openEndpoints = Enumerable.Range(0, graph.Nodes.Count)
                .Where(index => GetNodeDegree(adjacency, index) == 1)
                .ToList();

            DiagnosticRecorder.AppendDebug(
                "[ManualLiftBoundary] Rule=OpenGapQuadrilateral, " +
                "SelectedElementCount=" + elements.Count.ToString(CultureInfo.InvariantCulture) +
                ", NodeCount=" + graph.Nodes.Count.ToString(CultureInfo.InvariantCulture) +
                ", EdgeCount=" + graph.Edges.Count.ToString(CultureInfo.InvariantCulture) +
                ", OpenEndpointCount=" + openEndpoints.Count.ToString(CultureInfo.InvariantCulture));

            List<OpenGapCandidate> candidates = new List<OpenGapCandidate>();
            for (int aIndex = 0; aIndex < openEndpoints.Count; aIndex++)
            {
                for (int bIndex = aIndex + 1; bIndex < openEndpoints.Count; bIndex++)
                {
                    int a = openEndpoints[aIndex];
                    int b = openEndpoints[bIndex];
                    XYZ start = graph.Nodes[a].Point;
                    XYZ end = graph.Nodes[b].Point;
                    double gap = DistanceXY(start, end);
                    if (gap < minOpening - 1e-9 || gap > maxOpening + 1e-9)
                    {
                        continue;
                    }

                    if (HasEdge(graph.Edges, a, b) ||
                        !IsVirtualOpeningClear(graph, a, b, snapTolerance))
                    {
                        continue;
                    }

                    GraphBuildResult closedGraph = CloneGraphWithVirtualEdge(graph, a, b);
                    List<XYZ> loop = FindBestClosedLoop(closedGraph);
                    if (loop == null || loop.Count < 3 ||
                        !LoopUsesEdge(loop, start, end, snapTolerance))
                    {
                        continue;
                    }

                    List<XYZ> simplified = SimplifyCollinearLoop(loop, collinearTolerance);
                    if (simplified.Count != 4 || !IsSimplePolygon(simplified, snapTolerance))
                    {
                        continue;
                    }

                    double signedArea = ComputeSignedArea(simplified);
                    double areaM2 = UnitUtils.ConvertFromInternalUnits(
                        Math.Abs(signedArea),
                        UnitTypeId.SquareMeters);
                    if (areaM2 < MinAreaM2)
                    {
                        continue;
                    }

                    if (signedArea < 0.0)
                    {
                        simplified.Reverse();
                    }

                    candidates.Add(new OpenGapCandidate
                    {
                        Start = new XYZ(start.X, start.Y, baseZ),
                        End = new XYZ(end.X, end.Y, baseZ),
                        WidthMm = UnitUtils.ConvertFromInternalUnits(gap, UnitTypeId.Millimeters),
                        LoopPoints = simplified,
                        AreaM2 = areaM2
                    });
                }
            }

            if (candidates.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[ManualLiftBoundary] VirtualOpeningAccepted=False, " +
                    "Reason=No unique 1500-3000 mm opening produced a simple quadrilateral.");

                return FailOpenGap(
                    "The selected lift boundary must either form a closed loop containing a door, " +
                    "or form a four-sided lift shaft with one opening between 1500 mm and 3000 mm.");
            }

            // Only one opening is allowed. More than one valid candidate is
            // deliberately rejected rather than silently choosing a wrong lift entrance.
            if (candidates.Count > 1)
            {
                DiagnosticRecorder.AppendDebug(
                    "[ManualLiftBoundary] VirtualOpeningAccepted=False, " +
                    "Reason=Multiple openings detected, CandidateCount=" +
                    candidates.Count.ToString(CultureInfo.InvariantCulture));

                return FailOpenGap(
                    "More than one possible lift opening was detected. Please select only the walls that form one lift shaft.");
            }

            OpenGapCandidate selected = candidates[0];

            XYZ centroid = ComputeCentroid(selected.LoopPoints);
            BoundingBoxXYZ bbox = BuildBoundingBox(selected.LoopPoints, elements, baseZ);
            ManualRoomRecord record = new ManualRoomRecord
            {
                Key = string.Empty,
                SourceType = "Manual",
                LevelIdValue = levelId != null ? levelId.IntegerValue : -1,
                LevelName = ResolveLevelName(doc, levelId),
                BoundarySignature = BuildBoundarySignature(elements),
                AreaM2 = selected.AreaM2,
                Centroid = centroid,
                BBox = bbox,
                LoopPoints = selected.LoopPoints,
                BoundaryWalls = BuildBoundaryWallReferences(elements.OfType<Wall>().ToList())
            };

            DiagnosticRecorder.AppendDebug(
                "[ManualLiftBoundary] VirtualOpeningAccepted=True, " +
                "Rule=OpenGapQuadrilateral, GapWidthMm=" +
                Math.Round(selected.WidthMm, 1).ToString("F1", CultureInfo.InvariantCulture) +
                ", SimplifiedSideCount=4, AreaM2=" +
                Math.Round(selected.AreaM2, 2).ToString("F2", CultureInfo.InvariantCulture) +
                ", OpeningStart=" + FormatPoint(selected.Start) +
                ", OpeningEnd=" + FormatPoint(selected.End));

            return new ManualRoomBoundaryBuildResult
            {
                Success = true,
                Record = record,
                UsedVirtualOpening = true,
                VirtualOpeningStart = selected.Start,
                VirtualOpeningEnd = selected.End,
                VirtualOpeningWidthMm = selected.WidthMm,
                Rule = "OpenGapQuadrilateral"
            };
        }

        private static ManualRoomBoundaryBuildResult FailOpenGap(string message)
        {
            return new ManualRoomBoundaryBuildResult
            {
                Success = false,
                Message = message ?? string.Empty,
                Rule = "OpenGapQuadrilateral"
            };
        }

        private static ManualRoomBoundaryBuildResult Fail(string message)
        {
            return new ManualRoomBoundaryBuildResult
            {
                Success = false,
                Message = message ?? string.Empty
            };
        }

        public static bool IsSupportedBoundaryElement(Element element)
        {
            if (element is Wall)
            {
                return true;
            }

            Category category = element != null ? element.Category : null;
            if (category == null)
            {
                return false;
            }

            int id = category.Id.IntegerValue;
            return id == (int)BuiltInCategory.OST_Columns ||
                   id == (int)BuiltInCategory.OST_StructuralColumns ||
                   id == (int)BuiltInCategory.OST_GenericModel;
        }

        private static ElementId ResolveLevelId(View activeView, IList<Element> elements)
        {
            foreach (Element element in elements ?? new List<Element>())
            {
                Wall wall = element as Wall;
                Parameter wallBase = wall != null ? wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT) : null;
                ElementId wallLevelId = wallBase != null ? wallBase.AsElementId() : ElementId.InvalidElementId;
                if (IsValidElementId(wallLevelId))
                {
                    return wallLevelId;
                }

                Parameter familyLevel = element != null ? element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) : null;
                ElementId familyLevelId = familyLevel != null ? familyLevel.AsElementId() : ElementId.InvalidElementId;
                if (IsValidElementId(familyLevelId))
                {
                    return familyLevelId;
                }

                if (element != null && IsValidElementId(element.LevelId))
                {
                    return element.LevelId;
                }
            }

            return activeView != null ? activeView.GenLevel?.Id : ElementId.InvalidElementId;
        }

        private static double ResolveBaseZ(Document doc, ElementId levelId, IList<Element> elements)
        {
            Level level = levelId != null && levelId != ElementId.InvalidElementId ? doc.GetElement(levelId) as Level : null;
            if (level != null)
            {
                return level.Elevation;
            }

            foreach (Element element in elements ?? new List<Element>())
            {
                Wall wall = element as Wall;
                LocationCurve locationCurve = wall != null ? wall.Location as LocationCurve : null;
                Curve curve = locationCurve != null ? locationCurve.Curve : null;
                if (curve != null)
                {
                    return curve.GetEndPoint(0).Z;
                }

                BoundingBoxXYZ bbox = element != null ? element.get_BoundingBox(null) : null;
                if (bbox != null && bbox.Min != null)
                {
                    return bbox.Min.Z;
                }
            }

            return 0.0;
        }

        private static List<BoundarySegment> BuildSourceSegments(IList<Element> elements, View activeView, double baseZ)
        {
            List<BoundarySegment> result = new List<BoundarySegment>();
            foreach (Element element in elements ?? new List<Element>())
            {
                Wall wall = element as Wall;
                if (wall != null)
                {
                    LocationCurve locationCurve = wall.Location as LocationCurve;
                    Curve curve = locationCurve != null ? locationCurve.Curve : null;
                    if (curve != null)
                    {
                        AddSegment(result, ProjectToZ(curve.GetEndPoint(0), baseZ), ProjectToZ(curve.GetEndPoint(1), baseZ), element);
                    }

                    continue;
                }

                AddFootprintSegments(result, element, activeView, baseZ);
            }

            return result;
        }

        private static InteriorBoundaryResult ResolveInteriorBoundary(
            Document doc,
            ElementId levelId,
            IList<XYZ> roughLoopPoints,
            IList<Element> selectedElements,
            double baseZ)
        {
            if (doc == null || roughLoopPoints == null || roughLoopPoints.Count < 3)
            {
                return null;
            }

            XYZ seed = ComputeCentroid(roughLoopPoints);
            double windowSizeMm = ResolveInteriorBoundaryWindowSizeMm(roughLoopPoints);
            ModelBoundaryDatasetBuildResult build = ModelBoundarySegmentBuilder.BuildLocalDataset(
                doc,
                levelId,
                seed,
                windowSizeMm);
            if (build == null || build.Dataset == null || build.Dataset.Segments == null || build.Dataset.Segments.Count == 0)
            {
                return null;
            }

            HashSet<string> boundaryLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ModelBoundarySegmentBuilder.WallBoundaryLayerName,
                ModelBoundarySegmentBuilder.RoomSeparatorLayerName,
                ModelBoundarySegmentBuilder.DoorClosureLayerName
            };

            List<RoomCandidate> loops = RoomBoundaryLoopService.DetectMulti(
                build.Dataset,
                boundaryLayers,
                10.0,
                300.0,
                MinAreaM2,
                1200.0,
                350.0,
                false,
                false);

            List<RoomCandidate> containing = (loops ?? new List<RoomCandidate>())
                .Where(x => IsUsableInteriorLoop(x, seed, roughLoopPoints))
                .OrderBy(x => x.AreaM2)
                .ToList();
            RoomCandidate selected = containing.FirstOrDefault();
            if (selected == null)
            {
                ModelFloodFillService.FloodFillResult fill = TryFloodFillInteriorBoundary(doc, levelId, seed, windowSizeMm);
                if (fill == null || !fill.Success || fill.Polygon == null || fill.Polygon.Count < 3)
                {
                    return null;
                }

                return new InteriorBoundaryResult
                {
                    Success = true,
                    LoopPoints = NormalizeLoopPoints(fill.Polygon, baseZ),
                    AreaM2 = fill.AreaM2,
                    Centroid = fill.Centroid,
                    BBox = fill.BBox
                };
            }

            return new InteriorBoundaryResult
            {
                Success = true,
                LoopPoints = NormalizeLoopPoints(selected.LoopPoints, baseZ),
                AreaM2 = selected.AreaM2,
                Centroid = selected.Centroid,
                BBox = selected.BBox
            };
        }

        private static ModelFloodFillService.FloodFillResult TryFloodFillInteriorBoundary(
            Document doc,
            ElementId levelId,
            XYZ seed,
            double windowSizeMm)
        {
            List<Line> boundaries = ModelBoundaryCollector.CollectBoundaryLines(doc, levelId, seed, windowSizeMm);
            List<Line> doorClosures = DoorClosureBuilder.BuildDoorClosureLines(doc, levelId, seed, windowSizeMm);
            boundaries.AddRange(doorClosures);
            return ModelFloodFillService.DetectRoomPolygon(seed, boundaries, windowSizeMm, 150.0);
        }

        private static bool IsUsableInteriorLoop(RoomCandidate loop, XYZ seed, IList<XYZ> roughLoopPoints)
        {
            if (loop == null || loop.LoopPoints == null || loop.LoopPoints.Count < 4)
            {
                return false;
            }

            if (loop.Status == RoomBoundaryStatus.NeedsFix || loop.AreaM2 < MinAreaM2)
            {
                return false;
            }

            if (!PointInPolygon.ContainsPointXY(loop.LoopPoints, seed))
            {
                return false;
            }

            if (roughLoopPoints != null && roughLoopPoints.Count >= 3)
            {
                XYZ loopCenter = loop.Centroid ?? ComputeCentroid(loop.LoopPoints);
                if (!PointInPolygon.ContainsPointXY(roughLoopPoints.ToList(), loopCenter))
                {
                    return false;
                }
            }

            return true;
        }

        private static double ResolveInteriorBoundaryWindowSizeMm(IList<XYZ> roughLoopPoints)
        {
            double minX = roughLoopPoints.Min(p => p.X);
            double minY = roughLoopPoints.Min(p => p.Y);
            double maxX = roughLoopPoints.Max(p => p.X);
            double maxY = roughLoopPoints.Max(p => p.Y);
            double spanFeet = Math.Max(maxX - minX, maxY - minY);
            double spanMm = UnitUtils.ConvertFromInternalUnits(Math.Max(1.0, spanFeet), UnitTypeId.Millimeters);
            return Math.Max(10000.0, spanMm + 6000.0);
        }

        private static List<XYZ> NormalizeLoopPoints(IList<XYZ> points, double z)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ point in points ?? new List<XYZ>())
            {
                if (point == null)
                {
                    continue;
                }

                XYZ normalized = new XYZ(point.X, point.Y, z);
                if (result.Count == 0 || DistanceXY(result[result.Count - 1], normalized) > 1e-6)
                {
                    result.Add(normalized);
                }
            }

            if (result.Count > 1 && DistanceXY(result[0], result[result.Count - 1]) <= 1e-6)
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static BoundingBoxXYZ ExtendBoundingBoxZ(BoundingBoxXYZ source, BoundingBoxXYZ fallback)
        {
            if (source == null)
            {
                return fallback;
            }

            double minZ = fallback != null && fallback.Min != null ? fallback.Min.Z : source.Min.Z;
            double maxZ = fallback != null && fallback.Max != null ? fallback.Max.Z : source.Max.Z;
            return new BoundingBoxXYZ
            {
                Min = new XYZ(source.Min.X, source.Min.Y, minZ),
                Max = new XYZ(source.Max.X, source.Max.Y, maxZ)
            };
        }

        private static void AddFootprintSegments(List<BoundarySegment> result, Element element, View activeView, double baseZ)
        {
            BoundingBoxXYZ bbox = element != null ? element.get_BoundingBox(activeView) : null;
            if (bbox == null)
            {
                bbox = element != null ? element.get_BoundingBox(null) : null;
            }

            if (bbox == null || bbox.Min == null || bbox.Max == null)
            {
                return;
            }

            if (Math.Abs(bbox.Max.X - bbox.Min.X) < 1e-6 || Math.Abs(bbox.Max.Y - bbox.Min.Y) < 1e-6)
            {
                return;
            }

            XYZ p1 = new XYZ(bbox.Min.X, bbox.Min.Y, baseZ);
            XYZ p2 = new XYZ(bbox.Max.X, bbox.Min.Y, baseZ);
            XYZ p3 = new XYZ(bbox.Max.X, bbox.Max.Y, baseZ);
            XYZ p4 = new XYZ(bbox.Min.X, bbox.Max.Y, baseZ);
            AddSegment(result, p1, p2, element);
            AddSegment(result, p2, p3, element);
            AddSegment(result, p3, p4, element);
            AddSegment(result, p4, p1, element);
        }

        private static void AddSegment(List<BoundarySegment> result, XYZ start, XYZ end, Element element)
        {
            if (start == null || end == null || DistanceXY(start, end) < 1e-6)
            {
                return;
            }

            result.Add(new BoundarySegment
            {
                Start = start,
                End = end,
                Element = element,
                Parameters = new List<double> { 0.0, 1.0 }
            });
        }

        private static GraphBuildResult BuildIntersectionGraph(List<BoundarySegment> segments, double snapTolerance, double minSegment)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    AddIntersectionOrSnapPoints(segments[i], segments[j], snapTolerance);
                }
            }

            GraphBuildResult graph = new GraphBuildResult();
            foreach (BoundarySegment segment in segments)
            {
                List<double> parameters = segment.Parameters
                    .Select(Clamp01)
                    .Distinct(new ParameterComparer())
                    .OrderBy(x => x)
                    .ToList();

                for (int i = 0; i < parameters.Count - 1; i++)
                {
                    double a = parameters[i];
                    double b = parameters[i + 1];
                    XYZ start = PointAt(segment, a);
                    XYZ end = PointAt(segment, b);
                    if (DistanceXY(start, end) < minSegment)
                    {
                        continue;
                    }

                    int startIndex = FindOrAddNode(graph.Nodes, start, snapTolerance);
                    int endIndex = FindOrAddNode(graph.Nodes, end, snapTolerance);
                    if (startIndex == endIndex || HasEdge(graph.Edges, startIndex, endIndex))
                    {
                        continue;
                    }

                    graph.Edges.Add(new GraphEdge { A = startIndex, B = endIndex });
                }
            }

            return graph;
        }

        private static void AddIntersectionOrSnapPoints(BoundarySegment a, BoundarySegment b, double tolerance)
        {
            if (TrySegmentIntersection(a, b, tolerance, out double ta, out double tb))
            {
                AddParameter(a, ta);
                AddParameter(b, tb);
            }

            AddEndpointProjection(a, b, tolerance);
            AddEndpointProjection(b, a, tolerance);
        }

        private static bool TrySegmentIntersection(
            BoundarySegment a,
            BoundarySegment b,
            double tolerance,
            out double ta,
            out double tb)
        {
            ta = 0.0;
            tb = 0.0;
            XYZ p = a.Start;
            XYZ r = SubtractXY(a.End, a.Start);
            XYZ q = b.Start;
            XYZ s = SubtractXY(b.End, b.Start);
            double rxs = CrossXY(r, s);
            double qpxr = CrossXY(SubtractXY(q, p), r);

            if (Math.Abs(rxs) < 1e-10)
            {
                if (Math.Abs(qpxr) > tolerance * Math.Max(r.GetLength(), 1.0))
                {
                    return false;
                }

                double rr = DotXY(r, r);
                double ss = DotXY(s, s);
                if (rr < 1e-12 || ss < 1e-12)
                {
                    return false;
                }

                double t0 = DotXY(SubtractXY(q, p), r) / rr;
                double t1 = DotXY(SubtractXY(b.End, p), r) / rr;
                double lo = Math.Max(0.0, Math.Min(t0, t1));
                double hi = Math.Min(1.0, Math.Max(t0, t1));
                if (hi < lo)
                {
                    return false;
                }

                ta = (lo + hi) * 0.5;
                XYZ point = PointAt(a, ta);
                tb = DotXY(SubtractXY(point, q), s) / ss;
                return tb >= -0.05 && tb <= 1.05;
            }

            XYZ qp = SubtractXY(q, p);
            double t = CrossXY(qp, s) / rxs;
            double u = CrossXY(qp, r) / rxs;
            double tolA = tolerance / Math.Max(DistanceXY(a.Start, a.End), 1e-9);
            double tolB = tolerance / Math.Max(DistanceXY(b.Start, b.End), 1e-9);
            if (t < -tolA || t > 1.0 + tolA || u < -tolB || u > 1.0 + tolB)
            {
                return false;
            }

            ta = Clamp01(t);
            tb = Clamp01(u);
            return true;
        }

        private static void AddEndpointProjection(BoundarySegment endpointSegment, BoundarySegment targetSegment, double tolerance)
        {
            AddProjectedPoint(endpointSegment, targetSegment, 0.0, tolerance);
            AddProjectedPoint(endpointSegment, targetSegment, 1.0, tolerance);
        }

        private static void AddProjectedPoint(BoundarySegment endpointSegment, BoundarySegment targetSegment, double endpointParameter, double tolerance)
        {
            XYZ point = PointAt(endpointSegment, endpointParameter);
            double projected = ProjectParameter(targetSegment, point);
            if (projected < -0.05 || projected > 1.05)
            {
                return;
            }

            projected = Clamp01(projected);
            XYZ projectedPoint = PointAt(targetSegment, projected);
            if (DistanceXY(point, projectedPoint) <= tolerance)
            {
                AddParameter(endpointSegment, endpointParameter);
                AddParameter(targetSegment, projected);
            }
        }

        private static void AddParameter(BoundarySegment segment, double value)
        {
            double parameter = Clamp01(value);
            if (!segment.Parameters.Any(x => Math.Abs(x - parameter) <= 1e-6))
            {
                segment.Parameters.Add(parameter);
            }
        }


        private static int GetNodeDegree(Dictionary<int, List<int>> adjacency, int nodeIndex)
        {
            return adjacency != null &&
                   adjacency.TryGetValue(nodeIndex, out List<int> neighbors) &&
                   neighbors != null
                ? neighbors.Distinct().Count()
                : 0;
        }

        private static GraphBuildResult CloneGraphWithVirtualEdge(GraphBuildResult source, int a, int b)
        {
            GraphBuildResult clone = new GraphBuildResult();
            foreach (Node node in source.Nodes)
            {
                clone.Nodes.Add(new Node
                {
                    Point = node != null && node.Point != null
                        ? new XYZ(node.Point.X, node.Point.Y, node.Point.Z)
                        : XYZ.Zero
                });
            }

            foreach (GraphEdge edge in source.Edges)
            {
                clone.Edges.Add(new GraphEdge { A = edge.A, B = edge.B });
            }

            clone.Edges.Add(new GraphEdge { A = a, B = b });
            return clone;
        }

        private static bool IsVirtualOpeningClear(
            GraphBuildResult graph,
            int startIndex,
            int endIndex,
            double tolerance)
        {
            if (graph == null ||
                startIndex < 0 || startIndex >= graph.Nodes.Count ||
                endIndex < 0 || endIndex >= graph.Nodes.Count)
            {
                return false;
            }

            BoundarySegment virtualSegment = new BoundarySegment
            {
                Start = graph.Nodes[startIndex].Point,
                End = graph.Nodes[endIndex].Point,
                Parameters = new List<double> { 0.0, 1.0 }
            };

            foreach (GraphEdge edge in graph.Edges)
            {
                if (edge.A == startIndex || edge.B == startIndex ||
                    edge.A == endIndex || edge.B == endIndex)
                {
                    continue;
                }

                BoundarySegment existing = new BoundarySegment
                {
                    Start = graph.Nodes[edge.A].Point,
                    End = graph.Nodes[edge.B].Point,
                    Parameters = new List<double> { 0.0, 1.0 }
                };

                if (TrySegmentIntersection(virtualSegment, existing, tolerance, out double ta, out double tb) &&
                    ta > 1e-5 && ta < 1.0 - 1e-5 &&
                    tb > 1e-5 && tb < 1.0 - 1e-5)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LoopUsesEdge(
            IList<XYZ> loop,
            XYZ start,
            XYZ end,
            double tolerance)
        {
            if (loop == null || loop.Count < 2 || start == null || end == null)
            {
                return false;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                XYZ a = loop[i];
                XYZ b = loop[(i + 1) % loop.Count];
                bool forward = DistanceXY(a, start) <= tolerance && DistanceXY(b, end) <= tolerance;
                bool reverse = DistanceXY(a, end) <= tolerance && DistanceXY(b, start) <= tolerance;
                if (forward || reverse)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<XYZ> SimplifyCollinearLoop(IList<XYZ> points, double tolerance)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ point in points ?? new List<XYZ>())
            {
                if (point == null)
                {
                    continue;
                }

                if (result.Count == 0 || DistanceXY(result[result.Count - 1], point) > tolerance * 0.1)
                {
                    result.Add(new XYZ(point.X, point.Y, point.Z));
                }
            }

            if (result.Count > 1 && DistanceXY(result[0], result[result.Count - 1]) <= tolerance * 0.1)
            {
                result.RemoveAt(result.Count - 1);
            }

            bool changed = true;
            for (int guard = 0; changed && guard < 50 && result.Count > 3; guard++)
            {
                changed = false;
                for (int i = 0; i < result.Count; i++)
                {
                    XYZ previous = result[(i - 1 + result.Count) % result.Count];
                    XYZ current = result[i];
                    XYZ next = result[(i + 1) % result.Count];
                    if (IsPointNearlyCollinear(previous, current, next, tolerance))
                    {
                        result.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            return result;
        }

        private static bool IsPointNearlyCollinear(
            XYZ previous,
            XYZ current,
            XYZ next,
            double tolerance)
        {
            XYZ full = SubtractXY(next, previous);
            double fullLength = full.GetLength();
            if (fullLength < 1e-9)
            {
                return true;
            }

            double distanceToLine = Math.Abs(CrossXY(SubtractXY(current, previous), full)) / fullLength;
            if (distanceToLine > tolerance)
            {
                return false;
            }

            // The current point must lie between the two surrounding points.
            return DotXY(SubtractXY(current, previous), SubtractXY(current, next)) <= tolerance * tolerance;
        }

        private static bool IsSimplePolygon(IList<XYZ> points, double tolerance)
        {
            if (points == null || points.Count < 3)
            {
                return false;
            }

            for (int i = 0; i < points.Count; i++)
            {
                XYZ a1 = points[i];
                XYZ a2 = points[(i + 1) % points.Count];
                for (int j = i + 1; j < points.Count; j++)
                {
                    int nextJ = (j + 1) % points.Count;
                    if (i == j || (i + 1) % points.Count == j || nextJ == i)
                    {
                        continue;
                    }

                    BoundarySegment first = new BoundarySegment
                    {
                        Start = a1,
                        End = a2,
                        Parameters = new List<double> { 0.0, 1.0 }
                    };
                    BoundarySegment second = new BoundarySegment
                    {
                        Start = points[j],
                        End = points[nextJ],
                        Parameters = new List<double> { 0.0, 1.0 }
                    };

                    if (TrySegmentIntersection(first, second, tolerance, out double ta, out double tb) &&
                        ta > 1e-5 && ta < 1.0 - 1e-5 &&
                        tb > 1e-5 && tb < 1.0 - 1e-5)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return "-";
            }

            return "(" +
                point.X.ToString("F3", CultureInfo.InvariantCulture) + "," +
                point.Y.ToString("F3", CultureInfo.InvariantCulture) + "," +
                point.Z.ToString("F3", CultureInfo.InvariantCulture) + ")";
        }

        private static List<XYZ> FindBestClosedLoop(GraphBuildResult graph)
        {
            if (graph == null || graph.Nodes.Count < 3 || graph.Edges.Count < 3)
            {
                return new List<XYZ>();
            }

            Dictionary<int, List<int>> adjacency = BuildAdjacency(graph.Edges);
            Dictionary<int, List<int>> sortedAdjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<int, List<int>> pair in adjacency)
            {
                XYZ origin = graph.Nodes[pair.Key].Point;
                sortedAdjacency[pair.Key] = pair.Value
                    .Distinct()
                    .OrderBy(v => Math.Atan2(graph.Nodes[v].Point.Y - origin.Y, graph.Nodes[v].Point.X - origin.X))
                    .ToList();
            }

            HashSet<string> visited = new HashSet<string>();
            List<XYZ> best = new List<XYZ>();
            double bestArea = 0.0;

            foreach (GraphEdge edge in graph.Edges)
            {
                TryCollectFace(edge.A, edge.B, graph, sortedAdjacency, visited, ref best, ref bestArea);
                TryCollectFace(edge.B, edge.A, graph, sortedAdjacency, visited, ref best, ref bestArea);
            }

            return best;
        }

        private static void TryCollectFace(
            int start,
            int next,
            GraphBuildResult graph,
            Dictionary<int, List<int>> adjacency,
            HashSet<string> visited,
            ref List<XYZ> best,
            ref double bestArea)
        {
            string startKey = DirectedKey(start, next);
            if (visited.Contains(startKey))
            {
                return;
            }

            List<int> cycle = new List<int>();
            HashSet<int> local = new HashSet<int>();
            int previous = start;
            int current = next;
            cycle.Add(start);

            for (int guard = 0; guard < graph.Edges.Count * 3 + 10; guard++)
            {
                visited.Add(DirectedKey(previous, current));
                cycle.Add(current);
                if (current == start)
                {
                    break;
                }

                if (!local.Add(current))
                {
                    return;
                }

                if (!adjacency.TryGetValue(current, out List<int> neighbors) || neighbors.Count == 0)
                {
                    return;
                }

                int index = neighbors.IndexOf(previous);
                if (index < 0)
                {
                    return;
                }

                int nextIndex = (index - 1 + neighbors.Count) % neighbors.Count;
                int candidate = neighbors[nextIndex];
                previous = current;
                current = candidate;
            }

            if (cycle.Count < 4 || cycle[cycle.Count - 1] != start)
            {
                return;
            }

            cycle.RemoveAt(cycle.Count - 1);
            if (cycle.Distinct().Count() != cycle.Count)
            {
                return;
            }

            List<XYZ> points = cycle.Select(i => graph.Nodes[i].Point).ToList();
            double area = ComputeSignedArea(points);
            if (area <= 1e-9)
            {
                return;
            }

            if (area > bestArea)
            {
                bestArea = area;
                best = points;
            }
        }

        private static string DirectedKey(int a, int b)
        {
            return a.ToString(CultureInfo.InvariantCulture) + ">" + b.ToString(CultureInfo.InvariantCulture);
        }

        private static int FindOrAddNode(List<Node> nodes, XYZ point, double tolerance)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (DistanceXY(nodes[i].Point, point) <= tolerance)
                {
                    nodes[i].Point = new XYZ(
                        (nodes[i].Point.X + point.X) * 0.5,
                        (nodes[i].Point.Y + point.Y) * 0.5,
                        (nodes[i].Point.Z + point.Z) * 0.5);
                    return i;
                }
            }

            nodes.Add(new Node { Point = point });
            return nodes.Count - 1;
        }

        private static bool HasEdge(List<GraphEdge> edges, int a, int b)
        {
            return edges.Any(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));
        }

        private static Dictionary<int, List<int>> BuildAdjacency(List<GraphEdge> edges)
        {
            Dictionary<int, List<int>> adjacency = new Dictionary<int, List<int>>();
            foreach (GraphEdge edge in edges)
            {
                AddAdjacency(adjacency, edge.A, edge.B);
                AddAdjacency(adjacency, edge.B, edge.A);
            }

            return adjacency;
        }

        private static void AddAdjacency(Dictionary<int, List<int>> adjacency, int from, int to)
        {
            if (!adjacency.TryGetValue(from, out List<int> neighbors))
            {
                neighbors = new List<int>();
                adjacency[from] = neighbors;
            }

            if (!neighbors.Contains(to))
            {
                neighbors.Add(to);
            }
        }

        private static BoundingBoxXYZ BuildBoundingBox(IList<XYZ> points, IList<Element> elements, double baseZ)
        {
            double minX = points.Min(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxX = points.Max(p => p.X);
            double maxY = points.Max(p => p.Y);
            double height = UnitUtils.ConvertToInternalUnits(4000.0, UnitTypeId.Millimeters);
            foreach (Element element in elements ?? new List<Element>())
            {
                Wall wall = element as Wall;
                double wallHeight = ResolveWallHeight(wall);
                if (wallHeight > height)
                {
                    height = wallHeight;
                }

                BoundingBoxXYZ bbox = element != null ? element.get_BoundingBox(null) : null;
                if (bbox != null && bbox.Max != null)
                {
                    height = Math.Max(height, bbox.Max.Z - baseZ);
                }
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, baseZ),
                Max = new XYZ(maxX, maxY, baseZ + height)
            };
        }

        private static double ResolveWallHeight(Wall wall)
        {
            if (wall == null)
            {
                return 0.0;
            }

            Parameter unconnected = wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM);
            double value = unconnected != null ? unconnected.AsDouble() : 0.0;
            return value > 1e-6 ? value : 0.0;
        }

        private static List<RoomBoundaryWallReference> BuildBoundaryWallReferences(IList<Wall> walls)
        {
            List<RoomBoundaryWallReference> result = new List<RoomBoundaryWallReference>();
            int index = 1;
            foreach (Wall wall in walls ?? new List<Wall>())
            {
                if (wall == null)
                {
                    continue;
                }

                LocationCurve locationCurve = wall.Location as LocationCurve;
                Curve curve = locationCurve != null ? locationCurve.Curve : null;
                double lengthMm = curve != null ? UnitUtils.ConvertFromInternalUnits(curve.Length, UnitTypeId.Millimeters) : 0.0;
                result.Add(new RoomBoundaryWallReference
                {
                    ElementId = wall.Id.IntegerValue,
                    UniqueId = wall.UniqueId ?? string.Empty,
                    DisplayName = "WALL-" + index.ToString("0000", CultureInfo.InvariantCulture),
                    RevitName = wall.Name ?? string.Empty,
                    LengthMm = lengthMm
                });
                index++;
            }

            return result;
        }

        private static ElementId ResolveLevelIdFromElement(Element element)
        {
            return element != null && IsValidElementId(element.LevelId) ? element.LevelId : ElementId.InvalidElementId;
        }

        private static bool IsValidElementId(ElementId id)
        {
            return id != null && id != ElementId.InvalidElementId && id.IntegerValue > 0;
        }

        private static string ResolveLevelName(Document doc, ElementId levelId)
        {
            Level level = levelId != null && levelId != ElementId.InvalidElementId ? doc.GetElement(levelId) as Level : null;
            return level != null ? level.Name ?? string.Empty : string.Empty;
        }

        public static string BuildBoundarySignature(IList<Element> elements)
        {
            return string.Join(
                ",",
                (elements ?? new List<Element>())
                    .Where(e => e != null)
                    .Select(e => e.Id.IntegerValue)
                    .Distinct()
                    .OrderBy(id => id)
                    .Select(id => id.ToString(CultureInfo.InvariantCulture)));
        }

        public static string BuildBoundarySignature(IList<Wall> walls)
        {
            return BuildBoundarySignature((walls ?? new List<Wall>()).Cast<Element>().ToList());
        }

        private static XYZ ProjectToZ(XYZ point, double z)
        {
            return point == null ? null : new XYZ(point.X, point.Y, z);
        }

        private static XYZ PointAt(BoundarySegment segment, double parameter)
        {
            double t = Clamp01(parameter);
            return new XYZ(
                segment.Start.X + (segment.End.X - segment.Start.X) * t,
                segment.Start.Y + (segment.End.Y - segment.Start.Y) * t,
                segment.Start.Z + (segment.End.Z - segment.Start.Z) * t);
        }

        private static double ProjectParameter(BoundarySegment segment, XYZ point)
        {
            XYZ delta = SubtractXY(segment.End, segment.Start);
            double lengthSq = DotXY(delta, delta);
            if (lengthSq < 1e-12)
            {
                return 0.0;
            }

            return DotXY(SubtractXY(point, segment.Start), delta) / lengthSq;
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double DistanceXY(XYZ a, XYZ b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static XYZ SubtractXY(XYZ a, XYZ b)
        {
            return new XYZ(a.X - b.X, a.Y - b.Y, 0.0);
        }

        private static double CrossXY(XYZ a, XYZ b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        private static double DotXY(XYZ a, XYZ b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        private static double ComputeSignedArea(IList<XYZ> points)
        {
            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            return area * 0.5;
        }

        private static XYZ ComputeCentroid(IList<XYZ> points)
        {
            double area = ComputeSignedArea(points);
            if (Math.Abs(area) < 1e-9)
            {
                return new XYZ(points.Average(p => p.X), points.Average(p => p.Y), points.Average(p => p.Z));
            }

            double cx = 0.0;
            double cy = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                double factor = a.X * b.Y - b.X * a.Y;
                cx += (a.X + b.X) * factor;
                cy += (a.Y + b.Y) * factor;
            }

            double scale = 1.0 / (6.0 * area);
            return new XYZ(cx * scale, cy * scale, points.Average(p => p.Z));
        }

        private sealed class ParameterComparer : IEqualityComparer<double>
        {
            public bool Equals(double x, double y)
            {
                return Math.Abs(x - y) <= 1e-6;
            }

            public int GetHashCode(double obj)
            {
                return Math.Round(obj, 6).GetHashCode();
            }
        }

        private sealed class InteriorBoundaryResult
        {
            public bool Success { get; set; }
            public List<XYZ> LoopPoints { get; set; }
            public double AreaM2 { get; set; }
            public XYZ Centroid { get; set; }
            public BoundingBoxXYZ BBox { get; set; }
        }


        private sealed class OpenGapCandidate
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double WidthMm { get; set; }
            public List<XYZ> LoopPoints { get; set; }
            public double AreaM2 { get; set; }
        }

        private sealed class BoundarySegment
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public Element Element { get; set; }
            public List<double> Parameters { get; set; }
        }

        private sealed class GraphBuildResult
        {
            public List<Node> Nodes { get; set; } = new List<Node>();
            public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();
        }

        private sealed class Node
        {
            public XYZ Point { get; set; }
        }

        private sealed class GraphEdge
        {
            public int A { get; set; }
            public int B { get; set; }
        }
    }
}
