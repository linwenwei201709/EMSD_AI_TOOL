using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomFlexDuctService
    {
        internal sealed class DuctWallPickResult
        {
            public bool Succeeded { get; set; }

            public bool Canceled { get; set; }

            public string Message { get; set; }

            public ElementId WallElementId { get; set; } = ElementId.InvalidElementId;

            public XYZ PickPoint { get; set; }

            public string DisplayName { get; set; }
        }

        internal sealed class CreateFlexDuctResult
        {
            public bool Succeeded { get; set; }

            public string Message { get; set; }

            public ElementId FlexDuctElementId { get; set; } = ElementId.InvalidElementId;
        }

        internal static DuctWallPickResult PickWallPoint(UIDocument uiDoc)
        {
            DuctWallPickResult result = new DuctWallPickResult();
            if (uiDoc == null || uiDoc.Document == null)
            {
                result.Message = "Active document is not available.";
                return result;
            }

            try
            {
                Reference pickedReference = uiDoc.Selection.PickObject(
                    ObjectType.PointOnElement,
                    new WallPointSelectionFilter(),
                    "Pick a point on a wall for the flex duct end.");

                if (pickedReference == null)
                {
                    result.Message = "No wall point was selected.";
                    return result;
                }

                Element wall = uiDoc.Document.GetElement(pickedReference.ElementId);
                if (!(wall is Wall))
                {
                    result.Message = "The selected element is not a wall.";
                    return result;
                }

                result.Succeeded = true;
                result.WallElementId = wall.Id;
                result.PickPoint = pickedReference.GlobalPoint;
                result.DisplayName = BuildWallDisplayName(wall);
                result.Message = "Success";
                return result;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                result.Canceled = true;
                result.Message = "Wall point selection canceled.";
                return result;
            }
        }

        internal static CreateFlexDuctResult CreateFlexDuct(
            Document doc,
            ElementId equipmentInstanceId,
            ElementId wallElementId,
            XYZ wallPoint,
            string sizeText)
        {
            CreateFlexDuctResult result = new CreateFlexDuctResult();
            if (doc == null)
            {
                result.Message = "Document is null.";
                return result;
            }

            FamilyInstance equipment = doc.GetElement(equipmentInstanceId) as FamilyInstance;
            if (equipment == null)
            {
                result.Message = "Current equipment instance was not found.";
                return result;
            }

            Element wall = doc.GetElement(wallElementId);
            if (!(wall is Wall))
            {
                result.Message = "Selected wall was not found.";
                return result;
            }

            if (wallPoint == null)
            {
                result.Message = "Wall point is missing.";
                return result;
            }

            FlexDuctType flexDuctType = ResolveFlexDuctType(doc);
            if (flexDuctType == null)
            {
                result.Message = "No flex duct type is available in the current model.";
                return result;
            }

            MechanicalSystemType systemType = ResolveMechanicalSystemType(doc);
            if (systemType == null)
            {
                result.Message = "No mechanical system type is available in the current model.";
                return result;
            }

            ElementId levelId = ResolveLevelId(doc, equipment, wall);
            if (levelId == ElementId.InvalidElementId)
            {
                result.Message = "No level is available for flex duct creation.";
                return result;
            }

            Connector equipmentConnector = FindPrimaryDuctConnector(equipment);
            XYZ startPoint = equipmentConnector != null
                ? equipmentConnector.Origin
                : ResolveEquipmentReferencePoint(equipment, wallPoint);
            XYZ endPoint = ProjectPointToWallFace(wall as Wall, wallPoint) ?? wallPoint;
            if (startPoint == null)
            {
                result.Message = "Flex duct start point is missing.";
                return result;
            }

            if (endPoint == null)
            {
                result.Message = "Flex duct end point is missing.";
                return result;
            }

            double minDistance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
            if (startPoint.DistanceTo(endPoint) <= minDistance)
            {
                result.Message = "Flex duct path is too short.";
                return result;
            }

            XYZ controlPoint = BuildControlPoint(startPoint, endPoint);
            List<XYZ> pathPoints = BuildPathPoints(startPoint, endPoint);
            List<XYZ> distinctPathPoints = DistinctPathPoints(pathPoints, minDistance);
            LogPathDebug(startPoint, endPoint, controlPoint, pathPoints);
            if (distinctPathPoints.Count < 2)
            {
                result.Message = "Flex duct path has fewer than two valid distinct points.";
                return result;
            }

            double? diameterFeet = ParsePrimarySizeFeet(sizeText);

            using (Transaction tx = new Transaction(doc, "Create Room Flex Duct"))
            {
                tx.Start();
                try
                {
                    FlexDuct flexDuct = FlexDuct.Create(
                        doc,
                        systemType.Id,
                        flexDuctType.Id,
                        levelId,
                        distinctPathPoints);
                    if (flexDuct == null)
                    {
                        throw new System.InvalidOperationException("FlexDuct.Create returned null.");
                    }

                    if (diameterFeet.HasValue && diameterFeet.Value > 0.0)
                    {
                        Parameter diameter = flexDuct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
                        if (diameter != null && !diameter.IsReadOnly)
                        {
                            diameter.Set(diameterFeet.Value);
                        }
                    }

                    if (equipmentConnector != null)
                    {
                        Connector flexConnector = FindNearestConnector(flexDuct.ConnectorManager, equipmentConnector.Origin);
                        if (flexConnector != null && !flexConnector.IsConnected)
                        {
                            equipmentConnector.ConnectTo(flexConnector);
                        }
                    }

                    result.Succeeded = true;
                    result.FlexDuctElementId = flexDuct.Id;
                    result.Message = "Success";
                    tx.Commit();
                }
                catch (Exception ex)
                {
                    if (tx.HasStarted())
                    {
                        tx.RollBack();
                    }

                    result.Message = ex.Message;
                }
            }

            return result;
        }

        private static string BuildWallDisplayName(Element wall)
        {
            if (wall == null)
            {
                return "No wall selected";
            }

            string name = wall.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "Wall";
            }

            return name + " (" + wall.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private static FlexDuctType ResolveFlexDuctType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FlexDuctType))
                .Cast<FlexDuctType>()
                .FirstOrDefault();
        }

        private static MechanicalSystemType ResolveMechanicalSystemType(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystemType))
                .Cast<MechanicalSystemType>()
                .OrderByDescending(x => IsPreferredSystemName(x != null ? x.Name : string.Empty))
                .ThenBy(x => x != null ? x.Name : string.Empty)
                .FirstOrDefault();
        }

        private static int IsPreferredSystemName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return 0;
            }

            string normalized = name.ToLowerInvariant();
            return normalized.Contains("supply") || normalized.Contains("air") ? 1 : 0;
        }

        private static Connector FindPrimaryDuctConnector(FamilyInstance equipment)
        {
            return GetEquipmentDuctConnectors(equipment)
                .OrderByDescending(x => x.Origin != null ? x.Origin.Z : 0.0)
                .FirstOrDefault();
        }

        private static IEnumerable<Connector> GetEquipmentDuctConnectors(FamilyInstance equipment)
        {
            ConnectorSet connectorSet = equipment?.MEPModel?.ConnectorManager?.Connectors;
            if (connectorSet == null)
            {
                return Enumerable.Empty<Connector>();
            }

            return connectorSet
                .Cast<Connector>()
                .Where(x => x != null && x.Domain == Domain.DomainHvac)
                .ToList();
        }

        private static ElementId ResolveLevelId(Document doc, FamilyInstance equipment, Element wall)
        {
            if (equipment != null && equipment.LevelId != null && equipment.LevelId != ElementId.InvalidElementId)
            {
                return equipment.LevelId;
            }

            if (wall != null && wall.LevelId != null && wall.LevelId != ElementId.InvalidElementId)
            {
                return wall.LevelId;
            }

            Level viewLevel = doc.ActiveView != null ? doc.ActiveView.GenLevel : null;
            if (viewLevel != null)
            {
                return viewLevel.Id;
            }

            Level firstLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
            return firstLevel != null ? firstLevel.Id : ElementId.InvalidElementId;
        }

        private static XYZ ResolveEquipmentReferencePoint(FamilyInstance equipment, XYZ wallPoint)
        {
            BoundingBoxXYZ box = equipment != null ? equipment.get_BoundingBox(null) : null;
            if (box == null)
            {
                LocationPoint point = equipment != null ? equipment.Location as LocationPoint : null;
                return point != null ? point.Point : null;
            }

            XYZ center = (box.Min + box.Max) * 0.5;
            double topOffset = UnitUtils.ConvertToInternalUnits(150.0, UnitTypeId.Millimeters);
            XYZ elevated = new XYZ(center.X, center.Y, box.Max.Z + topOffset);
            if (wallPoint == null)
            {
                return elevated;
            }

            XYZ planarDirection = new XYZ(wallPoint.X - elevated.X, wallPoint.Y - elevated.Y, 0.0);
            if (planarDirection.GetLength() < 1e-6)
            {
                return elevated;
            }

            XYZ nudged = elevated + planarDirection.Normalize() * UnitUtils.ConvertToInternalUnits(120.0, UnitTypeId.Millimeters);
            return new XYZ(nudged.X, nudged.Y, elevated.Z);
        }

        private static XYZ ProjectPointToWallFace(Wall wall, XYZ point)
        {
            LocationCurve locationCurve = wall != null ? wall.Location as LocationCurve : null;
            Curve curve = locationCurve != null ? locationCurve.Curve : null;
            if (curve == null || point == null)
            {
                return point;
            }

            IntersectionResult projection = curve.Project(point);
            if (projection == null || projection.XYZPoint == null)
            {
                return point;
            }

            XYZ projected = projection.XYZPoint;
            return new XYZ(projected.X, projected.Y, point.Z);
        }

        private static XYZ BuildControlPoint(XYZ startPoint, XYZ endPoint)
        {
            XYZ midPoint = (startPoint + endPoint) * 0.5;
            double archOffset = Math.Max(
                UnitUtils.ConvertToInternalUnits(300.0, UnitTypeId.Millimeters),
                Math.Abs(endPoint.Z - startPoint.Z) * 0.5);
            return new XYZ(midPoint.X, midPoint.Y, Math.Max(startPoint.Z, endPoint.Z) + archOffset);
        }

        private static List<XYZ> BuildPathPoints(XYZ startPoint, XYZ endPoint)
        {
            XYZ controlPoint = BuildControlPoint(startPoint, endPoint);
            return new List<XYZ> { startPoint, controlPoint, endPoint };
        }

        private static List<XYZ> DistinctPathPoints(IEnumerable<XYZ> points, double tolerance)
        {
            List<XYZ> distinctPoints = new List<XYZ>();
            foreach (XYZ point in points ?? Enumerable.Empty<XYZ>())
            {
                if (point == null)
                {
                    continue;
                }

                if (!distinctPoints.Any(existing => existing.DistanceTo(point) <= tolerance))
                {
                    distinctPoints.Add(point);
                }
            }

            return distinctPoints;
        }

        private static void LogPathDebug(XYZ startPoint, XYZ endPoint, XYZ controlPoint, IList<XYZ> pathPoints)
        {
            DiagnosticRecorder.AppendDebug("[FlexDuct] StartPoint=" + FormatPoint(startPoint));
            DiagnosticRecorder.AppendDebug("[FlexDuct] EndPoint=" + FormatPoint(endPoint));
            DiagnosticRecorder.AppendDebug("[FlexDuct] ControlPoint=" + FormatPoint(controlPoint));
            DiagnosticRecorder.AppendDebug("[FlexDuct] PathPointCount=" + (pathPoints != null ? pathPoints.Count.ToString(CultureInfo.InvariantCulture) : "0"));

            int index = 0;
            foreach (XYZ point in pathPoints ?? Array.Empty<XYZ>())
            {
                DiagnosticRecorder.AppendDebug("[FlexDuct] PathPoint[" + index.ToString(CultureInfo.InvariantCulture) + "]=" + FormatPoint(point));
                index++;
            }
        }

        private static string FormatPoint(XYZ point)
        {
            if (point == null)
            {
                return "(null)";
            }

            return "(" +
                point.X.ToString("F6", CultureInfo.InvariantCulture) + ", " +
                point.Y.ToString("F6", CultureInfo.InvariantCulture) + ", " +
                point.Z.ToString("F6", CultureInfo.InvariantCulture) + ")";
        }

        private static double? ParsePrimarySizeFeet(string sizeText)
        {
            if (string.IsNullOrWhiteSpace(sizeText))
            {
                return null;
            }

            Match match = Regex.Match(sizeText, @"[-+]?\d+(\.\d+)?");
            if (!match.Success)
            {
                return null;
            }

            if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double sizeMm) &&
                !double.TryParse(match.Value, NumberStyles.Float, CultureInfo.CurrentCulture, out sizeMm))
            {
                return null;
            }

            if (sizeMm <= 0.0)
            {
                return null;
            }

            return UnitUtils.ConvertToInternalUnits(sizeMm, UnitTypeId.Millimeters);
        }

        private static Connector FindNearestConnector(ConnectorManager connectorManager, XYZ referencePoint)
        {
            ConnectorSet connectorSet = connectorManager != null ? connectorManager.Connectors : null;
            if (connectorSet == null || referencePoint == null)
            {
                return null;
            }

            return connectorSet
                .Cast<Connector>()
                .Where(x => x != null && x.Origin != null)
                .OrderBy(x => x.Origin.DistanceTo(referencePoint))
                .FirstOrDefault();
        }

        private sealed class WallPointSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is Wall;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
