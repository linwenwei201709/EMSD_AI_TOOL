using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    // Deprecated in Revit 2023 path preview pipeline.
    internal static class PathPreviewLinkedModelDisplayService
    {
        private static readonly Color SurfaceColor = new Color(210, 210, 210);
        private static readonly Color ProjectionLineColor = new Color(90, 90, 90);
        private const int SurfaceTransparency = 25;

        internal static void ApplyPreviewOverride(View3D view3D, RevitLinkInstance linkInstance)
        {
            if (view3D == null || linkInstance == null || linkInstance.Id == ElementId.InvalidElementId)
            {
                return;
            }

            ElementId solidFillId = GetSolidFillPatternId(view3D.Document);
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();

            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternVisible(true);
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceForegroundPatternColor(SurfaceColor);
            }

            ogs.SetProjectionLineColor(ProjectionLineColor);
            ogs.SetSurfaceTransparency(SurfaceTransparency);
            view3D.SetElementOverrides(linkInstance.Id, ogs);

            DiagnosticRecorder.AppendDebug("[PathPreview] ApplyPreviewOverride.Success linkInstanceId=" + linkInstance.Id.IntegerValue);
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            if (doc == null)
            {
                return ElementId.InvalidElementId;
            }

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);

            return solidFill != null ? solidFill.Id : ElementId.InvalidElementId;
        }
    }
}
