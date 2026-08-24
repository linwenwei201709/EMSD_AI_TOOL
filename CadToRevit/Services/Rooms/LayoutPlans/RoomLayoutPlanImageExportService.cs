using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathPreview;
using CadToRevit.Services.Rooms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.LayoutPlans
{
    public sealed class RoomLayoutPlanImageExportResult
    {
        public string MainViewImagePath { get; set; }

        public string KeyPlanImagePath { get; set; }
    }

    public static class RoomLayoutPlanImageExportService
    {
        public static RoomLayoutPlanImageExportResult ExportCurrentViews(UIApplication app, string tempDirectory, RoomLayoutPlanDto plan)
        {
            RoomLayoutPlanImageExportResult result = new RoomLayoutPlanImageExportResult();
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null || doc.ActiveView == null || string.IsNullOrWhiteSpace(tempDirectory))
            {
                return result;
            }

            Directory.CreateDirectory(tempDirectory);
            View mainView = ResolveMainRouteView(doc);
            View keyPlanView = ResolveKeyPlanView(doc);
            result.MainViewImagePath = TryExportCroppedMain3DView(doc, mainView as View3D, tempDirectory, plan);
            result.KeyPlanImagePath = TryExportCroppedKeyPlanView(doc, keyPlanView, tempDirectory, plan);
            return result;
        }

        private static View ResolveMainRouteView(Document doc)
        {
            if (doc.ActiveView is View3D && !doc.ActiveView.IsTemplate)
            {
                return doc.ActiveView;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(view => !view.IsTemplate)
                .OrderBy(view => string.Equals(view.Name, "{3D}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(view => view.Name)
                .FirstOrDefault();
        }

        private static View ResolveKeyPlanView(Document doc)
        {
            if (doc.ActiveView is ViewPlan && !doc.ActiveView.IsTemplate)
            {
                return doc.ActiveView;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(view => !view.IsTemplate && view.ViewType == ViewType.FloorPlan)
                .OrderBy(view => view.Name)
                .FirstOrDefault();
        }

        private static string TryExportView(Document doc, View view, string tempDirectory, string prefix, int pixelSize)
        {
            try
            {
                if (view == null)
                {
                    return string.Empty;
                }

                string basePath = Path.Combine(tempDirectory, prefix + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
                ImageExportOptions options = new ImageExportOptions
                {
                    ExportRange = ExportRange.SetOfViews,
                    FilePath = basePath,
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = pixelSize
                };
                options.SetViewsAndSheets(new List<ElementId> { view.Id });

                doc.ExportImage(options);
                string name = Path.GetFileName(basePath);
                return Directory.GetFiles(tempDirectory, name + "*.png")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryExportCroppedMain3DView(Document doc, View3D sourceView, string tempDirectory, RoomLayoutPlanDto plan)
        {
            if (sourceView == null)
            {
                return TryExportView(doc, sourceView, tempDirectory, "route_3d_view", 3200);
            }

            BoundingBoxXYZ sectionBox = BuildRouteExportSectionBox(doc, sourceView, plan) ??
                                        BuildDwgExportSectionBox(doc, sourceView);
            if (sectionBox == null)
            {
                return TryExportView(doc, sourceView, tempDirectory, "route_3d_view", 3200);
            }

            ElementId tempViewId = ElementId.InvalidElementId;
            try
            {
                using (Transaction tx = new Transaction(doc, "Prepare Layout Plan Main 3D View"))
                {
                    tx.Start();
                    tempViewId = sourceView.Duplicate(ViewDuplicateOption.Duplicate);
                    View3D tempView = doc.GetElement(tempViewId) as View3D;
                    if (tempView == null)
                    {
                        tx.RollBack();
                        return TryExportView(doc, sourceView, tempDirectory, "route_3d_view", 3200);
                    }

                    tempView.Name = "EMSD_TEMP_PDF_ROUTE_3D_VIEW_" + DateTime.Now.ToString("HHmmssfff");
                    tempView.IsSectionBoxActive = true;
                    tempView.SetSectionBox(sectionBox);
                    HideExportOnlyDatumCategories(doc, tempView);
                    tx.Commit();
                }

                return TryExportView(doc, doc.GetElement(tempViewId) as View, tempDirectory, "route_3d_view", 3200);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanPdfMainView] Cropped export fallback: " + ex.Message);
                return TryExportView(doc, sourceView, tempDirectory, "route_3d_view", 3200);
            }
            finally
            {
                DeleteTemporaryView(doc, tempViewId);
            }
        }

        private static BoundingBoxXYZ BuildDwgExportSectionBox(Document doc, View3D view, string debugPrefix = null)
        {
            BoundingBoxXYZ xyBox = CollectDwgBoundingBox(doc, view);
            double paddingMm = 3000.0;
            if (xyBox == null)
            {
                xyBox = CollectFallbackModelBoundingBox(doc, view);
                paddingMm = 5000.0;
            }

            if (xyBox == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(debugPrefix))
            {
                DiagnosticRecorder.AppendDebug(debugPrefix + " DWG bbox=" + FormatBox(xyBox));
            }

            double paddingFt = ToFeet(paddingMm);
            BoundingBoxXYZ zBox = CollectModelZBoundingBoxInsideXy(doc, view, xyBox, paddingFt);
            double minZ = zBox != null ? zBox.Min.Z : ToFeet(-500.0);
            double maxZ = zBox != null ? zBox.Max.Z : ToFeet(4500.0);
            minZ -= ToFeet(1000.0);
            maxZ += ToFeet(2000.0);
            if (maxZ - minZ < ToFeet(6500.0))
            {
                maxZ = minZ + ToFeet(6500.0);
            }

            BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
            {
                Min = new XYZ(xyBox.Min.X - paddingFt, xyBox.Min.Y - paddingFt, minZ),
                Max = new XYZ(xyBox.Max.X + paddingFt, xyBox.Max.Y + paddingFt, maxZ)
            };
            if (!string.IsNullOrWhiteSpace(debugPrefix))
            {
                DiagnosticRecorder.AppendDebug(debugPrefix + " SectionBox paddingMm=" + paddingMm.ToString("F0") + ", box=" + FormatBox(sectionBox));
            }

            return sectionBox;
        }

        private static BoundingBoxXYZ CollectDwgBoundingBox(Document doc, View view)
        {
            BoundingBoxXYZ best = null;
            double bestArea = 0.0;
            foreach (ImportInstance instance in new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>())
            {
                BoundingBoxXYZ box = GetElementBox(instance, view);
                if (box == null)
                {
                    continue;
                }

                double area = Math.Max(0.0, box.Max.X - box.Min.X) * Math.Max(0.0, box.Max.Y - box.Min.Y);
                if (area > bestArea)
                {
                    best = box;
                    bestArea = area;
                }
            }

            return best;
        }

        private static BoundingBoxXYZ CollectFallbackModelBoundingBox(Document doc, View view)
        {
            BoundingBoxXYZ merged = null;
            foreach (BuiltInCategory category in GetMainModelCategories())
            {
                foreach (Element element in new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    BoundingBoxXYZ box = GetElementBox(element, view);
                    if (box != null)
                    {
                        merged = UnionBoundingBox(merged, box);
                    }
                }
            }

            return merged;
        }

        private static BoundingBoxXYZ CollectModelZBoundingBoxInsideXy(Document doc, View3D view, BoundingBoxXYZ xyBox, double padFt)
        {
            BoundingBoxXYZ merged = null;
            foreach (BuiltInCategory category in GetMainModelCategories())
            {
                foreach (Element element in new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    if (element is ImportInstance)
                    {
                        continue;
                    }

                    BoundingBoxXYZ box = GetElementBox(element, view);
                    if (IsBoxNearRoute(box, xyBox, padFt))
                    {
                        merged = UnionBoundingBox(merged, box);
                    }
                }
            }

            return merged;
        }

        private static BuiltInCategory[] GetMainModelCategories()
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
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Floors
            };
        }

        private static BoundingBoxXYZ BuildRouteExportSectionBox(Document doc, View3D view, RoomLayoutPlanDto plan)
        {
            List<List<double>> points = ParsePathPoints(plan != null && plan.DeliveryRoute != null
                ? plan.DeliveryRoute.ResponseBody
                : null);
            if (points == null || points.Count < 2)
            {
                return null;
            }

            double minX = points.Min(p => p[0]);
            double maxX = points.Max(p => p[0]);
            double minY = points.Min(p => p[1]);
            double maxY = points.Max(p => p[1]);
            double minZ = points.Where(p => p.Count >= 3).Select(p => p[2]).DefaultIfEmpty(0.0).Min();
            double maxZ = points.Where(p => p.Count >= 3).Select(p => p[2]).DefaultIfEmpty(4000.0).Max();

            double width = Math.Max(5000.0, maxX - minX);
            double height = Math.Max(5000.0, maxY - minY);
            double routePadMm = Math.Max(1800.0, Math.Max(width, height) * 0.105);
            double nearbyPadFt = ToFeet(routePadMm);

            BoundingBoxXYZ routeBox = new BoundingBoxXYZ
            {
                Min = new XYZ(ToFeet(minX - routePadMm), ToFeet(minY - routePadMm), ToFeet(minZ - 1500.0)),
                Max = new XYZ(ToFeet(maxX + routePadMm), ToFeet(maxY + routePadMm), ToFeet(Math.Max(maxZ + 6500.0, 6500.0)))
            };

            BoundingBoxXYZ merged = routeBox;
            foreach (BoundingBoxXYZ box in CollectRouteRelatedElementBoxes(doc, view, routeBox, nearbyPadFt))
            {
                merged = UnionBoundingBox(merged, box);
            }

            double finalPadFt = ToFeet(600.0);
            double minHeightFt = ToFeet(6500.0);
            XYZ min = new XYZ(merged.Min.X - finalPadFt, merged.Min.Y - finalPadFt, merged.Min.Z - ToFeet(800.0));
            XYZ max = new XYZ(merged.Max.X + finalPadFt, merged.Max.Y + finalPadFt, merged.Max.Z + ToFeet(1600.0));
            ApplyMainViewZoom(ref min, ref max, minX, maxX, minY, maxY, 1.0);
            if (max.Z - min.Z < minHeightFt)
            {
                max = new XYZ(max.X, max.Y, min.Z + minHeightFt);
            }

            return new BoundingBoxXYZ
            {
                Min = min,
                Max = max
            };
        }

        private static void ApplyMainViewZoom(
            ref XYZ min,
            ref XYZ max,
            double routeMinXMm,
            double routeMaxXMm,
            double routeMinYMm,
            double routeMaxYMm,
            double zoomFactor)
        {
            if (min == null || max == null || zoomFactor <= 1.0)
            {
                return;
            }

            double routeCenterX = ToFeet((routeMinXMm + routeMaxXMm) * 0.5);
            double routeCenterY = ToFeet((routeMinYMm + routeMaxYMm) * 0.5);
            double halfX = (max.X - min.X) * 0.5 / zoomFactor;
            double halfY = (max.Y - min.Y) * 0.5 / zoomFactor;
            double routeSafetyFt = ToFeet(700.0);

            double zoomMinX = routeCenterX - halfX;
            double zoomMaxX = routeCenterX + halfX;
            double zoomMinY = routeCenterY - halfY;
            double zoomMaxY = routeCenterY + halfY;

            double routeMinX = ToFeet(routeMinXMm) - routeSafetyFt;
            double routeMaxX = ToFeet(routeMaxXMm) + routeSafetyFt;
            double routeMinY = ToFeet(routeMinYMm) - routeSafetyFt;
            double routeMaxY = ToFeet(routeMaxYMm) + routeSafetyFt;

            min = new XYZ(
                Math.Min(zoomMinX, routeMinX),
                Math.Min(zoomMinY, routeMinY),
                min.Z);
            max = new XYZ(
                Math.Max(zoomMaxX, routeMaxX),
                Math.Max(zoomMaxY, routeMaxY),
                max.Z);
        }

        private static IEnumerable<BoundingBoxXYZ> CollectRouteRelatedElementBoxes(Document doc, View3D view, BoundingBoxXYZ routeBox, double nearbyPadFt)
        {
            if (doc == null || routeBox == null)
            {
                yield break;
            }

            foreach (DirectShape shape in new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>())
            {
                if (!IsRouteExportDirectShape(shape))
                {
                    continue;
                }

                BoundingBoxXYZ box = GetElementBox(shape, view);
                if (IsBoxNearRoute(box, routeBox, nearbyPadFt))
                {
                    yield return box;
                }
            }

            BuiltInCategory[] modelCategories =
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_CurtainWallPanels,
                BuiltInCategory.OST_CurtainWallMullions,
                BuiltInCategory.OST_GenericModel
            };

            foreach (BuiltInCategory category in modelCategories)
            {
                foreach (Element element in new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    if (element is ImportInstance)
                    {
                        continue;
                    }

                    BoundingBoxXYZ box = GetElementBox(element, view);
                    if (IsBoxNearRoute(box, routeBox, nearbyPadFt))
                    {
                        yield return box;
                    }
                }
            }
        }

        private static bool IsRouteExportDirectShape(DirectShape shape)
        {
            if (shape == null)
            {
                return false;
            }

            string name = shape.Name ?? string.Empty;
            string applicationId = shape.ApplicationId ?? string.Empty;
            return name.StartsWith(PathPreviewConstants.SegmentNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(PathPreviewConstants.NodeNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(PathPreviewConstants.ArrowNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(Room3DVisualizationConstants.RegionNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(Room3DVisualizationConstants.MarkerNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(applicationId, PathPreviewConstants.ApplicationId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(applicationId, Room3DVisualizationConstants.ApplicationId, StringComparison.OrdinalIgnoreCase);
        }

        private static BoundingBoxXYZ GetElementBox(Element element, View view)
        {
            if (element == null)
            {
                return null;
            }

            return element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
        }

        private static bool IsBoxNearRoute(BoundingBoxXYZ box, BoundingBoxXYZ routeBox, double padFt)
        {
            if (box == null || routeBox == null)
            {
                return false;
            }

            return box.Max.X >= routeBox.Min.X - padFt &&
                   box.Min.X <= routeBox.Max.X + padFt &&
                   box.Max.Y >= routeBox.Min.Y - padFt &&
                   box.Min.Y <= routeBox.Max.Y + padFt;
        }

        private static BoundingBoxXYZ UnionBoundingBox(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null)
            {
                return b;
            }

            if (b == null)
            {
                return a;
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

        private static void HideExportOnlyDatumCategories(Document doc, View3D tempView)
        {
            BuiltInCategory[] datumCategories =
            {
                BuiltInCategory.OST_Levels,
                BuiltInCategory.OST_Grids,
                BuiltInCategory.OST_CLines
            };

            foreach (BuiltInCategory category in datumCategories)
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

        private static void DeleteTemporaryView(Document doc, ElementId viewId)
        {
            if (doc == null || viewId == ElementId.InvalidElementId)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Cleanup Layout Plan Main 3D View"))
                {
                    tx.Start();
                    doc.Delete(viewId);
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanPdfMainView] Temp view cleanup failed: " + ex.Message);
            }
        }

        private static string TryExportCroppedKeyPlanView(Document doc, View view, string tempDirectory, RoomLayoutPlanDto plan)
        {
            View3D sourceView = ResolveMainRouteView(doc) as View3D;
            BoundingBoxXYZ sectionBox = BuildDwgExportSectionBox(doc, sourceView, "[LayoutPlanPdfKeyPlan]");
            if (sourceView == null || sectionBox == null)
            {
                return TryExportView(doc, view, tempDirectory, "route_key_plan", 3600);
            }

            ElementId tempViewId = ElementId.InvalidElementId;
            try
            {
                using (Transaction tx = new Transaction(doc, "Prepare Layout Plan Key Plan Top View"))
                {
                    tx.Start();
                    View3D tempView = CreateTemporaryTop3DView(doc, sourceView, sectionBox);
                    tempViewId = tempView != null ? tempView.Id : ElementId.InvalidElementId;
                    tx.Commit();
                }

                string path = TryExportView(doc, doc.GetElement(tempViewId) as View, tempDirectory, "route_key_plan", 4200);
                DiagnosticRecorder.AppendDebug("[LayoutPlanPdfKeyPlan] ExportedPath=" + (path ?? string.Empty));
                string croppedPath = CropKeyPlanWhitespace(path);
                DiagnosticRecorder.AppendDebug("[LayoutPlanPdfKeyPlan] CroppedPath=" + (croppedPath ?? string.Empty));
                return string.IsNullOrWhiteSpace(croppedPath) ? path : croppedPath;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanPdfKeyPlan] Top 3D export fallback: " + ex.Message);
                return TryExportView(doc, view, tempDirectory, "route_key_plan", 3600);
            }
            finally
            {
                if (tempViewId != ElementId.InvalidElementId)
                {
                    try
                    {
                        using (Transaction tx = new Transaction(doc, "Cleanup Layout Plan Key Plan View"))
                        {
                            tx.Start();
                            doc.Delete(tempViewId);
                            tx.Commit();
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static View3D CreateTemporaryTop3DView(Document doc, View3D sourceView, BoundingBoxXYZ sectionBox)
        {
            ViewFamilyType type = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x != null && x.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null)
            {
                return null;
            }

            View3D tempView = View3D.CreateIsometric(doc, type.Id);
            tempView.Name = "EMSD_TEMP_PDF_KEY_PLAN_TOP_" + DateTime.Now.ToString("HHmmssfff");
            if (sourceView != null)
            {
                try
                {
                    tempView.DisplayStyle = sourceView.DisplayStyle;
                    tempView.DetailLevel = sourceView.DetailLevel;
                }
                catch
                {
                }
            }

            XYZ center = new XYZ(
                (sectionBox.Min.X + sectionBox.Max.X) * 0.5,
                (sectionBox.Min.Y + sectionBox.Max.Y) * 0.5,
                (sectionBox.Min.Z + sectionBox.Max.Z) * 0.5);
            double span = Math.Max(sectionBox.Max.X - sectionBox.Min.X, sectionBox.Max.Y - sectionBox.Min.Y);
            XYZ eye = new XYZ(center.X, center.Y, sectionBox.Max.Z + Math.Max(ToFeet(3000.0), span));
            tempView.SetOrientation(new ViewOrientation3D(eye, XYZ.BasisY, -XYZ.BasisZ));
            tempView.IsSectionBoxActive = true;
            tempView.SetSectionBox(sectionBox);
            HideExportOnlyDatumCategories(doc, tempView);
            return tempView;
        }

        private static string CropKeyPlanWhitespace(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return string.Empty;
            }

            try
            {
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(imagePath))
                {
                    int minX = bitmap.Width;
                    int minY = bitmap.Height;
                    int maxX = -1;
                    int maxY = -1;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        for (int x = 0; x < bitmap.Width; x++)
                        {
                            System.Drawing.Color color = bitmap.GetPixel(x, y);
                            if (IsContentPixel(color))
                            {
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                            }
                        }
                    }

                    if (maxX <= minX || maxY <= minY)
                    {
                        return imagePath;
                    }

                    int margin = Math.Max(24, Math.Min(bitmap.Width, bitmap.Height) / 40);
                    minX = Math.Max(0, minX - margin);
                    minY = Math.Max(0, minY - margin);
                    maxX = Math.Min(bitmap.Width - 1, maxX + margin);
                    maxY = Math.Min(bitmap.Height - 1, maxY + margin);
                    System.Drawing.Rectangle cropRect = System.Drawing.Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                    if (cropRect.Width >= bitmap.Width * 0.98 && cropRect.Height >= bitmap.Height * 0.98)
                    {
                        return imagePath;
                    }

                    using (System.Drawing.Bitmap cropped = bitmap.Clone(cropRect, bitmap.PixelFormat))
                    {
                        string croppedPath = Path.Combine(
                            Path.GetDirectoryName(imagePath),
                            Path.GetFileNameWithoutExtension(imagePath) + "_cropped.png");
                        cropped.Save(croppedPath, System.Drawing.Imaging.ImageFormat.Png);
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanPdfKeyPlan] ContentPixels crop=(" +
                            minX + "," + minY + ")-(" + maxX + "," + maxY + "), source=" +
                            bitmap.Width + "x" + bitmap.Height + ", cropped=" +
                            cropRect.Width + "x" + cropRect.Height);
                        return croppedPath;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanPdfKeyPlan] Crop whitespace failed: " + ex.Message);
                return imagePath;
            }
        }

        private static bool IsContentPixel(System.Drawing.Color color)
        {
            if (color.A < 16)
            {
                return false;
            }

            int max = Math.Max(color.R, Math.Max(color.G, color.B));
            int min = Math.Min(color.R, Math.Min(color.G, color.B));
            if (max < 245)
            {
                return true;
            }

            return max - min > 18;
        }

        private static BoundingBoxXYZ BuildDwgKeyPlanCropBox(Document doc, View view, double targetAspect)
        {
            BoundingBoxXYZ xyBox = CollectDwgBoundingBox(doc, view);
            double paddingMm = 3000.0;
            if (xyBox == null)
            {
                xyBox = CollectFallbackModelBoundingBox(doc, view);
                paddingMm = 5000.0;
            }

            if (xyBox == null)
            {
                return null;
            }

            double pad = ToFeet(paddingMm);
            double minX = xyBox.Min.X - pad;
            double maxX = xyBox.Max.X + pad;
            double minY = xyBox.Min.Y - pad;
            double maxY = xyBox.Max.Y + pad;
            ExpandXyToAspect(ref minX, ref maxX, ref minY, ref maxY, targetAspect);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, -10000.0),
                Max = new XYZ(maxX, maxY, 10000.0)
            };
        }

        private static void ExpandXyToAspect(ref double minX, ref double maxX, ref double minY, ref double maxY, double targetAspect)
        {
            if (targetAspect <= 0.0)
            {
                return;
            }

            double width = Math.Max(1e-6, maxX - minX);
            double height = Math.Max(1e-6, maxY - minY);
            double currentAspect = width / height;
            if (currentAspect < targetAspect)
            {
                double targetWidth = height * targetAspect;
                double extra = (targetWidth - width) * 0.5;
                minX -= extra;
                maxX += extra;
                return;
            }

            double targetHeight = width / targetAspect;
            double extraY = (targetHeight - height) * 0.5;
            minY -= extraY;
            maxY += extraY;
        }

        private static BoundingBoxXYZ BuildRouteCropBox(RoomLayoutPlanDto plan)
        {
            List<List<double>> points = ParsePathPoints(plan != null && plan.DeliveryRoute != null
                ? plan.DeliveryRoute.ResponseBody
                : null);
            if (points == null || points.Count < 2)
            {
                return null;
            }

            double minX = points.Min(p => p[0]);
            double maxX = points.Max(p => p[0]);
            double minY = points.Min(p => p[1]);
            double maxY = points.Max(p => p[1]);
            double width = Math.Max(5000.0, maxX - minX);
            double height = Math.Max(5000.0, maxY - minY);
            double pad = Math.Max(2500.0, Math.Max(width, height) * 0.18);

            BoundingBoxXYZ box = new BoundingBoxXYZ();
            box.Min = new XYZ(ToFeet(minX - pad), ToFeet(minY - pad), -10000.0);
            box.Max = new XYZ(ToFeet(maxX + pad), ToFeet(maxY + pad), 10000.0);
            return box;
        }

        private static List<List<double>> ParsePathPoints(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return null;
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RouteResponseDto));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(responseBody)))
                {
                    RouteResponseDto dto = serializer.ReadObject(stream) as RouteResponseDto;
                    return dto != null && dto.PathPoints != null
                        ? dto.PathPoints.Where(p => p != null && p.Count >= 2).ToList()
                        : null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static double ToFeet(double millimeters)
        {
            return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
        }

        private static double FromFeet(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        [DataContract]
        private sealed class RouteResponseDto
        {
            [DataMember(Name = "path_points")]
            public List<List<double>> PathPoints { get; set; }
        }
    }
}
