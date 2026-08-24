using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models;
using CadToRevit.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyzeWallCenterlinesCommand : IExternalCommand
    {
        private const bool DrawCenterlines = false;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            ImportInstance importInstance = GetSelectedImportInstance(uiDoc)
                ?? GetFirstImportInstance(doc);
            if (importInstance == null)
            {
                UiMessageService.Warning("Command.AnalyzeWallCenterlines.Title", "Dialog.NoCadLink.Message");
                return Result.Cancelled;
            }

            CadSegmentBuildResult segmentResult = CadSegmentBuilder.BuildSegments(doc, importInstance, null);
            WallRecognitionResult detectResult = WallRecognitionEngine.RecognizeWalls(segmentResult.Segments);

            if (DrawCenterlines && detectResult.Centerlines.Count > 0)
            {
                DrawInActiveView(doc, uiDoc.ActiveView, detectResult.Centerlines);
            }

            UiMessageService.ShowTaskDialogText("Command.AnalyzeWallCenterlines.Title", BuildOutput(importInstance, detectResult));
            return Result.Succeeded;
        }

        private static ImportInstance GetSelectedImportInstance(UIDocument uiDoc)
        {
            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                return null;
            }

            foreach (ElementId id in selectedIds)
            {
                ImportInstance instance = uiDoc.Document.GetElement(id) as ImportInstance;
                if (instance != null)
                {
                    return instance;
                }
            }

            return null;
        }

        private static ImportInstance GetFirstImportInstance(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .FirstOrDefault();
        }

        private static void DrawInActiveView(Document doc, View view, List<WallCenterlineCandidate> candidates)
        {
            if (view == null || view.IsTemplate)
            {
                return;
            }

            using (Transaction tx = new Transaction(doc, "Draw Wall Centerlines"))
            {
                tx.Start();
                foreach (WallCenterlineCandidate item in candidates)
                {
                    if (item.CenterLine == null)
                    {
                        continue;
                    }

                    doc.Create.NewDetailCurve(view, item.CenterLine);
                }

                tx.Commit();
            }
        }

        private static string BuildOutput(ImportInstance importInstance, WallRecognitionResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CAD Link: " + (importInstance.Name ?? "(Unnamed)"));
            sb.AppendLine("ElementId: " + importInstance.Id.IntegerValue);
            sb.AppendLine();
            sb.AppendLine("Total WALL segments: " + result.TotalWallSegments);
            sb.AppendLine("TypeA (DoubleLine): " + result.TypeADoubleLineWalls);
            sb.AppendLine("TypeB (SingleLine): " + result.TypeBSingleLineWalls);
            sb.AppendLine("TypeC (Polyline): " + result.TypeCPolylineWalls);
            sb.AppendLine("TypeD (Arc): " + result.TypeDArcWalls);
            sb.AppendLine("Merged walls: " + result.MergedWalls);
            sb.AppendLine();
            sb.AppendLine("Centerline Samples (first 10, mm):");

            List<WallCenterlineCandidate> samples = result.Centerlines.Take(10).ToList();
            if (samples.Count == 0)
            {
                sb.AppendLine("- none");
            }
            else
            {
                foreach (WallCenterlineCandidate item in samples)
                {
                    XYZ p0 = item.CenterLine.GetEndPoint(0);
                    XYZ p1 = item.CenterLine.GetEndPoint(1);
                    sb.AppendLine(
                        "- " + FormatPointMm(p0) +
                        " -> " + FormatPointMm(p1) +
                        ", thickness=" + item.ThicknessMm.ToString("F1") + "mm" +
                        ", overlap=" + item.OverlapLengthMm.ToString("F1") + "mm");
                }
            }

            return sb.ToString();
        }

        private static string FormatPointMm(XYZ point)
        {
            double x = UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters);
            double y = UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters);
            double z = UnitUtils.ConvertFromInternalUnits(point.Z, UnitTypeId.Millimeters);
            return "(" + x.ToString("F1") + ", " + y.ToString("F1") + ", " + z.ToString("F1") + ")";
        }
    }
}
