using Autodesk.Revit.DB;
using CadToRevit.Services.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewViewService
    {
        internal static View3D GetOrCreate(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            View3D existing = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, PathPreviewConstants.PreviewViewName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Reuse the dedicated preview view to keep the workflow deterministic.
                EnsureDisplayStyle(existing);
                return existing;
            }

            ViewFamilyType familyType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

            if (familyType == null)
            {
                return null;
            }

            View3D view3D = View3D.CreateIsometric(doc, familyType.Id);
            view3D.Name = PathPreviewConstants.PreviewViewName;
            EnsureDisplayStyle(view3D);
            return view3D;
        }

        internal static void FitToModelAndPath(View3D view3D, RevitLinkInstance linkInstance)
        {
            if (view3D == null)
            {
                return;
            }

            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            if (linkInstance != null)
            {
                BoundingBoxXYZ linkBox = linkInstance.get_BoundingBox(view3D) ?? linkInstance.get_BoundingBox(null);
                if (linkBox != null)
                {
                    boxes.Add(linkBox);
                }
            }

            List<DirectShape> pathShapes = new FilteredElementCollector(view3D.Document)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x => PathPreviewMetadataService.IsManagedName(x.Name))
                .ToList();

            foreach (DirectShape shape in pathShapes)
            {
                BoundingBoxXYZ box = shape.get_BoundingBox(view3D) ?? shape.get_BoundingBox(null);
                if (box != null)
                {
                    boxes.Add(box);
                }
            }

            BoundingBoxXYZ merged = MergeBoundingBoxes(boxes);
            if (merged == null)
            {
                return;
            }

            double padding = PathPreviewConstants.SectionBoxPaddingMm * PathPreviewConstants.MmToFeet;
            view3D.IsSectionBoxActive = true;
            view3D.SetSectionBox(new BoundingBoxXYZ
            {
                Min = new XYZ(merged.Min.X - padding, merged.Min.Y - padding, merged.Min.Z - padding),
                Max = new XYZ(merged.Max.X + padding, merged.Max.Y + padding, merged.Max.Z + padding)
            });
            EnsureDisplayStyle(view3D);
        }

        internal static void PrepareForSourceDocPreview(View3D view3D)
        {
            if (view3D == null)
            {
                return;
            }

            if (view3D.IsSectionBoxActive)
            {
                view3D.IsSectionBoxActive = false;
            }

            EnsureDisplayStyle(view3D);
        }

        private static void EnsureDisplayStyle(View3D view3D)
        {
            if (view3D == null)
            {
                return;
            }

            try
            {
                view3D.DisplayStyle = DisplayStyle.ShadingWithEdges;
            }
            catch
            {
                ViewDisplayStyleHelper.Ensure3DViewShaded(view3D);
            }
        }

        private static BoundingBoxXYZ MergeBoundingBoxes(List<BoundingBoxXYZ> boxes)
        {
            List<BoundingBoxXYZ> valid = (boxes ?? new List<BoundingBoxXYZ>())
                .Where(x => x != null && x.Min != null && x.Max != null)
                .ToList();
            if (valid.Count == 0)
            {
                return null;
            }

            double minX = valid.Min(x => x.Min.X);
            double minY = valid.Min(x => x.Min.Y);
            double minZ = valid.Min(x => x.Min.Z);
            double maxX = valid.Max(x => x.Max.X);
            double maxY = valid.Max(x => x.Max.Y);
            double maxZ = valid.Max(x => x.Max.Z);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }
    }
}
