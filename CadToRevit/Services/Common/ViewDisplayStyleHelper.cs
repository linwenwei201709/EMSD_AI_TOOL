using Autodesk.Revit.DB;

namespace CadToRevit.Services.Common
{
    internal static class ViewDisplayStyleHelper
    {
        internal static void Ensure3DViewShaded(View3D view3D)
        {
            if (view3D == null)
            {
                return;
            }

            if (view3D.DisplayStyle != DisplayStyle.Shading &&
                view3D.DisplayStyle != DisplayStyle.ShadingWithEdges)
            {
                // Ensure room-related color visualization is visible in 3D view.
                view3D.DisplayStyle = DisplayStyle.Shading;
            }

            // Revit 2025 English templates may show Scope Boxes in the default 3D view.
            // Keep plugin-opened 3D views clean by hiding the category only in this view.
            HideScopeBoxesInView(view3D);
        }

        private static void HideScopeBoxesInView(View3D view3D)
        {
            if (view3D == null || view3D.Document == null)
            {
                return;
            }

            try
            {
                Category scopeBoxCategory = Category.GetCategory(view3D.Document, BuiltInCategory.OST_VolumeOfInterest);
                ElementId categoryId = scopeBoxCategory?.Id;
                if (categoryId == null || categoryId == ElementId.InvalidElementId)
                {
                    return;
                }

                if (!view3D.CanCategoryBeHidden(categoryId))
                {
                    return;
                }

                if (!view3D.GetCategoryHidden(categoryId))
                {
                    view3D.SetCategoryHidden(categoryId, true);
                }
            }
            catch
            {
                // Best effort only: do not break DWG import or room/path visualization if a view template blocks this setting.
            }
        }
    }
}
