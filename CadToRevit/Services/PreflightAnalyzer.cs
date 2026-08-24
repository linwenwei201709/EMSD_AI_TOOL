using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public enum WorkloadLevel
    {
        Low,
        Medium,
        High,
        Extreme
    }

    public sealed class PreflightReport
    {
        public int RawSegmentCount { get; set; }

        public int AfterMinLengthCount { get; set; }

        public double MinLengthMm { get; set; }

        public double P50LengthMm { get; set; }

        public double P90LengthMm { get; set; }

        public double MaxLengthMm { get; set; }

        public double ExtentWidthMm { get; set; }

        public double ExtentHeightMm { get; set; }

        public int EstimatedWallCount { get; set; }

        public WorkloadLevel Workload { get; set; }

        public bool ExceedsPreview { get; set; }

        public bool ExceedsHardStop { get; set; }

        public bool ExceedsEstimatedWalls { get; set; }
    }

    public static class PreflightAnalyzer
    {
        private const double FtToMm = 304.8;

        public static PreflightReport Analyze(
            CadDataset dataset,
            ISet<string> selectedRawLayers,
            double minLengthMm,
            int maxSegmentsPreview,
            int maxSegmentsHardStop,
            int maxEstimatedWalls)
        {
            PreflightReport report = new PreflightReport();
            List<CadSegment> source = (dataset == null ? new List<CadSegment>() : dataset.Segments ?? new List<CadSegment>())
                .Where(x => x != null && !x.IsArc)
                .Where(x => selectedRawLayers == null || selectedRawLayers.Count == 0 ||
                            (!string.IsNullOrWhiteSpace(x.RawLayerName) && selectedRawLayers.Contains(x.RawLayerName)))
                .ToList();

            report.RawSegmentCount = source.Count;
            report.MinLengthMm = minLengthMm;

            List<double> lengthsMm = source
                .Where(x => x.P0 != null && x.P1 != null)
                .Select(x => x.P0.DistanceTo(x.P1) * FtToMm)
                .OrderBy(x => x)
                .ToList();
            List<double> keptMm = lengthsMm.Where(x => x >= minLengthMm).ToList();
            report.AfterMinLengthCount = keptMm.Count;
            report.MinLengthMm = Percentile(lengthsMm, 0.0);
            report.P50LengthMm = Percentile(lengthsMm, 0.5);
            report.P90LengthMm = Percentile(lengthsMm, 0.9);
            report.MaxLengthMm = Percentile(lengthsMm, 1.0);
            report.EstimatedWallCount = EstimateWallCount(report.AfterMinLengthCount, report.P50LengthMm, report.P90LengthMm);

            ComputeExtents(source, out double widthMm, out double heightMm);
            report.ExtentWidthMm = widthMm;
            report.ExtentHeightMm = heightMm;

            report.ExceedsPreview = report.RawSegmentCount > maxSegmentsPreview;
            report.ExceedsHardStop = report.RawSegmentCount > maxSegmentsHardStop;
            report.ExceedsEstimatedWalls = report.EstimatedWallCount > maxEstimatedWalls;
            report.Workload = ResolveWorkload(report, maxSegmentsPreview, maxSegmentsHardStop, maxEstimatedWalls);
            return report;
        }

        private static int EstimateWallCount(int filteredSegments, double p50LengthMm, double p90LengthMm)
        {
            if (filteredSegments <= 0)
            {
                return 0;
            }

            double densityFactor = p50LengthMm < 400 ? 0.45 : (p90LengthMm < 2000 ? 0.35 : 0.25);
            return Math.Max(1, (int)Math.Round(filteredSegments * densityFactor));
        }

        private static WorkloadLevel ResolveWorkload(
            PreflightReport report,
            int maxSegmentsPreview,
            int maxSegmentsHardStop,
            int maxEstimatedWalls)
        {
            if (report.ExceedsHardStop || report.ExceedsEstimatedWalls)
            {
                return WorkloadLevel.Extreme;
            }

            if (report.RawSegmentCount > maxSegmentsPreview || report.EstimatedWallCount > (int)(maxEstimatedWalls * 0.5))
            {
                return WorkloadLevel.High;
            }

            if (report.RawSegmentCount > (int)(maxSegmentsPreview * 0.4))
            {
                return WorkloadLevel.Medium;
            }

            return WorkloadLevel.Low;
        }

        private static void ComputeExtents(List<CadSegment> source, out double widthMm, out double heightMm)
        {
            widthMm = 0.0;
            heightMm = 0.0;
            if (source == null || source.Count == 0)
            {
                return;
            }

            List<XYZ> points = source
                .SelectMany(x => new[] { x.P0, x.P1 })
                .Where(x => x != null)
                .ToList();
            if (points.Count == 0)
            {
                return;
            }

            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);
            widthMm = (maxX - minX) * FtToMm;
            heightMm = (maxY - minY) * FtToMm;
        }

        private static double Percentile(List<double> values, double ratio)
        {
            if (values == null || values.Count == 0)
            {
                return 0.0;
            }

            if (ratio <= 0)
            {
                return values.First();
            }

            if (ratio >= 1)
            {
                return values.Last();
            }

            int index = (int)Math.Round((values.Count - 1) * ratio);
            index = Math.Max(0, Math.Min(values.Count - 1, index));
            return values[index];
        }
    }
}
