using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Rooms.LayoutPlanReports
{
    internal static class LayoutPlanReportImageExportService
    {
        internal static LayoutPlanReportImageExportResult Export(UIApplication app, string tempDirectory, RoomLayoutPlanDto plan)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null || string.IsNullOrWhiteSpace(tempDirectory) || plan == null)
            {
                throw new InvalidOperationException("Layout plan image export context is invalid.");
            }

            Directory.CreateDirectory(tempDirectory);
            DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Image export start. LayoutId=" + plan.LayoutId);

            // Saved/Submitted Layout Plans intentionally remove their preview ductwork/pipework
            // from the Revit model. Rebuild those MEP elements only for the report export, use
            // them while the three report images are rendered, then delete only the elements
            // created by this export. This mirrors the existing temporary Delivery Route export
            // pattern without changing the saved Layout Plan or the normal UI state.
            TemporaryMepExportContext temporaryMep = PrepareTemporaryMepForExport(doc, plan);
            try
            {
                ViewPlan preferredPlanView = uiDoc.ActiveView as ViewPlan;
                // Export both TOP views before composing the PDF. In the current report layout:
                // - OverallTop becomes the compact Key Plan on Page 1.
                // - KeyPlan becomes the dedicated room-detail TOP view on Page 2 at target 1:50.
                string main3DImagePath = ExportMain3D(doc, tempDirectory, plan, temporaryMep.ElementIds);
                string keyPlanImagePath = ExportKeyPlan(doc, tempDirectory, plan, preferredPlanView, temporaryMep.ElementIds);
                string overallTopViewImagePath = ExportOverallTop(
                    doc,
                    tempDirectory,
                    plan,
                    preferredPlanView,
                    temporaryMep.ElementIds,
                    keyPlanImagePath);

                LayoutPlanReportImageExportResult result = new LayoutPlanReportImageExportResult
                {
                    Main3DImagePath = main3DImagePath,
                    KeyPlanImagePath = keyPlanImagePath,
                    OverallTopViewImagePath = overallTopViewImagePath
                };

                if (string.IsNullOrWhiteSpace(result.Main3DImagePath) || !File.Exists(result.Main3DImagePath))
                {
                    throw new InvalidOperationException("Main 3D view export failed.");
                }

                if (string.IsNullOrWhiteSpace(result.KeyPlanImagePath) || !File.Exists(result.KeyPlanImagePath))
                {
                    throw new InvalidOperationException("Key plan view export failed.");
                }

                if (string.IsNullOrWhiteSpace(result.OverallTopViewImagePath) || !File.Exists(result.OverallTopViewImagePath))
                {
                    throw new InvalidOperationException("Overall top view export failed.");
                }

                return result;
            }
            finally
            {
                CleanupTemporaryMepForExport(doc, temporaryMep);
            }
        }

        private static string ExportMain3D(Document doc, string tempDirectory, RoomLayoutPlanDto plan, IReadOnlyCollection<ElementId> temporaryMepElementIds)
        {
            View3D source = Resolve3DView(doc);
            if (source == null)
            {
                throw new InvalidOperationException("No available 3D view for Layout Plan Report.");
            }

            ElementId tempViewId = ElementId.InvalidElementId;
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Layout Plan Report 3D View"))
                {
                    tx.Start();
                    tempViewId = source.Duplicate(ViewDuplicateOption.Duplicate);
                    View3D tempView = doc.GetElement(tempViewId) as View3D;
                    if (tempView == null)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Temporary 3D view could not be created.");
                    }

                    tempView.Name = "EMSD_TEMP_LAYOUT_REPORT_3D_" + DateTime.Now.ToString("HHmmssfff");
                    BoundingBoxXYZ box = BuildLayoutPlanReportSectionBox(doc, source, plan, temporaryMepElementIds);
                    if (box != null)
                    {
                        tempView.IsSectionBoxActive = true;
                        tempView.SetSectionBox(box);
                        PrepareKeyPlan3DVisibility(doc, tempView, plan, box, temporaryMepElementIds);

                        // Keep the SectionBox as the broad architectural context envelope, but
                        // do NOT use that padded 3D volume itself as the image CropBox. In an
                        // isometric view, the empty Z-padded corners of the SectionBox project into
                        // view X/Y and create large white margins. Frame the image from the actual
                        // room/layout + nearby architectural element boxes instead, then match the
                        // exact PDF panel aspect ratio. This keeps the surrounding context while
                        // letting visible geometry approach all four panel edges, like the prototype.
                        doc.Regenerate();
                        List<BoundingBoxXYZ> framingBoxes = CollectMain3DFramingBoxes(
                            doc,
                            tempView,
                            plan,
                            temporaryMepElementIds,
                            box);
                        ApplyProjectedCrop(tempView, framingBoxes, 366.0 / 245.0, 0.025, ToFeet(250.0));
                    }

                    HideDatumCategories(tempView);
                    tx.Commit();
                }

                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Main3D view created. ViewId=" + tempViewId.IntegerValue);
                string path = ExportView(doc, doc.GetElement(tempViewId) as View, tempDirectory, "main_3d", 3200);
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Main3D exported. Path=" + (path ?? string.Empty));
                return path;
            }
            finally
            {
                DeleteTemporaryView(doc, tempViewId);
            }
        }

        private static string ExportKeyPlan(Document doc, string tempDirectory, RoomLayoutPlanDto plan, ViewPlan preferredPlanView, IReadOnlyCollection<ElementId> temporaryMepElementIds)
        {
            // IMPORTANT: Do not build the Key Plan from a duplicated FloorPlan. Some AHU
            // families in this project do not expose usable 2D/plan geometry in an architectural
            // plan, even when the category is visible and the view range includes the family.
            // That is exactly why the previous implementation exported the room outline but not
            // the AHU family. The report prototype is visually a TOP view of the real 3D layout,
            // so generate it from the same 3D model representation used by Main3D and orient it
            // straight down. This guarantees that the actual AHU/duct/pipe geometry is present.
            View3D source = Resolve3DView(doc);
            if (source == null)
            {
                throw new InvalidOperationException("No available 3D view for Layout Plan Report key plan.");
            }

            ElementId tempViewId = ElementId.InvalidElementId;
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Layout Plan Report Key Plan"))
                {
                    tx.Start();
                    tempViewId = source.Duplicate(ViewDuplicateOption.Duplicate);
                    View3D tempView = doc.GetElement(tempViewId) as View3D;
                    if (tempView == null)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Temporary key plan 3D view could not be created.");
                    }

                    tempView.Name = "EMSD_TEMP_LAYOUT_REPORT_KEY_" + DateTime.Now.ToString("HHmmssfff");

                    // Page 2 uses the full A2 content frame (562 x 388 mm). Build the room-detail
                    // crop to exactly the same aspect ratio so the PDF does not add letterboxing.
                    BoundingBoxXYZ sectionBox = BuildKeyPlan3DSectionBox(doc, source, plan, temporaryMepElementIds, 562.0 / 388.0);
                    if (sectionBox == null)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Key plan room/model extent could not be resolved.");
                    }

                    PrepareKeyPlan3DVisibility(doc, tempView, plan, sectionBox, temporaryMepElementIds);

                    XYZ center = new XYZ(
                        (sectionBox.Min.X + sectionBox.Max.X) * 0.5,
                        (sectionBox.Min.Y + sectionBox.Max.Y) * 0.5,
                        (sectionBox.Min.Z + sectionBox.Max.Z) * 0.5);
                    double xySpan = Math.Max(
                        sectionBox.Max.X - sectionBox.Min.X,
                        sectionBox.Max.Y - sectionBox.Min.Y);
                    XYZ eye = new XYZ(center.X, center.Y, sectionBox.Max.Z + Math.Max(xySpan, ToFeet(6000.0)));

                    tempView.SetOrientation(new ViewOrientation3D(eye, XYZ.BasisY, new XYZ(0.0, 0.0, -1.0)));

                    // Client requirement: the room-detail drawing on Page 2 is a 1:50 plan.
                    // View.Scale is also set so Revit view-dependent graphics follow 1:50 semantics.
                    // The section/crop box below is calibrated to the physical PDF content size,
                    // which preserves the same 1:50 model-to-paper ratio after raster placement.
                    try
                    {
                        tempView.Scale = 50;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] Room detail View.Scale=50 skipped: " + ex.Message);
                    }

                    tempView.IsSectionBoxActive = true;
                    tempView.SetSectionBox(sectionBox);
                    doc.Regenerate();

                    // SectionBox clips the model, while CropBox controls the image framing.
                    // Apply both so the room occupies the intended portion of the PDF panel.
                    ApplyCrop(tempView, sectionBox);
                    HideDatumCategories(tempView);
                    tx.Commit();
                }

                string path = ExportView(doc, doc.GetElement(tempViewId) as View, tempDirectory, "room_detail_top_1_50", 3600);
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Room detail TOP-3D (target 1:50) exported. Path=" + (path ?? string.Empty));
                return path;
            }
            finally
            {
                DeleteTemporaryView(doc, tempViewId);
            }
        }

        private static string ExportOverallTop(
            Document doc,
            string tempDirectory,
            RoomLayoutPlanDto plan,
            ViewPlan preferredPlanView,
            IReadOnlyCollection<ElementId> temporaryMepElementIds,
            string keyPlanImagePath)
        {
            // Page 2 now follows the same deterministic rendering principle as Key Plan:
            // render the architectural DWG + selected RoomVisualization + AHU/MEP in ONE
            // top-oriented 3D view.  The previous FloorPlan + TOP-3D raster composition had two
            // independent Revit image transforms, so no amount of pixel registration could make
            // it fully reliable on every drawing.  A single Revit view has only one model-to-
            // raster transform, therefore the room and equipment remain exactly coincident with
            // the source DWG coordinates.
            View3D source3D = Resolve3DView(doc);
            if (source3D == null)
            {
                throw new InvalidOperationException("No available 3D view for Layout Plan Report overall top view.");
            }

            ViewPlan orientationPlan = ResolveFloorPlan(doc, plan, preferredPlanView);
            ImportInstance primaryDwg = ResolvePrimaryDwgInstance(doc, orientationPlan, plan);
            if (primaryDwg == null)
            {
                throw new InvalidOperationException("No DWG ImportInstance could be resolved for Layout Plan Report Page 2.");
            }

            ElementId tempViewId = ElementId.InvalidElementId;
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Layout Plan Report Unified Overall Top View"))
                {
                    tx.Start();
                    tempViewId = source3D.Duplicate(ViewDuplicateOption.Duplicate);
                    View3D tempView = doc.GetElement(tempViewId) as View3D;
                    if (tempView == null)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Temporary Page 2 top 3D view could not be created.");
                    }

                    tempView.Name = "EMSD_TEMP_LAYOUT_REPORT_OVERALL_TOP3D_" + DateTime.Now.ToString("HHmmssfff");

                    BoundingBoxXYZ overallCrop = BuildOverallTopCropBox(
                        doc,
                        tempView,
                        plan,
                        temporaryMepElementIds,
                        146.0 / 94.0);
                    BoundingBoxXYZ dwgBox = GetModelBox(primaryDwg, tempView);
                    BoundingBoxXYZ roomBox = CollectRoomVisualizationBox(doc, tempView, plan);
                    BoundingBoxXYZ layoutBox = CollectSavedLayoutElementBox(doc, tempView, plan, temporaryMepElementIds);
                    BoundingBoxXYZ contentBox = Union(dwgBox, Union(roomBox, layoutBox));
                    if (overallCrop == null || contentBox == null)
                    {
                        tx.RollBack();
                        throw new InvalidOperationException("Page 2 DWG/model extent could not be resolved.");
                    }

                    XYZ forward = orientationPlan != null ? orientationPlan.ViewDirection : new XYZ(0.0, 0.0, -1.0);
                    if (forward == null || forward.GetLength() < 1e-9)
                    {
                        forward = new XYZ(0.0, 0.0, -1.0);
                    }
                    else
                    {
                        forward = forward.Normalize();
                    }

                    // The report is a TOP plan.  Guard against a non-plan source orientation and
                    // force a downward orthographic view while preserving the plan's UpDirection
                    // (Project North / rotated plan orientation) when available.
                    if (Math.Abs(forward.Z) < 0.9)
                    {
                        forward = new XYZ(0.0, 0.0, -1.0);
                    }
                    else if (forward.Z > 0.0)
                    {
                        forward = forward.Multiply(-1.0);
                    }

                    XYZ up = orientationPlan != null ? orientationPlan.UpDirection : XYZ.BasisY;
                    if (up == null || up.GetLength() < 1e-9 || Math.Abs(up.DotProduct(forward)) > 0.98)
                    {
                        up = XYZ.BasisY;
                    }
                    else
                    {
                        up = up.Normalize();
                    }

                    double minZ = contentBox.Min.Z - ToFeet(800.0);
                    double maxZ = contentBox.Max.Z + ToFeet(1200.0);
                    if (maxZ - minZ < ToFeet(5000.0))
                    {
                        double midZ = (minZ + maxZ) * 0.5;
                        minZ = midZ - ToFeet(2500.0);
                        maxZ = midZ + ToFeet(2500.0);
                    }

                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
                    {
                        Min = new XYZ(overallCrop.Min.X, overallCrop.Min.Y, minZ),
                        Max = new XYZ(overallCrop.Max.X, overallCrop.Max.Y, maxZ)
                    };

                    XYZ center = new XYZ(
                        (sectionBox.Min.X + sectionBox.Max.X) * 0.5,
                        (sectionBox.Min.Y + sectionBox.Max.Y) * 0.5,
                        (sectionBox.Min.Z + sectionBox.Max.Z) * 0.5);
                    double xySpan = Math.Max(
                        sectionBox.Max.X - sectionBox.Min.X,
                        sectionBox.Max.Y - sectionBox.Min.Y);
                    double eyeDistance = Math.Max(xySpan, ToFeet(12000.0));
                    XYZ eye = center - forward.Multiply(eyeDistance);

                    tempView.SetOrientation(new ViewOrientation3D(eye, up, forward));
                    tempView.IsSectionBoxActive = true;
                    tempView.SetSectionBox(sectionBox);

                    PrepareUnifiedOverallTop3DVisibility(
                        doc,
                        tempView,
                        plan,
                        primaryDwg,
                        temporaryMepElementIds,
                        roomBox);

                    doc.Regenerate();
                    ApplyCrop(tempView, sectionBox);
                    HideDatumCategories(tempView);
                    tx.Commit();

                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] Page2 unified TOP-3D prepared. DWG=" + primaryDwg.Id.IntegerValue +
                        ", DWGBox=" + FormatBox(dwgBox) +
                        ", Room=" + FormatBox(roomBox) +
                        ", Layout=" + FormatBox(layoutBox) +
                        ", Crop=" + FormatBox(sectionBox));
                }

                string path = ExportView(
                    doc,
                    doc.GetElement(tempViewId) as View,
                    tempDirectory,
                    "overall_top_keyplan",
                    1800);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop unified TOP-3D exported. Path=" + (path ?? string.Empty));
                return path;
            }
            finally
            {
                DeleteTemporaryView(doc, tempViewId);
            }
        }

        private sealed class TemporaryMepExportContext
        {
            public List<ElementId> ElementIds { get; } = new List<ElementId>();

            public int DuctElementCount { get; set; }

            public int PipeElementCount { get; set; }
        }

        private static TemporaryMepExportContext PrepareTemporaryMepForExport(Document doc, RoomLayoutPlanDto plan)
        {
            TemporaryMepExportContext context = new TemporaryMepExportContext();
            if (doc == null || plan == null)
            {
                return context;
            }

            ElementId equipmentId = ResolveReportEquipmentId(doc, plan);
            if (equipmentId == ElementId.InvalidElementId)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Temporary MEP skipped: saved/submitted AHU instance was not found. RoomKey=" +
                    (plan.RoomKey ?? string.Empty));
                return context;
            }

            bool hasSavedDucts = HasResolvedLayoutElements(
                doc,
                plan.ActiveGeneratedElements != null ? plan.ActiveGeneratedElements.DuctElements : null);
            bool hasSavedPipes = HasResolvedLayoutElements(
                doc,
                plan.ActiveGeneratedElements != null ? plan.ActiveGeneratedElements.PipeElements : null);

            // Saved/Submitted plans normally have no live duct/pipe references because the UI
            // deliberately cleans those preview elements after saving. Only rebuild the missing
            // systems. If a live saved reference still exists, leave it untouched and do not
            // create a duplicate.
            if (!hasSavedDucts)
            {
                ElementId sadWallId = ResolveWallElementId(doc, plan.SadWall);
                ElementId radWallId = ResolveWallElementId(doc, plan.RadWall);
                if (sadWallId != ElementId.InvalidElementId &&
                    radWallId != ElementId.InvalidElementId &&
                    !string.IsNullOrWhiteSpace(plan.SadSize) &&
                    !string.IsNullOrWhiteSpace(plan.RadSize))
                {
                    try
                    {
                        RoomRigidDuctService.CreateDuctWorkResult ductResult =
                            RoomRigidDuctService.CreateSupplyReturnDuctWork(
                                doc,
                                equipmentId,
                                sadWallId,
                                plan.SadSize,
                                radWallId,
                                plan.RadSize,
                                new RoomRigidDuctService.RigidDuctOptions());

                        if (ductResult != null && ductResult.Succeeded)
                        {
                            AppendUniqueIds(context.ElementIds, ductResult.CreatedElementIds);
                            context.DuctElementCount = ductResult.CreatedElementIds != null
                                ? ductResult.CreatedElementIds.Count
                                : 0;
                            DiagnosticRecorder.AppendDebug(
                                "[LayoutPlanReport] Temporary ductwork created for export. Count=" +
                                context.DuctElementCount +
                                ", SAD=" + (plan.SadSize ?? string.Empty) +
                                ", RAD=" + (plan.RadSize ?? string.Empty));
                        }
                        else
                        {
                            DiagnosticRecorder.AppendDebug(
                                "[LayoutPlanReport] Temporary ductwork create failed. " +
                                (ductResult != null ? ductResult.Message ?? string.Empty : "No result."));
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] Temporary ductwork create exception. " + ex);
                    }
                }
                else
                {
                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] Temporary ductwork skipped: saved SAD/RAD wall or size is missing. " +
                        "SadWall=" + sadWallId.IntegerValue +
                        ", RadWall=" + radWallId.IntegerValue +
                        ", SadSize=" + (plan.SadSize ?? string.Empty) +
                        ", RadSize=" + (plan.RadSize ?? string.Empty));
                }
            }

            if (!hasSavedPipes)
            {
                ElementId chwsWallId = ResolveWallElementId(doc, plan.ChwsWall);
                ElementId chwrWallId = ResolveWallElementId(doc, plan.ChwrWall);
                if (chwsWallId != ElementId.InvalidElementId &&
                    chwrWallId != ElementId.InvalidElementId &&
                    !string.IsNullOrWhiteSpace(plan.ChwsPipeSize) &&
                    !string.IsNullOrWhiteSpace(plan.ChwrPipeSize))
                {
                    try
                    {
                        RoomPipeSystemService.CreatePipeWorkResult pipeResult =
                            RoomPipeSystemService.CreateChilledWaterPipeWork(
                                doc,
                                equipmentId,
                                chwsWallId,
                                plan.ChwsPipeSize,
                                chwrWallId,
                                plan.ChwrPipeSize,
                                new RoomPipeSystemService.PipeWorkOptions());

                        if (pipeResult != null && pipeResult.Succeeded)
                        {
                            AppendUniqueIds(context.ElementIds, pipeResult.CreatedElementIds);
                            context.PipeElementCount = pipeResult.CreatedElementIds != null
                                ? pipeResult.CreatedElementIds.Count
                                : 0;
                            DiagnosticRecorder.AppendDebug(
                                "[LayoutPlanReport] Temporary pipework created for export. Count=" +
                                context.PipeElementCount +
                                ", CHWS=" + (plan.ChwsPipeSize ?? string.Empty) +
                                ", CHWR=" + (plan.ChwrPipeSize ?? string.Empty));
                        }
                        else
                        {
                            DiagnosticRecorder.AppendDebug(
                                "[LayoutPlanReport] Temporary pipework create failed. " +
                                (pipeResult != null ? pipeResult.Message ?? string.Empty : "No result."));
                        }
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] Temporary pipework create exception. " + ex);
                    }
                }
                else
                {
                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] Temporary pipework skipped: saved CHWS/CHWR wall or size is missing. " +
                        "ChwsWall=" + chwsWallId.IntegerValue +
                        ", ChwrWall=" + chwrWallId.IntegerValue +
                        ", ChwsSize=" + (plan.ChwsPipeSize ?? string.Empty) +
                        ", ChwrSize=" + (plan.ChwrPipeSize ?? string.Empty));
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Temporary MEP prepared. Total=" + context.ElementIds.Count +
                ", Duct=" + context.DuctElementCount +
                ", Pipe=" + context.PipeElementCount +
                ", ExistingDucts=" + hasSavedDucts +
                ", ExistingPipes=" + hasSavedPipes);
            return context;
        }

        private static void CleanupTemporaryMepForExport(Document doc, TemporaryMepExportContext context)
        {
            if (doc == null || context == null || context.ElementIds.Count == 0)
            {
                return;
            }

            List<ElementId> existing = context.ElementIds
                .Where(x => x != null && x != ElementId.InvalidElementId && doc.GetElement(x) != null)
                .GroupBy(x => x.IntegerValue)
                .Select(x => x.First())
                .ToList();
            if (existing.Count == 0)
            {
                return;
            }

            try
            {
                using (Transaction tx = new Transaction(doc, "Cleanup Layout Plan Report Temporary MEP"))
                {
                    tx.Start();
                    ICollection<ElementId> deleted = doc.Delete(existing);
                    tx.Commit();
                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] Temporary MEP cleaned. Requested=" + existing.Count +
                        ", Deleted=" + (deleted != null ? deleted.Count : 0));
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Temporary MEP cleanup failed. " + ex);
            }
        }

        private static ElementId ResolveReportEquipmentId(Document doc, RoomLayoutPlanDto plan)
        {
            if (doc == null || plan == null)
            {
                return ElementId.InvalidElementId;
            }

            if (plan.ActiveGeneratedElements != null)
            {
                ElementId savedId = ResolveElementId(doc, plan.ActiveGeneratedElements.EquipmentInstance);
                if (savedId != ElementId.InvalidElementId && doc.GetElement(savedId) != null)
                {
                    return savedId;
                }
            }

            if (!string.IsNullOrWhiteSpace(plan.RoomKey) &&
                RoomCustomFamilyPlacementService.TryGetPlacedFamilyInstanceId(
                    doc,
                    plan.RoomKey,
                    out ElementId placedId) &&
                placedId != null &&
                placedId != ElementId.InvalidElementId &&
                doc.GetElement(placedId) != null)
            {
                return placedId;
            }

            return ElementId.InvalidElementId;
        }

        private static ElementId ResolveWallElementId(Document doc, LayoutWallSelectionDto wall)
        {
            if (doc == null || wall == null)
            {
                return ElementId.InvalidElementId;
            }

            if (!string.IsNullOrWhiteSpace(wall.UniqueId))
            {
                Element byUid = doc.GetElement(wall.UniqueId);
                if (byUid is Wall)
                {
                    return byUid.Id;
                }
            }

            if (wall.ElementId > 0)
            {
                ElementId id = new ElementId(wall.ElementId);
                if (doc.GetElement(id) is Wall)
                {
                    return id;
                }
            }

            return ElementId.InvalidElementId;
        }

        private static bool HasResolvedLayoutElements(Document doc, IEnumerable<LayoutElementRefDto> refs)
        {
            if (doc == null || refs == null)
            {
                return false;
            }

            foreach (LayoutElementRefDto elementRef in refs)
            {
                ElementId id = ResolveElementId(doc, elementRef);
                if (id != ElementId.InvalidElementId && doc.GetElement(id) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendUniqueIds(List<ElementId> target, IEnumerable<ElementId> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            HashSet<int> known = new HashSet<int>(
                target
                    .Where(x => x != null && x != ElementId.InvalidElementId)
                    .Select(x => x.IntegerValue));

            foreach (ElementId id in source)
            {
                if (id == null || id == ElementId.InvalidElementId || !known.Add(id.IntegerValue))
                {
                    continue;
                }

                target.Add(id);
            }
        }

        private static View3D Resolve3DView(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(x => x != null && !x.IsTemplate && !x.IsPerspective)
                .OrderBy(x => string.Equals(x.Name, "{3D}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.Name)
                .FirstOrDefault();
        }

        private static ViewPlan ResolveFloorPlan(Document doc, RoomLayoutPlanDto plan, ViewPlan preferredPlanView)
        {
            string levelText = plan != null ? plan.LevelText ?? string.Empty : string.Empty;

            // If the user is already looking at the correct floor plan, use that view as the
            // source. This avoids arbitrarily selecting another cropped floor-plan view on the
            // same level and produces a report consistent with what the user is working on.
            if (preferredPlanView != null &&
                !preferredPlanView.IsTemplate &&
                preferredPlanView.ViewType == ViewType.FloorPlan &&
                (string.IsNullOrWhiteSpace(levelText) ||
                 (preferredPlanView.GenLevel != null &&
                  levelText.IndexOf(preferredPlanView.GenLevel.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return preferredPlanView;
            }

            List<ViewPlan> views = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(x => x != null && !x.IsTemplate && x.ViewType == ViewType.FloorPlan)
                .ToList();

            if (views.Count == 0)
            {
                return null;
            }

            List<ViewPlan> sameLevel = views
                .Where(x =>
                    x.GenLevel != null &&
                    !string.IsNullOrWhiteSpace(levelText) &&
                    levelText.IndexOf(x.GenLevel.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            if (sameLevel.Count > 0)
            {
                // Prefer an architectural/base plan name when multiple views share the same level.
                return sameLevel
                    .OrderBy(x => (x.Name ?? string.Empty).IndexOf("Architect", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                    .ThenByDescending(GetCropArea)
                    .ThenBy(x => x.Name)
                    .FirstOrDefault();
            }

            return views
                .OrderByDescending(GetCropArea)
                .ThenBy(x => x.Name)
                .FirstOrDefault();
        }

        private static double GetCropArea(ViewPlan view)
        {
            try
            {
                BoundingBoxXYZ crop = view != null ? view.CropBox : null;
                if (crop == null)
                {
                    return 0.0;
                }

                return Math.Max(0.0, crop.Max.X - crop.Min.X) *
                       Math.Max(0.0, crop.Max.Y - crop.Min.Y);
            }
            catch
            {
                return 0.0;
            }
        }

        private static BoundingBoxXYZ BuildLayoutPlanReportSectionBox(Document doc, View3D view, RoomLayoutPlanDto plan, IReadOnlyCollection<ElementId> temporaryMepElementIds)
        {
            BoundingBoxXYZ merged = CollectSavedLayoutElementBox(doc, view, plan, temporaryMepElementIds);
            merged = Union(merged, CollectRoomVisualizationBox(doc, view, plan));
            if (merged == null)
            {
                merged = CollectModelBoundingBox(doc, view, false);
            }

            if (merged == null)
            {
                return null;
            }

            // Page 1 Main 3D is an overview, not a tight equipment crop. Keep a deliberately
            // wider architectural context around the saved room/layout so the AHU is not
            // visually oversized and nearby rooms/walls remain visible in the report.
            // This affects only the temporary report view and never the user's active 3D view.
            double pad = ToFeet(8000.0);
            double minZ = merged.Min.Z - ToFeet(1500.0);
            double maxZ = merged.Max.Z + ToFeet(3500.0);
            if (maxZ - minZ < ToFeet(7500.0))
            {
                maxZ = minZ + ToFeet(7500.0);
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(merged.Min.X - pad, merged.Min.Y - pad, minZ),
                Max = new XYZ(merged.Max.X + pad, merged.Max.Y + pad, maxZ)
            };
        }

        private static List<BoundingBoxXYZ> CollectMain3DFramingBoxes(
            Document doc,
            View3D view,
            RoomLayoutPlanDto plan,
            IReadOnlyCollection<ElementId> temporaryMepElementIds,
            BoundingBoxXYZ sectionBox)
        {
            List<BoundingBoxXYZ> boxes = new List<BoundingBoxXYZ>();
            if (doc == null || view == null || plan == null || sectionBox == null)
            {
                return boxes;
            }

            // Always keep the actual room/layout geometry in the framing set.
            BoundingBoxXYZ roomBox = CollectRoomVisualizationBox(doc, view, plan);
            BoundingBoxXYZ layoutBox = CollectSavedLayoutElementBox(doc, view, plan, temporaryMepElementIds);
            AddClippedBox(boxes, roomBox, sectionBox);
            AddClippedBox(boxes, layoutBox, sectionBox);

            // Frame against nearby ARCHITECTURAL geometry, not against an arbitrary symmetric
            // 8 m empty padding. Long/remote elements are clipped to the report SectionBox first,
            // so one wall cannot drag the exported view far away from the target room.
            BuiltInCategory[] contextCategories =
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

            int contextCount = 0;
            foreach (BuiltInCategory category in contextCategories)
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsNotElementType();

                foreach (Element element in collector)
                {
                    BoundingBoxXYZ elementBox = GetModelBox(element, view);
                    BoundingBoxXYZ clipped = IntersectBox(elementBox, sectionBox);
                    if (clipped == null)
                    {
                        continue;
                    }

                    boxes.Add(clipped);
                    contextCount++;
                }
            }

            // If category filtering found no usable context, preserve the previous behavior as a
            // safe fallback, but use the unpadded actual room/layout box rather than the full
            // SectionBox so the export still avoids excessive blank space.
            if (boxes.Count == 0)
            {
                BoundingBoxXYZ fallback = Union(roomBox, layoutBox);
                if (fallback != null)
                {
                    boxes.Add(fallback);
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Main3D framing boxes collected. Count=" + boxes.Count +
                ", ArchitecturalContext=" + contextCount +
                ", Section=" + FormatBox(sectionBox));
            return boxes;
        }

        private static void AddClippedBox(List<BoundingBoxXYZ> target, BoundingBoxXYZ box, BoundingBoxXYZ clip)
        {
            if (target == null || box == null)
            {
                return;
            }

            BoundingBoxXYZ clipped = clip != null ? IntersectBox(box, clip) : box;
            if (clipped != null)
            {
                target.Add(clipped);
            }
        }

        private static BoundingBoxXYZ IntersectBox(BoundingBoxXYZ box, BoundingBoxXYZ clip)
        {
            if (box == null || clip == null)
            {
                return null;
            }

            double minX = Math.Max(box.Min.X, clip.Min.X);
            double minY = Math.Max(box.Min.Y, clip.Min.Y);
            double minZ = Math.Max(box.Min.Z, clip.Min.Z);
            double maxX = Math.Min(box.Max.X, clip.Max.X);
            double maxY = Math.Min(box.Max.Y, clip.Max.Y);
            double maxZ = Math.Min(box.Max.Z, clip.Max.Z);
            if (maxX <= minX || maxY <= minY || maxZ <= minZ)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private static void ApplyProjectedCrop(
            View view,
            IEnumerable<BoundingBoxXYZ> modelBoxes,
            double targetAspect,
            double paddingRatio,
            double minimumPadding)
        {
            if (view == null || modelBoxes == null)
            {
                return;
            }

            // Reuse the same crop reset rules as ApplyCrop. A duplicated source view can inherit
            // a Scope Box or a custom crop region that prevents the report framing from taking
            // effect.
            try
            {
                Parameter scopeBox = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (scopeBox != null && !scopeBox.IsReadOnly)
                {
                    scopeBox.Set(ElementId.InvalidElementId);
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Main3D scope box clear skipped: " + ex.Message);
            }

            try
            {
                using (ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager())
                {
                    if (manager != null)
                    {
                        System.Reflection.MethodInfo remove = manager.GetType().GetMethod("RemoveCropRegionShape");
                        if (remove != null)
                        {
                            remove.Invoke(manager, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Main3D crop shape reset skipped: " + ex.Message);
            }

            view.CropBoxActive = true;
            view.CropBoxVisible = false;

            BoundingBoxXYZ current = view.CropBox;
            Transform viewToModel = current != null && current.Transform != null
                ? current.Transform
                : Transform.Identity;
            Transform modelToView = viewToModel.Inverse;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            int projectedBoxCount = 0;

            foreach (BoundingBoxXYZ box in modelBoxes)
            {
                if (box == null)
                {
                    continue;
                }

                XYZ[] corners =
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                    new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                };

                foreach (XYZ modelPoint in corners)
                {
                    XYZ local = modelToView.OfPoint(modelPoint);
                    minX = Math.Min(minX, local.X);
                    minY = Math.Min(minY, local.Y);
                    maxX = Math.Max(maxX, local.X);
                    maxY = Math.Max(maxY, local.Y);
                }

                projectedBoxCount++;
            }

            if (projectedBoxCount == 0 || minX == double.MaxValue || minY == double.MaxValue)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Main3D projected crop skipped: no framing boxes.");
                return;
            }

            double width = Math.Max(1e-6, maxX - minX);
            double height = Math.Max(1e-6, maxY - minY);
            double padX = Math.Max(minimumPadding, width * Math.Max(0.0, paddingRatio));
            double padY = Math.Max(minimumPadding, height * Math.Max(0.0, paddingRatio));
            minX -= padX;
            maxX += padX;
            minY -= padY;
            maxY += padY;

            // Match the Main 3D PDF panel (366 x 245 mm) in VIEW coordinates. This prevents
            // PDF FitImage from introducing a second pair of white bands around an otherwise
            // correctly cropped Revit export.
            if (targetAspect > 0.0)
            {
                width = Math.Max(1e-6, maxX - minX);
                height = Math.Max(1e-6, maxY - minY);
                if (width / height < targetAspect)
                {
                    double targetWidth = height * targetAspect;
                    double extra = (targetWidth - width) * 0.5;
                    minX -= extra;
                    maxX += extra;
                }
                else
                {
                    double targetHeight = width / targetAspect;
                    double extra = (targetHeight - height) * 0.5;
                    minY -= extra;
                    maxY += extra;
                }
            }

            double minZ = current != null ? current.Min.Z : -10000.0;
            double maxZ = current != null ? current.Max.Z : 10000.0;
            if (maxZ <= minZ)
            {
                minZ = -10000.0;
                maxZ = 10000.0;
            }

            view.CropBox = new BoundingBoxXYZ
            {
                Transform = viewToModel,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Main3D projected crop applied. Boxes=" + projectedBoxCount +
                ", TargetAspect=" + targetAspect.ToString("F4") +
                ", ViewLocalMin=(" + FromFeet(minX).ToString("F0") + "," + FromFeet(minY).ToString("F0") + ")" +
                ", ViewLocalMax=(" + FromFeet(maxX).ToString("F0") + "," + FromFeet(maxY).ToString("F0") + ") mm");
        }

        private static BoundingBoxXYZ BuildKeyPlan3DSectionBox(Document doc, View view, RoomLayoutPlanDto plan, IReadOnlyCollection<ElementId> temporaryMepElementIds, double aspect)
        {
            BoundingBoxXYZ roomBox = CollectRoomVisualizationBox(doc, view, plan);
            BoundingBoxXYZ layoutBox = CollectSavedLayoutElementBox(doc, view, plan, temporaryMepElementIds);
            BoundingBoxXYZ basis = roomBox ?? layoutBox;
            if (basis == null)
            {
                return null;
            }

            // Page 2 is an A2 room-detail drawing at 1:50. The PDF image content area is
            // 562 x 388 mm, therefore a true 1:50 viewport corresponds to 28,100 x 19,400 mm
            // in model space. Centre that fixed-size viewport on the selected room.
            const double targetScale = 50.0;
            const double paperContentWidthMm = 562.0;
            const double paperContentHeightMm = 388.0;
            double targetWidth = ToFeet(paperContentWidthMm * targetScale);
            double targetHeight = ToFeet(paperContentHeightMm * targetScale);

            // Keep the requested aspect parameter authoritative in case the PDF frame changes
            // later. For the current 562/388 frame this is already exact.
            double targetAspect = aspect > 1e-9 ? aspect : (paperContentWidthMm / paperContentHeightMm);
            if (Math.Abs((targetWidth / targetHeight) - targetAspect) > 1e-6)
            {
                targetHeight = targetWidth / targetAspect;
            }

            double centerX = (basis.Min.X + basis.Max.X) * 0.5;
            double centerY = (basis.Min.Y + basis.Max.Y) * 0.5;
            double minX = centerX - targetWidth * 0.5;
            double maxX = centerX + targetWidth * 0.5;
            double minY = centerY - targetHeight * 0.5;
            double maxY = centerY + targetHeight * 0.5;

            // A very large room cannot physically fit on the A2 content frame at 1:50. Avoid
            // clipping in that exceptional case by expanding only as much as necessary and log
            // that the effective printed scale will be smaller than 1:50.
            const double safetyMarginMm = 300.0;
            double requiredWidth = (basis.Max.X - basis.Min.X) + (2.0 * ToFeet(safetyMarginMm));
            double requiredHeight = (basis.Max.Y - basis.Min.Y) + (2.0 * ToFeet(safetyMarginMm));
            if (requiredWidth > targetWidth || requiredHeight > targetHeight)
            {
                double fitScale = Math.Max(requiredWidth / targetWidth, requiredHeight / targetHeight);
                targetWidth *= fitScale;
                targetHeight *= fitScale;
                minX = centerX - targetWidth * 0.5;
                maxX = centerX + targetWidth * 0.5;
                minY = centerY - targetHeight * 0.5;
                maxY = centerY + targetHeight * 0.5;
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Room detail exceeds A2 1:50 viewport; expanded to avoid clipping. " +
                    "Effective scale is smaller than 1:50. FitFactor=" + fitScale.ToString("F4"));
            }

            BoundingBoxXYZ zBox = Union(roomBox, layoutBox) ?? basis;
            double minZ = zBox.Min.Z - ToFeet(600.0);
            double maxZ = zBox.Max.Z + ToFeet(1000.0);
            if (maxZ - minZ < ToFeet(4500.0))
            {
                maxZ = minZ + ToFeet(4500.0);
            }

            BoundingBoxXYZ result = new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Room detail TOP-3D section box (target 1:50). Room=" + FormatBox(roomBox) +
                ", Layout=" + FormatBox(layoutBox) +
                ", Result=" + FormatBox(result));
            return result;
        }

        private static void PrepareKeyPlan3DVisibility(Document doc, View3D view, RoomLayoutPlanDto plan, BoundingBoxXYZ sectionBox, IReadOnlyCollection<ElementId> temporaryMepElementIds)
        {
            if (doc == null || view == null || plan == null)
            {
                return;
            }

            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan3D view template detach skipped: " + ex.Message);
            }

            try
            {
                view.DetailLevel = ViewDetailLevel.Fine;
            }
            catch
            {
            }

            HashSet<ElementId> elementIds = new HashSet<ElementId>(GetSavedElementIds(doc, plan, temporaryMepElementIds));
            foreach (DirectShape shape in new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>())
            {
                string name = shape != null ? (shape.Name ?? string.Empty) : string.Empty;
                string data = shape != null ? (shape.ApplicationDataId ?? string.Empty) : string.Empty;
                if (shape == null ||
                    (name.IndexOf("ROOMVIS", StringComparison.OrdinalIgnoreCase) < 0 &&
                     data.IndexOf("REGION::", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                BoundingBoxXYZ box = GetModelBox(shape, view);
                if (box != null && BoxesIntersectXy(box, sectionBox))
                {
                    elementIds.Add(shape.Id);
                }
            }

            BuiltInCategory[] requiredCategories =
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory
            };

            int categoriesShown = 0;
            foreach (BuiltInCategory category in requiredCategories)
            {
                try
                {
                    ElementId id = new ElementId(category);
                    if (view.CanCategoryBeHidden(id))
                    {
                        view.SetCategoryHidden(id, false);
                        categoriesShown++;
                    }
                }
                catch
                {
                }
            }

            List<ElementId> hiddenIds = new List<ElementId>();
            foreach (ElementId id in elementIds)
            {
                try
                {
                    Element element = doc.GetElement(id);
                    if (element != null && element.IsHidden(view))
                    {
                        hiddenIds.Add(id);
                    }
                }
                catch
                {
                }
            }

            if (hiddenIds.Count > 0)
            {
                try
                {
                    view.UnhideElements(hiddenIds);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan3D element unhide skipped: " + ex.Message);
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] KeyPlan3D visibility prepared. Elements=" + elementIds.Count +
                ", CategoriesShown=" + categoriesShown +
                ", ElementsUnhidden=" + hiddenIds.Count +
                ", View=" + (view.Name ?? string.Empty));
        }

        private static BoundingBoxXYZ BuildKeyPlanCropBox(Document doc, View view, RoomLayoutPlanDto plan, double aspect)
        {
            // Key Plan is a room-detail image. The room itself must drive the crop so the
            // AHU room fills the small PDF panel instead of being reduced by long MEP runs.
            BoundingBoxXYZ roomBox = CollectRoomVisualizationBox(doc, view, plan);
            if (roomBox != null)
            {
                BoundingBoxXYZ crop = ExpandBox(roomBox, ToFeet(1000.0), aspect);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] KeyPlan crop source=RoomVisualization, box=" + FormatBox(crop));
                return crop;
            }

            // Backward-compatible fallback for saved layouts created before room visualization
            // metadata was available. Keep the margin modest; the old 5.5 m margin made the
            // target room much too small in the Key Plan panel.
            BoundingBoxXYZ savedLayoutBox = CollectSavedLayoutElementBox(doc, view, plan, null);
            if (savedLayoutBox != null)
            {
                BoundingBoxXYZ crop = ExpandBox(savedLayoutBox, ToFeet(1200.0), aspect);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] KeyPlan crop source=SavedLayoutElements, box=" + FormatBox(crop));
                return crop;
            }

            BoundingBoxXYZ fallback = CollectViewModelBoundingBox(doc, view);
            BoundingBoxXYZ fallbackCrop = ExpandBox(fallback, ToFeet(1500.0), aspect);
            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] KeyPlan crop source=ViewModelFallback, box=" + FormatBox(fallbackCrop));
            return fallbackCrop;
        }

        private static BoundingBoxXYZ BuildOverallTopCropBox(Document doc, View view, RoomLayoutPlanDto plan, IReadOnlyCollection<ElementId> temporaryMepElementIds, double aspect)
        {
            // Use only the principal DWG visible in this level view. Unioning every ImportInstance
            // and all document-wide model elements can pull the crop toward remote objects and
            // leave the real building in one corner of the exported page.
            BoundingBoxXYZ primaryDwg = CollectPrimaryDwgBoundingBox(doc, view, plan);
            BoundingBoxXYZ box = primaryDwg ?? CollectViewModelBoundingBox(doc, view);

            // Keep the saved AHU/MEP layout in frame if it extends just beyond the DWG extents.
            box = Union(box, CollectSavedLayoutElementBox(doc, view, plan, temporaryMepElementIds));

            BoundingBoxXYZ crop = ExpandBoxByRatio(
                box,
                0.045,
                ToFeet(900.0),
                ToFeet(2200.0),
                aspect);

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] OverallTop crop source=" +
                (primaryDwg != null ? "PrimaryDWG" : "VisibleModel") +
                ", box=" + FormatBox(crop));
            return crop;
        }

        private static BoundingBoxXYZ CollectSavedLayoutElementBox(Document doc, View view, RoomLayoutPlanDto plan, IReadOnlyCollection<ElementId> temporaryMepElementIds = null)
        {
            BoundingBoxXYZ merged = null;
            foreach (ElementId id in GetSavedElementIds(doc, plan, temporaryMepElementIds))
            {
                Element element = doc.GetElement(id);
                merged = Union(merged, GetModelBox(element, view));
            }

            return merged;
        }

        private static IEnumerable<ElementId> GetSavedElementIds(Document doc, RoomLayoutPlanDto plan, IReadOnlyCollection<ElementId> temporaryMepElementIds = null)
        {
            if (doc == null || plan == null)
            {
                yield break;
            }

            if (plan.ActiveGeneratedElements != null)
            {
                ElementId id = ResolveElementId(doc, plan.ActiveGeneratedElements.EquipmentInstance);
                if (id != ElementId.InvalidElementId)
                {
                    yield return id;
                }

                foreach (LayoutElementRefDto elementRef in plan.ActiveGeneratedElements.DuctElements ?? new List<LayoutElementRefDto>())
                {
                    id = ResolveElementId(doc, elementRef);
                    if (id != ElementId.InvalidElementId)
                    {
                        yield return id;
                    }
                }

                foreach (LayoutElementRefDto elementRef in plan.ActiveGeneratedElements.PipeElements ?? new List<LayoutElementRefDto>())
                {
                    id = ResolveElementId(doc, elementRef);
                    if (id != ElementId.InvalidElementId)
                    {
                        yield return id;
                    }
                }
            }

            if (temporaryMepElementIds != null)
            {
                foreach (ElementId temporaryId in temporaryMepElementIds)
                {
                    if (temporaryId != null &&
                        temporaryId != ElementId.InvalidElementId &&
                        doc.GetElement(temporaryId) != null)
                    {
                        yield return temporaryId;
                    }
                }
            }
        }

        private static ElementId ResolveElementId(Document doc, LayoutElementRefDto elementRef)
        {
            if (doc == null || elementRef == null)
            {
                return ElementId.InvalidElementId;
            }

            if (!string.IsNullOrWhiteSpace(elementRef.UniqueId))
            {
                Element byUid = doc.GetElement(elementRef.UniqueId);
                if (byUid != null)
                {
                    return byUid.Id;
                }
            }

            return elementRef.ElementId > 0 ? new ElementId(elementRef.ElementId) : ElementId.InvalidElementId;
        }

        private static BoundingBoxXYZ CollectRoomVisualizationBox(Document doc, View view, RoomLayoutPlanDto plan)
        {
            if (doc == null || plan == null || string.IsNullOrWhiteSpace(plan.RoomKey))
            {
                return null;
            }

            string roomKey = (plan.RoomKey ?? string.Empty).Trim();
            string roomName = (plan.RoomName ?? string.Empty).Trim();
            BoundingBoxXYZ exactMerged = null;
            int exactCount = 0;
            List<Tuple<DirectShape, BoundingBoxXYZ>> fallbackCandidates = new List<Tuple<DirectShape, BoundingBoxXYZ>>();

            foreach (DirectShape shape in new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>())
            {
                string name = (shape.Name ?? string.Empty).Trim();
                string data = (shape.ApplicationDataId ?? string.Empty).Trim();

                bool looksLikeRoomVisualization =
                    name.IndexOf("ROOMVIS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    data.IndexOf("REGION::", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeRoomVisualization)
                {
                    continue;
                }

                BoundingBoxXYZ shapeBox = GetModelBox(shape, view);
                if (shapeBox == null)
                {
                    continue;
                }

                // Do NOT use a broad substring match here. For example RoomKey="AHU ROOM"
                // would otherwise merge AHU ROOM 1 / 2 / 3 / 41 / 42 ... into one huge box.
                bool exact =
                    string.Equals(data, roomKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(data, "REGION::" + roomKey, StringComparison.OrdinalIgnoreCase) ||
                    data.EndsWith("::" + roomKey, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, roomKey, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("__" + roomKey, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("::" + roomKey, StringComparison.OrdinalIgnoreCase);

                if (exact)
                {
                    exactMerged = Union(exactMerged, shapeBox);
                    exactCount++;
                    continue;
                }

                bool loose =
                    (!string.IsNullOrWhiteSpace(roomName) &&
                     (name.IndexOf(roomName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                      data.IndexOf(roomName, StringComparison.OrdinalIgnoreCase) >= 0)) ||
                    name.IndexOf(roomKey, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    data.IndexOf(roomKey, StringComparison.OrdinalIgnoreCase) >= 0;

                if (loose)
                {
                    fallbackCandidates.Add(Tuple.Create(shape, shapeBox));
                }
            }

            if (exactMerged != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] RoomVisualization exact match count=" + exactCount +
                    ", RoomKey=" + roomKey + ", box=" + FormatBox(exactMerged));
                return exactMerged;
            }

            // Old saved plans may not contain the exact REGION::<RoomKey> metadata. In that
            // case choose ONE matching room visualization nearest to the saved equipment,
            // rather than unioning every similarly-named AHU room in the project.
            BoundingBoxXYZ equipmentBox = GetEquipmentBox(doc, view, plan);
            XYZ equipmentCenter = GetCenter(equipmentBox);
            Tuple<DirectShape, BoundingBoxXYZ> best = null;
            double bestDistance = double.MaxValue;
            foreach (Tuple<DirectShape, BoundingBoxXYZ> candidate in fallbackCandidates)
            {
                XYZ center = GetCenter(candidate.Item2);
                double distance = equipmentCenter != null && center != null
                    ? center.DistanceTo(equipmentCenter)
                    : 0.0;
                if (best == null || distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] RoomVisualization fallback nearest. RoomKey=" + roomKey +
                    ", candidates=" + fallbackCandidates.Count +
                    ", box=" + FormatBox(best.Item2));
                return best.Item2;
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] RoomVisualization not found. RoomKey=" + roomKey +
                ", RoomName=" + roomName);
            return null;
        }

        private static BoundingBoxXYZ GetEquipmentBox(Document doc, View view, RoomLayoutPlanDto plan)
        {
            if (doc == null || plan == null || plan.ActiveGeneratedElements == null)
            {
                return null;
            }

            ElementId id = ResolveElementId(doc, plan.ActiveGeneratedElements.EquipmentInstance);
            return id != ElementId.InvalidElementId
                ? GetModelBox(doc.GetElement(id), view)
                : null;
        }

        private static XYZ GetCenter(BoundingBoxXYZ box)
        {
            return box == null
                ? null
                : new XYZ(
                    (box.Min.X + box.Max.X) * 0.5,
                    (box.Min.Y + box.Max.Y) * 0.5,
                    (box.Min.Z + box.Max.Z) * 0.5);
        }

        private static BoundingBoxXYZ CollectPrimaryDwgBoundingBox(Document doc, View view, RoomLayoutPlanDto plan)
        {
            ImportInstance instance = ResolvePrimaryDwgInstance(doc, view, plan);
            return instance != null
                ? instance.get_BoundingBox(null) ?? instance.get_BoundingBox(view)
                : null;
        }

        private static ImportInstance ResolvePrimaryDwgInstance(Document doc, View view, RoomLayoutPlanDto plan)
        {
            if (doc == null)
            {
                return null;
            }

            // The complete Rooms workflow already tracks the exact DWG used by Analyze/Generate
            // through DwgSessionManager.  Prefer that instance for report export.  The previous
            // report code guessed by largest/containing bounding box; in projects containing more
            // than one CAD import that can select a different drawing and makes Page 2 impossible
            // to align deterministically.
            try
            {
                CadToRevit.Services.DwgSessionInfo session = CadToRevit.Services.DwgSessionManager.Get(doc);
                if (session != null &&
                    session.LinkInstanceId != null &&
                    session.LinkInstanceId != ElementId.InvalidElementId)
                {
                    ImportInstance sessionImport = doc.GetElement(session.LinkInstanceId) as ImportInstance;
                    if (sessionImport != null)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] Primary DWG resolved from active session. ImportId=" +
                            sessionImport.Id.IntegerValue + ", Name=" + (sessionImport.Name ?? string.Empty));
                        return sessionImport;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Active DWG session resolve skipped. " + ex.Message);
            }

            ImportInstance bestContainingLayout = null;
            double bestContainingArea = 0.0;
            ImportInstance bestOverall = null;
            double bestOverallArea = 0.0;
            XYZ layoutCenter = GetCenter(GetEquipmentBox(doc, view, plan));

            foreach (ImportInstance instance in new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>())
            {
                BoundingBoxXYZ box = instance.get_BoundingBox(null) ?? instance.get_BoundingBox(view);
                if (box == null)
                {
                    continue;
                }

                double width = Math.Max(0.0, box.Max.X - box.Min.X);
                double height = Math.Max(0.0, box.Max.Y - box.Min.Y);
                double area = width * height;
                if (area > bestOverallArea)
                {
                    bestOverall = instance;
                    bestOverallArea = area;
                }

                bool containsLayout = layoutCenter != null &&
                    layoutCenter.X >= box.Min.X && layoutCenter.X <= box.Max.X &&
                    layoutCenter.Y >= box.Min.Y && layoutCenter.Y <= box.Max.Y;
                if (containsLayout && area > bestContainingArea)
                {
                    bestContainingLayout = instance;
                    bestContainingArea = area;
                }
            }

            ImportInstance fallback = bestContainingLayout ?? bestOverall;
            if (fallback != null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Primary DWG resolved by fallback heuristic. ImportId=" +
                    fallback.Id.IntegerValue + ", Name=" + (fallback.Name ?? string.Empty));
            }

            return fallback;
        }

        private static BoundingBoxXYZ CollectViewModelBoundingBox(Document doc, View view)
        {
            BoundingBoxXYZ merged = null;
            foreach (BuiltInCategory category in GetModelCategories())
            {
                FilteredElementCollector collector = view != null && view.Id != ElementId.InvalidElementId
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

                foreach (Element element in collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    merged = Union(merged, GetBox(element, view));
                }
            }

            return merged;
        }

        private static BoundingBoxXYZ CollectModelBoundingBox(Document doc, View view, bool includeImports)
        {
            BoundingBoxXYZ merged = null;
            foreach (BuiltInCategory category in GetModelCategories())
            {
                FilteredElementCollector collector = view != null && view.Id != ElementId.InvalidElementId
                    ? new FilteredElementCollector(doc, view.Id)
                    : new FilteredElementCollector(doc);

                foreach (Element element in collector
                    .OfCategory(category)
                    .WhereElementIsNotElementType())
                {
                    if (!includeImports && element is ImportInstance)
                    {
                        continue;
                    }

                    merged = Union(merged, GetBox(element, view));
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
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_MechanicalEquipment
            };
        }

        private static BoundingBoxXYZ GetBox(Element element, View view)
        {
            return element != null ? element.get_BoundingBox(view) ?? element.get_BoundingBox(null) : null;
        }

        private static BoundingBoxXYZ GetModelBox(Element element, View view)
        {
            // Crop calculations must be based on model extents, not on the possibly already
            // cropped source plan view. Otherwise elements outside the source crop return null
            // or a clipped box and the exported report keeps the wrong zoom/position.
            return element != null ? element.get_BoundingBox(null) ?? element.get_BoundingBox(view) : null;
        }

        private static BoundingBoxXYZ Union(BoundingBoxXYZ a, BoundingBoxXYZ b)
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
                Min = new XYZ(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
                Max = new XYZ(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z))
            };
        }

        private static BoundingBoxXYZ ExpandBox(BoundingBoxXYZ box, double pad, double aspect)
        {
            if (box == null)
            {
                return null;
            }

            double minX = box.Min.X - pad;
            double maxX = box.Max.X + pad;
            double minY = box.Min.Y - pad;
            double maxY = box.Max.Y + pad;
            ExpandXyToAspect(ref minX, ref maxX, ref minY, ref maxY, aspect);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, -10000.0),
                Max = new XYZ(maxX, maxY, 10000.0)
            };
        }

        private static BoundingBoxXYZ ExpandBoxByRatio(
            BoundingBoxXYZ box,
            double ratio,
            double minPad,
            double maxPad,
            double aspect)
        {
            if (box == null)
            {
                return null;
            }

            double width = Math.Max(0.0, box.Max.X - box.Min.X);
            double height = Math.Max(0.0, box.Max.Y - box.Min.Y);
            double pad = Math.Max(width, height) * Math.Max(0.0, ratio);
            pad = Math.Max(minPad, Math.Min(maxPad, pad));
            return ExpandBox(box, pad, aspect);
        }

        private static string FormatBox(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return "<null>";
            }

            return "min=(" +
                   FromFeet(box.Min.X).ToString("F0") + "," +
                   FromFeet(box.Min.Y).ToString("F0") + "), max=(" +
                   FromFeet(box.Max.X).ToString("F0") + "," +
                   FromFeet(box.Max.Y).ToString("F0") + ") mm";
        }

        private static void ExpandXyToAspect(ref double minX, ref double maxX, ref double minY, ref double maxY, double aspect)
        {
            if (aspect <= 0)
            {
                return;
            }

            double width = Math.Max(1e-6, maxX - minX);
            double height = Math.Max(1e-6, maxY - minY);
            if (width / height < aspect)
            {
                double targetWidth = height * aspect;
                double extra = (targetWidth - width) * 0.5;
                minX -= extra;
                maxX += extra;
            }
            else
            {
                double targetHeight = width / aspect;
                double extra = (targetHeight - height) * 0.5;
                minY -= extra;
                maxY += extra;
            }
        }

        private static void ApplyCrop(View view, BoundingBoxXYZ modelCrop)
        {
            if (view == null || modelCrop == null)
            {
                return;
            }

            // A duplicated plan may inherit a Scope Box or a non-rectangular crop region.
            // Either can prevent View.CropBox from taking effect. Clear/reset them on this
            // temporary report view only.
            try
            {
                Parameter scopeBox = view.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (scopeBox != null && !scopeBox.IsReadOnly)
                {
                    scopeBox.Set(ElementId.InvalidElementId);
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Scope box clear skipped: " + ex.Message);
            }

            try
            {
                using (ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager())
                {
                    if (manager != null)
                    {
                        // Keep compile compatibility across Revit minor API variations.
                        // If a custom/non-rectangular crop exists, remove it before assigning
                        // the rectangular CropBox below.
                        System.Reflection.MethodInfo remove = manager.GetType().GetMethod("RemoveCropRegionShape");
                        if (remove != null)
                        {
                            remove.Invoke(manager, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Crop shape reset skipped: " + ex.Message);
            }

            view.CropBoxActive = true;
            view.CropBoxVisible = false;

            // Revit CropBox.Min/Max are expressed in the VIEW coordinate system, whereas all
            // report boxes above are model/world XYZ. The previous implementation assigned
            // world coordinates directly, which is why the target model remained in one
            // corner and the requested Key/Overall crop appeared to have no effect.
            BoundingBoxXYZ current = view.CropBox;
            Transform viewToModel = current != null && current.Transform != null
                ? current.Transform
                : Transform.Identity;
            Transform modelToView = viewToModel.Inverse;

            // Use all eight corners. For an isometric 3D view, model Z contributes to the
            // projected view X/Y extents; using only four XY corners at a single Z level can
            // leave the crop too tight and makes a larger SectionBox appear not to zoom out.
            XYZ[] modelCorners =
            {
                new XYZ(modelCrop.Min.X, modelCrop.Min.Y, modelCrop.Min.Z),
                new XYZ(modelCrop.Min.X, modelCrop.Min.Y, modelCrop.Max.Z),
                new XYZ(modelCrop.Min.X, modelCrop.Max.Y, modelCrop.Min.Z),
                new XYZ(modelCrop.Min.X, modelCrop.Max.Y, modelCrop.Max.Z),
                new XYZ(modelCrop.Max.X, modelCrop.Min.Y, modelCrop.Min.Z),
                new XYZ(modelCrop.Max.X, modelCrop.Min.Y, modelCrop.Max.Z),
                new XYZ(modelCrop.Max.X, modelCrop.Max.Y, modelCrop.Min.Z),
                new XYZ(modelCrop.Max.X, modelCrop.Max.Y, modelCrop.Max.Z)
            };

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (XYZ modelPoint in modelCorners)
            {
                XYZ local = modelToView.OfPoint(modelPoint);
                minX = Math.Min(minX, local.X);
                minY = Math.Min(minY, local.Y);
                maxX = Math.Max(maxX, local.X);
                maxY = Math.Max(maxY, local.Y);
            }

            double minZ = current != null ? current.Min.Z : -10000.0;
            double maxZ = current != null ? current.Max.Z : 10000.0;
            if (maxZ <= minZ)
            {
                minZ = -10000.0;
                maxZ = 10000.0;
            }

            BoundingBoxXYZ viewCrop = new BoundingBoxXYZ
            {
                Transform = viewToModel,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
            view.CropBox = viewCrop;

            // Annotation symbols (section/elevation heads, tags, etc.) can otherwise extend well
            // outside the model crop and cause Revit image export to leave large blank areas.
            try
            {
                Parameter annotationCrop = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                if (annotationCrop != null && !annotationCrop.IsReadOnly)
                {
                    annotationCrop.Set(1);
                }

                using (ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager())
                {
                    if (manager != null && manager.CanHaveAnnotationCrop)
                    {
                        manager.LeftAnnotationCropOffset = 0.0;
                        manager.RightAnnotationCropOffset = 0.0;
                        manager.TopAnnotationCropOffset = 0.0;
                        manager.BottomAnnotationCropOffset = 0.0;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Annotation crop setup skipped: " + ex.Message);
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Crop applied. model=" + FormatBox(modelCrop) +
                ", viewLocalMin=(" + FromFeet(minX).ToString("F0") + "," + FromFeet(minY).ToString("F0") + ")" +
                ", viewLocalMax=(" + FromFeet(maxX).ToString("F0") + "," + FromFeet(maxY).ToString("F0") + ") mm");
        }

        private static void PrepareUnifiedOverallTop3DVisibility(
            Document doc,
            View3D view,
            RoomLayoutPlanDto plan,
            ImportInstance primaryDwg,
            IReadOnlyCollection<ElementId> temporaryMepElementIds,
            BoundingBoxXYZ selectedRoomBox)
        {
            if (doc == null || view == null || plan == null || primaryDwg == null)
            {
                return;
            }

            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Page2 TOP-3D template detach skipped: " + ex.Message);
            }

            try
            {
                view.DetailLevel = ViewDetailLevel.Fine;
            }
            catch
            {
            }

            try
            {
                foreach (ElementId filterId in view.GetFilters())
                {
                    try
                    {
                        view.SetFilterVisibility(filterId, true);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            HashSet<ElementId> keepIds = new HashSet<ElementId>(
                GetSavedElementIds(doc, plan, temporaryMepElementIds));
            keepIds.Add(primaryDwg.Id);
            foreach (ElementId roomId in CollectSelectedRoomVisualizationIds(doc, view, plan, selectedRoomBox))
            {
                keepIds.Add(roomId);
            }

            HashSet<int> keepCategoryIds = new HashSet<int>();
            foreach (ElementId id in keepIds)
            {
                Element element = doc.GetElement(id);
                if (element != null && element.Category != null)
                {
                    keepCategoryIds.Add(element.Category.Id.IntegerValue);
                }
            }

            // Make the exact linked DWG and every one of its imported layer subcategories visible.
            // A duplicated 3D view can inherit hidden ImportObjectStyles from the user's source
            // view, which would otherwise make Page 2 look incomplete even though the correct
            // ImportInstance was selected.
            Category dwgCategory = primaryDwg.Category;
            if (dwgCategory != null)
            {
                keepCategoryIds.Add(dwgCategory.Id.IntegerValue);
                TryShowCategoryAndSubCategories(view, dwgCategory);
            }

            foreach (int categoryInt in keepCategoryIds)
            {
                try
                {
                    ElementId categoryId = new ElementId(categoryInt);
                    if (view.CanCategoryBeHidden(categoryId))
                    {
                        view.SetCategoryHidden(categoryId, false);
                    }
                }
                catch
                {
                }
            }

            // Hide unrelated model categories.  Page 2 must show the original architectural DWG
            // plus only this saved Layout Plan, not the generated Revit walls/doors for the entire
            // project.  This is the full-sheet version of the already-successful Key Plan TOP-3D.
            try
            {
                foreach (Category category in doc.Settings.Categories)
                {
                    if (category == null || category.CategoryType != CategoryType.Model)
                    {
                        continue;
                    }

                    bool keepCategory = keepCategoryIds.Contains(category.Id.IntegerValue);
                    try
                    {
                        if (view.CanCategoryBeHidden(category.Id))
                        {
                            view.SetCategoryHidden(category.Id, !keepCategory);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Page2 TOP-3D category isolation partial. " + ex.Message);
            }

            // Kept categories such as Generic Models / Mechanical Equipment can contain many
            // unrelated elements.  Hide those individually and also hide all other ImportInstances
            // so the report always uses the exact DWG from the current CadToRevit session.
            List<ElementId> hideIds = new List<ElementId>();
            try
            {
                foreach (Element element in new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType())
                {
                    if (element == null || keepIds.Contains(element.Id))
                    {
                        continue;
                    }

                    bool isOtherImport = element is ImportInstance;
                    bool isKeptCategory = element.Category != null &&
                        keepCategoryIds.Contains(element.Category.Id.IntegerValue);
                    if (!isOtherImport && !isKeptCategory)
                    {
                        continue;
                    }

                    try
                    {
                        if (element.CanBeHidden(view))
                        {
                            hideIds.Add(element.Id);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Page2 TOP-3D element isolation scan partial. " + ex.Message);
            }

            const int batchSize = 400;
            for (int i = 0; i < hideIds.Count; i += batchSize)
            {
                List<ElementId> batch = hideIds.Skip(i).Take(batchSize).ToList();
                try
                {
                    view.HideElements(batch);
                }
                catch
                {
                    foreach (ElementId id in batch)
                    {
                        try
                        {
                            view.HideElements(new List<ElementId> { id });
                        }
                        catch
                        {
                        }
                    }
                }
            }

            List<ElementId> unhideIds = new List<ElementId>();
            foreach (ElementId id in keepIds)
            {
                Element element = doc.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                try
                {
                    if (element.IsHidden(view))
                    {
                        unhideIds.Add(id);
                    }
                }
                catch
                {
                }
            }

            if (unhideIds.Count > 0)
            {
                try
                {
                    view.UnhideElements(unhideIds);
                }
                catch
                {
                    foreach (ElementId id in unhideIds)
                    {
                        try
                        {
                            view.UnhideElements(new List<ElementId> { id });
                        }
                        catch
                        {
                        }
                    }
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Page2 unified TOP-3D visibility prepared. DWG=" +
                primaryDwg.Id.IntegerValue +
                ", Keep=" + keepIds.Count +
                ", HiddenOther=" + hideIds.Count +
                ", Unhidden=" + unhideIds.Count);
        }

        private static void TryShowCategoryAndSubCategories(View view, Category category)
        {
            if (view == null || category == null)
            {
                return;
            }

            try
            {
                if (view.CanCategoryBeHidden(category.Id))
                {
                    view.SetCategoryHidden(category.Id, false);
                }
            }
            catch
            {
            }

            try
            {
                CategoryNameMap subCategories = category.SubCategories;
                if (subCategories == null)
                {
                    return;
                }

                foreach (Category subCategory in subCategories)
                {
                    if (subCategory == null)
                    {
                        continue;
                    }

                    try
                    {
                        if (view.CanCategoryBeHidden(subCategory.Id))
                        {
                            view.SetCategoryHidden(subCategory.Id, false);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void PrepareOverallTopLayoutVisibility(
            Document doc,
            ViewPlan view,
            RoomLayoutPlanDto plan,
            BoundingBoxXYZ crop,
            IReadOnlyCollection<ElementId> temporaryMepElementIds)
        {
            if (doc == null || view == null || plan == null)
            {
                return;
            }

            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] OverallTop view template detach skipped: " + ex.Message);
            }

            try
            {
                view.DetailLevel = ViewDetailLevel.Fine;
            }
            catch
            {
            }

            try
            {
                foreach (ElementId filterId in view.GetFilters())
                {
                    try
                    {
                        view.SetFilterVisibility(filterId, true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] OverallTop filter visibility setup skipped: " + ex.Message);
            }

            HashSet<ElementId> layoutIds = new HashSet<ElementId>(
                GetSavedElementIds(doc, plan, temporaryMepElementIds));

            // Page 2 should highlight only the room belonging to this saved Layout Plan. The
            // previous implementation unhid EVERY room DirectShape inside the full-sheet crop,
            // which also made raster registration ambiguous when several detected rooms existed
            // on the same architectural drawing. Reuse the exact room-resolution logic already
            // used by Key Plan / overlay generation and keep only that room's visualization.
            BoundingBoxXYZ selectedRoomBox = CollectRoomVisualizationBox(doc, view, plan);
            foreach (ElementId roomId in CollectSelectedRoomVisualizationIds(doc, view, plan, selectedRoomBox))
            {
                layoutIds.Add(roomId);
            }

            HashSet<ElementId> categoryIds = new HashSet<ElementId>();
            foreach (ElementId id in layoutIds)
            {
                Element element = doc.GetElement(id);
                if (element != null && element.Category != null)
                {
                    categoryIds.Add(element.Category.Id);
                }
            }

            BuiltInCategory[] requiredCategories =
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory
            };

            foreach (BuiltInCategory category in requiredCategories)
            {
                categoryIds.Add(new ElementId(category));
            }

            int categoriesShown = 0;
            foreach (ElementId categoryId in categoryIds)
            {
                try
                {
                    if (view.CanCategoryBeHidden(categoryId))
                    {
                        view.SetCategoryHidden(categoryId, false);
                        categoriesShown++;
                    }
                }
                catch
                {
                }
            }

            // IMPORTANT for Page 2 registration: keep the architectural FloorPlan image clean.
            // The selected RoomVisualization / AHU / temporary MEP are rendered by the dedicated
            // TOP-3D overlay afterwards. If they remain visible in the base FloorPlan, the blue
            // RoomVisualization becomes a false registration target at the generated-model
            // coordinates, even when the underlying DWG import uses a translated CAD coordinate
            // system. That was why the previous pixel-registration version could align the AHU to
            // the blue rectangle yet still leave the whole highlighted room shifted from the real
            // architectural room.
            List<ElementId> visibleLayoutIds = new List<ElementId>();
            foreach (ElementId id in layoutIds)
            {
                try
                {
                    Element element = doc.GetElement(id);
                    if (element != null && !element.IsHidden(view) && element.CanBeHidden(view))
                    {
                        visibleLayoutIds.Add(id);
                    }
                }
                catch
                {
                }
            }

            if (visibleLayoutIds.Count > 0)
            {
                try
                {
                    view.HideElements(visibleLayoutIds);
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[LayoutPlanReport] OverallTop base layout hide skipped: " + ex.Message);
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] OverallTop architectural base prepared. LayoutElements=" + layoutIds.Count +
                ", CategoriesShown=" + categoriesShown +
                ", LayoutElementsHidden=" + visibleLayoutIds.Count +
                ", View=" + (view.Name ?? string.Empty));
        }

        private static void ExpandPlanViewRangeForContent(
            Document doc,
            ViewPlan view,
            RoomLayoutPlanDto plan,
            IReadOnlyCollection<ElementId> temporaryMepElementIds,
            string logPrefix)
        {
            if (doc == null || view == null || plan == null || view.GenLevel == null)
            {
                return;
            }

            BoundingBoxXYZ content = Union(
                CollectRoomVisualizationBox(doc, view, plan),
                CollectSavedLayoutElementBox(doc, view, plan, temporaryMepElementIds));
            if (content == null)
            {
                return;
            }

            try
            {
                PlanViewRange range = view.GetViewRange();
                double levelZ = view.GenLevel.Elevation;
                double desiredTop = content.Max.Z - levelZ + ToFeet(1200.0);
                double desiredBottom = content.Min.Z - levelZ - ToFeet(600.0);

                double top = range.GetOffset(PlanViewPlane.TopClipPlane);
                double bottom = range.GetOffset(PlanViewPlane.BottomClipPlane);
                double depth = range.GetOffset(PlanViewPlane.ViewDepthPlane);

                if (!double.IsNaN(desiredTop) && !double.IsInfinity(desiredTop))
                {
                    range.SetOffset(PlanViewPlane.TopClipPlane, Math.Max(top, desiredTop));
                }

                if (!double.IsNaN(desiredBottom) && !double.IsInfinity(desiredBottom))
                {
                    double newBottom = Math.Min(bottom, desiredBottom);
                    range.SetOffset(PlanViewPlane.BottomClipPlane, newBottom);
                    range.SetOffset(PlanViewPlane.ViewDepthPlane, Math.Min(depth, newBottom));
                }

                view.SetViewRange(range);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] " + (logPrefix ?? "Plan") + " view range expanded. ContentZ=" +
                    FromFeet(content.Min.Z).ToString("F0") + ".." +
                    FromFeet(content.Max.Z).ToString("F0") + " mm");
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] " + (logPrefix ?? "Plan") +
                    " view range expansion skipped: " + ex.Message);
            }
        }

        private static string ExportOverallTopLayoutOverlay(
            Document doc,
            string tempDirectory,
            RoomLayoutPlanDto plan,
            ViewPlan overallView,
            BoundingBoxXYZ overallModelCrop,
            IReadOnlyCollection<ElementId> temporaryMepElementIds,
            int pixelSize)
        {
            if (doc == null || plan == null || overallView == null || overallModelCrop == null)
            {
                return string.Empty;
            }

            View3D source3D = Resolve3DView(doc);
            if (source3D == null)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop full-page overlay skipped: no available 3D view.");
                return string.Empty;
            }

            ElementId tempOverlayViewId = ElementId.InvalidElementId;
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Layout Plan Report Overall Overlay"))
                {
                    tx.Start();
                    tempOverlayViewId = source3D.Duplicate(ViewDuplicateOption.Duplicate);
                    View3D overlayView = doc.GetElement(tempOverlayViewId) as View3D;
                    if (overlayView == null)
                    {
                        tx.RollBack();
                        return string.Empty;
                    }

                    overlayView.Name = "EMSD_TEMP_LAYOUT_REPORT_OVERLAY_" + DateTime.Now.ToString("HHmmssfff");

                    // Match the FloorPlan's view axes, not an assumed +Y up direction. This also
                    // handles plans whose Project North / crop transform is rotated.
                    XYZ forward = overallView.ViewDirection;
                    if (forward == null || forward.GetLength() < 1e-9)
                    {
                        forward = new XYZ(0.0, 0.0, -1.0);
                    }
                    else
                    {
                        forward = forward.Normalize();
                    }

                    XYZ up = overallView.UpDirection;
                    if (up == null || up.GetLength() < 1e-9)
                    {
                        up = XYZ.BasisY;
                    }
                    else
                    {
                        up = up.Normalize();
                    }

                    BoundingBoxXYZ roomBox = CollectRoomVisualizationBox(doc, overlayView, plan);
                    BoundingBoxXYZ layoutBox = CollectSavedLayoutElementBox(doc, overlayView, plan, temporaryMepElementIds);
                    BoundingBoxXYZ contentBox = Union(roomBox, layoutBox);
                    if (contentBox == null)
                    {
                        tx.RollBack();
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] OverallTop full-page overlay skipped: layout content box is empty.");
                        return string.Empty;
                    }

                    double minZ = contentBox.Min.Z - ToFeet(800.0);
                    double maxZ = contentBox.Max.Z + ToFeet(1200.0);
                    if (maxZ - minZ < ToFeet(4500.0))
                    {
                        double centerZ = (minZ + maxZ) * 0.5;
                        minZ = centerZ - ToFeet(2250.0);
                        maxZ = centerZ + ToFeet(2250.0);
                    }

                    BoundingBoxXYZ sectionBox = new BoundingBoxXYZ
                    {
                        Min = new XYZ(overallModelCrop.Min.X, overallModelCrop.Min.Y, minZ),
                        Max = new XYZ(overallModelCrop.Max.X, overallModelCrop.Max.Y, maxZ)
                    };

                    XYZ center = new XYZ(
                        (sectionBox.Min.X + sectionBox.Max.X) * 0.5,
                        (sectionBox.Min.Y + sectionBox.Max.Y) * 0.5,
                        (sectionBox.Min.Z + sectionBox.Max.Z) * 0.5);
                    double xySpan = Math.Max(
                        sectionBox.Max.X - sectionBox.Min.X,
                        sectionBox.Max.Y - sectionBox.Min.Y);
                    double distance = Math.Max(xySpan, ToFeet(12000.0));
                    XYZ eye = center - forward.Multiply(distance);

                    overlayView.SetOrientation(new ViewOrientation3D(eye, up, forward));
                    overlayView.IsSectionBoxActive = true;
                    overlayView.SetSectionBox(sectionBox);

                    // Use the SAME XY crop used by the exported architectural plan. Because this
                    // 3D view has the same forward/up axes as the FloorPlan, both PNGs represent
                    // the same model-to-pixel transform and can be composited at (0,0).
                    BoundingBoxXYZ overlayCrop = new BoundingBoxXYZ
                    {
                        Min = new XYZ(overallModelCrop.Min.X, overallModelCrop.Min.Y, minZ),
                        Max = new XYZ(overallModelCrop.Max.X, overallModelCrop.Max.Y, maxZ)
                    };
                    ApplyCrop(overlayView, overlayCrop);
                    PrepareOverallTopOverlayVisibility(doc, overlayView, plan, temporaryMepElementIds, roomBox);
                    HideDatumCategories(overlayView);
                    doc.Regenerate();
                    tx.Commit();
                }

                string path = ExportView(
                    doc,
                    doc.GetElement(tempOverlayViewId) as View,
                    tempDirectory,
                    "overall_top_overlay",
                    pixelSize);

                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop full-page TOP-3D overlay exported. Path=" +
                    (path ?? string.Empty));
                return path;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop full-page overlay export failed. " + ex.Message);
                return string.Empty;
            }
            finally
            {
                DeleteTemporaryView(doc, tempOverlayViewId);
            }
        }

        private static void PrepareOverallTopOverlayVisibility(
            Document doc,
            View3D view,
            RoomLayoutPlanDto plan,
            IReadOnlyCollection<ElementId> temporaryMepElementIds,
            BoundingBoxXYZ selectedRoomBox)
        {
            if (doc == null || view == null || plan == null)
            {
                return;
            }

            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }
            }
            catch
            {
            }

            try
            {
                view.DetailLevel = ViewDetailLevel.Fine;
            }
            catch
            {
            }

            HashSet<ElementId> keepIds = new HashSet<ElementId>(GetSavedElementIds(doc, plan, temporaryMepElementIds));
            foreach (ElementId roomId in CollectSelectedRoomVisualizationIds(doc, view, plan, selectedRoomBox))
            {
                keepIds.Add(roomId);
            }

            BuiltInCategory[] keepCategories =
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory
            };
            HashSet<int> keepCategoryIds = new HashSet<int>(
                keepCategories.Select(x => new ElementId(x).IntegerValue));

            // Also preserve the ACTUAL categories of the resolved saved elements / room shapes.
            // Some customer AHU families are Generic Model rather than Mechanical Equipment,
            // and DirectShape visualization categories can vary between builds.
            foreach (ElementId id in keepIds)
            {
                Element element = doc.GetElement(id);
                if (element != null && element.Category != null)
                {
                    keepCategoryIds.Add(element.Category.Id.IntegerValue);
                }
            }

            // First hide every unrelated MODEL category. The overlay should contain only the
            // selected room highlight and its AHU/MEP, never walls/floors/DWG from the source 3D
            // view; those remain visible in the architectural FloorPlan underneath.
            try
            {
                foreach (Category category in doc.Settings.Categories)
                {
                    if (category == null || category.CategoryType != CategoryType.Model)
                    {
                        continue;
                    }

                    bool keepCategory = keepCategoryIds.Contains(category.Id.IntegerValue);
                    try
                    {
                        if (view.CanCategoryBeHidden(category.Id))
                        {
                            view.SetCategoryHidden(category.Id, !keepCategory);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop overlay category isolation partial. " + ex.Message);
            }

            // A kept category can still contain unrelated AHUs, Generic Models or MEP elsewhere
            // in the building. Hide those individual elements so the overlay is deterministic.
            List<ElementId> hideIds = new List<ElementId>();
            try
            {
                foreach (Element element in new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType())
                {
                    if (element == null || keepIds.Contains(element.Id) || element.Category == null)
                    {
                        continue;
                    }

                    if (!keepCategoryIds.Contains(element.Category.Id.IntegerValue))
                    {
                        continue;
                    }

                    try
                    {
                        if (element.CanBeHidden(view))
                        {
                            hideIds.Add(element.Id);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop overlay element isolation scan partial. " + ex.Message);
            }

            // Hide in modest batches so a large project does not create an oversized API call.
            const int batchSize = 400;
            for (int i = 0; i < hideIds.Count; i += batchSize)
            {
                List<ElementId> batch = hideIds.Skip(i).Take(batchSize).ToList();
                try
                {
                    view.HideElements(batch);
                }
                catch
                {
                    foreach (ElementId id in batch)
                    {
                        try
                        {
                            view.HideElements(new List<ElementId> { id });
                        }
                        catch
                        {
                        }
                    }
                }
            }

            // The duplicated source view may already have one of the desired elements hidden.
            List<ElementId> unhideIds = new List<ElementId>();
            foreach (ElementId id in keepIds)
            {
                Element element = doc.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                try
                {
                    if (element.IsHidden(view))
                    {
                        unhideIds.Add(id);
                    }
                }
                catch
                {
                }
            }

            if (unhideIds.Count > 0)
            {
                try
                {
                    view.UnhideElements(unhideIds);
                }
                catch
                {
                }
            }

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] OverallTop overlay visibility isolated. Keep=" + keepIds.Count +
                ", HiddenOtherLayoutElements=" + hideIds.Count +
                ", Unhidden=" + unhideIds.Count);
        }

        private static IEnumerable<ElementId> CollectSelectedRoomVisualizationIds(
            Document doc,
            View view,
            RoomLayoutPlanDto plan,
            BoundingBoxXYZ selectedRoomBox)
        {
            if (doc == null || plan == null)
            {
                yield break;
            }

            string roomKey = (plan.RoomKey ?? string.Empty).Trim();
            string roomName = (plan.RoomName ?? string.Empty).Trim();
            List<Tuple<DirectShape, BoundingBoxXYZ>> exact = new List<Tuple<DirectShape, BoundingBoxXYZ>>();
            List<Tuple<DirectShape, BoundingBoxXYZ>> loose = new List<Tuple<DirectShape, BoundingBoxXYZ>>();

            foreach (DirectShape shape in new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>())
            {
                if (shape == null)
                {
                    continue;
                }

                string name = (shape.Name ?? string.Empty).Trim();
                string data = (shape.ApplicationDataId ?? string.Empty).Trim();
                bool looksLikeRoomVisualization =
                    name.IndexOf("ROOMVIS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    data.IndexOf("REGION::", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeRoomVisualization)
                {
                    continue;
                }

                BoundingBoxXYZ box = GetModelBox(shape, view);
                if (box == null)
                {
                    continue;
                }

                bool isExact =
                    (!string.IsNullOrWhiteSpace(roomKey) &&
                     (string.Equals(data, roomKey, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(data, "REGION::" + roomKey, StringComparison.OrdinalIgnoreCase) ||
                      data.EndsWith("::" + roomKey, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(name, roomKey, StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith("__" + roomKey, StringComparison.OrdinalIgnoreCase) ||
                      name.EndsWith("::" + roomKey, StringComparison.OrdinalIgnoreCase)));

                if (isExact)
                {
                    exact.Add(Tuple.Create(shape, box));
                    continue;
                }

                bool isLoose =
                    (!string.IsNullOrWhiteSpace(roomName) &&
                     (name.IndexOf(roomName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                      data.IndexOf(roomName, StringComparison.OrdinalIgnoreCase) >= 0)) ||
                    (!string.IsNullOrWhiteSpace(roomKey) &&
                     (name.IndexOf(roomKey, StringComparison.OrdinalIgnoreCase) >= 0 ||
                      data.IndexOf(roomKey, StringComparison.OrdinalIgnoreCase) >= 0));

                if (isLoose)
                {
                    loose.Add(Tuple.Create(shape, box));
                }
            }

            IEnumerable<Tuple<DirectShape, BoundingBoxXYZ>> candidates = exact.Count > 0 ? exact : loose;
            if (selectedRoomBox != null)
            {
                List<Tuple<DirectShape, BoundingBoxXYZ>> intersecting = candidates
                    .Where(x => x != null && x.Item2 != null && BoxesIntersectXy(x.Item2, selectedRoomBox))
                    .ToList();
                if (intersecting.Count > 0)
                {
                    candidates = intersecting;
                }
            }

            // Exact metadata can legitimately belong to multiple DirectShapes that together make
            // up one room visualization, so return every candidate after spatial restriction.
            foreach (Tuple<DirectShape, BoundingBoxXYZ> candidate in candidates)
            {
                if (candidate != null && candidate.Item1 != null)
                {
                    yield return candidate.Item1.Id;
                }
            }
        }

        private static void CompositeRegisteredFullPageLayoutOverlay(
            ViewPlan overallView,
            string overallImagePath,
            string overlayImagePath,
            BoundingBoxXYZ roomModelBox,
            Transform primaryDwgTransform)
        {
            if (overallView == null ||
                string.IsNullOrWhiteSpace(overallImagePath) ||
                string.IsNullOrWhiteSpace(overlayImagePath) ||
                !File.Exists(overallImagePath) ||
                !File.Exists(overlayImagePath))
            {
                return;
            }

            string stagedPath = overallImagePath + ".registered_layout_overlay.png";
            try
            {
                using (System.Drawing.Bitmap baseImage = new System.Drawing.Bitmap(overallImagePath))
                using (System.Drawing.Bitmap overlayImage = new System.Drawing.Bitmap(overlayImagePath))
                {
                    // IMPORTANT: the architectural base image no longer contains the generated
                    // RoomVisualization/AHU.  The previous implementation registered the TOP-3D
                    // overlay to that same generated blue rectangle in the FloorPlan image. That
                    // can only prove that the two Revit views agree with each other; it cannot
                    // prove that the generated room agrees with the original imported DWG. In the
                    // failing project the generated room coordinate system is slightly translated
                    // from the DWG, so the whole blue room + AHU remained shifted together.
                    //
                    // Build several model-coordinate candidates, then use the REAL architectural
                    // wall linework in the clean FloorPlan raster to refine the target rectangle.
                    // This makes the DWG itself the registration reference instead of another
                    // generated Revit element.
                    List<RoomTargetSeed> targetSeeds = new List<RoomTargetSeed>();

                    System.Drawing.Rectangle identityRoom = ModelBoxToCurrentViewCropRectangle(
                        overallView,
                        roomModelBox,
                        baseImage.Width,
                        baseImage.Height);
                    AddRoomTargetSeed(targetSeeds, "ModelIdentity", identityRoom);

                    if (primaryDwgTransform != null && roomModelBox != null)
                    {
                        BoundingBoxXYZ transformedRoom = TransformModelBoundingBox(roomModelBox, primaryDwgTransform);
                        System.Drawing.Rectangle transformedRect = ModelBoxToCurrentViewCropRectangle(
                            overallView,
                            transformedRoom,
                            baseImage.Width,
                            baseImage.Height);
                        AddRoomTargetSeed(targetSeeds, "DwgTransform", transformedRect);

                        try
                        {
                            BoundingBoxXYZ inverseRoom = TransformModelBoundingBox(roomModelBox, primaryDwgTransform.Inverse);
                            System.Drawing.Rectangle inverseRect = ModelBoxToCurrentViewCropRectangle(
                                overallView,
                                inverseRoom,
                                baseImage.Width,
                                baseImage.Height);
                            AddRoomTargetSeed(targetSeeds, "DwgInverse", inverseRect);
                        }
                        catch
                        {
                        }
                    }

                    RoomTargetResult architecturalTarget = FindBestArchitecturalRoomTarget(baseImage, targetSeeds);
                    System.Drawing.Rectangle targetRoom = architecturalTarget != null
                        ? architecturalTarget.Rectangle
                        : identityRoom;

                    // The TOP-3D overlay contains the blue RoomVisualization and therefore still
                    // gives us an excellent source anchor. Only the TARGET anchor has changed: it
                    // now comes from the architectural DWG wall rectangle found above.
                    System.Drawing.Rectangle sourceRoom = FindRoomHighlightRectangle(
                        overlayImage,
                        new System.Drawing.Rectangle(0, 0, overlayImage.Width, overlayImage.Height));

                    if (!IsUsableImageRectangle(targetRoom) || !IsUsableImageRectangle(sourceRoom))
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] OverallTop architectural registration unavailable. " +
                            "Identity=" + FormatRectangle(identityRoom) +
                            ", Target=" + FormatRectangle(targetRoom) +
                            ", Source=" + FormatRectangle(sourceRoom) +
                            ". Falling back to same-size composition.");
                        throw new InvalidOperationException("Architectural room registration anchor detection failed.");
                    }

                    double scaleX = (double)targetRoom.Width / Math.Max(1, sourceRoom.Width);
                    double scaleY = (double)targetRoom.Height / Math.Max(1, sourceRoom.Height);

                    if (scaleX < 0.35 || scaleX > 3.00 || scaleY < 0.35 || scaleY > 3.00)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] OverallTop architectural registration rejected. " +
                            "ScaleX=" + scaleX.ToString("F4") +
                            ", ScaleY=" + scaleY.ToString("F4") +
                            ", Target=" + FormatRectangle(targetRoom) +
                            ", Source=" + FormatRectangle(sourceRoom) +
                            ". Falling back to same-size composition.");
                        throw new InvalidOperationException("Architectural room registration scale validation failed.");
                    }

                    int destX = (int)Math.Round(targetRoom.X - (sourceRoom.X * scaleX));
                    int destY = (int)Math.Round(targetRoom.Y - (sourceRoom.Y * scaleY));
                    int destWidth = Math.Max(1, (int)Math.Round(overlayImage.Width * scaleX));
                    int destHeight = Math.Max(1, (int)Math.Round(overlayImage.Height * scaleY));
                    System.Drawing.Rectangle destination = new System.Drawing.Rectangle(
                        destX,
                        destY,
                        destWidth,
                        destHeight);

                    using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(baseImage))
                    using (System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                        attributes.SetColorKey(
                            System.Drawing.Color.FromArgb(246, 246, 246),
                            System.Drawing.Color.FromArgb(255, 255, 255));

                        graphics.DrawImage(
                            overlayImage,
                            destination,
                            0,
                            0,
                            overlayImage.Width,
                            overlayImage.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attributes);
                    }

                    baseImage.Save(stagedPath, System.Drawing.Imaging.ImageFormat.Png);

                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] OverallTop architectural-room registration composited. " +
                        "Seed=" + (architecturalTarget != null ? architecturalTarget.SeedName : "IdentityFallback") +
                        ", Score=" + (architecturalTarget != null ? architecturalTarget.Score.ToString("F4") : "0") +
                        ", Identity=" + FormatRectangle(identityRoom) +
                        ", TargetRoom=" + FormatRectangle(targetRoom) +
                        ", OverlayRoom=" + FormatRectangle(sourceRoom) +
                        ", Scale=(" + scaleX.ToString("F4") + "," + scaleY.ToString("F4") + ")" +
                        ", Destination=" + FormatRectangle(destination) +
                        ", DwgTransform=" + FormatTransform2D(primaryDwgTransform) +
                        ", BasePx=" + baseImage.Width + "x" + baseImage.Height +
                        ", OverlayPx=" + overlayImage.Width + "x" + overlayImage.Height);
                }

                File.Copy(stagedPath, overallImagePath, true);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop architectural-room registration failed. " + ex.Message);
                CompositeFullPageLayoutOverlay(overallImagePath, overlayImagePath);
            }
            finally
            {
                TryDeleteFile(stagedPath);
            }
        }

        private sealed class RoomTargetSeed
        {
            public string Name { get; set; }

            public System.Drawing.Rectangle Rectangle { get; set; }
        }

        private sealed class RoomTargetResult
        {
            public string SeedName { get; set; }

            public System.Drawing.Rectangle Rectangle { get; set; }

            public double Score { get; set; }
        }

        private static void AddRoomTargetSeed(
            List<RoomTargetSeed> seeds,
            string name,
            System.Drawing.Rectangle rectangle)
        {
            if (seeds == null || !IsUsableImageRectangle(rectangle))
            {
                return;
            }

            bool duplicate = seeds.Any(x =>
                x != null &&
                Math.Abs(x.Rectangle.X - rectangle.X) <= 2 &&
                Math.Abs(x.Rectangle.Y - rectangle.Y) <= 2 &&
                Math.Abs(x.Rectangle.Width - rectangle.Width) <= 2 &&
                Math.Abs(x.Rectangle.Height - rectangle.Height) <= 2);
            if (!duplicate)
            {
                seeds.Add(new RoomTargetSeed
                {
                    Name = name ?? string.Empty,
                    Rectangle = rectangle
                });
            }
        }

        private static RoomTargetResult FindBestArchitecturalRoomTarget(
            System.Drawing.Bitmap baseImage,
            List<RoomTargetSeed> seeds)
        {
            if (baseImage == null || seeds == null || seeds.Count == 0)
            {
                return null;
            }

            int[] integral = BuildArchitecturalInkIntegral(baseImage);
            if (integral == null || integral.Length == 0)
            {
                return null;
            }

            RoomTargetResult best = null;
            foreach (RoomTargetSeed seed in seeds)
            {
                RoomTargetResult refined = RefineArchitecturalRoomTarget(
                    integral,
                    baseImage.Width,
                    baseImage.Height,
                    seed);
                if (refined != null && (best == null || refined.Score > best.Score))
                {
                    best = refined;
                }
            }

            return best;
        }

        private static RoomTargetResult RefineArchitecturalRoomTarget(
            int[] integral,
            int imageWidth,
            int imageHeight,
            RoomTargetSeed seed)
        {
            if (integral == null || seed == null || !IsUsableImageRectangle(seed.Rectangle))
            {
                return null;
            }

            System.Drawing.Rectangle initial = seed.Rectangle;
            int maxDimension = Math.Max(initial.Width, initial.Height);
            int searchRadius = Math.Max(70, Math.Min(260, (int)Math.Round(maxDimension * 0.95)));
            int step = maxDimension > 180 ? 4 : 3;
            double[] scales = { 0.94, 0.97, 1.00, 1.03, 1.06 };

            double bestScore = double.MinValue;
            System.Drawing.Rectangle bestRectangle = initial;
            int initialCenterX = initial.Left + initial.Width / 2;
            int initialCenterY = initial.Top + initial.Height / 2;

            foreach (double scale in scales)
            {
                int width = Math.Max(8, (int)Math.Round(initial.Width * scale));
                int height = Math.Max(8, (int)Math.Round(initial.Height * scale));

                for (int dy = -searchRadius; dy <= searchRadius; dy += step)
                {
                    int centerY = initialCenterY + dy;
                    int top = centerY - height / 2;
                    if (top < 2 || top + height >= imageHeight - 2)
                    {
                        continue;
                    }

                    for (int dx = -searchRadius; dx <= searchRadius; dx += step)
                    {
                        int centerX = initialCenterX + dx;
                        int left = centerX - width / 2;
                        if (left < 2 || left + width >= imageWidth - 2)
                        {
                            continue;
                        }

                        System.Drawing.Rectangle candidate = new System.Drawing.Rectangle(left, top, width, height);
                        double boundaryScore = ScoreArchitecturalRoomBoundary(
                            integral,
                            imageWidth,
                            imageHeight,
                            candidate);

                        // Prefer an equally good wall rectangle that remains closer to the model /
                        // DWG-transform prediction. The penalty is deliberately small: real wall
                        // evidence always wins, but random nearby title/text rectangles do not.
                        double distance = Math.Sqrt((double)(dx * dx) + (double)(dy * dy));
                        double distancePenalty = Math.Min(0.08, distance / Math.Max(1.0, searchRadius) * 0.045);
                        double scalePenalty = Math.Abs(scale - 1.0) * 0.05;
                        double score = boundaryScore - distancePenalty - scalePenalty;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestRectangle = candidate;
                        }
                    }
                }
            }

            double seedScore = ScoreArchitecturalRoomBoundary(
                integral,
                imageWidth,
                imageHeight,
                initial);

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] Architectural room seed refined. Seed=" + (seed.Name ?? string.Empty) +
                ", Initial=" + FormatRectangle(initial) +
                ", InitialScore=" + seedScore.ToString("F4") +
                ", Best=" + FormatRectangle(bestRectangle) +
                ", BestScore=" + bestScore.ToString("F4"));

            return new RoomTargetResult
            {
                SeedName = seed.Name ?? string.Empty,
                Rectangle = bestRectangle,
                Score = bestScore
            };
        }

        private static int[] BuildArchitecturalInkIntegral(System.Drawing.Bitmap source)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return null;
            }

            int width = source.Width;
            int height = source.Height;
            int strideWidth = width + 1;
            int[] integral = new int[(width + 1) * (height + 1)];

            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(
                width,
                height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.DrawImageUnscaled(source, 0, 0);
                }

                System.Drawing.Imaging.BitmapData data = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, width, height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                try
                {
                    int stride = data.Stride;
                    int absoluteStride = Math.Abs(stride);
                    byte[] buffer = new byte[absoluteStride * height];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

                    for (int y = 0; y < height; y++)
                    {
                        int sourceY = stride >= 0 ? y : (height - 1 - y);
                        int rowOffset = sourceY * absoluteStride;
                        int rowSum = 0;
                        int integralRow = (y + 1) * strideWidth;
                        int previousRow = y * strideWidth;
                        for (int x = 0; x < width; x++)
                        {
                            int pixelOffset = rowOffset + (x * 3);
                            byte b = buffer[pixelOffset + 0];
                            byte g = buffer[pixelOffset + 1];
                            byte r = buffer[pixelOffset + 2];

                            // Architectural room boundaries in the source DWG are primarily
                            // dark/black. Keep the predicate intentionally conservative so cyan
                            // grid/text graphics and the red title block do not dominate the
                            // local rectangle search.
                            bool architecturalInk = r < 175 && g < 175 && b < 175;
                            rowSum += architecturalInk ? 1 : 0;
                            integral[integralRow + x + 1] = integral[previousRow + x + 1] + rowSum;
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }

            return integral;
        }

        private static double ScoreArchitecturalRoomBoundary(
            int[] integral,
            int imageWidth,
            int imageHeight,
            System.Drawing.Rectangle rectangle)
        {
            if (integral == null || !IsUsableImageRectangle(rectangle))
            {
                return 0.0;
            }

            int band = Math.Max(3, Math.Min(9, Math.Min(rectangle.Width, rectangle.Height) / 22));
            int halfBand = Math.Max(1, band / 2);

            System.Drawing.Rectangle top = ClipRectangle(
                new System.Drawing.Rectangle(
                    rectangle.Left,
                    rectangle.Top - halfBand,
                    rectangle.Width,
                    band),
                imageWidth,
                imageHeight);
            System.Drawing.Rectangle bottom = ClipRectangle(
                new System.Drawing.Rectangle(
                    rectangle.Left,
                    rectangle.Bottom - halfBand,
                    rectangle.Width,
                    band),
                imageWidth,
                imageHeight);
            System.Drawing.Rectangle left = ClipRectangle(
                new System.Drawing.Rectangle(
                    rectangle.Left - halfBand,
                    rectangle.Top,
                    band,
                    rectangle.Height),
                imageWidth,
                imageHeight);
            System.Drawing.Rectangle right = ClipRectangle(
                new System.Drawing.Rectangle(
                    rectangle.Right - halfBand,
                    rectangle.Top,
                    band,
                    rectangle.Height),
                imageWidth,
                imageHeight);

            double topDensity = IntegralDensity(integral, imageWidth, top);
            double bottomDensity = IntegralDensity(integral, imageWidth, bottom);
            double leftDensity = IntegralDensity(integral, imageWidth, left);
            double rightDensity = IntegralDensity(integral, imageWidth, right);

            double[] densities = { topDensity, bottomDensity, leftDensity, rightDensity };
            Array.Sort(densities);
            double average = densities.Average();
            double secondWeakest = densities.Length > 1 ? densities[1] : densities[0];
            double strongestPair = (densities[2] + densities[3]) * 0.5;

            // Doors legitimately interrupt one side of a room, so do not require all four edges
            // to be equally dense. Reward three-sided support plus strong continuous wall pairs.
            return (average * 0.50) + (secondWeakest * 0.30) + (strongestPair * 0.20);
        }

        private static System.Drawing.Rectangle ClipRectangle(
            System.Drawing.Rectangle rectangle,
            int imageWidth,
            int imageHeight)
        {
            return System.Drawing.Rectangle.Intersect(
                new System.Drawing.Rectangle(0, 0, imageWidth, imageHeight),
                rectangle);
        }

        private static double IntegralDensity(int[] integral, int imageWidth, System.Drawing.Rectangle rectangle)
        {
            if (integral == null || rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                return 0.0;
            }

            int stride = imageWidth + 1;
            int x1 = rectangle.Left;
            int y1 = rectangle.Top;
            int x2 = rectangle.Right;
            int y2 = rectangle.Bottom;
            int sum = integral[y2 * stride + x2]
                    - integral[y1 * stride + x2]
                    - integral[y2 * stride + x1]
                    + integral[y1 * stride + x1];
            return (double)sum / Math.Max(1, rectangle.Width * rectangle.Height);
        }

        private static BoundingBoxXYZ TransformModelBoundingBox(BoundingBoxXYZ box, Transform transform)
        {
            if (box == null || transform == null)
            {
                return box;
            }

            XYZ[] corners =
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            };

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;
            foreach (XYZ corner in corners)
            {
                XYZ point = transform.OfPoint(corner);
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }

            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
        }

        private static string FormatTransform2D(Transform transform)
        {
            if (transform == null)
            {
                return "null";
            }

            try
            {
                return "O(" + FromFeet(transform.Origin.X).ToString("F0") + "," +
                       FromFeet(transform.Origin.Y).ToString("F0") + ")mm" +
                       " X(" + transform.BasisX.X.ToString("F4") + "," + transform.BasisX.Y.ToString("F4") + ")" +
                       " Y(" + transform.BasisY.X.ToString("F4") + "," + transform.BasisY.Y.ToString("F4") + ")";
            }
            catch
            {
                return "unavailable";
            }
        }

        private static System.Drawing.Rectangle ModelBoxToCurrentViewCropRectangle(
            View view,
            BoundingBoxXYZ modelBox,
            int imageWidth,
            int imageHeight)
        {
            if (view == null || modelBox == null || imageWidth <= 0 || imageHeight <= 0)
            {
                return System.Drawing.Rectangle.Empty;
            }

            try
            {
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null)
                {
                    return System.Drawing.Rectangle.Empty;
                }

                Transform viewToModel = crop.Transform ?? Transform.Identity;
                Transform modelToView = viewToModel.Inverse;

                double minX = double.MaxValue;
                double minY = double.MaxValue;
                double maxX = double.MinValue;
                double maxY = double.MinValue;
                XYZ[] corners =
                {
                    new XYZ(modelBox.Min.X, modelBox.Min.Y, modelBox.Min.Z),
                    new XYZ(modelBox.Min.X, modelBox.Min.Y, modelBox.Max.Z),
                    new XYZ(modelBox.Min.X, modelBox.Max.Y, modelBox.Min.Z),
                    new XYZ(modelBox.Min.X, modelBox.Max.Y, modelBox.Max.Z),
                    new XYZ(modelBox.Max.X, modelBox.Min.Y, modelBox.Min.Z),
                    new XYZ(modelBox.Max.X, modelBox.Min.Y, modelBox.Max.Z),
                    new XYZ(modelBox.Max.X, modelBox.Max.Y, modelBox.Min.Z),
                    new XYZ(modelBox.Max.X, modelBox.Max.Y, modelBox.Max.Z)
                };

                foreach (XYZ point in corners)
                {
                    XYZ local = modelToView.OfPoint(point);
                    minX = Math.Min(minX, local.X);
                    minY = Math.Min(minY, local.Y);
                    maxX = Math.Max(maxX, local.X);
                    maxY = Math.Max(maxY, local.Y);
                }

                double cropWidth = Math.Max(1e-9, crop.Max.X - crop.Min.X);
                double cropHeight = Math.Max(1e-9, crop.Max.Y - crop.Min.Y);
                double nx1 = (minX - crop.Min.X) / cropWidth;
                double nx2 = (maxX - crop.Min.X) / cropWidth;
                double ny1 = (crop.Max.Y - maxY) / cropHeight;
                double ny2 = (crop.Max.Y - minY) / cropHeight;

                int x1 = ClampInt((int)Math.Round(nx1 * imageWidth), 0, imageWidth);
                int x2 = ClampInt((int)Math.Round(nx2 * imageWidth), 0, imageWidth);
                int y1 = ClampInt((int)Math.Round(ny1 * imageHeight), 0, imageHeight);
                int y2 = ClampInt((int)Math.Round(ny2 * imageHeight), 0, imageHeight);

                return new System.Drawing.Rectangle(
                    Math.Min(x1, x2),
                    Math.Min(y1, y2),
                    Math.Abs(x2 - x1),
                    Math.Abs(y2 - y1));
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Current crop model-to-image mapping failed. " + ex.Message);
                return System.Drawing.Rectangle.Empty;
            }
        }

        private static System.Drawing.Rectangle ExpandImageRectangle(
            System.Drawing.Rectangle rect,
            int imageWidth,
            int imageHeight,
            double factor,
            int minimumPadding)
        {
            if (imageWidth <= 0 || imageHeight <= 0)
            {
                return System.Drawing.Rectangle.Empty;
            }

            if (!IsUsableImageRectangle(rect))
            {
                return new System.Drawing.Rectangle(0, 0, imageWidth, imageHeight);
            }

            int padX = Math.Max(minimumPadding, (int)Math.Round(rect.Width * Math.Max(0.0, factor - 1.0) * 0.5));
            int padY = Math.Max(minimumPadding, (int)Math.Round(rect.Height * Math.Max(0.0, factor - 1.0) * 0.5));
            int x1 = Math.Max(0, rect.Left - padX);
            int y1 = Math.Max(0, rect.Top - padY);
            int x2 = Math.Min(imageWidth, rect.Right + padX);
            int y2 = Math.Min(imageHeight, rect.Bottom + padY);
            return new System.Drawing.Rectangle(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
        }

        private static System.Drawing.Rectangle FindRoomHighlightRectangle(
            System.Drawing.Bitmap source,
            System.Drawing.Rectangle searchArea)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                return System.Drawing.Rectangle.Empty;
            }

            System.Drawing.Rectangle imageBounds = new System.Drawing.Rectangle(0, 0, source.Width, source.Height);
            searchArea = System.Drawing.Rectangle.Intersect(imageBounds, searchArea);
            if (searchArea.Width < 2 || searchArea.Height < 2)
            {
                return System.Drawing.Rectangle.Empty;
            }

            // Work in a predictable 24-bpp buffer; Revit PNGs can otherwise open as 32-bpp,
            // indexed or premultiplied formats depending on the workstation/GDI+ version.
            using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(
                source.Width,
                source.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap))
                {
                    g.DrawImageUnscaled(source, 0, 0);
                }

                System.Drawing.Imaging.BitmapData data = null;
                try
                {
                    data = bitmap.LockBits(
                        searchArea,
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                    int stride = Math.Abs(data.Stride);
                    int byteCount = stride * searchArea.Height;
                    byte[] bytes = new byte[byteCount];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, byteCount);

                    int[] columnCounts = new int[searchArea.Width];
                    int[] rowCounts = new int[searchArea.Height];
                    int rawMinX = searchArea.Width;
                    int rawMinY = searchArea.Height;
                    int rawMaxX = -1;
                    int rawMaxY = -1;
                    int matchCount = 0;

                    for (int y = 0; y < searchArea.Height; y++)
                    {
                        int row = data.Stride >= 0
                            ? y * stride
                            : (searchArea.Height - 1 - y) * stride;
                        for (int x = 0; x < searchArea.Width; x++)
                        {
                            int offset = row + x * 3;
                            int b = bytes[offset];
                            int g = bytes[offset + 1];
                            int r = bytes[offset + 2];

                            // RoomVisualization fill in this plugin is a muted light blue. Keep
                            // the predicate intentionally relative rather than requiring one exact
                            // RGB value so anti-aliasing / monitor color settings do not matter.
                            // Solid blue ductwork (very low R/G) and cyan DWG linework (very high G)
                            // are excluded, which prevents them from stretching the room anchor.
                            bool isRoomBlue =
                                r >= 70 && r <= 205 &&
                                g >= 105 && g <= 225 &&
                                b >= 145 && b <= 245 &&
                                (g - r) >= 16 &&
                                (b - g) >= 8 &&
                                (b - r) >= 42;
                            if (!isRoomBlue)
                            {
                                continue;
                            }

                            matchCount++;
                            columnCounts[x]++;
                            rowCounts[y]++;
                            rawMinX = Math.Min(rawMinX, x);
                            rawMinY = Math.Min(rawMinY, y);
                            rawMaxX = Math.Max(rawMaxX, x);
                            rawMaxY = Math.Max(rawMaxY, y);
                        }
                    }

                    if (matchCount < 40 || rawMaxX < rawMinX || rawMaxY < rawMinY)
                    {
                        return System.Drawing.Rectangle.Empty;
                    }

                    int maxColumn = columnCounts.Length > 0 ? columnCounts.Max() : 0;
                    int maxRow = rowCounts.Length > 0 ? rowCounts.Max() : 0;
                    int columnThreshold = Math.Max(2, (int)Math.Round(maxColumn * 0.16));
                    int rowThreshold = Math.Max(2, (int)Math.Round(maxRow * 0.16));

                    int minX = Array.FindIndex(columnCounts, x => x >= columnThreshold);
                    int maxX = Array.FindLastIndex(columnCounts, x => x >= columnThreshold);
                    int minY = Array.FindIndex(rowCounts, x => x >= rowThreshold);
                    int maxY = Array.FindLastIndex(rowCounts, x => x >= rowThreshold);

                    if (minX < 0 || maxX < minX || minY < 0 || maxY < minY)
                    {
                        minX = rawMinX;
                        maxX = rawMaxX;
                        minY = rawMinY;
                        maxY = rawMaxY;
                    }

                    System.Drawing.Rectangle result = new System.Drawing.Rectangle(
                        searchArea.X + minX,
                        searchArea.Y + minY,
                        Math.Max(1, maxX - minX + 1),
                        Math.Max(1, maxY - minY + 1));

                    if (result.Width < 4 || result.Height < 4)
                    {
                        return System.Drawing.Rectangle.Empty;
                    }

                    return result;
                }
                finally
                {
                    if (data != null)
                    {
                        bitmap.UnlockBits(data);
                    }
                }
            }
        }

        private static bool IsUsableImageRectangle(System.Drawing.Rectangle rect)
        {
            return rect.Width >= 4 && rect.Height >= 4;
        }

        private static string FormatRectangle(System.Drawing.Rectangle rect)
        {
            return "(" + rect.X + "," + rect.Y + "," + rect.Width + "," + rect.Height + ")";
        }

        private static void CompositeFullPageLayoutOverlay(string overallImagePath, string overlayImagePath)
        {
            if (string.IsNullOrWhiteSpace(overallImagePath) ||
                string.IsNullOrWhiteSpace(overlayImagePath) ||
                !File.Exists(overallImagePath) ||
                !File.Exists(overlayImagePath))
            {
                return;
            }

            string stagedPath = overallImagePath + ".full_layout_overlay.png";
            try
            {
                using (System.Drawing.Bitmap baseImage = new System.Drawing.Bitmap(overallImagePath))
                using (System.Drawing.Bitmap overlayImage = new System.Drawing.Bitmap(overlayImagePath))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(baseImage))
                using (System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes())
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                    attributes.SetColorKey(
                        System.Drawing.Color.FromArgb(246, 246, 246),
                        System.Drawing.Color.FromArgb(255, 255, 255));

                    // Same crop + same orientation + same PixelSize means these should already be
                    // equal-sized PNGs. Draw to the full base rectangle; the explicit rectangle is
                    // also a safe fallback for a one-pixel Revit raster rounding difference.
                    graphics.DrawImage(
                        overlayImage,
                        new System.Drawing.Rectangle(0, 0, baseImage.Width, baseImage.Height),
                        0,
                        0,
                        overlayImage.Width,
                        overlayImage.Height,
                        System.Drawing.GraphicsUnit.Pixel,
                        attributes);

                    baseImage.Save(stagedPath, System.Drawing.Imaging.ImageFormat.Png);

                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] OverallTop full-page overlay composited. BasePx=" +
                        baseImage.Width + "x" + baseImage.Height +
                        ", OverlayPx=" + overlayImage.Width + "x" + overlayImage.Height);
                }

                File.Copy(stagedPath, overallImagePath, true);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop full-page overlay compositing failed. " + ex.Message);
            }
            finally
            {
                TryDeleteFile(stagedPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void CompositeLayoutOverlayOnOverallTop(
            ViewPlan overallView,
            string overallImagePath,
            string keyPlanImagePath,
            BoundingBoxXYZ overallModelCrop,
            BoundingBoxXYZ overlayModelBox)
        {
            if (overallView == null ||
                string.IsNullOrWhiteSpace(overallImagePath) ||
                string.IsNullOrWhiteSpace(keyPlanImagePath) ||
                overallModelCrop == null ||
                overlayModelBox == null)
            {
                return;
            }

            try
            {
                string stagedPath = overallImagePath + ".layout_overlay.png";
                using (System.Drawing.Bitmap baseImage = new System.Drawing.Bitmap(overallImagePath))
                using (System.Drawing.Bitmap overlayImage = new System.Drawing.Bitmap(keyPlanImagePath))
                {
                    System.Drawing.Rectangle target = ModelBoxToImageRectangle(
                        overallView,
                        overallModelCrop,
                        overlayModelBox,
                        baseImage.Width,
                        baseImage.Height);

                    if (target.Width < 2 || target.Height < 2)
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[LayoutPlanReport] OverallTop overlay skipped: target rectangle is empty.");
                        return;
                    }

                    // Keep a tiny inset so the TOP-3D room walls do not cover the original plan
                    // linework exactly on the crop boundary.  The room visualization and MEP
                    // remain clearly visible, matching the prototype's highlighted room.
                    int inset = Math.Max(0, Math.Min(target.Width, target.Height) / 120);
                    if (target.Width > inset * 2 + 2 && target.Height > inset * 2 + 2)
                    {
                        target = new System.Drawing.Rectangle(
                            target.X + inset,
                            target.Y + inset,
                            target.Width - inset * 2,
                            target.Height - inset * 2);
                    }

                    using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(baseImage))
                    using (System.Drawing.Imaging.ImageAttributes attributes = new System.Drawing.Imaging.ImageAttributes())
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                        // Revit TOP-3D exports have a white background.  Make only the near-white
                        // range transparent so the architectural FloorPlan, grids and annotation
                        // remain visible around/through the layout overlay.
                        attributes.SetColorKey(
                            System.Drawing.Color.FromArgb(246, 246, 246),
                            System.Drawing.Color.FromArgb(255, 255, 255));

                        graphics.DrawImage(
                            overlayImage,
                            target,
                            0,
                            0,
                            overlayImage.Width,
                            overlayImage.Height,
                            System.Drawing.GraphicsUnit.Pixel,
                            attributes);
                    }

                    baseImage.Save(stagedPath, System.Drawing.Imaging.ImageFormat.Png);

                    DiagnosticRecorder.AppendDebug(
                        "[LayoutPlanReport] OverallTop AHU/MEP overlay prepared. TargetPx=(" +
                        target.X + "," + target.Y + "," + target.Width + "," + target.Height + ")" +
                        ", OverlayModel=" + FormatBox(overlayModelBox));
                }

                // Replace the Revit export only after Bitmap releases its file handle.
                File.Copy(stagedPath, overallImagePath, true);
                File.Delete(stagedPath);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop AHU/MEP overlay composited successfully.");
            }
            catch (Exception ex)
            {
                // The Revit FloorPlan image is still valid if image compositing fails.  Record a
                // precise diagnostic rather than failing the entire two-page report.
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] OverallTop AHU/MEP overlay failed. " + ex.Message);
            }
        }

        private static System.Drawing.Rectangle ModelBoxToImageRectangle(
            View view,
            BoundingBoxXYZ overallModelCrop,
            BoundingBoxXYZ overlayModelBox,
            int imageWidth,
            int imageHeight)
        {
            if (view == null || overallModelCrop == null || overlayModelBox == null ||
                imageWidth <= 0 || imageHeight <= 0)
            {
                return System.Drawing.Rectangle.Empty;
            }

            BoundingBoxXYZ currentCrop = view.CropBox;
            Transform viewToModel = currentCrop != null && currentCrop.Transform != null
                ? currentCrop.Transform
                : Transform.Identity;
            Transform modelToView = viewToModel.Inverse;
            double modelZ = viewToModel.Origin.Z;

            double overallMinX;
            double overallMinY;
            double overallMaxX;
            double overallMaxY;
            GetModelBoxLocalExtents(
                modelToView,
                modelZ,
                overallModelCrop,
                out overallMinX,
                out overallMinY,
                out overallMaxX,
                out overallMaxY);

            double overlayMinX;
            double overlayMinY;
            double overlayMaxX;
            double overlayMaxY;
            GetModelBoxLocalExtents(
                modelToView,
                modelZ,
                overlayModelBox,
                out overlayMinX,
                out overlayMinY,
                out overlayMaxX,
                out overlayMaxY);

            double width = Math.Max(1e-9, overallMaxX - overallMinX);
            double height = Math.Max(1e-9, overallMaxY - overallMinY);

            double nx1 = (overlayMinX - overallMinX) / width;
            double nx2 = (overlayMaxX - overallMinX) / width;
            // Raster Y grows downward; Revit view-local Y grows upward.
            double ny1 = (overallMaxY - overlayMaxY) / height;
            double ny2 = (overallMaxY - overlayMinY) / height;

            int x1 = ClampInt((int)Math.Round(nx1 * imageWidth), 0, imageWidth);
            int x2 = ClampInt((int)Math.Round(nx2 * imageWidth), 0, imageWidth);
            int y1 = ClampInt((int)Math.Round(ny1 * imageHeight), 0, imageHeight);
            int y2 = ClampInt((int)Math.Round(ny2 * imageHeight), 0, imageHeight);

            return new System.Drawing.Rectangle(
                Math.Min(x1, x2),
                Math.Min(y1, y2),
                Math.Abs(x2 - x1),
                Math.Abs(y2 - y1));
        }

        private static void GetModelBoxLocalExtents(
            Transform modelToView,
            double modelZ,
            BoundingBoxXYZ box,
            out double minX,
            out double minY,
            out double maxX,
            out double maxY)
        {
            minX = double.MaxValue;
            minY = double.MaxValue;
            maxX = double.MinValue;
            maxY = double.MinValue;

            XYZ[] corners =
            {
                new XYZ(box.Min.X, box.Min.Y, modelZ),
                new XYZ(box.Min.X, box.Max.Y, modelZ),
                new XYZ(box.Max.X, box.Min.Y, modelZ),
                new XYZ(box.Max.X, box.Max.Y, modelZ)
            };

            foreach (XYZ point in corners)
            {
                XYZ local = modelToView.OfPoint(point);
                minX = Math.Min(minX, local.X);
                minY = Math.Min(minY, local.Y);
                maxX = Math.Max(maxX, local.X);
                maxY = Math.Max(maxY, local.Y);
            }
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static void PrepareKeyPlanLayoutVisibility(Document doc, ViewPlan view, RoomLayoutPlanDto plan, BoundingBoxXYZ crop)
        {
            if (doc == null || view == null || plan == null)
            {
                return;
            }

            // Do not let the source view template re-apply category/filter rules to this
            // temporary report view. This is one of the common reasons the AHU family is
            // present in the model but missing from an exported duplicated floor plan.
            try
            {
                if (view.ViewTemplateId != ElementId.InvalidElementId)
                {
                    view.ViewTemplateId = ElementId.InvalidElementId;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan view template detach skipped: " + ex.Message);
            }

            try
            {
                view.DetailLevel = ViewDetailLevel.Fine;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan detail level setup skipped: " + ex.Message);
            }

            // A filter copied from the architectural plan can hide Mechanical Equipment,
            // Generic Models, Ducts or Pipes even after the category itself is visible.
            try
            {
                foreach (ElementId filterId in view.GetFilters())
                {
                    try
                    {
                        view.SetFilterVisibility(filterId, true);
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan filter visibility setup skipped: " + ex.Message);
            }

            HashSet<ElementId> layoutIds = new HashSet<ElementId>(GetSavedElementIds(doc, plan));

            // Include room-visualization DirectShapes that intersect the selected room crop.
            // This preserves the blue room background used by the normal Layout Plan view.
            foreach (DirectShape shape in new FilteredElementCollector(doc)
                .OfClass(typeof(DirectShape))
                .Cast<DirectShape>())
            {
                string name = shape != null ? (shape.Name ?? string.Empty) : string.Empty;
                string data = shape != null ? (shape.ApplicationDataId ?? string.Empty) : string.Empty;
                if (shape == null ||
                    (name.IndexOf("ROOMVIS", StringComparison.OrdinalIgnoreCase) < 0 &&
                     data.IndexOf("REGION::", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                BoundingBoxXYZ box = GetModelBox(shape, view);
                if (box != null && BoxesIntersectXy(box, crop))
                {
                    layoutIds.Add(shape.Id);
                }
            }

            int categoriesShown = 0;
            int elementsUnhidden = 0;

            // First force the categories used by the saved Layout Plan to be visible.
            HashSet<ElementId> categoryIds = new HashSet<ElementId>();
            foreach (ElementId id in layoutIds)
            {
                Element element = doc.GetElement(id);
                if (element != null && element.Category != null)
                {
                    categoryIds.Add(element.Category.Id);
                }
            }

            BuiltInCategory[] requiredCategories =
            {
                BuiltInCategory.OST_MechanicalEquipment,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_DuctCurves,
                BuiltInCategory.OST_DuctFitting,
                BuiltInCategory.OST_DuctAccessory,
                BuiltInCategory.OST_DuctTerminal,
                BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_PipeFitting,
                BuiltInCategory.OST_PipeAccessory
            };

            foreach (BuiltInCategory category in requiredCategories)
            {
                categoryIds.Add(new ElementId(category));
            }

            foreach (ElementId categoryId in categoryIds)
            {
                try
                {
                    if (view.CanCategoryBeHidden(categoryId))
                    {
                        view.SetCategoryHidden(categoryId, false);
                        categoriesShown++;
                    }
                }
                catch
                {
                }
            }

            // Elements may also have been hidden individually in the source plan. Unhide only
            // the saved Layout Plan and the current room visualization on this temporary view.
            List<ElementId> hiddenIds = new List<ElementId>();
            foreach (ElementId id in layoutIds)
            {
                try
                {
                    Element element = doc.GetElement(id);
                    if (element != null && element.IsHidden(view))
                    {
                        hiddenIds.Add(id);
                    }
                }
                catch
                {
                }
            }

            if (hiddenIds.Count > 0)
            {
                try
                {
                    view.UnhideElements(hiddenIds);
                    elementsUnhidden = hiddenIds.Count;
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan element unhide skipped: " + ex.Message);
                }
            }

            ExpandKeyPlanViewRange(doc, view, plan);

            DiagnosticRecorder.AppendDebug(
                "[LayoutPlanReport] KeyPlan layout visibility prepared. LayoutElements=" + layoutIds.Count +
                ", CategoriesShown=" + categoriesShown +
                ", ElementsUnhidden=" + elementsUnhidden +
                ", View=" + (view.Name ?? string.Empty));
        }

        private static void ExpandKeyPlanViewRange(Document doc, ViewPlan view, RoomLayoutPlanDto plan)
        {
            if (doc == null || view == null || plan == null || view.GenLevel == null)
            {
                return;
            }

            BoundingBoxXYZ content = Union(
                CollectRoomVisualizationBox(doc, view, plan),
                CollectSavedLayoutElementBox(doc, view, plan, null));
            if (content == null)
            {
                return;
            }

            try
            {
                PlanViewRange range = view.GetViewRange();
                double levelZ = view.GenLevel.Elevation;
                double desiredTop = content.Max.Z - levelZ + ToFeet(1000.0);
                double desiredBottom = content.Min.Z - levelZ - ToFeet(500.0);

                double top = range.GetOffset(PlanViewPlane.TopClipPlane);
                double bottom = range.GetOffset(PlanViewPlane.BottomClipPlane);
                double depth = range.GetOffset(PlanViewPlane.ViewDepthPlane);

                if (!double.IsNaN(desiredTop) && !double.IsInfinity(desiredTop))
                {
                    range.SetOffset(PlanViewPlane.TopClipPlane, Math.Max(top, desiredTop));
                }

                if (!double.IsNaN(desiredBottom) && !double.IsInfinity(desiredBottom))
                {
                    double newBottom = Math.Min(bottom, desiredBottom);
                    range.SetOffset(PlanViewPlane.BottomClipPlane, newBottom);
                    range.SetOffset(PlanViewPlane.ViewDepthPlane, Math.Min(depth, newBottom));
                }

                view.SetViewRange(range);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] KeyPlan view range expanded. ContentZ=" +
                    FromFeet(content.Min.Z).ToString("F0") + ".." +
                    FromFeet(content.Max.Z).ToString("F0") + " mm");
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] KeyPlan view range expansion skipped: " + ex.Message);
            }
        }

        private static bool BoxesIntersectXy(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            return a.Max.X >= b.Min.X && a.Min.X <= b.Max.X &&
                   a.Max.Y >= b.Min.Y && a.Min.Y <= b.Max.Y;
        }

        private static void HideViewSpecificAnnotations(Document doc, View view)
        {
            if (doc == null || view == null || view.Id == ElementId.InvalidElementId)
            {
                return;
            }

            int hidden = 0;
            foreach (Element element in new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType())
            {
                try
                {
                    if (element == null || !element.ViewSpecific || element.Category == null ||
                        element.Category.CategoryType != CategoryType.Annotation)
                    {
                        continue;
                    }

                    view.HideElements(new List<ElementId> { element.Id });
                    hidden++;
                }
                catch
                {
                    // Some view-specific system elements cannot be hidden individually.
                }
            }

            DiagnosticRecorder.AppendDebug("[LayoutPlanReport] View-specific annotations hidden=" + hidden);
        }

        private static void HideDatumCategories(View view)
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
                    ElementId id = new ElementId(category);
                    if (view.CanCategoryBeHidden(id))
                    {
                        view.SetCategoryHidden(id, true);
                    }
                }
                catch
                {
                }
            }
        }

        private static string ExportView(Document doc, View view, string tempDirectory, string prefix, int pixelSize)
        {
            if (doc == null || view == null)
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

        private static void AddMain3DImageBorder(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return;
            }

            string stagedPath = imagePath + ".border.png";
            try
            {
                // Draw the report-frame border directly into the exported PNG. The PDF service
                // already places this PNG into the Main 3D panel, so doing it here keeps the
                // change isolated to Layout Plan Report image export and does not affect the
                // existing Delivery Route PDF implementation.
                using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(imagePath))
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                using (System.Drawing.Pen pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(75, 75, 75), 6.0f))
                {
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    graphics.DrawRectangle(
                        pen,
                        3,
                        3,
                        Math.Max(1, bitmap.Width - 7),
                        Math.Max(1, bitmap.Height - 7));
                    bitmap.Save(stagedPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                File.Copy(stagedPath, imagePath, true);
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Main3D image border applied. Path=" + imagePath);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[LayoutPlanReport] Main3D image border skipped. " + ex.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(stagedPath))
                    {
                        File.Delete(stagedPath);
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
                using (Transaction tx = new Transaction(doc, "Cleanup Layout Plan Report View"))
                {
                    tx.Start();
                    doc.Delete(viewId);
                    tx.Commit();
                }

                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Temporary views cleaned.");
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[LayoutPlanReport] Temporary view cleanup failed. " + ex.Message);
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
    }
}
