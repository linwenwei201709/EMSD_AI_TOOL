using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Rooms.Lifts
{
    public static class LiftSeedExtractor
    {
        private const double PairDistanceMm = 2500.0;
        private const double LiftGeometrySearchRadiusMm = 8000.0;
        private const double LiftEndpointJoinTolMm = 350.0;
        private const double LiftMinSizeMm = 700.0;
        private const double LiftMaxLengthMm = 10000.0;
        private const double LiftMaxWidthMm = 5000.0;
        private const double LiftMaxDiagonalMm = 12000.0;
        private const double LiftDefaultDoorHeightMm = 2300.0;
        private const double LiftDefaultDoorSillMm = 0.0;
        private const double LiftDefaultDoorWidthMm = 900.0;

        public static List<LiftRecognitionRecord> ExtractFromDataset(
            CadDataset dataset,
            string roomNameLayer,
            ElementId levelId,
            RoomRecognitionSettings roomSettings = null)
        {
            List<LiftRecognitionRecord> result = new List<LiftRecognitionRecord>();
            if (dataset == null || dataset.Texts == null || dataset.Texts.Count == 0)
            {
                return result;
            }

            IEnumerable<CadText> source = dataset.Texts.Where(x => x != null && x.Position != null);
            if (!string.IsNullOrWhiteSpace(roomNameLayer))
            {
                source = source.Where(x => string.Equals(x.RawLayerName, roomNameLayer, StringComparison.OrdinalIgnoreCase));
            }

            List<CadText> texts = source
                .Where(x => !string.IsNullOrWhiteSpace(x.Text))
                .Where(x => (x.Text ?? string.Empty).Trim().Length <= 120)
                .ToList();
            HashSet<CadText> consumed = new HashSet<CadText>();

            foreach (CadText text in texts)
            {
                string normalized = Normalize(text.Text);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    continue;
                }

                if (normalized.Contains("PASSENGERLIFT") || normalized.Contains("PASSENGER"))
                {
                    AddRecord(result, text, "Passenger Lift", "Passenger", levelId, text.Text);
                    consumed.Add(text);
                    continue;
                }

                if (normalized.Contains("SERVICELIFT") || normalized.Contains("SERVICE"))
                {
                    AddRecord(result, text, "Service Lift", "Service", levelId, text.Text);
                    consumed.Add(text);
                }
            }

            List<CadText> liftTokens = texts
                .Where(x => !consumed.Contains(x))
                .Where(x => Normalize(x.Text) == "LIFT")
                .ToList();
            List<CadText> passengerTokens = texts
                .Where(x => !consumed.Contains(x))
                .Where(x => Normalize(x.Text) == "PASSENGER")
                .ToList();
            List<CadText> serviceTokens = texts
                .Where(x => !consumed.Contains(x))
                .Where(x => Normalize(x.Text) == "SERVICE")
                .ToList();

            foreach (CadText passenger in passengerTokens)
            {
                CadText lift = FindNearestWithin(passenger, liftTokens, PairDistanceMm);
                if (lift == null)
                {
                    continue;
                }

                AddRecord(
                    result,
                    passenger,
                    "Passenger Lift",
                    "Passenger",
                    levelId,
                    "Passenger Lift",
                    MidPoint(passenger.Position, lift.Position));
                consumed.Add(passenger);
                consumed.Add(lift);
            }

            foreach (CadText service in serviceTokens)
            {
                CadText lift = FindNearestWithin(service, liftTokens.Where(x => !consumed.Contains(x)).ToList(), PairDistanceMm);
                if (lift == null)
                {
                    continue;
                }

                AddRecord(
                    result,
                    service,
                    "Service Lift",
                    "Service",
                    levelId,
                    "Service Lift",
                    MidPoint(service.Position, lift.Position));
                consumed.Add(service);
                consumed.Add(lift);
            }

            foreach (CadText passenger in passengerTokens.Where(x => !consumed.Contains(x)))
            {
                AddRecord(result, passenger, "Passenger Lift", "Passenger", levelId, passenger.Text);
                consumed.Add(passenger);
            }

            List<LiftRecognitionRecord> unique = result
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();

            ResolveLiftGeometryFromConfiguredLayers(dataset, unique, roomSettings);
            return unique;
        }

        public static void ApplyFixedProperties(LiftRecognitionRecord record)
        {
            if (record == null)
            {
                return;
            }

            if (string.Equals(record.LiftKind, "Passenger", StringComparison.OrdinalIgnoreCase) ||
                Normalize(record.LiftName).Contains("PASSENGER"))
            {
                record.LiftName = "Passenger Lift";
                record.LiftKind = "Passenger";
                record.LiftId = "LT-0003";
                record.LiftType = "Passenger";
                record.Dimension = "1400 x 1350 x 2500 mm";
                record.DoorSize = "900 x 2300 mm";
                record.Capacity = "1000 Kg";
                record.VirtualDoorHeightMm = LiftDefaultDoorHeightMm;
                record.VirtualDoorSillMm = LiftDefaultDoorSillMm;
                return;
            }

            record.LiftName = "Service Lift";
            record.LiftKind = "Service";
            record.LiftId = "LT-0002";
            record.LiftType = "Cargo";
            record.Dimension = "1600 x 1500 x 2500 mm";
            record.DoorSize = "900 x 2300 mm";
            record.Capacity = "1600 Kg";
            record.VirtualDoorHeightMm = LiftDefaultDoorHeightMm;
            record.VirtualDoorSillMm = LiftDefaultDoorSillMm;
        }

        private static void AddRecord(
            List<LiftRecognitionRecord> result,
            CadText text,
            string liftName,
            string liftKind,
            ElementId levelId,
            string rawText,
            XYZ overridePosition = null)
        {
            XYZ position = overridePosition ?? (text != null ? text.Position : null);
            if (position == null)
            {
                return;
            }

            LiftRecognitionRecord record = new LiftRecognitionRecord
            {
                LiftName = liftName,
                LiftKind = liftKind,
                Position = position,
                LevelId = levelId ?? ElementId.InvalidElementId,
                SourceLayer = text != null ? (text.RawLayerName ?? string.Empty) : string.Empty,
                RawText = rawText ?? (text != null ? (text.Text ?? string.Empty) : string.Empty),
                VirtualDoorHeightMm = LiftDefaultDoorHeightMm,
                VirtualDoorSillMm = LiftDefaultDoorSillMm
            };
            ApplyFixedProperties(record);
            record.Key = BuildKey(record);
            result.Add(record);
        }

        private static void ResolveLiftGeometryFromConfiguredLayers(CadDataset dataset, List<LiftRecognitionRecord> lifts, RoomRecognitionSettings roomSettings)
        {
            if (dataset == null || lifts == null || lifts.Count == 0)
            {
                return;
            }

            List<string> liftGeometryLayers = RoomRecognitionSettings.Clone(roomSettings).GetConfiguredLiftGeometryLayers();
            List<CadSegment> liftGeometrySegments = (dataset.Segments ?? new List<CadSegment>())
                .Where(x => IsLiftGeometrySegment(x, liftGeometryLayers))
                .Where(x => x.P0 != null && x.P1 != null && x.P0.DistanceTo(x.P1) > 1e-6)
                .ToList();
            if (liftGeometrySegments.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[LiftGeometry] configured lift geometry segment not found. Layers=[" + string.Join(", ", liftGeometryLayers) + "], Lifts=" + lifts.Count);
                return;
            }

            foreach (LiftRecognitionRecord lift in lifts)
            {
                if (lift == null || lift.Position == null)
                {
                    continue;
                }

                LiftGeometryCandidate geometry = ResolveNearestLiftGeometry(lift.Position, liftGeometrySegments);
                if (geometry == null || geometry.Boundary == null || geometry.Boundary.Count < 4)
                {
                    DiagnosticRecorder.AppendDebug("[LiftGeometry] Lift=" + (lift.LiftName ?? string.Empty) + ", GeometryResolved=False");
                    continue;
                }

                lift.Position = geometry.Center;
                lift.BoundaryPoints = geometry.Boundary;
                ApplyResolvedGeometryProperties(lift, geometry);
                lift.VirtualDoorStart = null;
                lift.VirtualDoorEnd = null;
                lift.VirtualDoorHostWallId = ElementId.InvalidElementId;
                lift.VirtualDoorWidthMm = 0.0;
                lift.VirtualDoorHeightMm = 0.0;
                lift.VirtualDoorSillMm = 0.0;
                lift.GeometrySourceLayer = string.Join(", ", liftGeometryLayers);
                lift.Key = BuildKey(lift);

                DiagnosticRecorder.AppendDebug(
                    "[LiftGeometry] Lift=" + (lift.LiftName ?? string.Empty) +
                    ", GeometryResolved=True" +
                    ", Layers=[" + string.Join(", ", liftGeometryLayers) + "]" +
                    ", CenterMode=XIntersection" +
                    ", Center=" + FormatMm(lift.Position) +
                    ", Dimension=" + (lift.Dimension ?? string.Empty) +
                    ", DoorSize=" + (lift.DoorSize ?? string.Empty) +
                    ", BoundaryPointCount=" + (lift.BoundaryPoints == null ? 0 : lift.BoundaryPoints.Count));
            }
        }

        private static void ApplyResolvedGeometryProperties(LiftRecognitionRecord record, LiftGeometryCandidate geometry)
        {
            if (record == null || geometry == null)
            {
                return;
            }

            double widthMm = NormalizeDisplayMm(geometry.WidthMm);
            double depthMm = NormalizeDisplayMm(geometry.HeightMm);
            if (widthMm > 0.0 && depthMm > 0.0)
            {
                record.Dimension = FormatDimensionMm(widthMm, depthMm, 2500.0);
            }

            double doorWidthMm = NormalizeDisplayMm(geometry.DoorWidthMm > 0.0
                ? geometry.DoorWidthMm
                : Math.Max(geometry.WidthMm, geometry.HeightMm));
            if (doorWidthMm < 300.0)
            {
                doorWidthMm = LiftDefaultDoorWidthMm;
            }

            record.DoorSize = FormatDoorSizeMm(doorWidthMm, 2300.0);
        }

        private static double ResolveLargeGapWidthMm(IList<XYZ> boundary, double widthMm, double heightMm)
        {
            double maxSideMm = 0.0;
            if (boundary != null && boundary.Count >= 4)
            {
                int last = boundary.Count;
                if (boundary[0] != null && boundary[boundary.Count - 1] != null && Distance2D(boundary[0], boundary[boundary.Count - 1]) <= 1e-6)
                {
                    last = boundary.Count - 1;
                }

                for (int i = 0; i < last; i++)
                {
                    XYZ a = boundary[i];
                    XYZ b = boundary[(i + 1) % last];
                    if (a == null || b == null)
                    {
                        continue;
                    }

                    double sideMm = UnitUtils.ConvertFromInternalUnits(Distance2D(a, b), UnitTypeId.Millimeters);
                    if (sideMm > maxSideMm)
                    {
                        maxSideMm = sideMm;
                    }
                }
            }

            if (maxSideMm >= 300.0)
            {
                return maxSideMm;
            }

            return Math.Max(widthMm, heightMm);
        }

        private static double NormalizeDisplayMm(double valueMm)
        {
            if (double.IsNaN(valueMm) || double.IsInfinity(valueMm) || valueMm <= 0.0)
            {
                return 0.0;
            }

            return Math.Round(valueMm / 10.0, 0) * 10.0;
        }

        private static string FormatDimensionMm(double widthMm, double depthMm, double heightMm)
        {
            return FormatWholeMm(widthMm) + " x " + FormatWholeMm(depthMm) + " x " + FormatWholeMm(heightMm) + " mm";
        }

        private static string FormatDoorSizeMm(double widthMm, double heightMm)
        {
            return FormatWholeMm(widthMm) + " x " + FormatWholeMm(heightMm) + " mm";
        }

        private static string FormatWholeMm(double valueMm)
        {
            return Math.Round(valueMm, 0).ToString("F0");
        }

        private static bool IsLiftGeometrySegment(CadSegment segment, IReadOnlyCollection<string> configuredLayerNames)
        {
            if (segment == null)
            {
                return false;
            }

            return IsConfiguredLiftGeometryLayer(segment.RawLayerName, configuredLayerNames) ||
                   IsConfiguredLiftGeometryLayer(segment.LayerName, configuredLayerNames) ||
                   IsConfiguredLiftGeometryLayer(segment.NormalizedLayer, configuredLayerNames) ||
                   IsConfiguredLiftGeometryLayer(segment.SemanticLayer, configuredLayerNames);
        }

        private static bool IsConfiguredLiftGeometryLayer(string value, IReadOnlyCollection<string> configuredLayerNames)
        {
            string normalized = NormalizeLayer(value);
            if (string.IsNullOrWhiteSpace(normalized) || configuredLayerNames == null || configuredLayerNames.Count == 0)
            {
                return false;
            }

            foreach (string configured in configuredLayerNames)
            {
                string configuredNormalized = NormalizeLayer(configured);
                if (string.IsNullOrWhiteSpace(configuredNormalized))
                {
                    continue;
                }

                if (string.Equals(normalized, configuredNormalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.EndsWith("|" + configuredNormalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.IndexOf(configuredNormalized, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidLiftGeometrySize(LiftGeometryCandidate candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate.WidthMm >= LiftMinSizeMm &&
                   candidate.HeightMm >= LiftMinSizeMm &&
                   IsWithinLiftMaxSize(candidate.WidthMm, candidate.HeightMm);
        }

        private static bool IsWithinLiftMaxSize(double widthMm, double heightMm)
        {
            if (double.IsNaN(widthMm) || double.IsNaN(heightMm) || double.IsInfinity(widthMm) || double.IsInfinity(heightMm))
            {
                return false;
            }

            double longSideMm = Math.Max(widthMm, heightMm);
            double shortSideMm = Math.Min(widthMm, heightMm);
            return longSideMm >= LiftMinSizeMm &&
                   shortSideMm >= LiftMinSizeMm &&
                   longSideMm <= LiftMaxLengthMm &&
                   shortSideMm <= LiftMaxWidthMm;
        }

        private static List<LiftGeometryCandidate> BuildCrossBoxCandidates(List<CadSegment> segments, XYZ seed)
        {
            List<LiftGeometryCandidate> result = new List<LiftGeometryCandidate>();
            if (segments == null || seed == null)
            {
                return result;
            }

            List<CadSegment> diagonals = segments
                .Where(IsLiftDiagonalSegment)
                .ToList();

            for (int i = 0; i < diagonals.Count; i++)
            {
                for (int j = i + 1; j < diagonals.Count; j++)
                {
                    CadSegment a = diagonals[i];
                    CadSegment b = diagonals[j];
                    LiftGeometryCandidate candidate = TryBuildCrossBoxCandidate(a, b, seed);
                    if (candidate != null)
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }

        private static bool IsLiftDiagonalSegment(CadSegment segment)
        {
            if (segment == null || segment.P0 == null || segment.P1 == null)
            {
                return false;
            }

            double dx = Math.Abs(segment.P1.X - segment.P0.X);
            double dy = Math.Abs(segment.P1.Y - segment.P0.Y);
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= UnitUtils.ConvertToInternalUnits(600.0, UnitTypeId.Millimeters) ||
                len >= UnitUtils.ConvertToInternalUnits(LiftMaxDiagonalMm, UnitTypeId.Millimeters))
            {
                return false;
            }

            double minComponent = len * 0.25;
            return dx >= minComponent && dy >= minComponent;
        }

        private static LiftGeometryCandidate TryBuildCrossBoxCandidate(CadSegment first, CadSegment second, XYZ seed)
        {
            if (first == null || second == null || seed == null ||
                first.P0 == null || first.P1 == null || second.P0 == null || second.P1 == null)
            {
                return null;
            }

            XYZ midA = MidPoint(first.P0, first.P1);
            XYZ midB = MidPoint(second.P0, second.P1);
            double midTolFt = UnitUtils.ConvertToInternalUnits(900.0, UnitTypeId.Millimeters);
            if (Distance2D(midA, midB) > midTolFt)
            {
                return null;
            }

            double lenA = Distance2D(first.P0, first.P1);
            double lenB = Distance2D(second.P0, second.P1);
            if (lenA <= 1e-9 || lenB <= 1e-9)
            {
                return null;
            }

            double ratio = Math.Min(lenA, lenB) / Math.Max(lenA, lenB);
            if (ratio < 0.55)
            {
                return null;
            }

            XYZ dirA = new XYZ((first.P1.X - first.P0.X) / lenA, (first.P1.Y - first.P0.Y) / lenA, 0.0);
            XYZ dirB = new XYZ((second.P1.X - second.P0.X) / lenB, (second.P1.Y - second.P0.Y) / lenB, 0.0);
            double dot = Math.Abs(dirA.DotProduct(dirB));
            if (dot > 0.90)
            {
                return null;
            }

            XYZ center;
            if (!TryGetSegmentIntersection2D(
                    first.P0,
                    first.P1,
                    second.P0,
                    second.P1,
                    UnitUtils.ConvertToInternalUnits(180.0, UnitTypeId.Millimeters),
                    seed.Z,
                    out center))
            {
                return null;
            }

            double z = seed.Z;
            List<XYZ> cornerPoints = RemoveDuplicatePoints2D(
                new List<XYZ>
                {
                    new XYZ(first.P0.X, first.P0.Y, z),
                    new XYZ(first.P1.X, first.P1.Y, z),
                    new XYZ(second.P0.X, second.P0.Y, z),
                    new XYZ(second.P1.X, second.P1.Y, z)
                },
                UnitUtils.ConvertToInternalUnits(120.0, UnitTypeId.Millimeters));
            if (cornerPoints.Count < 4)
            {
                return null;
            }

            List<XYZ> boundary = cornerPoints
                .OrderBy(x => Math.Atan2(x.Y - center.Y, x.X - center.X))
                .ToList();
            if (PolygonArea2D(boundary) < UnitUtils.ConvertToInternalUnits(0.15, UnitTypeId.SquareMeters))
            {
                return null;
            }
            boundary.Add(boundary[0]);

            double minX = boundary.Min(x => x.X);
            double minY = boundary.Min(x => x.Y);
            double maxX = boundary.Max(x => x.X);
            double maxY = boundary.Max(x => x.Y);
            double widthMm = UnitUtils.ConvertFromInternalUnits(maxX - minX, UnitTypeId.Millimeters);
            double heightMm = UnitUtils.ConvertFromInternalUnits(maxY - minY, UnitTypeId.Millimeters);
            if (!IsWithinLiftMaxSize(widthMm, heightMm))
            {
                return null;
            }

            double distanceToSeed = Distance2D(center, seed);

            return new LiftGeometryCandidate
            {
                Boundary = boundary,
                Center = center,
                DoorStart = null,
                DoorEnd = null,
                DoorWidthMm = ResolveLargeGapWidthMm(boundary, widthMm, heightMm),
                WidthMm = widthMm,
                HeightMm = heightMm,
                SegmentCount = 2,
                DistanceToSeed = distanceToSeed,
                Score = distanceToSeed - UnitUtils.ConvertToInternalUnits(2500.0, UnitTypeId.Millimeters)
            };
        }

        private static bool TryGetSegmentIntersection2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1, double toleranceFt, double z, out XYZ intersection)
        {
            intersection = null;
            if (a0 == null || a1 == null || b0 == null || b1 == null)
            {
                return false;
            }

            double rx = a1.X - a0.X;
            double ry = a1.Y - a0.Y;
            double sx = b1.X - b0.X;
            double sy = b1.Y - b0.Y;
            double denominator = Cross2D(rx, ry, sx, sy);
            if (Math.Abs(denominator) <= 1e-12)
            {
                return false;
            }

            double qpx = b0.X - a0.X;
            double qpy = b0.Y - a0.Y;
            double t = Cross2D(qpx, qpy, sx, sy) / denominator;
            double u = Cross2D(qpx, qpy, rx, ry) / denominator;
            double lenA = Math.Sqrt(rx * rx + ry * ry);
            double lenB = Math.Sqrt(sx * sx + sy * sy);
            double tolA = lenA > 1e-9 ? toleranceFt / lenA : 0.0;
            double tolB = lenB > 1e-9 ? toleranceFt / lenB : 0.0;
            if (t < -tolA || t > 1.0 + tolA || u < -tolB || u > 1.0 + tolB)
            {
                return false;
            }

            double x = a0.X + rx * t;
            double y = a0.Y + ry * t;
            intersection = new XYZ(x, y, z);
            return true;
        }

        private static double Cross2D(double ax, double ay, double bx, double by)
        {
            return ax * by - ay * bx;
        }

        private static List<XYZ> RemoveDuplicatePoints2D(List<XYZ> points, double toleranceFt)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (XYZ point in points ?? new List<XYZ>())
            {
                if (point == null)
                {
                    continue;
                }

                if (!result.Any(x => Distance2D(x, point) <= toleranceFt))
                {
                    result.Add(point);
                }
            }

            return result;
        }

        private static double PolygonArea2D(IList<XYZ> points)
        {
            if (points == null || points.Count < 3)
            {
                return 0.0;
            }

            double sum = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                sum += a.X * b.Y - b.X * a.Y;
            }

            return Math.Abs(sum) * 0.5;
        }

        private static LiftGeometryCandidate BuildNearbyBoundingBoxCandidate(List<CadSegment> nearby, XYZ seed)
        {
            if (nearby == null || nearby.Count == 0 || seed == null)
            {
                return null;
            }

            double tightFt = UnitUtils.ConvertToInternalUnits(3500.0, UnitTypeId.Millimeters);
            List<CadSegment> tight = nearby
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .Where(x => DistancePointToSegment2D(seed, x.P0, x.P1) <= tightFt)
                .ToList();
            if (tight.Count == 0)
            {
                tight = nearby;
            }

            LiftGeometryCandidate candidate = BuildGeometryCandidate(tight, seed.Z);
            if (candidate == null || !IsValidLiftGeometrySize(candidate))
            {
                return null;
            }

            candidate.DistanceToSeed = Distance2D(candidate.Center, seed);
            candidate.Score = candidate.DistanceToSeed + UnitUtils.ConvertToInternalUnits(2500.0, UnitTypeId.Millimeters);
            return candidate;
        }

        private static DoorSide ResolveMostOpenSideForBoundary(List<XYZ> boundary, XYZ seed)
        {
            if (boundary == null || boundary.Count < 4 || seed == null)
            {
                return new DoorSide { Name = "UNKNOWN", Start = seed, End = seed };
            }

            List<DoorSide> sides = new List<DoorSide>();
            for (int i = 0; i < boundary.Count - 1; i++)
            {
                XYZ a = boundary[i];
                XYZ b = boundary[i + 1];
                XYZ mid = MidPoint(a, b);
                sides.Add(new DoorSide
                {
                    Name = "SIDE" + i.ToString(),
                    Start = a,
                    End = b,
                    LengthFeet = Distance2D(a, b),
                    CoverageFeet = Distance2D(mid, seed),
                    OpenRatio = 0.0
                });
            }

            // Use the side nearest the text seed as the virtual lift entrance.
            return sides
                .OrderBy(x => x.CoverageFeet)
                .ThenByDescending(x => x.LengthFeet)
                .FirstOrDefault() ?? new DoorSide { Name = "UNKNOWN", Start = seed, End = seed };
        }

        private static LiftGeometryCandidate ResolveNearestLiftGeometry(XYZ seed, List<CadSegment> allDt001Segments)
        {
            double searchFt = UnitUtils.ConvertToInternalUnits(LiftGeometrySearchRadiusMm, UnitTypeId.Millimeters);
            List<CadSegment> nearby = allDt001Segments
                .Where(x => DistancePointToSegment2D(seed, x.P0, x.P1) <= searchFt)
                .ToList();
            if (nearby.Count == 0)
            {
                return null;
            }

            // DT001 lift symbols normally contain a rectangle with an X inside it.
            // Prefer that X-box over the text seed, otherwise the black center point
            // can end up on the words "Service Lift" instead of the real lift core.
            List<LiftGeometryCandidate> xBoxCandidates = BuildCrossBoxCandidates(nearby, seed);
            LiftGeometryCandidate xBox = xBoxCandidates
                .Where(IsValidLiftGeometrySize)
                .OrderBy(x => x.Score)
                .FirstOrDefault();
            if (xBox != null)
            {
                return xBox;
            }

            List<List<CadSegment>> components = BuildConnectedComponents(nearby);
            List<LiftGeometryCandidate> candidates = new List<LiftGeometryCandidate>();
            foreach (List<CadSegment> component in components)
            {
                LiftGeometryCandidate candidate = BuildGeometryCandidate(component, seed.Z);
                if (candidate == null)
                {
                    continue;
                }

                candidate.DistanceToSeed = candidate.Center != null ? Distance2D(candidate.Center, seed) : double.MaxValue;
                bool containsSeed = candidate.Contains2D(seed);
                candidate.Score = (containsSeed ? 0.0 : candidate.DistanceToSeed) + Math.Max(0, 10 - component.Count) * 0.001;
                candidates.Add(candidate);
            }

            LiftGeometryCandidate componentCandidate = candidates
                .Where(IsValidLiftGeometrySize)
                .OrderBy(x => x.Score)
                .FirstOrDefault();
            if (componentCandidate != null)
            {
                return componentCandidate;
            }

            // Final fallback: if DT001 is split into small non-touching pieces, use the
            // bounding box of nearby DT001 geometry. This still gives a rectangular
            // lift area instead of a circular placeholder.
            return BuildNearbyBoundingBoxCandidate(nearby, seed);
        }

        private static List<List<CadSegment>> BuildConnectedComponents(List<CadSegment> segments)
        {
            List<List<CadSegment>> result = new List<List<CadSegment>>();
            if (segments == null || segments.Count == 0)
            {
                return result;
            }

            double tolFt = UnitUtils.ConvertToInternalUnits(LiftEndpointJoinTolMm, UnitTypeId.Millimeters);
            HashSet<CadSegment> visited = new HashSet<CadSegment>();
            foreach (CadSegment segment in segments)
            {
                if (segment == null || visited.Contains(segment))
                {
                    continue;
                }

                List<CadSegment> component = new List<CadSegment>();
                Queue<CadSegment> queue = new Queue<CadSegment>();
                queue.Enqueue(segment);
                visited.Add(segment);

                while (queue.Count > 0)
                {
                    CadSegment current = queue.Dequeue();
                    component.Add(current);
                    foreach (CadSegment other in segments)
                    {
                        if (other == null || visited.Contains(other))
                        {
                            continue;
                        }

                        if (SegmentsTouch2D(current, other, tolFt))
                        {
                            visited.Add(other);
                            queue.Enqueue(other);
                        }
                    }
                }

                result.Add(component);
            }

            return result;
        }

        private static LiftGeometryCandidate BuildGeometryCandidate(List<CadSegment> component, double z)
        {
            if (component == null || component.Count == 0)
            {
                return null;
            }

            List<XYZ> pts = component
                .Where(x => x != null && x.P0 != null && x.P1 != null)
                .SelectMany(x => new[] { x.P0, x.P1 })
                .ToList();
            if (pts.Count < 3)
            {
                return null;
            }

            double minX = pts.Min(x => x.X);
            double minY = pts.Min(x => x.Y);
            double maxX = pts.Max(x => x.X);
            double maxY = pts.Max(x => x.Y);
            if ((maxX - minX) <= 1e-6 || (maxY - minY) <= 1e-6)
            {
                return null;
            }

            double widthMm = UnitUtils.ConvertFromInternalUnits(maxX - minX, UnitTypeId.Millimeters);
            double heightMm = UnitUtils.ConvertFromInternalUnits(maxY - minY, UnitTypeId.Millimeters);
            XYZ a = new XYZ(minX, minY, z);
            XYZ b = new XYZ(maxX, minY, z);
            XYZ c = new XYZ(maxX, maxY, z);
            XYZ d = new XYZ(minX, maxY, z);
            List<XYZ> boundary = new List<XYZ> { a, b, c, d, a };

            DoorSide doorSide = ResolveMostOpenSide(component, minX, minY, maxX, maxY, z);
            XYZ doorStart = doorSide.Start;
            XYZ doorEnd = doorSide.End;
            double doorWidthMm = UnitUtils.ConvertFromInternalUnits(doorStart.DistanceTo(doorEnd), UnitTypeId.Millimeters);
            if (doorWidthMm < 300.0)
            {
                doorWidthMm = LiftDefaultDoorWidthMm;
            }

            return new LiftGeometryCandidate
            {
                Boundary = boundary,
                Center = new XYZ((minX + maxX) * 0.5, (minY + maxY) * 0.5, z),
                DoorStart = null,
                DoorEnd = null,
                DoorWidthMm = ResolveLargeGapWidthMm(boundary, widthMm, heightMm),
                WidthMm = widthMm,
                HeightMm = heightMm,
                SegmentCount = component.Count
            };
        }

        private static DoorSide ResolveMostOpenSide(List<CadSegment> segments, double minX, double minY, double maxX, double maxY, double z)
        {
            List<DoorSide> sides = new List<DoorSide>
            {
                new DoorSide { Name = "BOTTOM", Start = new XYZ(minX, minY, z), End = new XYZ(maxX, minY, z) },
                new DoorSide { Name = "RIGHT", Start = new XYZ(maxX, minY, z), End = new XYZ(maxX, maxY, z) },
                new DoorSide { Name = "TOP", Start = new XYZ(maxX, maxY, z), End = new XYZ(minX, maxY, z) },
                new DoorSide { Name = "LEFT", Start = new XYZ(minX, maxY, z), End = new XYZ(minX, minY, z) }
            };

            double closeTolFt = UnitUtils.ConvertToInternalUnits(180.0, UnitTypeId.Millimeters);
            foreach (DoorSide side in sides)
            {
                side.CoverageFeet = EstimateCoverageOnSide(segments, side.Start, side.End, closeTolFt);
                side.LengthFeet = side.Start.DistanceTo(side.End);
                side.OpenRatio = side.LengthFeet > 1e-9 ? 1.0 - Math.Min(1.0, side.CoverageFeet / side.LengthFeet) : 0.0;
            }

            // Prefer the least-covered side because the lift entrance normally appears as an open gap.
            // On ties, prefer the upper side to match typical DT001 service-lift drawings.
            return sides
                .OrderByDescending(x => x.OpenRatio)
                .ThenByDescending(x => string.Equals(x.Name, "TOP", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(x => x.LengthFeet)
                .First();
        }

        private static double EstimateCoverageOnSide(List<CadSegment> segments, XYZ sideStart, XYZ sideEnd, double closeTolFt)
        {
            if (segments == null || sideStart == null || sideEnd == null)
            {
                return 0.0;
            }

            XYZ side = sideEnd - sideStart;
            double sideLength = Math.Sqrt(side.X * side.X + side.Y * side.Y);
            if (sideLength <= 1e-9)
            {
                return 0.0;
            }

            XYZ dir = new XYZ(side.X / sideLength, side.Y / sideLength, 0.0);
            double coverage = 0.0;
            foreach (CadSegment seg in segments)
            {
                if (seg == null || seg.P0 == null || seg.P1 == null)
                {
                    continue;
                }

                double d0 = DistancePointToLine2D(seg.P0, sideStart, sideEnd);
                double d1 = DistancePointToLine2D(seg.P1, sideStart, sideEnd);
                if (d0 > closeTolFt || d1 > closeTolFt)
                {
                    continue;
                }

                XYZ s = seg.P1 - seg.P0;
                double segLength = Math.Sqrt(s.X * s.X + s.Y * s.Y);
                if (segLength <= 1e-9)
                {
                    continue;
                }

                XYZ segDir = new XYZ(s.X / segLength, s.Y / segLength, 0.0);
                double parallel = Math.Abs(segDir.DotProduct(dir));
                if (parallel < Math.Cos(Math.PI / 12.0))
                {
                    continue;
                }

                double t0 = ProjectParamOnSide(seg.P0, sideStart, dir);
                double t1 = ProjectParamOnSide(seg.P1, sideStart, dir);
                double lo = Math.Max(0.0, Math.Min(t0, t1));
                double hi = Math.Min(sideLength, Math.Max(t0, t1));
                if (hi > lo)
                {
                    coverage += (hi - lo);
                }
            }

            return Math.Min(sideLength, coverage);
        }

        private static double ProjectParamOnSide(XYZ p, XYZ origin, XYZ dir)
        {
            if (p == null || origin == null || dir == null)
            {
                return 0.0;
            }

            return (p.X - origin.X) * dir.X + (p.Y - origin.Y) * dir.Y;
        }

        private static bool SegmentsTouch2D(CadSegment a, CadSegment b, double tolFt)
        {
            if (a == null || b == null || a.P0 == null || a.P1 == null || b.P0 == null || b.P1 == null)
            {
                return false;
            }

            return Distance2D(a.P0, b.P0) <= tolFt ||
                   Distance2D(a.P0, b.P1) <= tolFt ||
                   Distance2D(a.P1, b.P0) <= tolFt ||
                   Distance2D(a.P1, b.P1) <= tolFt ||
                   DistanceBetweenSegments2D(a.P0, a.P1, b.P0, b.P1) <= tolFt;
        }

        private static double DistanceBetweenSegments2D(XYZ a0, XYZ a1, XYZ b0, XYZ b1)
        {
            return Math.Min(
                Math.Min(DistancePointToSegment2D(a0, b0, b1), DistancePointToSegment2D(a1, b0, b1)),
                Math.Min(DistancePointToSegment2D(b0, a0, a1), DistancePointToSegment2D(b1, a0, a1)));
        }

        private static double DistancePointToSegment2D(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            double vx = b.X - a.X;
            double vy = b.Y - a.Y;
            double wx = p.X - a.X;
            double wy = p.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            if (len2 <= 1e-12)
            {
                return Distance2D(p, a);
            }

            double t = (wx * vx + wy * vy) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double x = a.X + t * vx;
            double y = a.Y + t * vy;
            double dx = p.X - x;
            double dy = p.Y - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DistancePointToLine2D(XYZ p, XYZ a, XYZ b)
        {
            if (p == null || a == null || b == null)
            {
                return double.MaxValue;
            }

            double vx = b.X - a.X;
            double vy = b.Y - a.Y;
            double len = Math.Sqrt(vx * vx + vy * vy);
            if (len <= 1e-9)
            {
                return Distance2D(p, a);
            }

            return Math.Abs((p.X - a.X) * vy - (p.Y - a.Y) * vx) / len;
        }

        private static double Distance2D(XYZ a, XYZ b)
        {
            if (a == null || b == null)
            {
                return double.MaxValue;
            }

            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string BuildKey(LiftRecognitionRecord record)
        {
            if (record == null || record.Position == null)
            {
                return string.Empty;
            }

            int level = record.LevelId != null ? record.LevelId.IntegerValue : -1;
            double xMm = UnitUtils.ConvertFromInternalUnits(record.Position.X, UnitTypeId.Millimeters);
            double yMm = UnitUtils.ConvertFromInternalUnits(record.Position.Y, UnitTypeId.Millimeters);
            return "LIFT|" + level + "|" + Normalize(record.LiftName) + "|" + Math.Round(xMm, 0) + "|" + Math.Round(yMm, 0);
        }

        private static CadText FindNearestWithin(CadText source, IList<CadText> candidates, double maxDistanceMm)
        {
            if (source == null || source.Position == null || candidates == null)
            {
                return null;
            }

            double maxFt = UnitUtils.ConvertToInternalUnits(maxDistanceMm, UnitTypeId.Millimeters);
            return candidates
                .Where(x => x != null && x.Position != null)
                .Select(x => new CandidateDistance { Text = x, Distance = source.Position.DistanceTo(x.Position) })
                .Where(x => x.Distance <= maxFt)
                .OrderBy(x => x.Distance)
                .Select(x => x.Text)
                .FirstOrDefault();
        }

        private static XYZ MidPoint(XYZ a, XYZ b)
        {
            if (a == null)
            {
                return b;
            }

            if (b == null)
            {
                return a;
            }

            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string s = text.Trim().ToUpperInvariant();
            s = s.Replace("锛?", "/");
            s = s.Replace("銆€", string.Empty);
            s = s.Replace(" ", string.Empty);
            s = s.Replace("\t", string.Empty);
            s = s.Replace("\r", string.Empty);
            s = s.Replace("\n", string.Empty);
            return s;
        }

        private static string NormalizeLayer(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Trim().ToUpperInvariant().Replace(" ", string.Empty);
        }

        private static string FormatMm(XYZ p)
        {
            if (p == null)
            {
                return "-";
            }

            double x = UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters);
            return x.ToString("F0") + "," + y.ToString("F0");
        }

        private sealed class CandidateDistance
        {
            public CadText Text { get; set; }

            public double Distance { get; set; }
        }

        private sealed class LiftGeometryCandidate
        {
            public List<XYZ> Boundary { get; set; }
            public XYZ Center { get; set; }
            public XYZ DoorStart { get; set; }
            public XYZ DoorEnd { get; set; }
            public double DoorWidthMm { get; set; }
            public double WidthMm { get; set; }
            public double HeightMm { get; set; }
            public int SegmentCount { get; set; }
            public double DistanceToSeed { get; set; }
            public double Score { get; set; }

            public bool Contains2D(XYZ p)
            {
                if (p == null || Boundary == null || Boundary.Count < 4)
                {
                    return false;
                }

                double minX = Boundary.Min(x => x.X);
                double maxX = Boundary.Max(x => x.X);
                double minY = Boundary.Min(x => x.Y);
                double maxY = Boundary.Max(x => x.Y);
                return p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY;
            }
        }

        private sealed class DoorSide
        {
            public string Name { get; set; }
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public double LengthFeet { get; set; }
            public double CoverageFeet { get; set; }
            public double OpenRatio { get; set; }
        }
    }
}
