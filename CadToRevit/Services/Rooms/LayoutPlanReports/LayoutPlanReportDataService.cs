using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Services.Rooms.LayoutPlans;
using System.Collections.Generic;
using System.Globalization;

namespace CadToRevit.Services.Rooms.LayoutPlanReports
{
    internal static class LayoutPlanReportDataService
    {
        internal static LayoutPlanReportData Build(RoomLayoutPlanDto plan)
        {
            LayoutPlanReportData data = new LayoutPlanReportData();
            data.ScheduleRows.Add(BuildScheduleRow("SAD", plan != null ? plan.SadSize : null, plan != null ? plan.SadWall : null));
            data.ScheduleRows.Add(BuildScheduleRow("RAD", plan != null ? plan.RadSize : null, plan != null ? plan.RadWall : null));
            data.ScheduleRows.Add(BuildScheduleRow("CHWS", plan != null ? plan.ChwsPipeSize : null, plan != null ? plan.ChwsWall : null));
            data.ScheduleRows.Add(BuildScheduleRow("CHWR", plan != null ? plan.ChwrPipeSize : null, plan != null ? plan.ChwrWall : null));

            data.InformationRows = BuildInformationRows(plan);
            return data;
        }

        private static LayoutPlanReportScheduleRow BuildScheduleRow(string system, string size, LayoutWallSelectionDto wall)
        {
            return new LayoutPlanReportScheduleRow
            {
                System = system,
                Size = Normalize(size),
                Target = Normalize(wall != null ? wall.DisplayName : null)
            };
        }

        private static List<LayoutPlanReportInfoRow> BuildInformationRows(RoomLayoutPlanDto plan)
        {
            List<LayoutPlanReportInfoRow> rows = new List<LayoutPlanReportInfoRow>();
            rows.Add(Row("Target Room", plan != null ? plan.RoomName : null));
            rows.Add(Row("Room Dimensions", FormatDimensions(plan != null ? plan.RoomLengthMm : 0, plan != null ? plan.RoomWidthMm : 0, plan != null ? plan.RoomHeightMm : 0)));
            rows.Add(Row("Door Dimensions", FormatDoor(plan != null ? plan.DoorWidthMm : 0, plan != null ? plan.DoorHeightMm : 0)));
            rows.Add(Row("Equipment Model", plan != null ? plan.EquipmentDisplayName : null));
            rows.Add(Row("Equip. Dimensions", FormatDimensions(plan != null ? plan.EquipmentLengthMm : 0, plan != null ? plan.EquipmentWidthMm : 0, plan != null ? plan.EquipmentHeightMm : 0)));
            rows.Add(Row("Airflow Rate", plan != null ? plan.FlowRate : null));
            rows.Add(Row("Weight", FormatNumber(plan != null ? plan.EquipmentWeightKg : 0, "kg")));
            rows.Add(Row("Required Maintenance Space", FormatMaintenance(plan)));
            rows.Add(Row("Clearance Check", FormatClearance(plan != null ? plan.EquipmentValidation : null)));
            return rows;
        }

        private static LayoutPlanReportInfoRow Row(string label, string value)
        {
            return new LayoutPlanReportInfoRow
            {
                Label = label,
                Value = Normalize(value)
            };
        }

        private static string FormatDimensions(double length, double width, double height)
        {
            if (length <= 0 || width <= 0 || height <= 0)
            {
                return "-";
            }

            return "L:" + Mm(length) + " x W:" + Mm(width) + " x H:" + Mm(height);
        }

        private static string FormatDoor(double width, double height)
        {
            if (width <= 0 || height <= 0)
            {
                return "-";
            }

            return "W:" + Mm(width) + " x H:" + Mm(height);
        }

        private static string FormatMaintenance(RoomLayoutPlanDto plan)
        {
            if (plan == null || plan.RequiredMaintenanceSpaceMm <= 0)
            {
                return "-";
            }

            string side = string.IsNullOrWhiteSpace(plan.RequiredMaintenanceSpaceSide)
                ? string.Empty
                : " (" + plan.RequiredMaintenanceSpaceSide + ")";
            return Mm(plan.RequiredMaintenanceSpaceMm) + side;
        }

        private static string FormatClearance(EquipmentPlacementValidationDto validation)
        {
            if (validation == null || !validation.HasResult)
            {
                return "-";
            }

            return validation.IsValid ? "Passed" : "Failed";
        }

        private static string FormatNumber(double value, string unit)
        {
            if (value <= 0)
            {
                return "-";
            }

            return value.ToString("F0", CultureInfo.InvariantCulture) + " " + unit;
        }

        private static string Mm(double value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture) + " mm";
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
