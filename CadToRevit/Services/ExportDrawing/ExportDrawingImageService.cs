using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.ExportDrawing
{
    public static class ExportDrawingImageService
    {
        private const double FeetPerMillimeter = 1.0 / 304.8;

        public static ExportDrawingImageResult ExportFiveViews(Document doc, string tempDirectory)
        {
            if (doc == null)
            {
                throw new InvalidOperationException("No active Revit document.");
            }

            if (string.IsNullOrWhiteSpace(tempDirectory))
            {
                throw new ArgumentException("Temporary directory is required.", nameof(tempDirectory));
            }

            Directory.CreateDirectory(tempDirectory);
            DiagnosticRecorder.AppendDebug("[ExportDrawing] Start");

            View3D sourceView = ResolveSource3DView(doc);
            BoundingBoxXYZ sectionBox = BuildModelExportSectionBox(doc, sourceView);
            if (sectionBox == null)
            {
                throw new InvalidOperationException("No model geometry was found to export.");
            }

            DiagnosticRecorder.AppendDebug("[ExportDrawing] Model box=" + FormatBox(sectionBox));

            List<ElementId> tempViewIds = new List<ElementId>();
            List<ExportDrawingTempView> tempViews = new List<ExportDrawingTempView>();
            try
            {
                using (Transaction tx = new Transaction(doc, "Prepare Export Drawing Views"))
                {
                    tx.Start();
                    string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    tempViews.Add(CreateTemporaryView(doc, sourceView, sectionBox, "Top View", "TOP", suffix, 1));
                    tempViews.Add(CreateTemporaryView(doc, sourceView, sectionBox, "Front View", "FRONT", suffix, 2));
                    tempViews.Add(CreateTemporaryView(doc, sourceView, sectionBox, "Back View", "BACK", suffix, 3));
                    tempViews.Add(CreateTemporaryView(doc, sourceView, sectionBox, "Left View", "LEFT", suffix, 4));
                    tempViews.Add(CreateTemporaryView(doc, sourceView, sectionBox, "Right View", "RIGHT", suffix, 5));

                    foreach (ExportDrawingTempView tempView in tempViews)
                    {
                        tempViewIds.Add(tempView.View.Id);
                    }

                    tx.Commit();
                }

                ExportDrawingImageResult result = new ExportDrawingImageResult();
                foreach (ExportDrawingTempView tempView in tempViews.OrderBy(x => x.PageNumber))
                {
                    string imagePath = ExportView(doc, tempView.View, tempDirectory, BuildImagePrefix(tempView));
                    if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                    {
                        throw new InvalidOperationException("Failed to export " + tempView.DisplayName + ".");
                    }

                    DiagnosticRecorder.AppendDebug("[ExportDrawing] " + tempView.DisplayName + " exported=" + imagePath);
                    result.Views.Add(new ExportDrawingViewImage
                    {
                        ViewName = tempView.DisplayName,
                        ImagePath = imagePath,
                        PageNumber = tempView.PageNumber
                    });
                }

                return result;
            }
            finally
            {
                CleanupTemporaryViews(doc, tempViewIds);
            }
        }

        private static View3D ResolveSource3DView(Document doc)
        {
            if (doc.ActiveView is View3D active3D && !active3D.IsTemplate)
            {
                return active3D;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(view => !view.IsTemplate)
                .OrderBy(view => string.Equals(view.Name, "{3D}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(view => view.Name)
                .FirstOrDefault();
        }

        private static ExportDrawingTempView CreateTemporaryView(
            Document doc,
            View3D sourceView,
            BoundingBoxXYZ sectionBox,
            string displayName,
            string key,
            string suffix,
            int pageNumber)
        {
            ViewFamilyType type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x != null && x.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                throw new InvalidOperationException("No 3D view family type was found.");
            }

            View3D tempView = View3D.CreateIsometric(doc, type.Id);
            tempView.Name = "EMSD_TEMP_EXPORT_DRAWING_" + key + "_" + suffix;
            ApplySourceStyle(sourceView, tempView);
            SetOrientation(tempView, sectionBox, key);
            tempView.IsSectionBoxActive = true;
            tempView.SetSectionBox(CloneBox(sectionBox));
            HideExportOnlyDatumCategories(doc, tempView);
            HideAhuPlacementPointMarker(doc, tempView);

            return new ExportDrawingTempView
            {
                View = tempView,
                DisplayName = displayName,
                Key = key,
                PageNumber = pageNumber
            };
        }

        private static void ApplySourceStyle(View3D sourceView, View3D tempView)
        {
            if (sourceView == null || tempView == null)
            {
                return;
            }

            try
            {
                tempView.DisplayStyle = sourceView.DisplayStyle;
                tempView.DetailLevel = sourceView.DetailLevel;
            }
            catch
            {
            }
        }

        private static void SetOrientation(View3D tempView, BoundingBoxXYZ sectionBox, string key)
        {
            XYZ center = new XYZ(
                (sectionBox.Min.X + sectionBox.Max.X) * 0.5,
                (sectionBox.Min.Y + sectionBox.Max.Y) * 0.5,
                (sectionBox.Min.Z + sectionBox.Max.Z) * 0.5);
            double maxSpan = Math.Max(
                Math.Max(sectionBox.Max.X - sectionBox.Min.X, sectionBox.Max.Y - sectionBox.Min.Y),
                sectionBox.Max.Z - sectionBox.Min.Z);
            double distance = Math.Max(ToFeet(3000.0), maxSpan * 1.25);

            if (string.Equals(key, "TOP", StringComparison.OrdinalIgnoreCase))
            {
                tempView.SetOrientation(new ViewOrientation3D(
                    new XYZ(center.X, center.Y, sectionBox.Max.Z + distance),
                    XYZ.BasisY,
                    -XYZ.BasisZ));
                return;
            }

            if (string.Equals(key, "FRONT", StringComparison.OrdinalIgnoreCase))
            {
                tempView.SetOrientation(new ViewOrientation3D(
                    new XYZ(center.X, sectionBox.Min.Y - distance, center.Z),
                    XYZ.BasisZ,
                    XYZ.BasisY));
                return;
            }

            if (string.Equals(key, "BACK", StringComparison.OrdinalIgnoreCase))
            {
                tempView.SetOrientation(new ViewOrientation3D(
                    new XYZ(center.X, sectionBox.Max.Y + distance, center.Z),
                    XYZ.BasisZ,
                    -XYZ.BasisY));
                return;
            }

            if (string.Equals(key, "LEFT", StringComparison.OrdinalIgnoreCase))
            {
                tempView.SetOrientation(new ViewOrientation3D(
                    new XYZ(sectionBox.Min.X - distance, center.Y, center.Z),
                    XYZ.BasisZ,
                    XYZ.BasisX));
                return;
            }

            tempView.SetOrientation(new ViewOrientation3D(
                new XYZ(sectionBox.Max.X + distance, center.Y, center.Z),
                XYZ.BasisZ,
                -XYZ.BasisX));
        }

        private static BoundingBoxXYZ BuildModelExportSectionBox(Document doc, View3D view)
        {
            BoundingBoxXYZ box = CollectModelBoundingBox(doc, view);
            if (box == null || !IsValidBox(box))
            {
                return null;
            }

            double spanX = Math.Max(0.0, box.Max.X - box.Min.X);
            double spanY = Math.Max(0.0, box.Max.Y - box.Min.Y);
            double spanZ = Math.Max(0.0, box.Max.Z - box.Min.Z);
            double maxSpan = Math.Max(Math.Max(spanX, spanY), spanZ);
            double xyPadding = Math.Max(ToFeet(3000.0), maxSpan * 0.05);
            double zPadding = Math.Max(ToFeet(1500.0), spanZ * 0.05);

            return new BoundingBoxXYZ
            {
                Min = new XYZ(box.Min.X - xyPadding, box.Min.Y - xyPadding, box.Min.Z - zPadding),
                Max = new XYZ(box.Max.X + xyPadding, box.Max.Y + xyPadding, box.Max.Z + zPadding)
            };
        }

        private static BoundingBoxXYZ CollectModelBoundingBox(Document doc, View view)
        {
            BoundingBoxXYZ merged = null;
            foreach (BuiltInCategory category in GetModelCategories())
            {
                IEnumerable<Element> elements;
                try
                {
                    elements = new FilteredElementCollector(doc)
                        .OfCategory(category)
                        .WhereElementIsNotElementType()
                        .ToElements();
                }
                catch
                {
                    continue;
                }

                foreach (Element element in elements)
                {
                    DirectShape directShape = element as DirectShape;
                    if (directShape != null &&
                        !string.IsNullOrWhiteSpace(directShape.Name) &&
                        directShape.Name.StartsWith(
                            Room3DVisualizationConstants.AhuPlacementPointMarkerNamePrefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    BoundingBoxXYZ box = GetElementBox(element, view);
                    if (box != null && IsValidBox(box))
                    {
                        merged = UnionBoundingBox(merged, box);
                    }
                }
            }

            foreach (ImportInstance instance in new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>())
            {
                BoundingBoxXYZ box = GetElementBox(instance, view);
                if (box != null && IsValidBox(box))
                {
                    merged = UnionBoundingBox(merged, box);
                }
            }

            return merged;
        }

        private static BuiltInCategory[] GetModelCategories()
        {
            return new[]
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Roofs,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory
            };
        }

        private static string ExportView(Document doc, View view, string tempDirectory, string prefix)
        {
            string basePath = Path.Combine(tempDirectory, prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            ImageExportOptions options = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews,
                FilePath = basePath,
                FitDirection = FitDirectionType.Horizontal,
                HLRandWFViewsFileType = ImageFileType.PNG,
                ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_150,
                PixelSize = 2800
            };
            options.SetViewsAndSheets(new List<ElementId> { view.Id });

            doc.ExportImage(options);
            string name = Path.GetFileName(basePath);
            return Directory.GetFiles(tempDirectory, name + "*.png")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        private static void HideAhuPlacementPointMarker(Document doc, View3D tempView)
        {
            if (doc == null || tempView == null)
            {
                return;
            }

            List<ElementId> ids = new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .Cast<DirectShape>()
                .Where(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.Name) &&
                    x.Name.StartsWith(
                        Room3DVisualizationConstants.AhuPlacementPointMarkerNamePrefix,
                        StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            try
            {
                tempView.HideElements(ids);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[ExportDrawing] Failed to hide AHU placement-point marker. Error=" +
                    ex.Message);
            }
        }

        private static void HideExportOnlyDatumCategories(Document doc, View3D tempView)
        {
            BuiltInCategory[] categories =
            {
                BuiltInCategory.OST_Levels,
                BuiltInCategory.OST_Grids,
                BuiltInCategory.OST_CLines
            };

            foreach (BuiltInCategory category in categories)
            {
                try
                {
                    ElementId categoryId = new ElementId(category);
                    if (tempView.CanCategoryBeHidden(categoryId))
                    {
                        tempView.SetCategoryHidden(categoryId, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static void CleanupTemporaryViews(Document doc, IList<ElementId> viewIds)
        {
            if (doc == null || viewIds == null || viewIds.Count == 0)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Cleanup Export Drawing Views"))
                {
                    tx.Start();
                    foreach (ElementId id in viewIds.Where(x => x != null && x != ElementId.InvalidElementId))
                    {
                        if (doc.GetElement(id) != null)
                        {
                            doc.Delete(id);
                        }
                    }

                    tx.Commit();
                }

                DiagnosticRecorder.AppendDebug("[ExportDrawing] Cleanup temp views count=" + viewIds.Count);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ExportDrawing] Cleanup failed: " + ex.Message);
            }
        }

        private static BoundingBoxXYZ GetElementBox(Element element, View view)
        {
            if (element == null)
            {
                return null;
            }

            try
            {
                return element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            }
            catch
            {
                return null;
            }
        }

        private static BoundingBoxXYZ UnionBoundingBox(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null)
            {
                return CloneBox(b);
            }

            if (b == null)
            {
                return CloneBox(a);
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(
                    Math.Min(a.Min.X, b.Min.X),
                    Math.Min(a.Min.Y, b.Min.Y),
                    Math.Min(a.Min.Z, b.Min.Z)),
                Max = new XYZ(
                    Math.Max(a.Max.X, b.Max.X),
                    Math.Max(a.Max.Y, b.Max.Y),
                    Math.Max(a.Max.Z, b.Max.Z))
            };
        }

        private static BoundingBoxXYZ CloneBox(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                Max = new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };
        }

        private static bool IsValidBox(BoundingBoxXYZ box)
        {
            return box != null &&
                   box.Min != null &&
                   box.Max != null &&
                   box.Max.X > box.Min.X &&
                   box.Max.Y > box.Min.Y &&
                   box.Max.Z > box.Min.Z;
        }

        private static double ToFeet(double millimeters)
        {
            return millimeters * FeetPerMillimeter;
        }

        private static double FromFeet(double feet)
        {
            return feet / FeetPerMillimeter;
        }

        private static string FormatBox(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return "<null>";
            }

            return "min=(" +
                   FromFeet(box.Min.X).ToString("F0") + "," +
                   FromFeet(box.Min.Y).ToString("F0") + "," +
                   FromFeet(box.Min.Z).ToString("F0") + "), max=(" +
                   FromFeet(box.Max.X).ToString("F0") + "," +
                   FromFeet(box.Max.Y).ToString("F0") + "," +
                   FromFeet(box.Max.Z).ToString("F0") + ") mm";
        }

        private static string BuildImagePrefix(ExportDrawingTempView tempView)
        {
            return "export_drawing_" + tempView.Key.ToLowerInvariant();
        }

        private sealed class ExportDrawingTempView
        {
            public View3D View { get; set; }

            public string DisplayName { get; set; }

            public string Key { get; set; }

            public int PageNumber { get; set; }
        }
    }
}
