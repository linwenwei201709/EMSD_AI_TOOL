using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CadToRevit.Services.Ceiling
{
    public sealed class CeilingDetectionResult
    {
        public int TotalCircuitCount { get; set; }
        public int ClosedCircuitCount { get; set; }
        public int SkippedByMinArea { get; set; }
        public string LogPath { get; set; }
    }

    public static class CeilingDetectionService
    {
        public static CeilingDetectionResult Detect(Document doc, ElementId levelId, double minAreaM2)
        {
            CeilingDetectionResult result = new CeilingDetectionResult();
            Level level = doc.GetElement(levelId) as Level;
            if (level == null)
            {
                result.LogPath = WriteLog("Detect", levelId, ElementId.InvalidElementId, 0, minAreaM2, result, new List<string> { "Invalid level." });
                return result;
            }

            List<string> failures = new List<string>();
            using (Transaction tx = new Transaction(doc, "Ceiling Detect (No Commit)"))
            {
                tx.Start();
                PlanTopology topology = doc.get_PlanTopology(level);
                if (topology == null)
                {
                    failures.Add("No PlanTopology.");
                }
                else
                {
                    double minAreaFt2 = UnitUtils.ConvertToInternalUnits(minAreaM2, UnitTypeId.SquareMeters);
                    foreach (PlanCircuit circuit in topology.Circuits)
                    {
                        result.TotalCircuitCount++;
                        if (circuit == null || circuit.IsRoomLocated)
                        {
                            continue;
                        }

                        double area = circuit.Area;
                        if (area <= 1e-9)
                        {
                            continue;
                        }

                        result.ClosedCircuitCount++;
                        if (area < minAreaFt2)
                        {
                            result.SkippedByMinArea++;
                        }
                    }
                }

                tx.RollBack();
            }

            result.LogPath = WriteLog("Detect", levelId, ElementId.InvalidElementId, 0, minAreaM2, result, failures);
            return result;
        }

        internal static string WriteLog(
            string mode,
            ElementId levelId,
            ElementId ceilingTypeId,
            double ceilingHeightMm,
            double minAreaM2,
            CeilingDetectionResult detect,
            List<string> failures)
        {
            try
            {
                string logRoot = DiagnosticRecorder.GetLogDirectory();
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = Path.Combine(logRoot, "ceiling_autogen_" + stamp + ".log");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Mode=" + mode);
                sb.AppendLine("LevelId=" + (levelId == null ? -1 : levelId.IntegerValue));
                sb.AppendLine("CeilingTypeId=" + (ceilingTypeId == null ? -1 : ceilingTypeId.IntegerValue));
                sb.AppendLine("CeilingHeightMm=" + ceilingHeightMm.ToString("F2"));
                sb.AppendLine("MinAreaM2=" + minAreaM2.ToString("F2"));
                sb.AppendLine("TotalCircuits=" + detect.TotalCircuitCount);
                sb.AppendLine("ClosedCircuits=" + detect.ClosedCircuitCount);
                sb.AppendLine("SkippedByMinArea=" + detect.SkippedByMinArea);
                if (failures != null && failures.Count > 0)
                {
                    sb.AppendLine("Failures=" + failures.Count);
                    foreach (string item in failures)
                    {
                        sb.AppendLine(" - " + item);
                    }
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
