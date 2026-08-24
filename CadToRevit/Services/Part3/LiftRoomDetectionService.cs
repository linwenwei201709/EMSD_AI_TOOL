using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Part3
{
    public static class LiftRoomDetectionService
    {
        private const double MinLiftRoomAreaM2 = 5.0;
        private const double MaxLiftRoomAreaM2 = 20.0;
        private const double MaxBoundaryDistanceMm = 1000.0;

        public static List<LiftRecognitionRecord> Detect(Document doc, TargetRoomModelRecognitionService.RecognitionSummary summary)
        {
            DiagnosticRecorder.AppendDebug("[LiftDetect] Started. Source=AnalyzeRoomsPostProcess");

            List<LiftRecognitionRecord> result = new List<LiftRecognitionRecord>();
            if (doc == null || summary == null || summary.RunResult == null)
            {
                DiagnosticRecorder.AppendDebug("[LiftDetect] LiftDoorAnchors=0");
                DiagnosticRecorder.AppendDebug("[LiftDetect] LiftCandidates=0");
                DiagnosticRecorder.AppendDebug("[LiftDetect] Finished");
                return result;
            }

            List<RoomSemanticRecord> rooms = (summary.RunResult.Rooms ?? new List<RoomSemanticRecord>())
                .Where(IsLiftRoomAreaCandidate)
                .Where(x => x.LoopPoints != null && x.LoopPoints.Count >= 4)
                .ToList();

            List<LiftDoorAnchor> anchors = CollectLiftDoorAnchors(doc);
            DiagnosticRecorder.AppendDebug("[LiftDetect] LiftDoorAnchors=" + anchors.Count.ToString(CultureInfo.InvariantCulture));
            foreach (LiftDoorAnchor anchor in anchors)
            {
                DiagnosticRecorder.AppendDebug("[LiftDetect] Anchor=" + (anchor.SourceName ?? string.Empty) +
                    ", Category=" + (anchor.CategoryName ?? string.Empty) +
                    ", SourceGroup=" + (anchor.SourceGroup ?? "-"));
            }

            HashSet<string> matchedRoomKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LiftDoorAnchor anchor in anchors)
            {
                LiftRoomMatch match = FindBestRoomMatch(anchor, rooms, matchedRoomKeys);
                if (match == null || match.Room == null)
                {
                    continue;
                }

                matchedRoomKeys.Add(match.Room.Key ?? string.Empty);
                result.Add(BuildLiftRecord(doc, summary, match.Room, anchor, match.DistanceMm));
                DiagnosticRecorder.AppendDebug("[LiftDetect] Matched Room=" + (match.Room.RoomName ?? match.Room.Key ?? string.Empty) +
                    ", Area=" + match.Room.AreaM2.ToString("F1", CultureInfo.InvariantCulture) +
                    ", DistanceMm=" + match.DistanceMm.ToString("F0", CultureInfo.InvariantCulture));
            }

            DiagnosticRecorder.AppendDebug("[LiftDetect] LiftCandidates=" + result.Count.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[LiftDetect] Finished");
            return result;
        }

        private static bool IsLiftRoomAreaCandidate(RoomSemanticRecord room)
        {
            return room != null &&
                   room.AreaM2 >= MinLiftRoomAreaM2 &&
                   room.AreaM2 <= MaxLiftRoomAreaM2;
        }

        private static List<LiftDoorAnchor> CollectLiftDoorAnchors(Document doc)
        {
            Dictionary<int, LiftDoorAnchor> anchors = new Dictionary<int, LiftDoorAnchor>();
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                TryAddAnchor(doc, anchors, element, string.Empty);
            }

            foreach (Group group in new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>())
            {
                IList<ElementId> memberIds = group.GetMemberIds();
                foreach (ElementId memberId in memberIds ?? new List<ElementId>())
                {
                    Element member = doc.GetElement(memberId);
                    TryAddAnchor(doc, anchors, member, group.Name ?? ("Group " + group.Id.IntegerValue.ToString(CultureInfo.InvariantCulture)));
                }
            }

            return anchors.Values.OrderBy(x => x.SourceName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void TryAddAnchor(Document doc, Dictionary<int, LiftDoorAnchor> anchors, Element element, string sourceGroup)
        {
            if (doc == null || anchors == null || element == null || anchors.ContainsKey(element.Id.IntegerValue))
            {
                return;
            }

            if (!(element is FamilyInstance))
            {
                return;
            }

            BuiltInCategory category = ToBuiltInCategory(element.Category);
            if (category != BuiltInCategory.OST_Doors &&
                category != BuiltInCategory.OST_GenericModel &&
                category != BuiltInCategory.OST_SpecialityEquipment)
            {
                return;
            }

            List<string> names = CollectSearchNames(doc, element);
            if (!names.Any(IsLiftDoorName))
            {
                return;
            }

            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            string sourceName = names.FirstOrDefault(IsLiftDoorName) ?? element.Name ?? element.Id.IntegerValue.ToString(CultureInfo.InvariantCulture);
            anchors[element.Id.IntegerValue] = new LiftDoorAnchor
            {
                ElementId = element.Id,
                SourceName = sourceName,
                CategoryName = element.Category != null ? (element.Category.Name ?? string.Empty) : string.Empty,
                SourceGroup = sourceGroup ?? string.Empty,
                BBox = box,
                Center = new XYZ((box.Min.X + box.Max.X) * 0.5, (box.Min.Y + box.Max.Y) * 0.5, (box.Min.Z + box.Max.Z) * 0.5),
                DoorSizeDisplay = ResolveDoorSizeDisplay(doc, element, box)
            };
        }

        private static BuiltInCategory ToBuiltInCategory(Category category)
        {
            if (category == null)
            {
                return (BuiltInCategory)0;
            }

            return (BuiltInCategory)category.Id.IntegerValue;
        }

        private static List<string> CollectSearchNames(Document doc, Element element)
        {
            List<string> names = new List<string>();
            AddName(names, element != null ? element.Name : string.Empty);
            AddName(names, element != null && element.Category != null ? element.Category.Name : string.Empty);

            Element type = element != null && element.GetTypeId() != ElementId.InvalidElementId
                ? doc.GetElement(element.GetTypeId())
                : null;
            AddName(names, type != null ? type.Name : string.Empty);

            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null && familyInstance.Symbol != null)
            {
                AddName(names, familyInstance.Symbol.Name);
                AddName(names, familyInstance.Symbol.Family != null ? familyInstance.Symbol.Family.Name : string.Empty);
            }

            return names;
        }

        private static void AddName(List<string> names, string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        private static bool IsLiftDoorName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            return normalized.Contains("door") &&
                   (normalized.Contains("lift") ||
                    normalized.Contains("elevator") ||
                    normalized.Contains("life"));
        }

        private static string ResolveDoorSizeDisplay(Document doc, Element element, BoundingBoxXYZ box)
        {
            double widthMm = ReadLengthParameterMm(element, "Width", "Door Width", "DOOR_WIDTH");
            double heightMm = ReadLengthParameterMm(element, "Height", "Door Height", "DOOR_HEIGHT");

            Element type = doc != null && element != null && element.GetTypeId() != ElementId.InvalidElementId
                ? doc.GetElement(element.GetTypeId())
                : null;
            if (widthMm <= 0.0)
            {
                widthMm = ReadLengthParameterMm(type, "Width", "Door Width", "DOOR_WIDTH");
            }
            if (heightMm <= 0.0)
            {
                heightMm = ReadLengthParameterMm(type, "Height", "Door Height", "DOOR_HEIGHT");
            }

            if (widthMm > 0.0 && heightMm > 0.0)
            {
                DiagnosticRecorder.AppendDebug("[LiftDetect] DoorSize Source=Parameter, WidthMm=" +
                    widthMm.ToString("F0", CultureInfo.InvariantCulture) +
                    ", HeightMm=" + heightMm.ToString("F0", CultureInfo.InvariantCulture));
                return FormatDoorSize(widthMm, heightMm);
            }

            if (box == null || box.Min == null || box.Max == null)
            {
                return "-";
            }

            double dxMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.X - box.Min.X), UnitTypeId.Millimeters);
            double dyMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Y - box.Min.Y), UnitTypeId.Millimeters);
            double dzMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Z - box.Min.Z), UnitTypeId.Millimeters);
            widthMm = Math.Max(dxMm, dyMm);
            heightMm = dzMm > 100.0 ? dzMm : Math.Min(dxMm, dyMm);
            string display = widthMm > 0.0 && heightMm > 0.0 ? FormatDoorSize(widthMm, heightMm) : "-";
            DiagnosticRecorder.AppendDebug("[LiftDetect] DoorSize Source=BoundingBox, DxMm=" +
                dxMm.ToString("F0", CultureInfo.InvariantCulture) +
                ", DyMm=" + dyMm.ToString("F0", CultureInfo.InvariantCulture) +
                ", DzMm=" + dzMm.ToString("F0", CultureInfo.InvariantCulture) +
                ", Display=" + display);
            return display;
        }

        private static double ReadLengthParameterMm(Element element, params string[] names)
        {
            if (element == null)
            {
                return 0.0;
            }

            foreach (string name in names ?? new string[0])
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter == null || !parameter.HasValue)
                {
                    continue;
                }

                if (parameter.StorageType == StorageType.Double)
                {
                    double value = parameter.AsDouble();
                    if (value > 0.0)
                    {
                        return UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters);
                    }
                }
                else if (parameter.StorageType == StorageType.Integer)
                {
                    int value = parameter.AsInteger();
                    if (value > 0)
                    {
                        return value;
                    }
                }
                else if (parameter.StorageType == StorageType.String)
                {
                    double value;
                    if (double.TryParse(parameter.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0.0)
                    {
                        return value;
                    }
                }
            }

            return 0.0;
        }

        private static string FormatDoorSize(double widthMm, double heightMm)
        {
            return widthMm.ToString("F0", CultureInfo.InvariantCulture) +
                   " x " +
                   heightMm.ToString("F0", CultureInfo.InvariantCulture) +
                   " mm";
        }

        private static LiftRoomMatch FindBestRoomMatch(LiftDoorAnchor anchor, List<RoomSemanticRecord> rooms, HashSet<string> matchedRoomKeys)
        {
            LiftRoomMatch best = null;
            foreach (RoomSemanticRecord room in rooms ?? new List<RoomSemanticRecord>())
            {
                if (room == null || string.IsNullOrWhiteSpace(room.Key) || matchedRoomKeys.Contains(room.Key))
                {
                    continue;
                }

                LiftRoomMatch match = Match(anchor, room);
                if (match == null)
                {
                    continue;
                }

                if (best == null || match.Score < best.Score)
                {
                    best = match;
                }
            }

            return best;
        }

        private static LiftRoomMatch Match(LiftDoorAnchor anchor, RoomSemanticRecord room)
        {
            if (anchor == null || room == null || anchor.Center == null)
            {
                return null;
            }

            bool containsCenter = PointInPolygon.ContainsPointXY(room.LoopPoints, anchor.Center);
            bool bboxOverlaps = BBoxOverlapsXY(anchor.BBox, room.BBox);
            double distanceMm = DistancePointToLoopMm(anchor.Center, room.LoopPoints);

            if (!containsCenter && !bboxOverlaps && distanceMm > MaxBoundaryDistanceMm)
            {
                return null;
            }

            double score = containsCenter ? 0.0 : (bboxOverlaps ? 100.0 : distanceMm);
            return new LiftRoomMatch
            {
                Room = room,
                DistanceMm = containsCenter || bboxOverlaps ? 0.0 : distanceMm,
                Score = score
            };
        }

        private static bool BBoxOverlapsXY(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null || a.Min == null || a.Max == null || b.Min == null || b.Max == null)
            {
                return false;
            }

            return a.Min.X <= b.Max.X &&
                   a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y &&
                   a.Max.Y >= b.Min.Y;
        }

        private static double DistancePointToLoopMm(XYZ point, IList<XYZ> loop)
        {
            if (point == null || loop == null || loop.Count < 2)
            {
                return double.MaxValue;
            }

            double bestFeet = double.MaxValue;
            for (int i = 0; i < loop.Count; i++)
            {
                XYZ a = loop[i];
                XYZ b = loop[(i + 1) % loop.Count];
                if (a == null || b == null)
                {
                    continue;
                }

                double d = DistancePointToSegment2D(point, a, b);
                if (d < bestFeet)
                {
                    bestFeet = d;
                }
            }

            return UnitUtils.ConvertFromInternalUnits(bestFeet, UnitTypeId.Millimeters);
        }

        private static double DistancePointToSegment2D(XYZ point, XYZ a, XYZ b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len2 = dx * dx + dy * dy;
            if (len2 <= 1e-12)
            {
                return Math.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));
            }

            double t = ((point.X - a.X) * dx + (point.Y - a.Y) * dy) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double px = a.X + t * dx;
            double py = a.Y + t * dy;
            return Math.Sqrt((point.X - px) * (point.X - px) + (point.Y - py) * (point.Y - py));
        }

        private static LiftRecognitionRecord BuildLiftRecord(
            Document doc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            RoomSemanticRecord room,
            LiftDoorAnchor anchor,
            double distanceMm)
        {
            ElementId levelId = ResolveLevelId(summary, room);
            string levelName = ResolveLevelName(doc, levelId);
            string source = string.IsNullOrWhiteSpace(anchor.SourceName) ? "Lift_Door1" : anchor.SourceName;
            XYZ position = room.Centroid ?? anchor.Center ?? XYZ.Zero;

            return new LiftRecognitionRecord
            {
                Key = "lift_demo_" + (room.Key ?? Guid.NewGuid().ToString("N")),
                LiftName = string.IsNullOrWhiteSpace(room.RoomName) ? "Lift Room" : room.RoomName,
                LiftKind = "Unknown",
                Position = position,
                LevelId = levelId,
                SourceLayer = source,
                RawText = source,
                LiftId = source,
                LiftType = "Unknown",
                Dimension = "Area=" + room.AreaM2.ToString("F1", CultureInfo.InvariantCulture) + " m2, Level=" + levelName,
                DoorSize = string.IsNullOrWhiteSpace(anchor.DoorSizeDisplay) ? "-" : anchor.DoorSizeDisplay,
                Capacity = "-",
                BoundaryPoints = room.LoopPoints != null ? new List<XYZ>(room.LoopPoints) : new List<XYZ>(),
                GeometrySourceLayer = "AnalyzeRoomsPostProcess",
                VirtualDoorWidthMm = 0.0,
                VirtualDoorHeightMm = 2100.0,
                VirtualDoorSillMm = distanceMm
            };
        }

        private static ElementId ResolveLevelId(TargetRoomModelRecognitionService.RecognitionSummary summary, RoomSemanticRecord room)
        {
            if (summary != null && room != null && !string.IsNullOrWhiteSpace(room.Key) &&
                summary.SeedLevelIdByKey.TryGetValue(room.Key, out int levelIdValue) &&
                levelIdValue > 0)
            {
                return new ElementId(levelIdValue);
            }

            return ElementId.InvalidElementId;
        }

        private static string ResolveLevelName(Document doc, ElementId levelId)
        {
            if (doc == null || levelId == null || levelId == ElementId.InvalidElementId)
            {
                return "-";
            }

            Level level = doc.GetElement(levelId) as Level;
            return level != null ? (level.Name ?? "-") : "-";
        }

        private sealed class LiftDoorAnchor
        {
            public ElementId ElementId { get; set; }
            public string SourceName { get; set; }
            public string CategoryName { get; set; }
            public string SourceGroup { get; set; }
            public BoundingBoxXYZ BBox { get; set; }
            public XYZ Center { get; set; }
            public string DoorSizeDisplay { get; set; }
        }

        private sealed class LiftRoomMatch
        {
            public RoomSemanticRecord Room { get; set; }
            public double DistanceMm { get; set; }
            public double Score { get; set; }
        }
    }
}
