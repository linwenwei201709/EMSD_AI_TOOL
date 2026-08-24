using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Part3
{
    internal static class RvtDoorOpeningPostProcessor
    {
        private const double OpeningToleranceMm = 20.0;
        private const double DefaultDoorHeightMm = 2100.0;
        private const double DefaultDoorWidthMm = 900.0;

        public static void ReplaceDoorsWithWallOpenings(Document doc)
        {
            DiagnosticRecorder.AppendDebug("[RvtDoorOpeningPost] Started");
            if (doc == null)
            {
                DiagnosticRecorder.AppendDebug("[RvtDoorOpeningPost] FailedReason=DocumentNull");
                return;
            }

            List<FamilyInstance> doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(x => x != null)
                .ToList();

            int hostWallDoors = 0;
            int openingsCreated = 0;
            int doorsDeleted = 0;
            int failed = 0;

            using (Transaction tx = new Transaction(doc, "RVT Door Family To Wall Opening"))
            {
                tx.Start();
                foreach (FamilyInstance door in doors)
                {
                    int doorId = door.Id.IntegerValue;
                    Wall hostWall = door.Host as Wall;
                    if (hostWall == null)
                    {
                        continue;
                    }

                    hostWallDoors++;
                    try
                    {
                        if (!TryCreateOpening(doc, door, hostWall, out Opening opening, out string reason))
                        {
                            failed++;
                            DiagnosticRecorder.AppendDebug("[RvtDoorOpeningPost] FailedReason=" + reason + ", DoorId=" + doorId.ToString(CultureInfo.InvariantCulture));
                            continue;
                        }

                        openingsCreated++;
                        doc.Delete(door.Id);
                        doorsDeleted++;
                        DiagnosticRecorder.AppendDebug("[RvtDoorOpeningPost] OpeningCreated DoorId=" +
                            doorId.ToString(CultureInfo.InvariantCulture) +
                            ", OpeningId=" + (opening != null ? opening.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) : "-"));
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        DiagnosticRecorder.AppendDebug("[RvtDoorOpeningPost] FailedReason=" + ex.Message + ", DoorId=" + doorId.ToString(CultureInfo.InvariantCulture));
                    }
                }

                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug("[RvtDoorOpeningPost] DoorFamilies=" + doors.Count.ToString(CultureInfo.InvariantCulture) +
                ", HostWallDoors=" + hostWallDoors.ToString(CultureInfo.InvariantCulture) +
                ", OpeningsCreated=" + openingsCreated.ToString(CultureInfo.InvariantCulture) +
                ", DoorsDeleted=" + doorsDeleted.ToString(CultureInfo.InvariantCulture) +
                ", Failed=" + failed.ToString(CultureInfo.InvariantCulture));
        }

        private static bool TryCreateOpening(Document doc, FamilyInstance door, Wall wall, out Opening opening, out string reason)
        {
            opening = null;
            reason = string.Empty;
            Line wallLine = ResolveWallLine(wall);
            if (wallLine == null)
            {
                reason = "HostWallNotLinear";
                return false;
            }

            XYZ center = ResolveDoorCenter(door);
            if (center == null)
            {
                reason = "DoorCenterNotResolved";
                return false;
            }

            XYZ projected = wallLine.Project(center)?.XYZPoint;
            if (projected == null)
            {
                reason = "DoorCenterProjectionFailed";
                return false;
            }

            double widthMm = Math.Max(100.0, ResolveDoorWidthMm(door) + OpeningToleranceMm);
            double heightMm = Math.Max(100.0, ResolveDoorHeightMm(door) + OpeningToleranceMm);
            double bottomZ = ResolveDoorBottomZ(door, center);
            XYZ dir = wallLine.Direction.Normalize();
            double halfWidthFt = UnitUtils.ConvertToInternalUnits(widthMm * 0.5, UnitTypeId.Millimeters);
            double heightFt = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
            XYZ p1Plan = projected - dir.Multiply(halfWidthFt);
            XYZ p2Plan = projected + dir.Multiply(halfWidthFt);
            XYZ p1 = new XYZ(p1Plan.X, p1Plan.Y, bottomZ);
            XYZ p2 = new XYZ(p2Plan.X, p2Plan.Y, bottomZ + heightFt);
            opening = doc.Create.NewOpening(wall, p1, p2);
            if (opening == null)
            {
                reason = "NewOpeningReturnedNull";
                return false;
            }

            TrySetTextParameter(opening, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "RVT_DoorFamilyConvertedToOpening");
            return true;
        }

        private static Line ResolveWallLine(Wall wall)
        {
            LocationCurve location = wall != null ? wall.Location as LocationCurve : null;
            return location != null ? location.Curve as Line : null;
        }

        private static XYZ ResolveDoorCenter(FamilyInstance door)
        {
            LocationPoint location = door != null ? door.Location as LocationPoint : null;
            if (location != null && location.Point != null)
            {
                return location.Point;
            }

            BoundingBoxXYZ box = door != null ? door.get_BoundingBox(null) : null;
            if (box != null && box.Min != null && box.Max != null)
            {
                return (box.Min + box.Max) * 0.5;
            }

            return null;
        }

        private static double ResolveDoorBottomZ(FamilyInstance door, XYZ center)
        {
            BoundingBoxXYZ box = door != null ? door.get_BoundingBox(null) : null;
            if (box != null && box.Min != null)
            {
                return box.Min.Z;
            }

            return center != null ? center.Z : 0.0;
        }

        private static double ResolveDoorWidthMm(FamilyInstance door)
        {
            double value = ResolveLengthMm(door, new[] { "Width", "Rough Width", "Door Width", "宽度", "寬度" }, 0.0);
            if (value > 0.0)
            {
                return value;
            }

            BoundingBoxXYZ box = door != null ? door.get_BoundingBox(null) : null;
            if (box != null && box.Min != null && box.Max != null)
            {
                double widthFt = Math.Max(Math.Abs(box.Max.X - box.Min.X), Math.Abs(box.Max.Y - box.Min.Y));
                return UnitUtils.ConvertFromInternalUnits(widthFt, UnitTypeId.Millimeters);
            }

            return DefaultDoorWidthMm;
        }

        private static double ResolveDoorHeightMm(FamilyInstance door)
        {
            double value = ResolveLengthMm(door, new[] { "Height", "Rough Height", "Door Height", "高度" }, 0.0);
            if (value > 0.0)
            {
                return value;
            }

            BoundingBoxXYZ box = door != null ? door.get_BoundingBox(null) : null;
            if (box != null && box.Min != null && box.Max != null)
            {
                double heightMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Z - box.Min.Z), UnitTypeId.Millimeters);
                if (heightMm > 0.0)
                {
                    return heightMm;
                }
            }

            return DefaultDoorHeightMm;
        }

        private static double ResolveLengthMm(FamilyInstance door, IEnumerable<string> names, double fallbackMm)
        {
            Parameter parameter = FindLengthParameter(door, names);
            if (parameter != null)
            {
                return UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters);
            }

            FamilySymbol symbol = door != null ? door.Symbol : null;
            parameter = FindLengthParameter(symbol, names);
            return parameter != null ? UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters) : fallbackMm;
        }

        private static Parameter FindLengthParameter(Element element, IEnumerable<string> names)
        {
            if (element == null)
            {
                return null;
            }

            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                Parameter parameter = element.LookupParameter(name);
                if (parameter != null && parameter.StorageType == StorageType.Double && parameter.AsDouble() > 0.0)
                {
                    return parameter;
                }
            }

            return null;
        }

        private static void TrySetTextParameter(Element element, BuiltInParameter parameterId, string value)
        {
            try
            {
                Parameter parameter = element != null ? element.get_Parameter(parameterId) : null;
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                }
            }
            catch
            {
            }
        }
    }
}
