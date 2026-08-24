using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Rooms.Manual;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms.Lifts
{
    internal static class ManualLiftRecordBuilder
    {
        internal static LiftRecognitionRecord Build(Document doc, ManualRoomRecord boundaryRecord, string liftName)
        {
            return Build(doc, boundaryRecord, liftName, null, null, 0.0);
        }

        internal static LiftRecognitionRecord Build(
            Document doc,
            ManualRoomBoundaryBuildResult buildResult,
            string liftName)
        {
            if (buildResult == null)
            {
                return null;
            }

            return Build(
                doc,
                buildResult.Record,
                liftName,
                buildResult.UsedVirtualOpening ? buildResult.VirtualOpeningStart : null,
                buildResult.UsedVirtualOpening ? buildResult.VirtualOpeningEnd : null,
                buildResult.UsedVirtualOpening ? buildResult.VirtualOpeningWidthMm : 0.0);
        }

        private static LiftRecognitionRecord Build(
            Document doc,
            ManualRoomRecord boundaryRecord,
            string liftName,
            XYZ virtualDoorStart,
            XYZ virtualDoorEnd,
            double virtualDoorWidthMm)
        {
            if (boundaryRecord == null)
            {
                return null;
            }

            bool hasVirtualDoor =
                virtualDoorStart != null &&
                virtualDoorEnd != null &&
                virtualDoorWidthMm > 0.0;

            return new LiftRecognitionRecord
            {
                Key = "manual_lift_" + Guid.NewGuid().ToString("N"),
                LiftName = string.IsNullOrWhiteSpace(liftName) ? "LIFT 001" : liftName.Trim(),
                LiftKind = "Manual",
                LiftType = "Manual",
                LiftId = string.Empty,
                Position = boundaryRecord.Centroid,
                LevelId = boundaryRecord.LevelIdValue > 0 ? new ElementId(boundaryRecord.LevelIdValue) : ElementId.InvalidElementId,
                SourceLayer = "Manual",
                RawText = hasVirtualDoor ? "Manual Lift - Open Gap" : "Manual Lift",
                GeometrySourceLayer = "Manual",
                BoundaryPoints = ClonePoints(boundaryRecord.LoopPoints),
                Dimension = BuildDimension(boundaryRecord),
                DoorSize = hasVirtualDoor
                    ? FormatMm(virtualDoorWidthMm) + " mm x 2100 mm"
                    : "900 mm x 2100 mm",
                Capacity = "-",
                VirtualDoorStart = ClonePoint(virtualDoorStart),
                VirtualDoorEnd = ClonePoint(virtualDoorEnd),
                VirtualDoorHostWallId = ElementId.InvalidElementId,
                VirtualDoorWidthMm = hasVirtualDoor ? virtualDoorWidthMm : 0.0,
                VirtualDoorHeightMm = 2100.0,
                VirtualDoorSillMm = 0.0
            };
        }

        private static XYZ ClonePoint(XYZ point)
        {
            return point == null ? null : new XYZ(point.X, point.Y, point.Z);
        }

        private static List<XYZ> ClonePoints(IList<XYZ> points)
        {
            return (points ?? new List<XYZ>())
                .Where(p => p != null)
                .Select(p => new XYZ(p.X, p.Y, p.Z))
                .ToList();
        }

        private static string BuildDimension(ManualRoomRecord boundaryRecord)
        {
            BoundingBoxXYZ box = boundaryRecord.BBox;
            if (box == null || box.Min == null || box.Max == null)
            {
                return "-";
            }

            double lengthMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.X - box.Min.X), UnitTypeId.Millimeters);
            double widthMm = UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Y - box.Min.Y), UnitTypeId.Millimeters);
            double heightMm = Math.Max(
                2500.0,
                UnitUtils.ConvertFromInternalUnits(Math.Abs(box.Max.Z - box.Min.Z), UnitTypeId.Millimeters));

            return FormatMm(lengthMm) + " mm x " +
                   FormatMm(widthMm) + " mm x " +
                   FormatMm(heightMm) + " mm";
        }

        private static string FormatMm(double value)
        {
            return Math.Round(value, 0).ToString("F0", CultureInfo.InvariantCulture);
        }
    }
}
