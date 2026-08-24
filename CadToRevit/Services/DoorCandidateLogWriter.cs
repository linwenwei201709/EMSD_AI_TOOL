using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CadToRevit.Services
{
    public static class DoorCandidateLogWriter
    {
        public static void Write(DoorDetectResult result)
        {
            if (result == null)
            {
                return;
            }

            string root = DiagnosticRecorder.GetLogDirectory();
            Directory.CreateDirectory(root);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string jsonPath = Path.Combine(root, "door_candidates_" + stamp + ".json");
            string csvPath = Path.Combine(root, "door_candidates_" + stamp + ".csv");

            File.WriteAllText(jsonPath, BuildJson(result), Encoding.UTF8);
            File.WriteAllText(csvPath, BuildCsv(result), Encoding.UTF8);

            result.JsonLogPath = jsonPath;
            result.CsvLogPath = csvPath;
        }

        private static string BuildJson(DoorDetectResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"summary\": {");
            sb.AppendLine("    \"doorSegmentsTotal\": " + result.DoorSegmentsTotal + ",");
            sb.AppendLine("    \"arcSegmentsTotal\": " + result.ArcSegmentsTotal + ",");
            sb.AppendLine("    \"arcCountOnDoorLayer\": " + result.ArcCountOnDoorLayer + ",");
            sb.AppendLine("    \"enabledRules\": [" + string.Join(", ", (result.EnabledRules ?? Enumerable.Empty<string>()).Select(x => "\"" + Escape(x) + "\"")) + "],");
            sb.AppendLine("    \"rule1Count\": " + result.Rule1Count + ",");
            sb.AppendLine("    \"rule2Count\": " + result.Rule2Count + ",");
            sb.AppendLine("    \"rule3Count\": " + result.Rule3Count + ",");
            sb.AppendLine("    \"mergedCandidateCount\": " + result.MergedCandidateCount + ",");
            sb.AppendLine("    \"matchedCount\": " + result.MatchedCount + ",");
            sb.AppendLine("    \"unmatchedCount\": " + result.UnmatchedCount);
            sb.AppendLine("  },");
            sb.AppendLine("  \"candidates\": [");

            for (int i = 0; i < result.Candidates.Count; i++)
            {
                DoorCandidate c = result.Candidates[i];
                sb.AppendLine("    {");
                sb.AppendLine("      \"candidateId\": " + c.CandidateId + ",");
                sb.AppendLine("      \"ruleSource\": \"" + Escape(c.RuleSource) + "\",");
                sb.AppendLine("      \"symbolFamilyKind\": \"" + c.SymbolFamilyKind + "\",");
                sb.AppendLine("      \"widthMm\": " + c.WidthMm.ToString("F3") + ",");
                sb.AppendLine("      \"arcRadiusMm\": " + c.ArcRadiusMm.ToString("F3") + ",");
                sb.AppendLine("      \"arcSweepDeg\": " + c.ArcSweepDeg.ToString("F3") + ",");
                sb.AppendLine("      \"distToWallMm\": " + c.DistToWallMm.ToString("F3") + ",");
                sb.AppendLine("      \"matchedWallId\": " + (c.MatchedWallId == null ? "null" : c.MatchedWallId.IntegerValue.ToString()) + ",");
                sb.AppendLine("      \"center\": \"" + Escape(FormatPointMm(c.CenterPoint)) + "\",");
                sb.AppendLine("      \"hinge\": \"" + Escape(FormatPointMm(c.HingePoint)) + "\",");
                sb.AppendLine("      \"arcMid\": \"" + Escape(FormatPointMm(c.ArcMidPoint)) + "\",");
                sb.AppendLine("      \"wallDirHint\": \"" + Escape(FormatVector(c.WallDirHint)) + "\",");
                sb.AppendLine("      \"leafHinge\": \"" + Escape(FormatPointMm(c.LeafHinge)) + "\",");
                sb.AppendLine("      \"leafLatch\": \"" + Escape(FormatPointMm(c.LeafLatch)) + "\",");
                sb.AppendLine("      \"doorLeafBaseStart\": \"" + Escape(FormatPointMm(c.DoorLeafBaseStart)) + "\",");
                sb.AppendLine("      \"doorLeafBaseEnd\": \"" + Escape(FormatPointMm(c.DoorLeafBaseEnd)) + "\",");
                sb.AppendLine("      \"doorLeafBaseCenter\": \"" + Escape(FormatPointMm(c.DoorLeafBaseCenter)) + "\",");
                sb.AppendLine("      \"widthSource\": \"" + Escape(c.WidthSource) + "\",");
                sb.AppendLine("      \"doorKind\": \"" + (c.IsDoubleDoor ? "Double" : "Single") + "\",");
                sb.AppendLine("      \"leftEdge\": \"" + Escape(FormatPointMm(c.LeftEdgePoint)) + "\",");
                sb.AppendLine("      \"rightEdge\": \"" + Escape(FormatPointMm(c.RightEdgePoint)) + "\",");
                sb.AppendLine("      \"openingWidthMm\": " + c.OpeningWidthMm.ToString("F3") + ",");
                sb.AppendLine("      \"openingCenter\": \"" + Escape(FormatPointMm(c.OpeningCenterPoint)) + "\",");
                sb.AppendLine("      \"virtualOpeningBaseStart\": \"" + Escape(FormatPointMm(c.VirtualOpeningBaseStart)) + "\",");
                sb.AppendLine("      \"virtualOpeningBaseEnd\": \"" + Escape(FormatPointMm(c.VirtualOpeningBaseEnd)) + "\",");
                sb.AppendLine("      \"virtualOpeningBaseCenter\": \"" + Escape(FormatPointMm(c.VirtualOpeningBaseCenter)) + "\",");
                sb.AppendLine("      \"virtualOpeningWidthMm\": " + c.VirtualOpeningWidthMm.ToString("F3") + ",");
                sb.AppendLine("      \"preferVirtualOpeningHost\": " + (c.PreferVirtualOpeningHost ? "true" : "false") + ",");
                sb.AppendLine("      \"placementSource\": \"" + Escape(c.PlacementSource) + "\",");
                sb.AppendLine("      \"finalPlacement\": \"" + Escape(FormatPointMm(c.FinalPlacementPoint)) + "\",");
                sb.AppendLine("      \"finalWidthMmApplied\": " + c.FinalWidthMmApplied.ToString("F3") + ",");
                sb.AppendLine("      \"finalHeightMmApplied\": " + c.FinalHeightMmApplied.ToString("F3") + ",");
                sb.AppendLine("      \"deltaAlongWallMm\": " + c.DeltaAlongWallMm.ToString("F3") + ",");
                sb.AppendLine("      \"leafLineSegmentId\": " + c.LeafLineSegmentId + ",");
                sb.AppendLine("      \"projected\": \"" + Escape(FormatPointMm(c.ProjectedPointOnWall)) + "\",");
                sb.AppendLine("      \"segmentIds\": [" + string.Join(", ", c.SegmentIds ?? Enumerable.Empty<int>()) + "],");
                sb.AppendLine("      \"unmatchedReason\": " + (string.IsNullOrWhiteSpace(c.UnmatchedReason) ? "null" : ("\"" + Escape(c.UnmatchedReason) + "\"")));
                sb.Append("    }");
                if (i < result.Candidates.Count - 1)
                {
                    sb.Append(",");
                }

                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildCsv(DoorDetectResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CandidateId,RuleSource,SymbolFamilyKind,DoorKind,WidthMm,WidthSource,LeftEdgeMm,RightEdgeMm,OpeningWidthMm,ArcRadiusMm,ArcSweepDeg,DistToWallMm,MatchedWallId,CenterMm,HingeMm,ArcMidMm,WallDirHint,LeafHingeMm,LeafLatchMm,DoorLeafBaseStartMm,DoorLeafBaseEndMm,DoorLeafBaseCenterMm,OpeningCenterMm,VirtualOpeningBaseStartMm,VirtualOpeningBaseEndMm,VirtualOpeningBaseCenterMm,VirtualOpeningWidthMm,PreferVirtualOpeningHost,PlacementSource,FinalPlacementMm,FinalWidthMmApplied,FinalHeightMmApplied,DeltaAlongWallMm,LeafLineSegmentId,ProjectedMm,SegmentIds,UnmatchedReason");
            foreach (DoorCandidate c in result.Candidates)
            {
                string row = string.Join(",",
                    c.CandidateId.ToString(),
                    EscapeCsv(c.RuleSource),
                    EscapeCsv(c.SymbolFamilyKind.ToString()),
                    EscapeCsv(c.IsDoubleDoor ? "Double" : "Single"),
                    c.WidthMm.ToString("F3"),
                    EscapeCsv(c.WidthSource),
                    EscapeCsv(FormatPointMm(c.LeftEdgePoint)),
                    EscapeCsv(FormatPointMm(c.RightEdgePoint)),
                    c.OpeningWidthMm.ToString("F3"),
                    c.ArcRadiusMm.ToString("F3"),
                    c.ArcSweepDeg.ToString("F3"),
                    c.DistToWallMm.ToString("F3"),
                    c.MatchedWallId == null ? "" : c.MatchedWallId.IntegerValue.ToString(),
                    EscapeCsv(FormatPointMm(c.CenterPoint)),
                    EscapeCsv(FormatPointMm(c.HingePoint)),
                    EscapeCsv(FormatPointMm(c.ArcMidPoint)),
                    EscapeCsv(FormatVector(c.WallDirHint)),
                    EscapeCsv(FormatPointMm(c.LeafHinge)),
                    EscapeCsv(FormatPointMm(c.LeafLatch)),
                    EscapeCsv(FormatPointMm(c.DoorLeafBaseStart)),
                    EscapeCsv(FormatPointMm(c.DoorLeafBaseEnd)),
                    EscapeCsv(FormatPointMm(c.DoorLeafBaseCenter)),
                    EscapeCsv(FormatPointMm(c.OpeningCenterPoint)),
                    EscapeCsv(FormatPointMm(c.VirtualOpeningBaseStart)),
                    EscapeCsv(FormatPointMm(c.VirtualOpeningBaseEnd)),
                    EscapeCsv(FormatPointMm(c.VirtualOpeningBaseCenter)),
                    c.VirtualOpeningWidthMm.ToString("F3"),
                    c.PreferVirtualOpeningHost ? "true" : "false",
                    EscapeCsv(c.PlacementSource),
                    EscapeCsv(FormatPointMm(c.FinalPlacementPoint)),
                    c.FinalWidthMmApplied.ToString("F3"),
                    c.FinalHeightMmApplied.ToString("F3"),
                    c.DeltaAlongWallMm.ToString("F3"),
                    c.LeafLineSegmentId.ToString(),
                    EscapeCsv(FormatPointMm(c.ProjectedPointOnWall)),
                    EscapeCsv(string.Join("|", c.SegmentIds ?? Enumerable.Empty<int>())),
                    EscapeCsv(c.UnmatchedReason));
                sb.AppendLine(row);
            }

            return sb.ToString();
        }

        private static string FormatPointMm(XYZ point)
        {
            if (point == null)
            {
                return "";
            }

            double x = UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters);
            double z = UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters);
            return "(" + x.ToString("F1") + " " + y.ToString("F1") + " " + z.ToString("F1") + ")";
        }

        private static string FormatVector(XYZ vector)
        {
            if (vector == null)
            {
                return "";
            }

            return "(" + vector.X.ToString("F4") + " " + vector.Y.ToString("F4") + " " + vector.Z.ToString("F4") + ")";
        }

        private static string Escape(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }

            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string EscapeCsv(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            string escaped = text.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }
    }
}
