using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class AnalyzeLayersCommand : IExternalCommand
    {
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
                TaskDialog.Show("\u56fe\u5c42\u8bed\u4e49\u5206\u6790", "No CAD Link (ImportInstance) found.");
                return Result.Cancelled;
            }

            List<CadGeometryData> items = CadGeometryReader.ReadGeometryItems(doc, importInstance).ToList();
            if (items.Count == 0)
            {
                TaskDialog.Show("\u56fe\u5c42\u8bed\u4e49\u5206\u6790", "No analyzable geometry found.");
                return Result.Succeeded;
            }

            var rows = items
                .GroupBy(x => x.LayerName)
                .Select(g => new
                {
                    Layer = g.Key,
                    Total = g.Count(),
                    Line = g.Count(x => x.GeometryType == "Line"),
                    PolyLine = g.Count(x => x.GeometryType == "PolyLine"),
                    Arc = g.Count(x => x.GeometryType == "Arc"),
                    Other = g.Count(x => x.GeometryType == "OtherCurve"),
                    Semantic = LayerNameMapper.Map(g.Key)
                })
                .OrderByDescending(x => x.Total)
                .ToList();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Layer | Total | Line | PolyLine | Arc | Other | Semantic");
            foreach (var row in rows.Take(30))
            {
                sb.AppendLine(
                    row.Layer + " | " +
                    row.Total + " | " +
                    row.Line + " | " +
                    row.PolyLine + " | " +
                    row.Arc + " | " +
                    row.Other + " | " +
                    row.Semantic);
            }

            TaskDialog.Show("\u56fe\u5c42\u8bed\u4e49\u5206\u6790", sb.ToString());
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
    }
}
