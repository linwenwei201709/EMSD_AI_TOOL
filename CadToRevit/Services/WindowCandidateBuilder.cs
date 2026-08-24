using Autodesk.Revit.DB;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class WindowCandidateBuilder
    {
        public static List<WindowCandidate> Build(
            Document doc,
            ImportInstance importInstance,
            WindowCreateSettings settings)
        {
            WindowCreateSettings effective = settings ?? new WindowCreateSettings();
            HashSet<string> filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WINDOW" };
            CadSegmentBuildResult build = CadSegmentBuilder.BuildSegments(doc, importInstance, filter);
            List<CadSegment> segments = build.Segments
                .Where(x => string.Equals(x.SemanticLayer, "WINDOW", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return BuildFromSegments(segments, effective);
        }

        public static List<WindowCandidate> BuildByRawLayer(
            List<CadSegment> segments,
            string rawLayerName,
            WindowCreateSettings settings)
        {
            WindowCreateSettings effective = settings ?? new WindowCreateSettings();
            List<CadSegment> filtered = (segments ?? new List<CadSegment>())
                .Where(x => x != null &&
                            !x.IsArc &&
                            !string.IsNullOrWhiteSpace(x.RawLayerName) &&
                            string.Equals(x.RawLayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return BuildFromSegments(filtered, effective);
        }

        private static List<WindowCandidate> BuildFromSegments(
            List<CadSegment> segments,
            WindowCreateSettings effective)
        {
            List<WindowCandidate> raw = new List<WindowCandidate>();
            foreach (CadSegment segment in segments ?? new List<CadSegment>())
            {
                double lengthMm = UnitUtils.ConvertFromInternalUnits(segment.P0.DistanceTo(segment.P1), UnitTypeId.Millimeters);
                if (lengthMm < effective.TinySegmentTolMm)
                {
                    continue;
                }

                if (lengthMm < effective.MinWindowWidthMm || lengthMm > effective.MaxWindowWidthMm)
                {
                    continue;
                }

                XYZ dir = Normalize(segment.P1 - segment.P0);
                XYZ center = Mid(segment.P0, segment.P1);
                raw.Add(new WindowCandidate
                {
                    CenterPoint = center,
                    Dir = dir,
                    WidthMm = lengthMm,
                    RuleId = "Rw2",
                    SegmentIds = new List<int> { segment.SegmentId }
                });
            }

            return Merge(raw, effective);
        }

        private static List<WindowCandidate> Merge(List<WindowCandidate> input, WindowCreateSettings settings)
        {
            List<WindowCandidate> merged = new List<WindowCandidate>();
            double centerTolFeet = UnitUtils.ConvertToInternalUnits(settings.MergeTolMm, UnitTypeId.Millimeters);
            double cosTol = Math.Cos(settings.AngleTolDeg * Math.PI / 180.0);
            foreach (WindowCandidate c in input)
            {
                WindowCandidate existing = merged.FirstOrDefault(x =>
                    x.CenterPoint.DistanceTo(c.CenterPoint) <= centerTolFeet &&
                    Math.Abs(Dot(x.Dir, c.Dir)) >= cosTol);
                if (existing == null)
                {
                    merged.Add(c);
                    continue;
                }

                existing.WidthMm = Math.Max(existing.WidthMm, c.WidthMm);
                existing.SegmentIds = existing.SegmentIds.Union(c.SegmentIds).ToList();
            }

            int id = 1;
            foreach (WindowCandidate c in merged)
            {
                c.CandidateId = id++;
            }

            return merged;
        }

        private static XYZ Mid(XYZ a, XYZ b)
        {
            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static XYZ Normalize(XYZ v)
        {
            double len = v.GetLength();
            if (len < 1e-9)
            {
                return new XYZ(1, 0, 0);
            }

            return new XYZ(v.X / len, v.Y / len, v.Z / len);
        }

        private static double Dot(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }
    }
}
