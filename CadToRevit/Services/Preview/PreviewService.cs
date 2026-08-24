using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Cad;
using CadToRevit.Services.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Preview
{
    public enum PreviewStatus
    {
        Success,
        UnsupportedView,
        Empty,
        Failed
    }

    public sealed class PreviewResult
    {
        public PreviewStatus Status { get; set; }

        public int CreatedCount { get; set; }

        public string Message { get; set; }
    }

    public static class PreviewService
    {
        private const string PreviewLineStyleName = "HelixPreview";
        private static readonly List<ElementId> PreviewElementIds = new List<ElementId>();

        public static PreviewResult ShowLayerSegments(UIDocument uiDoc, CadDataset dataset, ISet<string> selectedRawLayers)
        {
            if (uiDoc == null || uiDoc.Document == null || uiDoc.ActiveView == null || dataset == null || selectedRawLayers == null)
            {
                return new PreviewResult
                {
                    Status = PreviewStatus.Failed,
                    Message = "Preview failed: invalid context."
                };
            }

            Document doc = uiDoc.Document;
            View view = uiDoc.ActiveView;
            if (!IsDetailPreviewView(view))
            {
                return new PreviewResult
                {
                    Status = PreviewStatus.UnsupportedView,
                    Message = "当前视图不支持预览线。请切换至平面图/剖面图后再 Preview。"
                };
            }

            Clear(doc);
            int created = 0;
            try
            {
                using (Transaction tx = new Transaction(doc, "CadToRevit Preview"))
                {
                    tx.Start();
                    GraphicsStyle style = EnsurePreviewLineStyle(doc);
                    foreach (CadSegment segment in dataset.Segments)
                    {
                        if (segment == null || segment.P0 == null || segment.P1 == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(segment.RawLayerName) || !selectedRawLayers.Contains(segment.RawLayerName))
                        {
                            continue;
                        }

                        Line line = Line.CreateBound(segment.P0, segment.P1);
                        DetailCurve detail = doc.Create.NewDetailCurve(view, line) as DetailCurve;
                        if (detail != null)
                        {
                            if (style != null)
                            {
                                detail.LineStyle = style;
                            }

                            PreviewElementIds.Add(detail.Id);
                            created++;
                        }
                    }

                    tx.Commit();
                }
            }
            catch (System.Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Preview] Failed: " + ex.Message);
                return new PreviewResult
                {
                    Status = PreviewStatus.Failed,
                    CreatedCount = created,
                    Message = "Preview failed."
                };
            }

            if (created <= 0)
            {
                return new PreviewResult
                {
                    Status = PreviewStatus.Empty,
                    CreatedCount = 0,
                    Message = "No valid segments found for this layer."
                };
            }

            try
            {
                IList<ElementId> ids = new List<ElementId>(PreviewElementIds);
                uiDoc.Selection.SetElementIds(ids);
                uiDoc.ShowElements(ids);
            }
            catch
            {
            }

            return new PreviewResult
            {
                Status = PreviewStatus.Success,
                CreatedCount = created,
                Message = "Preview created: " + created + " lines."
            };
        }

        public static void Clear(Document doc)
        {
            if (doc == null || PreviewElementIds.Count == 0)
            {
                return;
            }

            using (Transaction tx = new Transaction(doc, "Clear CadToRevit Preview"))
            {
                tx.Start();
                foreach (ElementId id in new List<ElementId>(PreviewElementIds))
                {
                    try
                    {
                        doc.Delete(id);
                    }
                    catch
                    {
                    }
                }

                tx.Commit();
            }

            PreviewElementIds.Clear();
        }

        private static bool IsDetailPreviewView(View view)
        {
            if (view == null)
            {
                return false;
            }

            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Section:
                case ViewType.Elevation:
                case ViewType.Detail:
                case ViewType.DraftingView:
                    return true;
                default:
                    return false;
            }
        }

        private static GraphicsStyle EnsurePreviewLineStyle(Document doc)
        {
            if (doc == null || doc.Settings == null || doc.Settings.Categories == null)
            {
                return null;
            }

            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines == null)
            {
                return null;
            }

            Category preview = null;
            if (lines.SubCategories != null)
            {
                preview = lines.SubCategories
                    .Cast<Category>()
                    .FirstOrDefault(x => x != null && string.Equals(x.Name, PreviewLineStyleName, System.StringComparison.OrdinalIgnoreCase));
            }

            if (preview == null)
            {
                preview = doc.Settings.Categories.NewSubcategory(lines, PreviewLineStyleName);
            }

            try
            {
                preview.LineColor = new Color(0, 255, 255);
            }
            catch
            {
            }

            try
            {
                preview.SetLineWeight(8, GraphicsStyleType.Projection);
            }
            catch
            {
            }

            try
            {
                return preview.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            catch
            {
                return null;
            }
        }
    }
}
