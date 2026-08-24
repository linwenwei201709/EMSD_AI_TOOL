using CadToRevit.Models.Rooms.LayoutPlans;
using System;
using System.Collections.Generic;

namespace CadToRevit.Services.Rooms.LayoutPlanReports
{
    internal sealed class LayoutPlanReportData
    {
        public List<LayoutPlanReportScheduleRow> ScheduleRows { get; set; } =
            new List<LayoutPlanReportScheduleRow>();

        public List<LayoutPlanReportInfoRow> InformationRows { get; set; } =
            new List<LayoutPlanReportInfoRow>();
    }

    internal sealed class LayoutPlanReportScheduleRow
    {
        public string System { get; set; }

        public string Size { get; set; }

        public string Target { get; set; }
    }

    internal sealed class LayoutPlanReportInfoRow
    {
        public string Label { get; set; }

        public string Value { get; set; }
    }

    internal sealed class LayoutPlanReportImageExportResult
    {
        public string Main3DImagePath { get; set; }

        public string KeyPlanImagePath { get; set; }

        public string OverallTopViewImagePath { get; set; }
    }

    internal sealed class LayoutPlanReportPdfContext
    {
        public RoomLayoutPlanDto Plan { get; set; }

        public LayoutPlanReportData ReportData { get; set; }

        public string Main3DImagePath { get; set; }

        public string KeyPlanImagePath { get; set; }

        public string OverallTopViewImagePath { get; set; }
    }

    public sealed class LayoutPlanReportPdfExportResult
    {
        public string PdfPath { get; set; }

        public string FileName { get; set; }

        public DateTime GeneratedAt { get; set; }
    }
}
