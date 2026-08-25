using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Commands;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Models;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Models.Units;
using CadToRevit.Services;
using CadToRevit.Services.Cad;
using CadToRevit.Services.CadRuntime;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathPreview;
using CadToRevit.Services.Preview;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Units;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace CadToRevit.UI.Dockable
{
    public sealed class PreviewPaneDataService
    {
        private const double FtToMm = 304.8;
        private const double DefaultWallHeightMm = 4000.0;
        // CAD layer highlight settings.
        // Change these values to adjust the highlight color and line weight.
        private const byte CadLayerHighlightRed = 0;
        private const byte CadLayerHighlightGreen = 80;
        private const byte CadLayerHighlightBlue = 255;

        // Set to 0 or a negative value to keep original CAD line weight and only change color.
        private const int CadLayerHighlightLineWeight = 5;

        private static readonly Color CadLayerHighlightColor = new Color(
            CadLayerHighlightRed,
            CadLayerHighlightGreen,
            CadLayerHighlightBlue);

        private ElementId _lastHighlightedCadLayerCategoryId = ElementId.InvalidElementId;
        private ElementId _lastHighlightedCadLayerViewId = ElementId.InvalidElementId;
        private OverrideGraphicSettings _lastHighlightedCadLayerOriginalOverrides;
        private string _lastHighlightedCadLayerName;


        public PreviewPaneState BuildState(UIApplication uiApp)
        {
            Document doc = uiApp != null && uiApp.ActiveUIDocument != null ? uiApp.ActiveUIDocument.Document : null;
            PreviewPaneState state = new PreviewPaneState
            {
                DocumentTitle = doc != null ? doc.Title : "(No Document)",
                LevelName = "-",
                IsCadVisible = true,
                IsBuildingElementsVisible = true,
                RoomRecognitionSettings = RoomRecognitionSettings.CreateDefault(),
                GlobalGenerationSettings = GlobalGenerationSettings.CreateDefault()
            };
            if (doc == null)
            {
                return state;
            }

            LayerOverrideStoreData overrideData = LoadDocScopedOverrides(doc);
            state.RoomRecognitionSettings = RoomRecognitionSettings.Clone(overrideData != null ? overrideData.RoomRecognitionSettings : null);
            state.GlobalGenerationSettings = GlobalGenerationSettings.Clone(overrideData != null ? overrideData.GlobalGenerationSettings : null);
            View activeView = doc.ActiveView;
            if (activeView != null)
            {
                state.IsCadVisible = ComputeCadVisibleInView(doc, activeView);
                state.IsBuildingElementsVisible = ComputeGeneratedBuildingElementsVisibleInView(doc, activeView);
            }

            if (doc.ActiveView != null && doc.ActiveView.GenLevel != null)
            {
                state.LevelName = doc.ActiveView.GenLevel.Name;
                return state;
            }

            Level level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
            state.LevelName = level != null ? level.Name : "(No Level)";
            return state;
        }

        public List<PreviewPaneLayerItem> LoadLayerMappings(UIApplication uiApp)
        {
            Document doc = uiApp != null && uiApp.ActiveUIDocument != null ? uiApp.ActiveUIDocument.Document : null;
            if (doc == null)
            {
                return new List<PreviewPaneLayerItem>();
            }

            List<MapRow> savedRows = new List<MapRow>();
            string contextSignature = BuildDockableContextSignature(doc);
            // Prefer persistent state store, then fallback to in-session cache for unsaved documents.
            bool hasSavedRows = WizardStateStoreService.TryLoad(doc, contextSignature, out savedRows);
            if (!hasSavedRows)
            {
                hasSavedRows = WizardSessionCache.TryLoad(doc, contextSignature, out savedRows);
            }
            LayerOverrideStoreData overrideData = LoadDocScopedOverrides(doc);
            HashSet<string> dwgLayers = LoadDwgLayerNames(doc);
            Dictionary<MapCategory, List<(string Name, ElementId Id)>> familyCatalog = BuildFamilyCatalog(doc);
            ImportInstance currentImport = ResolveCurrentImportInstance(doc);
            Level currentLevel = ResolveLevel(doc);
            HashSet<string> generatedLayerKeys = BuildGeneratedLayerKeys(doc, currentImport, currentLevel);
            Dictionary<string, bool> generatedLayerHiddenStates = BuildGeneratedLayerHiddenStates(doc, currentImport, currentLevel, doc.ActiveView);

            Dictionary<string, MapRow> byLayer = new Dictionary<string, MapRow>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> savedLayerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (hasSavedRows)
            {
                foreach (MapRow row in savedRows.Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName)))
                {
                    if (!dwgLayers.Contains(row.RawLayerName))
                    {
                        continue;
                    }

                    byLayer[row.RawLayerName] = row;
                    savedLayerNames.Add(row.RawLayerName);
                }
            }

            foreach (string layer in dwgLayers)
            {
                if (!byLayer.ContainsKey(layer))
                {
                    MapCategory inferredCategory = InferDefaultCategoryFromLayerName(layer);
                    byLayer[layer] = new MapRow
                    {
                        RawLayerName = layer,
                        Category = inferredCategory,
                        Settings = new AdvancedSettingsRow
                        {
                            EnableLayerOverride = true
                        }
                    };
                }
            }

            foreach (KeyValuePair<string, AdvancedSettingsRow> kv in overrideData.LayerOverrides)
            {
                if (!byLayer.TryGetValue(kv.Key, out MapRow row))
                {
                    continue;
                }

                ApplySettings(row.Settings, kv.Value);
            }

            List<PreviewPaneLayerItem> result = new List<PreviewPaneLayerItem>();
            foreach (MapRow row in byLayer.Values.OrderBy(x => x.RawLayerName, StringComparer.OrdinalIgnoreCase))
            {
                AdvancedSettingsRow s = row.Settings ?? new AdvancedSettingsRow();
                PreviewPaneLayerItem item = ToLayerItem(row, s, savedLayerNames.Contains(row.RawLayerName));
                item.IsGenerated = IsLayerGenerated(row, currentImport, currentLevel, generatedLayerKeys);
                if (item.IsGenerated && currentImport != null && currentLevel != null && row != null)
                {
                    string generatedRowKey = WizardGenerationTrackingStoreService.BuildRowKey(row.RawLayerName, row.Category, currentLevel.Id, currentImport.Id);
                    bool isHidden;
                    if (generatedLayerHiddenStates.TryGetValue(generatedRowKey, out isHidden))
                    {
                        item.IsGeneratedElementsHidden = isHidden;
                    }
                }
                foreach (KeyValuePair<MapCategory, List<(string Name, ElementId Id)>> kv in familyCatalog)
                {
                    List<string> names = kv.Value.Select(x => x.Name).ToList();
                    item.FamilyTypeOptionsByCategory[kv.Key] = names;
                }

                if (item.Category.HasValue &&
                    (item.Category.Value == MapCategory.Unknown || item.Category.Value == MapCategory.NotForBuild))
                {
                    item.FamilyTypeOptions.Add(UnknownFamilyTypePlaceholder);
                    item.FamilyTypeName = UnknownFamilyTypePlaceholder;
                }
                else if (item.Category.HasValue && item.FamilyTypeOptionsByCategory.TryGetValue(item.Category.Value, out List<string> currentOptions))
                {
                    foreach (string option in currentOptions)
                    {
                        item.FamilyTypeOptions.Add(option);
                    }
                }

                if (item.Category.HasValue && item.Category.Value != MapCategory.Unknown && item.Category.Value != MapCategory.NotForBuild &&
                    string.IsNullOrWhiteSpace(item.FamilyTypeName) && item.FamilyTypeOptions.Count > 0)
                {
                    item.FamilyTypeName = ResolvePreferredFamilyTypeName(item.Category, item.FamilyTypeOptions)
                        ?? item.FamilyTypeOptions[0];
                }

                result.Add(item);
            }

            return result;
        }

        public PreviewPaneResponse SaveLayerMappings(UIApplication uiApp, IList<PreviewPaneLayerItem> items, RoomRecognitionSettings roomRecognitionSettings, GlobalGenerationSettings globalGenerationSettings)
        {
            Document doc = uiApp != null && uiApp.ActiveUIDocument != null ? uiApp.ActiveUIDocument.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            List<MapRow> rows = new List<MapRow>();
            Dictionary<MapCategory, List<(string Name, ElementId Id)>> familyCatalog = BuildFamilyCatalog(doc);
            foreach (PreviewPaneLayerItem item in items ?? new List<PreviewPaneLayerItem>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName))
                {
                    continue;
                }

                MapCategory savedCategory = ResolveSavedCategory(item);
                bool canResolveType = IsGeneratableCategory(savedCategory);
                MapRow row = new MapRow
                {
                    RawLayerName = item.RawLayerName.Trim(),
                    Category = savedCategory,
                    RevitTypeName = canResolveType ? item.FamilyTypeName : string.Empty,
                    RevitTypeId = canResolveType
                        ? ResolveTypeId(familyCatalog, savedCategory, item.FamilyTypeName)
                        : ElementId.InvalidElementId,
                    Settings = ToAdvancedSettings(item)
                };
                rows.Add(row);
            }

            LayerOverrideStoreService.Save(doc, rows, roomRecognitionSettings, globalGenerationSettings);
            string contextSignature = BuildDockableContextSignature(doc);
            WizardStateStoreService.Save(doc, contextSignature, rows);
            WizardSessionCache.Save(doc, contextSignature, rows);

            return new PreviewPaneResponse { Message = "Layer mappings saved." };
        }

        public PreviewPaneAnalyzeSnapshot CaptureAnalyzeSnapshot(UIApplication uiApp)
        {
            Document doc = uiApp != null && uiApp.ActiveUIDocument != null ? uiApp.ActiveUIDocument.Document : null;
            if (doc == null)
            {
                return new PreviewPaneAnalyzeSnapshot { DwgName = "(No DWG)", UnitText = "Auto" };
            }

            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return new PreviewPaneAnalyzeSnapshot { DwgName = "(No DWG)", UnitText = "Auto" };
            }

            CadSegmentBuildResult build = CadSegmentBuilder.BuildSegments(doc, import, null);
            UnitContext ctx = BuildRevitImportInstanceUnitContext(doc);
            CadToRevit.Models.Cad.CadDataset scaled = CadDatasetScaler.Scale(CadDatasetBuilder.Build(build), ctx);
            List<CadSegment> segments = scaled != null ? scaled.Segments ?? new List<CadSegment>() : new List<CadSegment>();

            PreviewPaneAnalyzeSnapshot snapshot = new PreviewPaneAnalyzeSnapshot
            {
                DwgName = import.Name ?? string.Empty,
                UnitText = ctx != null ? ctx.SourceUnit.ToString() : "Auto",
                LayerCount = scaled != null && scaled.SegmentsByRawLayer != null ? scaled.SegmentsByRawLayer.Count : 0,
                SegmentCount = segments.Count,
                ArcCount = segments.Count(x => x != null && x.IsArc),
                PolylineCount = segments.Count(x => x != null && x.SourceType == CadCurveSourceType.PolyLineSegment),
                RawLayerNames = scaled != null && scaled.SegmentsByRawLayer != null
                    ? scaled.SegmentsByRawLayer.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                    : new List<string>(),
                LengthsMm = segments
                    .Where(x => x != null && x.P0 != null && x.P1 != null)
                    .Select(x => x.P0.DistanceTo(x.P1) * FtToMm)
                    .OrderBy(x => x)
                    .ToList()
            };
            return snapshot;
        }

        public PreviewPaneAnalyzeReport ComputeAnalyzeReport(PreviewPaneAnalyzeSnapshot snapshot)
        {
            PreviewPaneAnalyzeSnapshot s = snapshot ?? new PreviewPaneAnalyzeSnapshot();
            PreviewPaneAnalyzeReport report = new PreviewPaneAnalyzeReport
            {
                DwgName = s.DwgName,
                UnitText = s.UnitText,
                LayerCount = s.LayerCount,
                SegmentCount = s.SegmentCount,
                ArcCount = s.ArcCount,
                PolylineCount = s.PolylineCount,
                P50LengthMm = Percentile(s.LengthsMm, 0.5),
                P90LengthMm = Percentile(s.LengthsMm, 0.9)
            };

            int linear = Math.Max(0, s.SegmentCount - s.ArcCount);
            report.PreviewWallCount = Math.Max(0, (int)Math.Round(linear * 0.18));
            report.PreviewDoorCount = Math.Max(0, (int)Math.Round((s.ArcCount * 0.30) + (s.PolylineCount * 0.15)));
            if (s.SegmentCount == 0)
            {
                report.Errors.Add("No CAD segments found.");
            }
            if (s.LayerCount == 0)
            {
                report.Errors.Add("No CAD layers found.");
            }

            return report;
        }

        public PreviewPaneResponse ExecutePreview(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null)
            {
                return Error("No active document.");
            }

            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return Error("No CAD Link found.");
            }

            CadSegmentBuildResult build = CadSegmentBuilder.BuildSegments(doc, import, null);
            UnitContext ctx = BuildRevitImportInstanceUnitContext(doc);
            CadToRevit.Models.Cad.CadDataset scaled = CadDatasetScaler.Scale(CadDatasetBuilder.Build(build), ctx);
            HashSet<string> selectedLayers = GetWallRawLayers(doc, scaled);
            PreviewResult preview = PreviewService.ShowLayerSegments(uiDoc, scaled, selectedLayers);

            PreviewPaneResponse response = new PreviewPaneResponse
            {
                Message = preview != null ? preview.Message : "Preview finished."
            };
            return response;
        }

        public PreviewPaneResponse ExecuteClearPreview(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            PreviewService.Clear(doc);
            return new PreviewPaneResponse { Message = "Preview cleared." };
        }

        public PreviewPaneResponse ExecuteCreateWalls(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            WallType wallType = ResolveWallType(doc);
            if (wallType == null)
            {
                return Error("No wall type found.");
            }

            List<CadSegment> segments = (CadSegmentBuilder.BuildSegments(doc, import, null).Segments ?? new List<CadSegment>())
                .Where(x => x != null)
                .ToList();
            HashSet<string> selectedLayers = GetWallRawLayers(doc, segments);
            AdvancedSettingsRow row = TryGetWallCategoryDefaults(doc);
            WallRecognitionResult detect = WallRecognitionEngine.RecognizeWalls(segments, selectedLayers, row, null);
            List<WallCenterlineCandidate> centerlines = detect != null ? detect.Centerlines ?? new List<WallCenterlineCandidate>() : new List<WallCenterlineCandidate>();
            if (centerlines.Count == 0)
            {
                return Error("No wall centerlines detected.");
            }

            double heightMm = row != null && row.WallHeightMm.HasValue && row.WallHeightMm.Value > 0 ? row.WallHeightMm.Value : DefaultWallHeightMm;
            double baseOffsetMm = row != null && row.WallBaseOffsetMm.HasValue ? row.WallBaseOffsetMm.Value : 0.0;
            double wallHeightFeet = UnitUtils.ConvertToInternalUnits(heightMm, UnitTypeId.Millimeters);
            double baseOffsetFeet = UnitUtils.ConvertToInternalUnits(baseOffsetMm, UnitTypeId.Millimeters);

            int created = 0;
            int failed = 0;
            List<string> errors = new List<string>();
            using (Transaction tx = new Transaction(doc, "Dockable Create Walls"))
            {
                tx.Start();
                FailureHandlingOptions fho = tx.GetFailureHandlingOptions();
                fho.SetFailuresPreprocessor(new WallBatchFailuresPreprocessor());
                tx.SetFailureHandlingOptions(fho);

                foreach (WallCenterlineCandidate candidate in centerlines)
                {
                    if (candidate == null || candidate.CenterLine == null || candidate.CenterLine.Length <= 1e-6)
                    {
                        failed++;
                        continue;
                    }

                    try
                    {
                        Wall wall = Wall.Create(doc, candidate.CenterLine, wallType.Id, level.Id, wallHeightFeet, baseOffsetFeet, false, false);
                        ApplySingleWallPlacementMode(wall, candidate, row);
                        created++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        if (errors.Count < 20)
                        {
                            errors.Add(ex.Message);
                        }
                    }
                }

                tx.Commit();
            }

            PreviewPaneResponse response = new PreviewPaneResponse
            {
                Message = "Walls created: " + created + ", failed: " + failed,
                Errors = errors
            };
            return response;
        }

        // Apply placement strategy for force-single walls from tagged double-line pairs.
        private static void ApplySingleWallPlacementMode(Wall wall, WallCenterlineCandidate candidate, AdvancedSettingsRow rowSettings)
        {
            if (wall == null || candidate == null || rowSettings == null)
            {
                return;
            }

            // Only derived single-wall candidates can use this placement mode.
            if (!candidate.IsDoubleLinePairedSingleWall)
            {
                return;
            }

            if (!string.Equals(
                rowSettings.WallDoubleLineSingleWallPlaceMode,
                AdvancedSettingsRow.WallPlaceModeInsideFaceOnCadLine,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Parameter keyRef = wall.get_Parameter(BuiltInParameter.WALL_KEY_REF_PARAM);
            if (keyRef != null && !keyRef.IsReadOnly)
            {
                keyRef.Set((int)WallLocationLine.FinishFaceExterior);
            }

            XYZ inside = candidate.InsideNormal;
            if (inside == null)
            {
                return;
            }

            XYZ inside2d = new XYZ(inside.X, inside.Y, 0);
            if (inside2d.GetLength() <= 1e-9)
            {
                return;
            }

            XYZ wallOrientation = wall.Orientation;
            if (wallOrientation == null)
            {
                return;
            }

            XYZ wall2d = new XYZ(wallOrientation.X, wallOrientation.Y, 0);
            if (wall2d.GetLength() <= 1e-9)
            {
                return;
            }

            if (wall2d.Normalize().DotProduct(inside2d.Normalize()) < 0)
            {
                wall.Flip();
            }
        }

        public PreviewPaneResponse ExecuteCreateDoors(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return Error("No CAD Link found.");
            }

            List<MapRow> persistedRows;
            string contextSignature = BuildDockableContextSignature(doc);
            bool hasRows = WizardStateStoreService.TryLoad(doc, contextSignature, out persistedRows);
            List<MapRow> doorRows = (hasRows ? (persistedRows ?? new List<MapRow>()) : new List<MapRow>())
                .Where(x => x != null &&
                            x.Category == MapCategory.Doors &&
                            !string.IsNullOrWhiteSpace(x.RawLayerName))
                .ToList();

            Dictionary<MapCategory, List<(string Name, ElementId Id)>> catalog = BuildFamilyCatalog(doc);
            int created = 0;
            int skipped = 0;
            List<string> reasons = new List<string>();

            if (doorRows.Count > 0)
            {
                // Keep preview creation aligned with persisted layer settings and selected type.
                foreach (MapRow row in doorRows)
                {
                    DoorDetectSettings detectSettings = BuildDoorDetectSettings(row.Settings);
                    DoorDetectResult detect = DoorCandidateDetector.DetectByRawLayer(doc, import, detectSettings, row.RawLayerName);
                    if (detect == null || detect.Candidates == null || detect.Candidates.Count == 0)
                    {
                        continue;
                    }

                    FamilySymbol forcedSymbol = null;
                    ElementId typeId = ResolveTypeId(catalog, MapCategory.Doors, row.RevitTypeName);
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                    {
                        forcedSymbol = doc.GetElement(typeId) as FamilySymbol;
                    }

                    DoorCreateResult result = DoorCreatorService.CreateDoors(
                        doc,
                        detect.Candidates,
                        forcedSymbol,
                        null,
                        true,
                        null,
                        row.Settings);

                    created += result.CreatedDoors;
                    skipped += result.SkippedDoors;
                    foreach (string reason in result.SkipReasons ?? new List<string>())
                    {
                        if (reasons.Count >= 20)
                        {
                            break;
                        }

                        reasons.Add("[" + row.RawLayerName + "] " + reason);
                    }
                }
            }
            else
            {
                // Fallback for legacy usage when no persisted mappings exist.
                DoorDetectSettings settings = new DoorDetectSettings();
                DoorDetectResult detect = DoorCandidateDetector.Detect(doc, import, settings);
                if (detect == null || detect.Candidates == null || detect.Candidates.Count == 0)
                {
                    return Error("No door candidates detected.");
                }

                DoorCreateResult result = DoorCreatorService.CreateDoors(doc, detect.Candidates);
                created = result.CreatedDoors;
                skipped = result.SkippedDoors;
                reasons = (result.SkipReasons ?? new List<string>()).Take(20).ToList();
            }

            PreviewPaneResponse response = new PreviewPaneResponse
            {
                Message = "Doors created: " + created + ", skipped: " + skipped
            };

            foreach (string reason in reasons)
            {
                if (response.Errors.Count >= 20)
                {
                    break;
                }

                response.Errors.Add(reason);
            }

            return response;
        }

        private static DoorDetectSettings BuildDoorDetectSettings(AdvancedSettingsRow settings)
        {
            DoorDetectSettings detect = new DoorDetectSettings();
            if (settings == null)
            {
                return detect;
            }

            if (settings.MinDoorWidthMm.HasValue && settings.MinDoorWidthMm.Value > 0)
            {
                detect.DoorWidthMinMm = settings.MinDoorWidthMm.Value;
            }

            if (settings.MaxDoorWidthMm.HasValue && settings.MaxDoorWidthMm.Value > 0)
            {
                detect.DoorWidthMaxMm = settings.MaxDoorWidthMm.Value;
            }

            if (settings.DoorWallMatchTolMm.HasValue && settings.DoorWallMatchTolMm.Value > 0)
            {
                detect.WallMatchDistTolMm = settings.DoorWallMatchTolMm.Value;
            }

            return detect;
        }

        public PreviewPaneResponse ExecuteCreateFloors(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            FloorType floorType = new FilteredElementCollector(doc)
                .OfClass(typeof(FloorType))
                .Cast<FloorType>()
                .FirstOrDefault();
            if (floorType == null)
            {
                return Error("No floor type found.");
            }

            BoundingBoxXYZ box = CollectModelBoundingBox(doc) ?? CollectImportBoundingBox(doc);
            if (box == null)
            {
                return Error("No model range found.");
            }

            double padding = UnitUtils.ConvertToInternalUnits(1000.0, UnitTypeId.Millimeters);
            double minX = box.Min.X - padding;
            double minY = box.Min.Y - padding;
            double maxX = box.Max.X + padding;
            double maxY = box.Max.Y + padding;
            double z = level.Elevation;

            CurveLoop loop = new CurveLoop();
            loop.Append(Line.CreateBound(new XYZ(minX, minY, z), new XYZ(maxX, minY, z)));
            loop.Append(Line.CreateBound(new XYZ(maxX, minY, z), new XYZ(maxX, maxY, z)));
            loop.Append(Line.CreateBound(new XYZ(maxX, maxY, z), new XYZ(minX, maxY, z)));
            loop.Append(Line.CreateBound(new XYZ(minX, maxY, z), new XYZ(minX, minY, z)));

            using (Transaction tx = new Transaction(doc, "Dockable Create Floor"))
            {
                tx.Start();
                Floor floor = Floor.Create(doc, new List<CurveLoop> { loop }, floorType.Id, level.Id);
                if (floor != null)
                {
                    Parameter pStructural = floor.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
                    if (pStructural != null && !pStructural.IsReadOnly)
                    {
                        pStructural.Set(0);
                    }
                }

                tx.Commit();
            }

            return new PreviewPaneResponse { Message = "Floor created." };
        }

        public PreviewPaneResponse ExecuteCreateElements(UIApplication uiApp)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            WarnIfCadRuntimeUnavailable(uiApp);

            List<MapRow> persistedRows;
            string contextSignature = BuildDockableContextSignature(doc);
            bool hasRows = WizardStateStoreService.TryLoad(doc, contextSignature, out persistedRows);
            if (!hasRows || persistedRows == null || persistedRows.Count == 0)
            {
                return Error("No saved layer mappings found. Please click Save first.");
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            VerticalDimensionSettings verticalSettings = VerticalDimensionStoreService.Load(doc);
            GlobalGenerationSettings globalSettings = LoadGlobalGenerationSettings(doc);
            Dictionary<string, MapRow> currentSelectedByKey = BuildCurrentSelectedRowsByKey(doc, selectedImport.Id, level.Id, persistedRows);
            Dictionary<string, int> selectedRowOrderByKey = BuildSelectedRowOrderByKey(persistedRows, selectedImport.Id, level.Id);
            Dictionary<string, WizardGenerationRowRecord> historyByKey = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .Select(x => new
                {
                    Key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x),
                    Record = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Record, StringComparer.OrdinalIgnoreCase);

            SyncPlan syncPlan = BuildSyncPlan(currentSelectedByKey, historyByKey);
            DiagnosticRecorder.AppendDebug(
                "[SyncPlan] CurrentSelected=" + currentSelectedByKey.Count +
                ", History=" + historyByKey.Count +
                ", Create=" + syncPlan.RowsToCreate.Count +
                ", Rebuild=" + syncPlan.RowsToRebuild.Count +
                ", Delete=" + syncPlan.RowsToDelete.Count +
                ", Skip=" + syncPlan.RowsToSkip.Count);

            int deletedRows = 0;
            int deletedElements = 0;
            int rebuiltRows = 0;
            int createdRows = 0;
            int skippedRows = 0;
            int createdElements = 0;
            List<string> errors = new List<string>();
            string batchId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            ClearRoomVisualizationBeforeLayerOperation(doc);

            using (TransactionGroup tg = new TransactionGroup(doc, "Sync Elements From CAD Layers"))
            {
                tg.Start();

                foreach (WizardGenerationRowRecord deleteRecord in syncPlan.RowsToDelete)
                {
                    string stableDeleteKey = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(deleteRecord);
                    using (Transaction tx = new Transaction(doc, "Delete Generated Elements - " + stableDeleteKey))
                    {
                        tx.Start();
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, deleteRecord, errors);
                        tx.Commit();

                        if (cleanup.DeletedCount > 0)
                        {
                            ApplyDeletedElementsToTracking(historyByKey, cleanup, stableDeleteKey);
                        }
                        deletedRows++;
                        deletedElements += cleanup.DeletedCount;
                        historyByKey.Remove(stableDeleteKey);
                        LogRowAction(
                            stableDeleteKey,
                            deleteRecord.RawLayerName,
                            "Delete",
                            "Unchecked",
                            cleanup.RequestedCount,
                            cleanup.DeletedCount);
                        if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                        {
                            DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                            errors.Add(cleanup.WarningMessage);
                        }
                        foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                        {
                            DiagnosticRecorder.AppendDebug(detail);
                        }
                    }
                }

                List<PendingGenerationAction> pendingActions = BuildPendingGenerationActions(
                    syncPlan,
                    selectedImport.Id,
                    level.Id,
                    selectedRowOrderByKey);
                LogGenerationOrder(
                    "Create",
                    pendingActions.Select(x => x.Row));

                foreach (PendingGenerationAction action in pendingActions)
                {
                    MapRow row = action.Row;
                    if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                    {
                        continue;
                    }

                    string rowKey = action.RowKey;
                    if (action.DeleteBeforeBuild && historyByKey.TryGetValue(rowKey, out WizardGenerationRowRecord oldRecord))
                    {
                        using (Transaction tx = new Transaction(doc, "Delete Before Rebuild - " + rowKey))
                        {
                            tx.Start();
                            CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, oldRecord, errors);
                            tx.Commit();
                            if (cleanup.DeletedCount > 0)
                            {
                                ApplyDeletedElementsToTracking(historyByKey, cleanup, rowKey);
                            }
                            deletedElements += cleanup.DeletedCount;
                            LogRowAction(
                                rowKey,
                                row.RawLayerName,
                                "Delete",
                                "ConfigChanged",
                                cleanup.RequestedCount,
                                cleanup.DeletedCount);
                            if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                            {
                                DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                                errors.Add(cleanup.WarningMessage);
                            }
                            foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                            {
                                DiagnosticRecorder.AppendDebug(detail);
                            }
                        }
                    }

                    HashSet<int> beforeIds = CollectCategoryElementIds(doc, row.Category);
                    LogGenerationRun(action.BuildAction, row);
                    LogRowAction(rowKey, row.RawLayerName, action.BuildAction, action.BuildReason);

                    CreateElementsExecutionSummary run = WallWizardCommand.ExecuteForDockable(
                        doc,
                        selectedImport.Id,
                        level.Id,
                        new List<MapRow> { row },
                        joinWallsAfterCreate: globalSettings.AutoJoinWallsAfterCreate,
                        safeModeEnabled: globalSettings.SafeModeEnabled,
                        verticalSettings: verticalSettings,
                        enableIdempotencySkip: false);

                    HashSet<int> afterIds = CollectCategoryElementIds(doc, row.Category);
                    List<int> newIds = ResolveGeneratedElementIdsForRow(doc, row, run, beforeIds, afterIds);
                    createdElements += newIds.Count;
                    if (action.DeleteBeforeBuild)
                    {
                        rebuiltRows++;
                    }
                    else
                    {
                        createdRows++;
                    }

                    WizardGenerationRowRecord newRecord = new WizardGenerationRowRecord
                    {
                        RowKey = rowKey,
                        RawLayerName = row.RawLayerName ?? string.Empty,
                        Category = row.Category.ToString(),
                        LevelId = level.Id.IntegerValue,
                        DwgId = selectedImport.Id.IntegerValue,
                        RevitTypeName = row.RevitTypeName ?? string.Empty,
                        MappingFingerprint = WizardGenerationTrackingStoreService.BuildMappingFingerprint(row),
                        GenerationBatchId = batchId,
                        LastGeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                        ElementIds = newIds,
                        LastSyncAction = action.BuildAction,
                        LastSyncReason = action.BuildReason,
                        GeneratedCount = newIds.Count,
                        LastSyncedAt = DateTime.UtcNow.ToString("o")
                    };
                    historyByKey[rowKey] = newRecord;

                    if (newIds.Count > 0)
                    {
                        using (Transaction tx = new Transaction(doc, "Stamp Generated Metadata - " + rowKey))
                        {
                            tx.Start();
                            GeneratedElementMetadataService.WriteBatch(
                                doc,
                                newIds.Select(x => new ElementId(x)).ToList(),
                                rowKey,
                                batchId,
                                row.RawLayerName ?? string.Empty,
                                row.Category.ToString(),
                                level.Id.IntegerValue,
                                selectedImport.Id.IntegerValue);
                            tx.Commit();
                        }
                    }

                    if (run != null && run.Errors != null && run.Errors.Count > 0)
                    {
                        foreach (string err in run.Errors.Take(20))
                        {
                            if (errors.Count >= 60)
                            {
                                break;
                            }

                            errors.Add("[Row " + row.RawLayerName + "] " + err);
                        }
                    }

                    DiagnosticRecorder.AppendDebug("[RowBuildFinish] RowKey=" + rowKey + ", Created=" + newIds.Count);
                }

                foreach (MapRow row in syncPlan.RowsToSkip)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                    {
                        continue;
                    }

                    string rowKey = WizardGenerationTrackingStoreService.BuildRowKey(row.RawLayerName, row.Category, level.Id, selectedImport.Id);
                    skippedRows++;
                    LogRowAction(rowKey, row.RawLayerName, "Skip", "NoChange");
                }

                WizardGenerationTrackingStoreService.Save(doc, historyByKey.Values.ToList());
                DiagnosticRecorder.AppendDebug(
                    "[TrackingSave] Rows=" + historyByKey.Count +
                    ", UpdatedAtUtc=" + DateTime.UtcNow.ToString("o"));

                tg.Assimilate();
            }

            MarkRoutePlannerDirty(doc, "CAD layer sync changed generated elements.");

            return new PreviewPaneResponse
            {
                Message =
                    "Sync finished. DeletedRows=" + deletedRows +
                    ", DeletedElements=" + deletedElements +
                    ", CreatedRows=" + createdRows +
                    ", RebuiltRows=" + rebuiltRows +
                    ", SkippedRows=" + skippedRows +
                    ", CreatedElements=" + createdElements +
                    ", Errors=" + errors.Count +
                    ", ElapsedMs=" + stopwatch.ElapsedMilliseconds,
                Errors = errors
            };
        }

        public PreviewPaneResponse ExecuteDeleteSingleLayer(UIApplication uiApp, string rawLayerName, MapCategory? selectedCategory)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            if (string.IsNullOrWhiteSpace(rawLayerName))
            {
                return Error("Invalid layer selection.");
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            List<WizardGenerationRowRecord> allRows = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();

            string exactRowKey = selectedCategory.HasValue
                ? WizardGenerationTrackingStoreService.BuildRowKey(rawLayerName.Trim(), selectedCategory.Value, level.Id, selectedImport.Id)
                : string.Empty;

            List<WizardGenerationRowRecord> targetRecords = allRows
                .Where(x => x != null && x.DwgId == selectedImport.Id.IntegerValue && x.LevelId == level.Id.IntegerValue)
                .Where(IsGeneratedRecord)
                .Where(x => !string.IsNullOrWhiteSpace(exactRowKey) &&
                    string.Equals(WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x), exactRowKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (targetRecords.Count == 0)
            {
                targetRecords = allRows
                    .Where(x => x != null && x.DwgId == selectedImport.Id.IntegerValue && x.LevelId == level.Id.IntegerValue)
                    .Where(IsGeneratedRecord)
                    .Where(x => string.Equals((x.RawLayerName ?? string.Empty).Trim(), rawLayerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (targetRecords.Count == 0)
            {
                return new PreviewPaneResponse
                {
                    Message = "No generated elements found for layer " + rawLayerName + "."
                };
            }

            int deletedElements = 0;
            List<string> errors = new List<string>();
            Dictionary<string, WizardGenerationRowRecord> historyByKey = allRows
                .Select(x => new
                {
                    Key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x),
                    Record = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Record, StringComparer.OrdinalIgnoreCase);

            ClearRoomVisualizationBeforeLayerOperation(doc);

            using (TransactionGroup tg = new TransactionGroup(doc, "Delete CAD Layer Generated Elements - " + rawLayerName))
            {
                tg.Start();

                foreach (WizardGenerationRowRecord record in targetRecords)
                {
                    string rowKey = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(record);
                    using (Transaction tx = new Transaction(doc, "Delete Generated Elements - " + rowKey))
                    {
                        tx.Start();
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, record, errors);
                        tx.Commit();

                        deletedElements += cleanup.DeletedCount;
                        if (cleanup.DeletedCount > 0)
                        {
                            ApplyDeletedElementsToTracking(historyByKey, cleanup, rowKey);
                        }

                        historyByKey.Remove(rowKey);
                        LogRowAction(
                            rowKey,
                            record.RawLayerName,
                            "Delete",
                            "UserTriggeredSingleLayerDelete",
                            cleanup.RequestedCount,
                            cleanup.DeletedCount);

                        if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                        {
                            DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                            errors.Add(cleanup.WarningMessage);
                        }

                        foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                        {
                            DiagnosticRecorder.AppendDebug(detail);
                        }
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, historyByKey.Values.ToList());
                tg.Assimilate();
            }

            MarkRoutePlannerDirty(doc, "CAD layer delete changed generated elements.");

            if (uiDoc != null)
            {
                uiDoc.RefreshActiveView();
            }

            return new PreviewPaneResponse
            {
                Message =
                    "Delete layer finished. Layer=" + rawLayerName +
                    ", DeletedRows=" + targetRecords.Count +
                    ", DeletedElements=" + deletedElements +
                    ", Errors=" + errors.Count +
                    ", ElapsedMs=" + stopwatch.ElapsedMilliseconds,
                Errors = errors
            };
        }

        public PreviewPaneResponse ExecuteDeleteSelectedLayers(UIApplication uiApp)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            List<MapRow> persistedRows;
            string contextSignature = BuildDockableContextSignature(doc);
            bool hasRows = WizardStateStoreService.TryLoad(doc, contextSignature, out persistedRows);
            if (!hasRows || persistedRows == null || persistedRows.Count == 0)
            {
                return Error("No saved layer mappings found. Please click Save first.");
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            Dictionary<string, MapRow> selectedRowsByKey = BuildCurrentSelectedRowsByKey(doc, selectedImport.Id, level.Id, persistedRows);
            HashSet<string> selectedKeys = new HashSet<string>(selectedRowsByKey.Keys, StringComparer.OrdinalIgnoreCase);
            HashSet<string> selectedRawLayerNames = new HashSet<string>(
                selectedRowsByKey.Values
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                    .Select(x => x.RawLayerName.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (selectedKeys.Count == 0 && selectedRawLayerNames.Count == 0)
            {
                return new PreviewPaneResponse
                {
                    Message = "No generatable selected layers found. Please select at least one generated layer before deleting."
                };
            }

            List<WizardGenerationRowRecord> allRows = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();

            List<WizardGenerationRowRecord> targetRecords = allRows
                .Where(x => x != null && x.DwgId == selectedImport.Id.IntegerValue && x.LevelId == level.Id.IntegerValue)
                .Where(IsGeneratedRecord)
                .Where(x =>
                {
                    string key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x);
                    bool keyMatched = !string.IsNullOrWhiteSpace(key) && selectedKeys.Contains(key);
                    bool layerMatched = !string.IsNullOrWhiteSpace(x.RawLayerName) && selectedRawLayerNames.Contains(x.RawLayerName.Trim());
                    return keyMatched || layerMatched;
                })
                .GroupBy(x => WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x), StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Last())
                .ToList();

            if (targetRecords.Count == 0)
            {
                return new PreviewPaneResponse
                {
                    Message = "No generated elements found for selected layers."
                };
            }

            int deletedElements = 0;
            int deletedRows = 0;
            List<string> errors = new List<string>();
            Dictionary<string, WizardGenerationRowRecord> historyByKey = allRows
                .Select(x => new
                {
                    Key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x),
                    Record = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Record, StringComparer.OrdinalIgnoreCase);

            ClearRoomVisualizationBeforeLayerOperation(doc);

            using (TransactionGroup tg = new TransactionGroup(doc, "Delete Selected CAD Layer Generated Elements"))
            {
                tg.Start();

                foreach (WizardGenerationRowRecord record in targetRecords)
                {
                    string rowKey = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(record);
                    using (Transaction tx = new Transaction(doc, "Delete Generated Elements - " + rowKey))
                    {
                        tx.Start();
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, record, errors);
                        tx.Commit();

                        deletedRows++;
                        deletedElements += cleanup.DeletedCount;
                        if (cleanup.DeletedCount > 0)
                        {
                            ApplyDeletedElementsToTracking(historyByKey, cleanup, rowKey);
                        }

                        historyByKey.Remove(rowKey);
                        LogRowAction(
                            rowKey,
                            record.RawLayerName,
                            "Delete",
                            "UserTriggeredSelectedLayersDelete",
                            cleanup.RequestedCount,
                            cleanup.DeletedCount);

                        if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                        {
                            DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                            errors.Add(cleanup.WarningMessage);
                        }

                        foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                        {
                            DiagnosticRecorder.AppendDebug(detail);
                        }
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, historyByKey.Values.ToList());
                tg.Assimilate();
            }

            MarkRoutePlannerDirty(doc, "CAD batch delete changed generated elements.");

            if (uiDoc != null)
            {
                uiDoc.RefreshActiveView();
            }

            return new PreviewPaneResponse
            {
                Message =
                    "Delete selected layers finished. DeletedRows=" + deletedRows +
                    ", DeletedElements=" + deletedElements +
                    ", Errors=" + errors.Count +
                    ", ElapsedMs=" + stopwatch.ElapsedMilliseconds,
                Errors = errors
            };
        }

        public PreviewPaneResponse ExecuteGenerateSingleLayer(UIApplication uiApp, PreviewPaneLayerItem selectedLayerItem)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            WarnIfCadRuntimeUnavailable(uiApp);

            if (selectedLayerItem == null || string.IsNullOrWhiteSpace(selectedLayerItem.RawLayerName))
            {
                return Error("Please select a valid layer before generating.");
            }

            if (!selectedLayerItem.Category.HasValue || !IsGeneratableCategory(selectedLayerItem.Category.Value))
            {
                return Error("Please set a valid Category and Family Type before generating this layer.");
            }

            if (string.IsNullOrWhiteSpace(selectedLayerItem.FamilyTypeName) ||
                string.Equals(selectedLayerItem.FamilyTypeName.Trim(), UnknownFamilyTypePlaceholder, StringComparison.OrdinalIgnoreCase))
            {
                return Error("Please set a valid Category and Family Type before generating this layer.");
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            MapCategory category = selectedLayerItem.Category.Value;
            Dictionary<MapCategory, List<(string Name, ElementId Id)>> familyCatalog = BuildFamilyCatalog(doc);
            MapRow targetRow = new MapRow
            {
                RawLayerName = selectedLayerItem.RawLayerName.Trim(),
                Category = category,
                RevitTypeName = selectedLayerItem.FamilyTypeName,
                RevitTypeId = ResolveTypeId(familyCatalog, category, selectedLayerItem.FamilyTypeName),
                Settings = ToAdvancedSettings(selectedLayerItem)
            };

            VerticalDimensionSettings verticalSettings = VerticalDimensionStoreService.Load(doc);
            GlobalGenerationSettings globalSettings = LoadGlobalGenerationSettings(doc);
            string rowKey = WizardGenerationTrackingStoreService.BuildRowKey(targetRow.RawLayerName, targetRow.Category, level.Id, selectedImport.Id);
            List<WizardGenerationRowRecord> allRows = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();
            Dictionary<string, WizardGenerationRowRecord> historyByKey = allRows
                .Select(x => new
                {
                    Key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x),
                    Record = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Record, StringComparer.OrdinalIgnoreCase);

            bool hadGeneratedRecord = historyByKey.TryGetValue(rowKey, out WizardGenerationRowRecord oldRecord) && IsGeneratedRecord(oldRecord);
            string action = hadGeneratedRecord ? "Rebuild" : "Generate";
            string syncAction = hadGeneratedRecord ? "RegenerateSingleLayer" : "GenerateSingleLayer";
            int deletedElements = 0;
            int createdElements = 0;
            List<string> errors = new List<string>();
            string batchId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            ClearRoomVisualizationBeforeLayerOperation(doc);

            using (TransactionGroup tg = new TransactionGroup(doc, "Generate CAD Layer - " + targetRow.RawLayerName))
            {
                tg.Start();

                if (oldRecord != null)
                {
                    using (Transaction tx = new Transaction(doc, "Delete Generated Elements - " + rowKey))
                    {
                        tx.Start();
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, oldRecord, errors);
                        tx.Commit();
                        if (cleanup.DeletedCount > 0)
                        {
                            ApplyDeletedElementsToTracking(historyByKey, cleanup, rowKey);
                        }

                        deletedElements += cleanup.DeletedCount;
                        historyByKey.Remove(rowKey);
                        LogRowAction(
                            rowKey,
                            targetRow.RawLayerName,
                            "Delete",
                            syncAction,
                            cleanup.RequestedCount,
                            cleanup.DeletedCount);
                        if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                        {
                            DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                            errors.Add(cleanup.WarningMessage);
                        }

                        foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                        {
                            DiagnosticRecorder.AppendDebug(detail);
                        }
                    }
                }

                HashSet<int> beforeIds = CollectCategoryElementIds(doc, targetRow.Category);
                LogGenerationRun(syncAction, targetRow);
                LogRowAction(rowKey, targetRow.RawLayerName, syncAction, "UserTriggeredSingleLayer");

                CreateElementsExecutionSummary run = WallWizardCommand.ExecuteForDockable(
                    doc,
                    selectedImport.Id,
                    level.Id,
                    new List<MapRow> { targetRow },
                    joinWallsAfterCreate: globalSettings.AutoJoinWallsAfterCreate,
                    safeModeEnabled: globalSettings.SafeModeEnabled,
                    verticalSettings: verticalSettings,
                    enableIdempotencySkip: false);

                HashSet<int> afterIds = CollectCategoryElementIds(doc, targetRow.Category);
                List<int> newIds = ResolveGeneratedElementIdsForRow(doc, targetRow, run, beforeIds, afterIds);
                createdElements += newIds.Count;

                WizardGenerationRowRecord newRecord = new WizardGenerationRowRecord
                {
                    RowKey = rowKey,
                    RawLayerName = targetRow.RawLayerName ?? string.Empty,
                    Category = targetRow.Category.ToString(),
                    LevelId = level.Id.IntegerValue,
                    DwgId = selectedImport.Id.IntegerValue,
                    RevitTypeName = targetRow.RevitTypeName ?? string.Empty,
                    MappingFingerprint = WizardGenerationTrackingStoreService.BuildMappingFingerprint(targetRow),
                    GenerationBatchId = batchId,
                    LastGeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                    ElementIds = newIds,
                    LastSyncAction = syncAction,
                    LastSyncReason = "UserTriggeredSingleLayer",
                    GeneratedCount = newIds.Count,
                    LastSyncedAt = DateTime.UtcNow.ToString("o")
                };
                historyByKey[rowKey] = newRecord;

                if (newIds.Count > 0)
                {
                    using (Transaction tx = new Transaction(doc, "Stamp Generated Metadata - " + rowKey))
                    {
                        tx.Start();
                        GeneratedElementMetadataService.WriteBatch(
                            doc,
                            newIds.Select(x => new ElementId(x)).ToList(),
                            rowKey,
                            batchId,
                            targetRow.RawLayerName ?? string.Empty,
                            targetRow.Category.ToString(),
                            level.Id.IntegerValue,
                            selectedImport.Id.IntegerValue);
                        tx.Commit();
                    }
                }

                if (run != null && run.Errors != null && run.Errors.Count > 0)
                {
                    foreach (string err in run.Errors.Take(20))
                    {
                        if (errors.Count >= 60)
                        {
                            break;
                        }

                        errors.Add("[Row " + targetRow.RawLayerName + "] " + err);
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, historyByKey.Values.ToList());
                tg.Assimilate();
            }

            MarkRoutePlannerDirty(doc, "CAD layer generate/rebuild changed generated elements.");

            return new PreviewPaneResponse
            {
                Message =
                    action + " layer finished. Layer=" + targetRow.RawLayerName +
                    ", Category=" + targetRow.Category +
                    ", DeletedElements=" + deletedElements +
                    ", CreatedElements=" + createdElements +
                    ", Errors=" + errors.Count +
                    ", ElapsedMs=" + stopwatch.ElapsedMilliseconds,
                Errors = errors
            };
        }

        public PreviewPaneResponse ExecuteGenerateSingleLayer(UIApplication uiApp, string rawLayerName, MapCategory? selectedCategory)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            WarnIfCadRuntimeUnavailable(uiApp);

            if (string.IsNullOrWhiteSpace(rawLayerName) || !selectedCategory.HasValue || !IsGeneratableCategory(selectedCategory.Value))
            {
                return Error("Invalid layer selection.");
            }

            List<MapRow> persistedRows;
            string contextSignature = BuildDockableContextSignature(doc);
            bool hasRows = WizardStateStoreService.TryLoad(doc, contextSignature, out persistedRows);
            if (!hasRows || persistedRows == null || persistedRows.Count == 0)
            {
                return Error("No saved layer mappings found. Please click Save first.");
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            MapRow targetRow = persistedRows
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                .Where(x => string.Equals(x.RawLayerName.Trim(), rawLayerName.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(x => IsGeneratableCategory(x.Category))
                .OrderByDescending(x => x.Category == selectedCategory.Value)
                .FirstOrDefault();
            if (targetRow == null)
            {
                return Error("Selected layer is not enabled for generation. Please check the layer and category, then try again.");
            }

            VerticalDimensionSettings verticalSettings = VerticalDimensionStoreService.Load(doc);
            GlobalGenerationSettings globalSettings = LoadGlobalGenerationSettings(doc);
            string rowKey = WizardGenerationTrackingStoreService.BuildRowKey(targetRow.RawLayerName, targetRow.Category, level.Id, selectedImport.Id);
            List<WizardGenerationRowRecord> allRows = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();
            Dictionary<string, WizardGenerationRowRecord> historyByKey = allRows
                .Select(x => new
                {
                    Key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x),
                    Record = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Record, StringComparer.OrdinalIgnoreCase);

            bool hadGeneratedRecord = historyByKey.TryGetValue(rowKey, out WizardGenerationRowRecord oldRecord) && IsGeneratedRecord(oldRecord);
            string action = hadGeneratedRecord ? "Regen" : "Gen";
            string syncAction = hadGeneratedRecord ? "RegenerateSingleLayer" : "GenerateSingleLayer";
            int deletedElements = 0;
            int createdElements = 0;
            List<string> errors = new List<string>();
            string batchId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            ClearRoomVisualizationBeforeLayerOperation(doc);

            using (TransactionGroup tg = new TransactionGroup(doc, "Generate CAD Layer - " + targetRow.RawLayerName))
            {
                tg.Start();

                if (oldRecord != null)
                {
                    using (Transaction tx = new Transaction(doc, "Delete Generated Elements - " + rowKey))
                    {
                        tx.Start();
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, oldRecord, errors);
                        tx.Commit();
                        if (cleanup.DeletedCount > 0)
                        {
                            ApplyDeletedElementsToTracking(historyByKey, cleanup, rowKey);
                        }

                        deletedElements += cleanup.DeletedCount;
                        historyByKey.Remove(rowKey);
                        LogRowAction(
                            rowKey,
                            targetRow.RawLayerName,
                            "Delete",
                            syncAction,
                            cleanup.RequestedCount,
                            cleanup.DeletedCount);
                        if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                        {
                            DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                            errors.Add(cleanup.WarningMessage);
                        }

                        foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                        {
                            DiagnosticRecorder.AppendDebug(detail);
                        }
                    }
                }

                HashSet<int> beforeIds = CollectCategoryElementIds(doc, targetRow.Category);
                LogGenerationRun(syncAction, targetRow);
                LogRowAction(rowKey, targetRow.RawLayerName, syncAction, "UserTriggeredSingleLayer");

                CreateElementsExecutionSummary run = WallWizardCommand.ExecuteForDockable(
                    doc,
                    selectedImport.Id,
                    level.Id,
                    new List<MapRow> { targetRow },
                    joinWallsAfterCreate: globalSettings.AutoJoinWallsAfterCreate,
                    safeModeEnabled: globalSettings.SafeModeEnabled,
                    verticalSettings: verticalSettings,
                    enableIdempotencySkip: false);

                HashSet<int> afterIds = CollectCategoryElementIds(doc, targetRow.Category);
                List<int> newIds = ResolveGeneratedElementIdsForRow(doc, targetRow, run, beforeIds, afterIds);
                createdElements += newIds.Count;

                WizardGenerationRowRecord newRecord = new WizardGenerationRowRecord
                {
                    RowKey = rowKey,
                    RawLayerName = targetRow.RawLayerName ?? string.Empty,
                    Category = targetRow.Category.ToString(),
                    LevelId = level.Id.IntegerValue,
                    DwgId = selectedImport.Id.IntegerValue,
                    RevitTypeName = targetRow.RevitTypeName ?? string.Empty,
                    MappingFingerprint = WizardGenerationTrackingStoreService.BuildMappingFingerprint(targetRow),
                    GenerationBatchId = batchId,
                    LastGeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                    ElementIds = newIds,
                    LastSyncAction = syncAction,
                    LastSyncReason = "UserTriggeredSingleLayer",
                    GeneratedCount = newIds.Count,
                    LastSyncedAt = DateTime.UtcNow.ToString("o")
                };
                historyByKey[rowKey] = newRecord;

                if (newIds.Count > 0)
                {
                    using (Transaction tx = new Transaction(doc, "Stamp Generated Metadata - " + rowKey))
                    {
                        tx.Start();
                        GeneratedElementMetadataService.WriteBatch(
                            doc,
                            newIds.Select(x => new ElementId(x)).ToList(),
                            rowKey,
                            batchId,
                            targetRow.RawLayerName ?? string.Empty,
                            targetRow.Category.ToString(),
                            level.Id.IntegerValue,
                            selectedImport.Id.IntegerValue);
                        tx.Commit();
                    }
                }

                if (run != null && run.Errors != null && run.Errors.Count > 0)
                {
                    foreach (string err in run.Errors.Take(20))
                    {
                        if (errors.Count >= 60)
                        {
                            break;
                        }

                        errors.Add("[Row " + targetRow.RawLayerName + "] " + err);
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, historyByKey.Values.ToList());
                tg.Assimilate();
            }

            MarkRoutePlannerDirty(doc, "CAD layer generate/rebuild changed generated elements.");

            return new PreviewPaneResponse
            {
                Message =
                    action + " layer finished. Layer=" + targetRow.RawLayerName +
                    ", Category=" + targetRow.Category +
                    ", DeletedElements=" + deletedElements +
                    ", CreatedElements=" + createdElements +
                    ", Errors=" + errors.Count +
                    ", ElapsedMs=" + stopwatch.ElapsedMilliseconds,
                Errors = errors
            };
        }

        public PreviewPaneResponse ExecuteRegenerateAll(UIApplication uiApp)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return Error("No active document.");
            }

            WarnIfCadRuntimeUnavailable(uiApp);

            List<MapRow> persistedRows;
            string contextSignature = BuildDockableContextSignature(doc);
            bool hasRows = WizardStateStoreService.TryLoad(doc, contextSignature, out persistedRows);
            if (!hasRows || persistedRows == null || persistedRows.Count == 0)
            {
                return Error("No saved layer mappings found. Please click Save first.");
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return Error("No CAD Link found.");
            }

            Level level = ResolveLevel(doc);
            if (level == null)
            {
                return Error("No level found.");
            }

            VerticalDimensionSettings verticalSettings = VerticalDimensionStoreService.Load(doc);
            GlobalGenerationSettings globalSettings = LoadGlobalGenerationSettings(doc);
            Dictionary<string, MapRow> currentSelectedByKey = BuildCurrentSelectedRowsByKey(doc, selectedImport.Id, level.Id, persistedRows);
            if (currentSelectedByKey.Count == 0)
            {
                return Error("Please select at least one valid layer before rebuilding.");
            }

            Dictionary<string, int> selectedRowOrderByKey = BuildSelectedRowOrderByKey(persistedRows, selectedImport.Id, level.Id);
            HashSet<string> selectedLayerNames = new HashSet<string>(
                currentSelectedByKey.Values
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                    .Select(x => x.RawLayerName.Trim()),
                StringComparer.OrdinalIgnoreCase);
            List<WizardGenerationRowRecord> allRows = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();
            List<WizardGenerationRowRecord> currentDwgRows = allRows
                .Where(x => x.DwgId == selectedImport.Id.IntegerValue)
                .Where(x => x.LevelId == level.Id.IntegerValue)
                .Where(x => selectedLayerNames.Contains((x.RawLayerName ?? string.Empty).Trim()))
                .ToList();
            Dictionary<string, WizardGenerationRowRecord> historyByKey = allRows
                .Select(x => new
                {
                    Key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(x),
                    Record = x
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Last().Record, StringComparer.OrdinalIgnoreCase);

            int deletedRows = 0;
            int deletedElements = 0;
            int createdRows = 0;
            int createdElements = 0;
            List<string> errors = new List<string>();
            string batchId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            ClearRoomVisualizationBeforeLayerOperation(doc);

            using (TransactionGroup tg = new TransactionGroup(doc, "Rebuild Selected CAD Layers"))
            {
                tg.Start();

                foreach (WizardGenerationRowRecord row in currentDwgRows)
                {
                    string rowKey = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(row);
                    using (Transaction tx = new Transaction(doc, "Delete Generated Elements - " + rowKey))
                    {
                        tx.Start();
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, row, errors);
                        tx.Commit();

                        if (cleanup.DeletedCount > 0)
                        {
                            ApplyDeletedElementsToTracking(historyByKey, cleanup, rowKey);
                        }

                        historyByKey.Remove(rowKey);
                        deletedRows++;
                        deletedElements += cleanup.DeletedCount;
                        if (cleanup.HasWarning && !string.IsNullOrWhiteSpace(cleanup.WarningMessage))
                        {
                            DiagnosticRecorder.AppendDebug(cleanup.WarningMessage);
                            errors.Add(cleanup.WarningMessage);
                        }

                        foreach (string detail in cleanup.ForeignDeleteDecisionLogs.Take(60))
                        {
                            DiagnosticRecorder.AppendDebug(detail);
                        }
                    }
                }

                List<KeyValuePair<string, MapRow>> orderedRows = OrderRowsForGeneration(currentSelectedByKey, selectedRowOrderByKey);
                LogGenerationOrder(
                    "Regenerate",
                    orderedRows.Select(x => x.Value));

                foreach (KeyValuePair<string, MapRow> kv in orderedRows)
                {
                    string rowKey = kv.Key;
                    MapRow row = kv.Value;
                    if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                    {
                        continue;
                    }

                    HashSet<int> beforeIds = CollectCategoryElementIds(doc, row.Category);
                    LogGenerationRun("Regenerate", row);
                    LogRowAction(rowKey, row.RawLayerName, "Regenerate", "UserTriggeredSelectedLayersRebuild");

                    CreateElementsExecutionSummary run = WallWizardCommand.ExecuteForDockable(
                        doc,
                        selectedImport.Id,
                        level.Id,
                        new List<MapRow> { row },
                        joinWallsAfterCreate: globalSettings.AutoJoinWallsAfterCreate,
                        safeModeEnabled: globalSettings.SafeModeEnabled,
                        verticalSettings: verticalSettings,
                        enableIdempotencySkip: false);

                    HashSet<int> afterIds = CollectCategoryElementIds(doc, row.Category);
                    List<int> newIds = ResolveGeneratedElementIdsForRow(doc, row, run, beforeIds, afterIds);
                    createdRows++;
                    createdElements += newIds.Count;

                    WizardGenerationRowRecord newRecord = new WizardGenerationRowRecord
                    {
                        RowKey = rowKey,
                        RawLayerName = row.RawLayerName ?? string.Empty,
                        Category = row.Category.ToString(),
                        LevelId = level.Id.IntegerValue,
                        DwgId = selectedImport.Id.IntegerValue,
                        RevitTypeName = row.RevitTypeName ?? string.Empty,
                        MappingFingerprint = WizardGenerationTrackingStoreService.BuildMappingFingerprint(row),
                        GenerationBatchId = batchId,
                        LastGeneratedAtUtc = DateTime.UtcNow.ToString("o"),
                        ElementIds = newIds,
                        LastSyncAction = "Regenerate",
                        LastSyncReason = "UserTriggeredSelectedLayersRebuild",
                        GeneratedCount = newIds.Count,
                        LastSyncedAt = DateTime.UtcNow.ToString("o")
                    };
                    historyByKey[rowKey] = newRecord;

                    if (newIds.Count > 0)
                    {
                        using (Transaction tx = new Transaction(doc, "Stamp Generated Metadata - " + rowKey))
                        {
                            tx.Start();
                            GeneratedElementMetadataService.WriteBatch(
                                doc,
                                newIds.Select(x => new ElementId(x)).ToList(),
                                rowKey,
                                batchId,
                                row.RawLayerName ?? string.Empty,
                                row.Category.ToString(),
                                level.Id.IntegerValue,
                                selectedImport.Id.IntegerValue);
                            tx.Commit();
                        }
                    }

                    if (run != null && run.Errors != null && run.Errors.Count > 0)
                    {
                        foreach (string err in run.Errors.Take(20))
                        {
                            if (errors.Count >= 60)
                            {
                                break;
                            }

                            errors.Add("[Row " + row.RawLayerName + "] " + err);
                        }
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, historyByKey.Values.ToList());
                tg.Assimilate();
            }

            MarkRoutePlannerDirty(doc, "CAD batch rebuild changed generated elements.");

            return new PreviewPaneResponse
            {
                Message =
                    "Batch Rebuild finished. DeletedRows=" + deletedRows +
                    ", DeletedElements=" + deletedElements +
                    ", CreatedRows=" + createdRows +
                    ", CreatedElements=" + createdElements +
                    ", Errors=" + errors.Count +
                    ", ElapsedMs=" + stopwatch.ElapsedMilliseconds,
                Errors = errors
            };
        }

        public PreviewPaneResponse ExecuteHighlightGeneratedElementsForSelectedLayer(
            UIApplication uiApp,
            string rawLayerName,
            MapCategory? category)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            View view = doc != null ? doc.ActiveView : null;
            if (doc == null || view == null)
            {
                return new PreviewPaneResponse { Message = "No active view." };
            }

            if (string.IsNullOrWhiteSpace(rawLayerName))
            {
                return ExecuteClearGeneratedElementHighlight(uiApp);
            }

            ImportInstance selectedImport = ResolveCurrentImportInstance(doc);
            if (selectedImport == null)
            {
                return new PreviewPaneResponse
                {
                    Message = Loc.T("DockablePane.Status.HighlightUnavailable")
                };
            }

            List<ElementId> generatedElementIds = ResolveGeneratedElementIdsForSelectedLayer(
                doc,
                view,
                selectedImport,
                rawLayerName,
                category);

            Category cadLayerCategory = FindCadLayerCategory(doc, selectedImport, rawLayerName);
            if (cadLayerCategory == null || cadLayerCategory.Id == null || cadLayerCategory.Id == ElementId.InvalidElementId)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] CAD layer category not found for highlight. Layer=" + rawLayerName);

                uiDoc.Selection.SetElementIds(generatedElementIds);
                uiDoc.RefreshActiveView();

                return new PreviewPaneResponse
                {
                    Message = generatedElementIds.Count > 0
                        ? "CAD layer not found in current DWG, selected generated elements for layer: " + rawLayerName
                        : "CAD layer not found in current DWG: " + rawLayerName
                };
            }

            using (Transaction tx = new Transaction(doc, "Highlight CAD Layer"))
            {
                tx.Start();
                RestorePreviousCadLayerHighlight(doc);

                _lastHighlightedCadLayerCategoryId = cadLayerCategory.Id;
                _lastHighlightedCadLayerViewId = view.Id;
                _lastHighlightedCadLayerName = rawLayerName;
                _lastHighlightedCadLayerOriginalOverrides = TryGetCategoryOverrides(view, cadLayerCategory.Id);

                OverrideGraphicSettings highlight = new OverrideGraphicSettings();
                highlight.SetProjectionLineColor(CadLayerHighlightColor);

                if (CadLayerHighlightLineWeight > 0)
                {
                    highlight.SetProjectionLineWeight(CadLayerHighlightLineWeight);
                }

                view.SetCategoryOverrides(cadLayerCategory.Id, highlight);
                tx.Commit();
            }

            uiDoc.Selection.SetElementIds(generatedElementIds);
            uiDoc.RefreshActiveView();
            return new PreviewPaneResponse
            {
                Message = generatedElementIds.Count > 0
                    ? "Highlighted CAD layer and selected generated elements: " + rawLayerName
                    : "Highlighted CAD layer: " + rawLayerName
            };
        }

        public PreviewPaneResponse ExecuteClearGeneratedElementHighlight(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            View view = doc != null ? doc.ActiveView : null;
            if (doc == null || view == null)
            {
                return new PreviewPaneResponse { Message = "No active view." };
            }

            using (Transaction tx = new Transaction(doc, "Clear CAD Layer Highlight"))
            {
                tx.Start();
                RestorePreviousCadLayerHighlight(doc);
                tx.Commit();
            }

            uiDoc.Selection.SetElementIds(new List<ElementId>());
            uiDoc.RefreshActiveView();

            return new PreviewPaneResponse
            {
                Message = Loc.T("DockablePane.Status.HighlightCleared")
            };
        }

        private static List<ElementId> ResolveGeneratedElementIdsForSelectedLayer(
            Document doc,
            View view,
            ImportInstance selectedImport,
            string rawLayerName,
            MapCategory? category)
        {
            if (doc == null || selectedImport == null || string.IsNullOrWhiteSpace(rawLayerName) || !category.HasValue || !IsGeneratableCategory(category.Value))
            {
                return new List<ElementId>();
            }

            Level level = ResolveLevel(doc);
            WizardGenerationRowRecord record = FindGeneratedLayerRecord(doc, selectedImport, level, rawLayerName, category.Value);
            if (record == null)
            {
                return new List<ElementId>();
            }

            return ResolveExistingGeneratedElementIds(doc, view, record, requireCanBeHidden: false);
        }

        private static Category FindCadLayerCategory(Document doc, ImportInstance import, string rawLayerName)
        {
            if (doc == null || import == null || string.IsNullOrWhiteSpace(rawLayerName))
            {
                return null;
            }

            Category fromSubCategory = FindCadLayerCategoryFromSubCategories(import.Category, rawLayerName);
            if (fromSubCategory != null)
            {
                return fromSubCategory;
            }

            try
            {
                Options options = new Options
                {
                    IncludeNonVisibleObjects = true,
                    ComputeReferences = false,
                    DetailLevel = ViewDetailLevel.Fine
                };

                GeometryElement geometry = import.get_Geometry(options);
                return FindCadLayerCategoryFromGeometry(doc, geometry, rawLayerName);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] Find CAD layer category from geometry failed. Layer=" + rawLayerName + ", Error=" + ex.Message);
                return null;
            }
        }

        private static Category FindCadLayerCategoryFromSubCategories(Category parentCategory, string rawLayerName)
        {
            if (parentCategory == null || parentCategory.SubCategories == null)
            {
                return null;
            }

            foreach (Category subCategory in parentCategory.SubCategories.Cast<Category>())
            {
                if (subCategory == null)
                {
                    continue;
                }

                if (IsCadLayerNameMatch(subCategory.Name, rawLayerName))
                {
                    return subCategory;
                }
            }

            return null;
        }

        private static Category FindCadLayerCategoryFromGeometry(Document doc, GeometryElement geometry, string rawLayerName)
        {
            if (doc == null || geometry == null)
            {
                return null;
            }

            foreach (GeometryObject geometryObject in geometry)
            {
                Category matched = FindCadLayerCategoryFromGeometryObject(doc, geometryObject, rawLayerName);
                if (matched != null)
                {
                    return matched;
                }
            }

            return null;
        }

        private static Category FindCadLayerCategoryFromGeometryObject(Document doc, GeometryObject geometryObject, string rawLayerName)
        {
            if (doc == null || geometryObject == null)
            {
                return null;
            }

            if (geometryObject.GraphicsStyleId != null && geometryObject.GraphicsStyleId != ElementId.InvalidElementId)
            {
                GraphicsStyle graphicsStyle = doc.GetElement(geometryObject.GraphicsStyleId) as GraphicsStyle;
                Category category = graphicsStyle != null ? graphicsStyle.GraphicsStyleCategory : null;
                if (category != null && IsCadLayerNameMatch(category.Name, rawLayerName))
                {
                    return category;
                }
            }

            GeometryInstance geometryInstance = geometryObject as GeometryInstance;
            if (geometryInstance != null)
            {
                try
                {
                    GeometryElement instanceGeometry = geometryInstance.GetInstanceGeometry();
                    Category matched = FindCadLayerCategoryFromGeometry(doc, instanceGeometry, rawLayerName);
                    if (matched != null)
                    {
                        return matched;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[PreviewPane] Traverse CAD geometry instance failed. Layer=" + rawLayerName + ", Error=" + ex.Message);
                }
            }

            return null;
        }

        private static bool IsCadLayerNameMatch(string candidateName, string rawLayerName)
        {
            string candidate = NormalizeCadLayerName(candidateName);
            string layer = NormalizeCadLayerName(rawLayerName);
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(layer))
            {
                return false;
            }

            if (string.Equals(candidate, layer, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Some imported CAD categories may include a prefix such as "DWG|Layer".
            return candidate.EndsWith("|" + layer, StringComparison.OrdinalIgnoreCase) ||
                   candidate.EndsWith(":" + layer, StringComparison.OrdinalIgnoreCase) ||
                   candidate.EndsWith("/" + layer, StringComparison.OrdinalIgnoreCase) ||
                   candidate.EndsWith("\\" + layer, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCadLayerName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
        }

        private void RestorePreviousCadLayerHighlight(Document doc)
        {
            if (doc == null ||
                _lastHighlightedCadLayerCategoryId == null ||
                _lastHighlightedCadLayerCategoryId == ElementId.InvalidElementId ||
                _lastHighlightedCadLayerViewId == null ||
                _lastHighlightedCadLayerViewId == ElementId.InvalidElementId)
            {
                ClearPreviousCadLayerHighlightState();
                return;
            }

            View previousView = doc.GetElement(_lastHighlightedCadLayerViewId) as View;
            if (previousView == null)
            {
                ClearPreviousCadLayerHighlightState();
                return;
            }

            try
            {
                OverrideGraphicSettings restore = _lastHighlightedCadLayerOriginalOverrides ?? new OverrideGraphicSettings();
                previousView.SetCategoryOverrides(_lastHighlightedCadLayerCategoryId, restore);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] Restore previous CAD layer highlight failed. Layer=" + _lastHighlightedCadLayerName + ", Error=" + ex.Message);
            }
            finally
            {
                ClearPreviousCadLayerHighlightState();
            }
        }

        private void ClearPreviousCadLayerHighlightState()
        {
            _lastHighlightedCadLayerCategoryId = ElementId.InvalidElementId;
            _lastHighlightedCadLayerViewId = ElementId.InvalidElementId;
            _lastHighlightedCadLayerOriginalOverrides = null;
            _lastHighlightedCadLayerName = null;
        }

        private static OverrideGraphicSettings TryGetCategoryOverrides(View view, ElementId categoryId)
        {
            if (view == null || categoryId == null || categoryId == ElementId.InvalidElementId)
            {
                return new OverrideGraphicSettings();
            }

            try
            {
                return view.GetCategoryOverrides(categoryId);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] Get CAD layer original overrides failed. CategoryId=" + categoryId.IntegerValue + ", Error=" + ex.Message);
                return new OverrideGraphicSettings();
            }
        }

        public PreviewPaneResponse ExecuteDetachSelectedElements(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null)
            {
                return Error("No active document.");
            }

            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                const string noSelectionMessage = "No generated CAD elements were selected.";
                LocalizedDialogService.Info(uiApp, noSelectionMessage, "EMSD AI Tool");
                return new PreviewPaneResponse { Message = noSelectionMessage };
            }

            List<WizardGenerationRowRecord> records = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();
            HashSet<int> trackedIds = new HashSet<int>(
                records
                    .Where(x => x.ElementIds != null)
                    .SelectMany(x => x.ElementIds)
                    .Where(x => x > 0));

            List<DetachTarget> targetElements = new List<DetachTarget>();
            List<string> detachErrors = new List<string>();
            View activeView = doc.ActiveView;
            foreach (ElementId id in selectedIds)
            {
                Element element = doc.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                bool hasFullMetadata = GeneratedElementMetadataService.TryGetFullMetadata(element, out GeneratedElementFullMetadataSnapshot original) &&
                    original != null &&
                    !string.IsNullOrWhiteSpace(original.RowKey);
                bool isTracked = trackedIds.Contains(id.IntegerValue);
                if (hasFullMetadata)
                {
                    targetElements.Add(new DetachTarget
                    {
                        Element = element,
                        Original = original,
                        UniqueId = element.UniqueId,
                        OriginalFamilyType = ResolveElementTypeName(element),
                        OriginalViewOverride = TryGetElementOverrides(activeView, element.Id),
                        ViewId = activeView != null ? activeView.Id : ElementId.InvalidElementId
                    });
                }
                else if (isTracked)
                {
                    detachErrors.Add("Element " + id.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is tracked but has no restorable generated metadata.");
                }
            }

            if (targetElements.Count == 0)
            {
                const string noGeneratedSelectionMessage = "No generated CAD elements were selected.";
                LocalizedDialogService.Info(uiApp, noGeneratedSelectionMessage, "EMSD AI Tool");
                return new PreviewPaneResponse { Message = noGeneratedSelectionMessage };
            }

            HashSet<int> detachIds = new HashSet<int>(targetElements.Select(x => x.Element.Id.IntegerValue));
            using (Transaction tx = new Transaction(doc, "Detach Selected CAD Generated Elements"))
            {
                tx.Start();
                foreach (DetachTarget target in targetElements)
                {
                    DetachedGeneratedElementMetadataService.WriteDetachedSnapshot(target.Element, target.Original);
                    GeneratedElementMetadataService.ClearGeneratedBinding(target.Element);
                }

                foreach (DetachTarget target in targetElements)
                {
                    string normalizedRowKey = WizardGenerationTrackingStoreService.NormalizeRowKey(target.Original.RowKey);
                    WizardGenerationRowRecord record = records.FirstOrDefault(x =>
                        string.Equals(
                            WizardGenerationTrackingStoreService.NormalizeRowKey(x.RowKey),
                            normalizedRowKey,
                            StringComparison.OrdinalIgnoreCase));
                    if (record == null || record.ElementIds == null)
                    {
                        continue;
                    }

                    int before = record.ElementIds.Count;
                    record.ElementIds = record.ElementIds
                        .Where(x => x != target.Element.Id.IntegerValue)
                        .Distinct()
                        .ToList();
                    if (record.ElementIds.Count != before)
                    {
                        record.GeneratedCount = record.ElementIds.Count;
                        record.LastSyncAction = "Detach";
                        record.LastSyncReason = "Detached by user";
                        record.LastSyncedAt = DateTime.UtcNow.ToString("o");
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, records);
                DetachedElementVisualOverrideService.ApplyDetachedOverride(doc, targetElements.Select(x => x.Element.Id));
                tx.Commit();
            }

            DetachUndoBatch undoBatch = new DetachUndoBatch
            {
                Items = targetElements
                    .Select(x => new DetachUndoItem
                    {
                        ElementId = x.Element.Id,
                        UniqueId = x.UniqueId,
                        OriginalLayerName = x.Original.RawLayerName,
                        OriginalCategory = x.Original.Category,
                        OriginalFamilyType = x.OriginalFamilyType,
                        OriginalViewOverride = x.OriginalViewOverride,
                        ViewId = x.ViewId
                    })
                    .ToList()
            };
            DetachUndoStackService.Push(doc, undoBatch);

            string message =
                "Detached " + targetElements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " element(s). Detached elements are highlighted in green in the current view.";
            DiagnosticRecorder.AppendDebug("[DetachGeneratedElements] Count=" + targetElements.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            LocalizedDialogService.Success(uiApp, message, "EMSD AI Tool");
            return new PreviewPaneResponse
            {
                Message = message,
                DetachedElementCount = targetElements.Count,
                Errors = detachErrors
            };
        }

        public PreviewPaneResponse ExecuteRestoreSelectedBindings(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null)
            {
                return Error("No active document.");
            }

            RestoreBindingResult restore = GeneratedElementBindingRestoreService.RestoreSelectedBindings(uiDoc);
            string message;
            if (restore.RestoredCount <= 0)
            {
                message = "No detached elements with restorable CAD layer information were found in the current selection.";
                LocalizedDialogService.Info(uiApp, message, "EMSD AI Tool");
            }
            else if (restore.SkippedElementIds.Count > 0)
            {
                message =
                    "Restored binding for " + restore.RestoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " element(s). " + restore.SkippedElementIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " item(s) were skipped.";
                LocalizedDialogService.Success(uiApp, message, "EMSD AI Tool");
            }
            else
            {
                message = "Restored binding for " + restore.RestoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " element(s).";
                LocalizedDialogService.Success(uiApp, message, "EMSD AI Tool");
            }

            DiagnosticRecorder.AppendDebug("[RestoreGeneratedBinding] Count=" + restore.RestoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new PreviewPaneResponse
            {
                Message = message,
                RestoredElementCount = restore.RestoredCount,
                Errors = restore.Errors
            };
        }

        public PreviewPaneResponse ExecuteUndoLastDetachBatch(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null)
            {
                return Error("No active document.");
            }

            RestoreBindingResult undo = DetachUndoStackService.UndoLastDetachBatch(doc);
            string message;
            if (undo.RestoredCount <= 0)
            {
                message = "No detachable undo batch is available.";
                LocalizedDialogService.Info(uiApp, message, "EMSD AI Tool");
            }
            else if (undo.SkippedElementIds.Count > 0)
            {
                message =
                    "Undo Detach restored " + undo.RestoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " element(s). " + undo.SkippedElementIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    " item(s) were skipped.";
                LocalizedDialogService.Success(uiApp, message, "EMSD AI Tool");
            }
            else
            {
                message = "Undo Detach restored " + undo.RestoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " element(s).";
                LocalizedDialogService.Success(uiApp, message, "EMSD AI Tool");
            }

            DiagnosticRecorder.AppendDebug("[UndoDetachGeneratedElements] Count=" + undo.RestoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new PreviewPaneResponse
            {
                Message = message,
                RestoredElementCount = undo.RestoredCount,
                Errors = undo.Errors
            };
        }

        public PreviewPaneResponse ExecuteToggleCadVisibility(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            View view = doc != null ? doc.ActiveView : null;
            if (doc == null || view == null)
            {
                return Error("No active view.");
            }

            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return Error("No CAD Link found.");
            }

            if (!import.CanBeHidden(view))
            {
                return Error("CAD instance cannot be hidden in current view.");
            }

            bool currentlyVisible = !IsElementHiddenInView(doc, view, import.Id);
            using (Transaction tx = new Transaction(doc, currentlyVisible ? "Hide CAD In View" : "Show CAD In View"))
            {
                tx.Start();
                if (currentlyVisible)
                {
                    view.HideElements(new List<ElementId> { import.Id });
                }
                else
                {
                    view.UnhideElements(new List<ElementId> { import.Id });
                }

                tx.Commit();
            }

            uiDoc.RefreshActiveView();
            return new PreviewPaneResponse { Message = currentlyVisible ? "CAD hidden in current view." : "CAD shown in current view." };
        }

        public PreviewPaneResponse ExecuteToggleBuildingElementsVisibility(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            View view = doc != null ? doc.ActiveView : null;
            if (doc == null || view == null)
            {
                return Error("No active view.");
            }

            List<ElementId> targetIds = CollectGeneratedBuildingElementsInView(doc, view);
            if (targetIds.Count == 0)
            {
                return new PreviewPaneResponse { Message = "No generated building elements found in current view." };
            }

            bool currentlyVisible = targetIds.Any(id => !IsElementHiddenInView(doc, view, id));
            using (Transaction tx = new Transaction(doc, currentlyVisible ? "Hide Generated Building Elements" : "Show Generated Building Elements"))
            {
                tx.Start();
                if (currentlyVisible)
                {
                    view.HideElements(targetIds);
                }
                else
                {
                    view.UnhideElements(targetIds);
                }

                tx.Commit();
            }

            uiDoc.RefreshActiveView();
            return new PreviewPaneResponse { Message = currentlyVisible ? "Building elements hidden in current view." : "Building elements shown in current view." };
        }

        public PreviewPaneResponse ExecuteToggleGeneratedElementsVisibilityForLayer(
            UIApplication uiApp,
            string rawLayerName,
            MapCategory? category)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            View view = doc != null ? doc.ActiveView : null;
            if (doc == null || view == null)
            {
                return Error("No active view.");
            }

            if (string.IsNullOrWhiteSpace(rawLayerName) || !category.HasValue || !IsGeneratableCategory(category.Value))
            {
                return Error("No generated elements found for this layer.");
            }

            ImportInstance import = ResolveCurrentImportInstance(doc);
            Level level = ResolveLevel(doc);
            WizardGenerationRowRecord record = FindGeneratedLayerRecord(doc, import, level, rawLayerName, category.Value);
            if (record == null)
            {
                return Error("No generated elements found for this layer.");
            }

            List<ElementId> targetIds = ResolveExistingGeneratedElementIds(doc, view, record, requireCanBeHidden: true);
            if (targetIds.Count == 0)
            {
                return Error("No hideable generated elements found for this layer in current view.");
            }

            bool currentlyVisible = targetIds.Any(id => !IsElementHiddenInView(doc, view, id));
            List<ElementId> idsToUpdate = currentlyVisible
                ? targetIds.Where(id => !IsElementHiddenInView(doc, view, id)).ToList()
                : targetIds.Where(id => IsElementHiddenInView(doc, view, id)).ToList();
            if (idsToUpdate.Count == 0)
            {
                return new PreviewPaneResponse
                {
                    Message = currentlyVisible
                        ? "Generated elements are already shown for layer: " + rawLayerName
                        : "Generated elements are already hidden for layer: " + rawLayerName,
                    LayerGeneratedElementsHidden = !currentlyVisible
                };
            }

            using (Transaction tx = new Transaction(doc, currentlyVisible ? "Hide Layer Generated Elements" : "Show Layer Generated Elements"))
            {
                tx.Start();
                if (currentlyVisible)
                {
                    view.HideElements(idsToUpdate);
                }
                else
                {
                    view.UnhideElements(idsToUpdate);
                }

                tx.Commit();
            }

            uiDoc.RefreshActiveView();
            bool hiddenAfterAction = currentlyVisible;
            return new PreviewPaneResponse
            {
                Message = currentlyVisible
                    ? "Generated elements hidden for layer: " + rawLayerName
                    : "Generated elements shown for layer: " + rawLayerName,
                LayerGeneratedElementsHidden = hiddenAfterAction
            };
        }

        private static Dictionary<string, MapRow> BuildCurrentSelectedRowsByKey(
            Document doc,
            ElementId dwgId,
            ElementId levelId,
            IEnumerable<MapRow> rows)
        {
            Dictionary<string, MapRow> selected = new Dictionary<string, MapRow>(StringComparer.OrdinalIgnoreCase);
            foreach (MapRow row in rows ?? new List<MapRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                {
                    continue;
                }

                if (!IsGeneratableCategory(row.Category))
                {
                    continue;
                }

                string key = WizardGenerationTrackingStoreService.BuildRowKey(row.RawLayerName, row.Category, levelId, dwgId);
                selected[key] = row;
            }

            return selected;
        }

        private static SyncPlan BuildSyncPlan(
            Dictionary<string, MapRow> currentSelectedByKey,
            Dictionary<string, WizardGenerationRowRecord> historyByKey)
        {
            SyncPlan plan = new SyncPlan();
            foreach (KeyValuePair<string, WizardGenerationRowRecord> kv in historyByKey)
            {
                if (!currentSelectedByKey.ContainsKey(kv.Key))
                {
                    plan.RowsToDelete.Add(kv.Value);
                }
            }

            foreach (KeyValuePair<string, MapRow> kv in currentSelectedByKey)
            {
                string rowKey = kv.Key;
                MapRow currentRow = kv.Value;
                string newFingerprint = WizardGenerationTrackingStoreService.BuildMappingFingerprint(currentRow);

                if (!historyByKey.TryGetValue(rowKey, out WizardGenerationRowRecord oldRecord))
                {
                    plan.RowsToCreate.Add(currentRow);
                    continue;
                }

                bool configChanged = !string.Equals(
                    newFingerprint,
                    oldRecord.MappingFingerprint,
                    StringComparison.OrdinalIgnoreCase);

                if (configChanged)
                {
                    plan.RowsToRebuild.Add(currentRow);
                }
                else
                {
                    plan.RowsToSkip.Add(currentRow);
                }
            }

            return plan;
        }

        private static Dictionary<string, int> BuildSelectedRowOrderByKey(
            IEnumerable<MapRow> rows,
            ElementId dwgId,
            ElementId levelId)
        {
            Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            foreach (MapRow row in rows ?? new List<MapRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName) || !IsGeneratableCategory(row.Category))
                {
                    continue;
                }

                string rowKey = WizardGenerationTrackingStoreService.BuildRowKey(row.RawLayerName, row.Category, levelId, dwgId);
                result[rowKey] = index;
                index++;
            }

            return result;
        }

        private static List<PendingGenerationAction> BuildPendingGenerationActions(
            SyncPlan syncPlan,
            ElementId dwgId,
            ElementId levelId,
            Dictionary<string, int> selectedRowOrderByKey)
        {
            List<PendingGenerationAction> pending = new List<PendingGenerationAction>();

            foreach (MapRow row in syncPlan != null ? syncPlan.RowsToRebuild : new List<MapRow>())
            {
                pending.Add(CreatePendingGenerationAction(
                    row,
                    dwgId,
                    levelId,
                    selectedRowOrderByKey,
                    "Rebuild",
                    "ConfigChanged",
                    true));
            }

            foreach (MapRow row in syncPlan != null ? syncPlan.RowsToCreate : new List<MapRow>())
            {
                pending.Add(CreatePendingGenerationAction(
                    row,
                    dwgId,
                    levelId,
                    selectedRowOrderByKey,
                    "Create",
                    "NewSelected",
                    false));
            }

            return pending
                .Where(x => x != null && x.Row != null && !string.IsNullOrWhiteSpace(x.Row.RawLayerName))
                .OrderBy(x => GetGenerationCategoryPriority(x.Row.Category))
                .ThenBy(x => x.OriginalOrder)
                .ToList();
        }

        private static PendingGenerationAction CreatePendingGenerationAction(
            MapRow row,
            ElementId dwgId,
            ElementId levelId,
            Dictionary<string, int> selectedRowOrderByKey,
            string buildAction,
            string buildReason,
            bool deleteBeforeBuild)
        {
            string rowKey = row != null && !string.IsNullOrWhiteSpace(row.RawLayerName)
                ? WizardGenerationTrackingStoreService.BuildRowKey(row.RawLayerName, row.Category, levelId, dwgId)
                : string.Empty;
            int originalOrder = int.MaxValue;
            if (!string.IsNullOrWhiteSpace(rowKey) && selectedRowOrderByKey != null && selectedRowOrderByKey.TryGetValue(rowKey, out int order))
            {
                originalOrder = order;
            }

            return new PendingGenerationAction
            {
                Row = row,
                RowKey = rowKey,
                BuildAction = buildAction,
                BuildReason = buildReason,
                DeleteBeforeBuild = deleteBeforeBuild,
                OriginalOrder = originalOrder
            };
        }

        private static List<KeyValuePair<string, MapRow>> OrderRowsForGeneration(
            Dictionary<string, MapRow> rowsByKey,
            Dictionary<string, int> selectedRowOrderByKey)
        {
            return (rowsByKey ?? new Dictionary<string, MapRow>(StringComparer.OrdinalIgnoreCase))
                .Where(x => x.Value != null && !string.IsNullOrWhiteSpace(x.Value.RawLayerName))
                .OrderBy(x => GetGenerationCategoryPriority(x.Value.Category))
                .ThenBy(x => GetRowOrderOrDefault(x.Key, selectedRowOrderByKey))
                .ToList();
        }

        private static int GetGenerationCategoryPriority(MapCategory category)
        {
            switch (category)
            {
                case MapCategory.Walls:
                    return 100;
                case MapCategory.Columns:
                    return 200;
                case MapCategory.Beams:
                    return 300;
                case MapCategory.Doors:
                    return 400;
                case MapCategory.Windows:
                    return 500;
                case MapCategory.Floors:
                    return 600;
                case MapCategory.Ceilings:
                    return 700;
                case MapCategory.Ignore:
                case MapCategory.Unknown:
                default:
                    return 999;
            }
        }

        private static int GetRowOrderOrDefault(string rowKey, Dictionary<string, int> selectedRowOrderByKey)
        {
            if (!string.IsNullOrWhiteSpace(rowKey) &&
                selectedRowOrderByKey != null &&
                selectedRowOrderByKey.TryGetValue(rowKey, out int order))
            {
                return order;
            }

            return int.MaxValue;
        }

        private static void LogGenerationOrder(string phase, IEnumerable<MapRow> rows)
        {
            List<string> parts = (rows ?? Enumerable.Empty<MapRow>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                .Select((x, index) => (index + 1) + "=" + x.Category + ":" + x.RawLayerName)
                .ToList();

            DiagnosticRecorder.AppendDebug(
                "[WpfGenerationOrder][" + (phase ?? string.Empty) + "] " +
                (parts.Count > 0 ? string.Join(", ", parts) : "(empty)"));
        }

        private static void LogGenerationRun(string action, MapRow row)
        {
            if (row == null)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[WpfGenerationRun] Action=" + (action ?? string.Empty) +
                ", Category=" + row.Category +
                ", Layer=" + (row.RawLayerName ?? string.Empty));
        }

        private static void LogRowAction(
            string rowKey,
            string layerName,
            string action,
            string reason,
            int? requestedDeleteCount = null,
            int? actualDeleteCount = null)
        {
            string message =
                "[RowAction] RowKey=" + (rowKey ?? string.Empty) +
                ", LayerName=" + (layerName ?? string.Empty) +
                ", Action=" + (action ?? string.Empty) +
                ", Reason=" + (reason ?? string.Empty);

            if (requestedDeleteCount.HasValue || actualDeleteCount.HasValue)
            {
                message +=
                    ", RequestedDeleteCount=" + (requestedDeleteCount ?? 0) +
                    ", ActualDeletedCount=" + (actualDeleteCount ?? 0);
            }

            if (requestedDeleteCount.HasValue && actualDeleteCount.HasValue && actualDeleteCount.Value > requestedDeleteCount.Value)
            {
                message += ", Warning=ActualDeletedCount exceeds RequestedDeleteCount";
            }

            DiagnosticRecorder.AppendDebug(message);
        }

        private static void ApplyDeletedElementsToTracking(
            Dictionary<string, WizardGenerationRowRecord> historyByKey,
            CleanupRowResult cleanup,
            string targetRowKey)
        {
            if (historyByKey == null || cleanup == null || cleanup.DeletedElementIds == null || cleanup.DeletedElementIds.Count == 0)
            {
                return;
            }

            HashSet<int> deletedSet = new HashSet<int>(cleanup.DeletedElementIds);
            List<string> keysToRemove = new List<string>();
            foreach (KeyValuePair<string, WizardGenerationRowRecord> kv in historyByKey)
            {
                WizardGenerationRowRecord record = kv.Value;
                if (record == null || record.ElementIds == null || record.ElementIds.Count == 0)
                {
                    continue;
                }

                int before = record.ElementIds.Count;
                record.ElementIds = record.ElementIds.Where(x => !deletedSet.Contains(x)).Distinct().ToList();
                if (record.ElementIds.Count != before)
                {
                    record.GeneratedCount = record.ElementIds.Count;
                    record.LastSyncedAt = DateTime.UtcNow.ToString("o");
                }

                bool isTargetRow = string.Equals(
                    WizardGenerationTrackingStoreService.NormalizeRowKey(kv.Key),
                    WizardGenerationTrackingStoreService.NormalizeRowKey(targetRowKey),
                    StringComparison.OrdinalIgnoreCase);

                if (!isTargetRow && record.ElementIds.Count == 0)
                {
                    keysToRemove.Add(kv.Key);
                }
            }

            foreach (string key in keysToRemove.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                historyByKey.Remove(key);
                DiagnosticRecorder.AppendDebug(
                    "[TrackingCleanup] RemovedEmptyRowAfterDependentDelete RowKey=" + key);
            }
        }

        private static HashSet<int> CollectCategoryElementIds(Document doc, MapCategory category)
        {
            HashSet<int> result = new HashSet<int>();
            if (doc == null)
            {
                return result;
            }

            Action<BuiltInCategory> collectByCategory = bic =>
            {
                foreach (ElementId id in new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsNotElementType()
                    .ToElementIds())
                {
                    result.Add(id.IntegerValue);
                }
            };

            if (category == MapCategory.Walls)
            {
                collectByCategory(BuiltInCategory.OST_Walls);
            }
            else if (category == MapCategory.Doors)
            {
                collectByCategory(BuiltInCategory.OST_Doors);
            }
            else if (category == MapCategory.Windows)
            {
                collectByCategory(BuiltInCategory.OST_Windows);
            }
            else if (category == MapCategory.Beams)
            {
                collectByCategory(BuiltInCategory.OST_StructuralFraming);
            }
            else if (category == MapCategory.Columns)
            {
                collectByCategory(BuiltInCategory.OST_Columns);
                collectByCategory(BuiltInCategory.OST_StructuralColumns);
                collectByCategory(BuiltInCategory.OST_GenericModel);
            }

            return result;
        }

        private static List<int> ResolveGeneratedElementIdsForRow(
            Document doc,
            MapRow row,
            object runSummary,
            HashSet<int> beforeIds,
            HashSet<int> afterIds)
        {
            if (row != null && row.Category == MapCategory.Doors)
            {
                CreateElementsExecutionSummary typedRun = runSummary as CreateElementsExecutionSummary;
                List<int> createdIds = (typedRun != null ? typedRun.CreatedElementIds : new List<int>())
                    .Where(x => x > 0 && doc != null && doc.GetElement(new ElementId(x)) != null)
                    .Distinct()
                    .ToList();
                if (createdIds.Count > 0)
                {
                    return createdIds;
                }
            }

            return (afterIds ?? new HashSet<int>())
                .Except(beforeIds ?? new HashSet<int>())
                .Distinct()
                .ToList();
        }

        private sealed class PendingGenerationAction
        {
            // Keep the original row order stable within the same category priority.
            public int OriginalOrder { get; set; }
            public MapRow Row { get; set; }
            public string RowKey { get; set; }
            public string BuildAction { get; set; }
            public string BuildReason { get; set; }
            public bool DeleteBeforeBuild { get; set; }
        }

        private static double Percentile(List<double> values, double ratio)
        {
            if (values == null || values.Count == 0)
            {
                return 0.0;
            }

            if (ratio <= 0) return values[0];
            if (ratio >= 1) return values[values.Count - 1];
            int index = (int)Math.Round((values.Count - 1) * ratio);
            index = Math.Max(0, Math.Min(values.Count - 1, index));
            return values[index];
        }

        private static PreviewPaneResponse Error(string message)
        {
            return new PreviewPaneResponse
            {
                Message = message,
                Errors = new List<string> { message }
            };
        }


        private static Dictionary<string, bool> BuildGeneratedLayerHiddenStates(Document doc, ImportInstance importInstance, Level level, View view)
        {
            Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || importInstance == null || level == null || view == null)
            {
                return result;
            }

            foreach (WizardGenerationRowRecord record in WizardGenerationTrackingStoreService.Load(doc).Where(x => x != null))
            {
                if (record.DwgId != importInstance.Id.IntegerValue || record.LevelId != level.Id.IntegerValue)
                {
                    continue;
                }

                if (!IsGeneratedRecord(record))
                {
                    continue;
                }

                string key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(record);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                List<ElementId> ids = ResolveExistingGeneratedElementIds(doc, view, record, requireCanBeHidden: true);
                if (ids.Count == 0)
                {
                    continue;
                }

                bool anyVisible = ids.Any(id => !IsElementHiddenInView(doc, view, id));
                result[key] = !anyVisible;
            }

            return result;
        }

        private static WizardGenerationRowRecord FindGeneratedLayerRecord(
            Document doc,
            ImportInstance importInstance,
            Level level,
            string rawLayerName,
            MapCategory category)
        {
            if (doc == null || importInstance == null || level == null || string.IsNullOrWhiteSpace(rawLayerName))
            {
                return null;
            }

            string targetKey = WizardGenerationTrackingStoreService.BuildRowKey(rawLayerName, category, level.Id, importInstance.Id);
            string normalizedTargetKey = WizardGenerationTrackingStoreService.NormalizeRowKey(targetKey);
            foreach (WizardGenerationRowRecord record in WizardGenerationTrackingStoreService.Load(doc).Where(x => x != null))
            {
                if (record.DwgId != importInstance.Id.IntegerValue || record.LevelId != level.Id.IntegerValue)
                {
                    continue;
                }

                if (!IsGeneratedRecord(record))
                {
                    continue;
                }

                string recordKey = WizardGenerationTrackingStoreService.NormalizeRowKey(
                    WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(record));
                if (string.Equals(recordKey, normalizedTargetKey, StringComparison.OrdinalIgnoreCase))
                {
                    return record;
                }
            }

            return null;
        }

        private static List<ElementId> ResolveExistingGeneratedElementIds(
            Document doc,
            View view,
            WizardGenerationRowRecord record,
            bool requireCanBeHidden)
        {
            List<ElementId> result = new List<ElementId>();
            if (doc == null || record == null || record.ElementIds == null)
            {
                return result;
            }

            foreach (int rawId in record.ElementIds.Distinct())
            {
                if (rawId <= 0)
                {
                    continue;
                }

                ElementId id = new ElementId(rawId);
                Element element = doc.GetElement(id);
                if (element == null)
                {
                    continue;
                }

                if (requireCanBeHidden && (view == null || !element.CanBeHidden(view)))
                {
                    continue;
                }

                result.Add(id);
            }

            return result.Distinct().ToList();
        }

        private static HashSet<string> BuildGeneratedLayerKeys(Document doc, ImportInstance importInstance, Level level)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || importInstance == null || level == null)
            {
                return keys;
            }

            foreach (WizardGenerationRowRecord record in WizardGenerationTrackingStoreService.Load(doc).Where(x => x != null))
            {
                if (record.DwgId != importInstance.Id.IntegerValue || record.LevelId != level.Id.IntegerValue)
                {
                    continue;
                }

                if (!IsGeneratedRecord(record))
                {
                    continue;
                }

                string key = WizardGenerationTrackingStoreService.BuildStableRowKeyForRecord(record);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    keys.Add(key);
                }
            }

            return keys;
        }

        private static bool IsLayerGenerated(MapRow row, ImportInstance importInstance, Level level, HashSet<string> generatedLayerKeys)
        {
            if (row == null || !IsGeneratableCategory(row.Category) || importInstance == null || level == null || generatedLayerKeys == null)
            {
                return false;
            }

            string key = WizardGenerationTrackingStoreService.BuildRowKey(row.RawLayerName, row.Category, level.Id, importInstance.Id);
            return generatedLayerKeys.Contains(key);
        }

        private static bool IsGeneratedRecord(WizardGenerationRowRecord record)
        {
            if (record == null)
            {
                return false;
            }

            if (record.GeneratedCount > 0)
            {
                return true;
            }

            return record.ElementIds != null && record.ElementIds.Count > 0;
        }

        private static PreviewPaneLayerItem ToLayerItem(MapRow row, AdvancedSettingsRow settings, bool hasSavedMapping)
        {
            bool isInvalidStandardLayer = IsInvalidStandardLayer(row.RawLayerName);
            bool useSavedGeneratableCategory = hasSavedMapping && IsGeneratableCategory(row.Category);

            // Unknown layers are shown for review, but they should not be selected by default.
            // Users can map them to a supported category first, then check them manually if needed.
            MapCategory defaultCategory;
            if (!useSavedGeneratableCategory && (isInvalidStandardLayer || row.Category == MapCategory.Unknown))
            {
                defaultCategory = MapCategory.Unknown;
            }
            else if (row.Category == MapCategory.Ignore || row.Category == MapCategory.NotForBuild)
            {
                defaultCategory = MapCategory.NotForBuild;
            }
            else
            {
                defaultCategory = row.Category;
            }

            bool isSelected = IsGeneratableCategory(defaultCategory);
            PreviewPaneLayerItem item = new PreviewPaneLayerItem
            {
                RawLayerName = row.RawLayerName,
                IsSelected = isSelected,
                IsDirty = false,
                IsLayerStandardInvalid = isInvalidStandardLayer,
                Category = defaultCategory,
                FamilyTypeName = !IsGeneratableCategory(defaultCategory) ? UnknownFamilyTypePlaceholder : row.RevitTypeName,
                EnableLayerOverride = settings.EnableLayerOverride,
                ApplyAsCategoryDefault = settings.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = settings.DoorExpectedWidthMm,
                MinDoorWidthMm = settings.MinDoorWidthMm,
                MaxDoorWidthMm = settings.MaxDoorWidthMm,
                DoorWallMatchTolMm = settings.DoorWallMatchTolMm,
                DoorHeightMm = settings.DoorHeightMm,
                DoorSillHeightMm = settings.DoorSillHeightMm,
                UseFixedDoorWidth = settings.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = settings.PreferGeometryOpeningWidth,
                WindowHeightMm = settings.WindowHeightMm,
                WindowSillHeightMm = settings.WindowSillHeightMm,
                WindowUseSillPlusHeight = settings.WindowUseSillPlusHeight,
                BeamMinLengthMm = settings.BeamMinLengthMm,
                BeamElevationOffsetMm = settings.BeamElevationOffsetMm,
                ColumnHeightMm = settings.ColumnHeightMm,
                ColumnClusterAlgorithm = settings.ColumnClusterAlgorithm,
                ColumnClusterTolMm = settings.ColumnClusterTolMm,
                ColumnMinGroupSegments = settings.ColumnMinGroupSegments,
                ColumnEndpointTolMm = settings.ColumnEndpointTolMm,
                ColumnGapTolMm = settings.ColumnGapTolMm,
                ColumnMinSizeMm = settings.ColumnMinSizeMm,
                ColumnMaxSizeMm = settings.ColumnMaxSizeMm,
                ColumnMinAreaM2 = settings.ColumnMinAreaM2,
                ColumnMaxAspectRatio = settings.ColumnMaxAspectRatio,
                ColumnMinFillRatio = settings.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = settings.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = settings.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = settings.ColumnEnableMerge,
                ColumnMergeTolMm = settings.ColumnMergeTolMm,
                ColumnMergeStrategy = settings.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = settings.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = settings.ColumnAreaWeight,
                ColumnSegmentCountWeight = settings.ColumnSegmentCountWeight,
                ColumnRectnessWeight = settings.ColumnRectnessWeight,
                ColumnLongLinePenalty = settings.ColumnLongLinePenalty,
                ColumnIrregularEnable = settings.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = settings.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = settings.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = settings.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = settings.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = settings.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = settings.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = settings.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = settings.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = settings.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = settings.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = settings.ColumnDebugExportReport,
                MinWallLengthMm = settings.WallMinWallLengthMm,
                DefaultSingleWallThicknessMm = settings.WallDefaultSingleWallThicknessMm,
                WallHeightMm = settings.WallHeightMm,
                WallBaseOffsetMm = settings.WallBaseOffsetMm,
                WallThicknessTolMm = settings.WallThicknessTolMm,
                WallMaxWallThicknessMm = settings.WallMaxWallThicknessMm,
                WallParallelAngleTolDeg = settings.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = settings.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = settings.WallArcThicknessTolMm,
                WallEndpointClusterTolMm = settings.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = settings.WallExtendSearchTolMm,
                WallDuplicateTolMm = settings.WallDuplicateTolMm,
                WallAngleSnapDeg = settings.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = settings.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = settings.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = settings.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = settings.WallEnableDuplicateRemoval,
                WallEnableExtendCollinear = settings.WallEnableExtendCollinear,
                WallEnableMergeCollinear = settings.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = settings.WallExtendCollinearTolMm,
                WallCollinearOffsetTolMm = settings.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = settings.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = settings.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = settings.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = settings.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = settings.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = settings.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = settings.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = settings.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = settings.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = settings.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = settings.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = settings.WallDoubleLineAdaptiveExtendMaxMm
            };
            ApplyUiDefaults(item);
            return item;
        }

        private static AdvancedSettingsRow ToAdvancedSettings(PreviewPaneLayerItem item)
        {
            return new AdvancedSettingsRow
            {
                EnableLayerOverride = item.EnableLayerOverride,
                ApplyAsCategoryDefault = item.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = item.DoorExpectedWidthMm,
                MinDoorWidthMm = item.MinDoorWidthMm,
                MaxDoorWidthMm = item.MaxDoorWidthMm,
                DoorWallMatchTolMm = item.DoorWallMatchTolMm,
                DoorHeightMm = item.DoorHeightMm,
                DoorSillHeightMm = item.DoorSillHeightMm,
                UseFixedDoorWidth = item.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = item.PreferGeometryOpeningWidth,
                WindowHeightMm = item.WindowHeightMm,
                WindowSillHeightMm = item.WindowSillHeightMm,
                WindowUseSillPlusHeight = item.WindowUseSillPlusHeight,
                BeamMinLengthMm = item.BeamMinLengthMm,
                BeamElevationOffsetMm = item.BeamElevationOffsetMm,
                ColumnHeightMm = item.ColumnHeightMm,
                ColumnClusterAlgorithm = item.ColumnClusterAlgorithm,
                ColumnClusterTolMm = item.ColumnClusterTolMm,
                ColumnMinGroupSegments = item.ColumnMinGroupSegments,
                ColumnEndpointTolMm = item.ColumnEndpointTolMm,
                ColumnGapTolMm = item.ColumnGapTolMm,
                ColumnMinSizeMm = item.ColumnMinSizeMm,
                ColumnMaxSizeMm = item.ColumnMaxSizeMm,
                ColumnMinAreaM2 = item.ColumnMinAreaM2,
                ColumnMaxAspectRatio = item.ColumnMaxAspectRatio,
                ColumnMinFillRatio = item.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = item.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = item.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = item.ColumnEnableMerge,
                ColumnMergeTolMm = item.ColumnMergeTolMm,
                ColumnMergeStrategy = item.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = item.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = item.ColumnAreaWeight,
                ColumnSegmentCountWeight = item.ColumnSegmentCountWeight,
                ColumnRectnessWeight = item.ColumnRectnessWeight,
                ColumnLongLinePenalty = item.ColumnLongLinePenalty,
                ColumnIrregularEnable = item.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = item.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = item.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = item.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = item.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = item.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = item.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = item.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = item.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = item.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = item.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = item.ColumnDebugExportReport,
                WallMinWallLengthMm = item.MinWallLengthMm,
                WallDefaultSingleWallThicknessMm = item.DefaultSingleWallThicknessMm,
                WallHeightMm = item.WallHeightMm,
                WallBaseOffsetMm = item.WallBaseOffsetMm,
                WallThicknessTolMm = item.WallThicknessTolMm,
                WallMaxWallThicknessMm = item.WallMaxWallThicknessMm,
                WallParallelAngleTolDeg = item.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = item.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = item.WallArcThicknessTolMm,
                WallEndpointClusterTolMm = item.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = item.WallExtendSearchTolMm,
                WallDuplicateTolMm = item.WallDuplicateTolMm,
                WallAngleSnapDeg = item.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = item.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = item.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = item.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = item.WallEnableDuplicateRemoval,
                WallEnableExtendCollinear = item.WallEnableExtendCollinear,
                WallEnableMergeCollinear = item.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = item.WallExtendCollinearTolMm,
                WallCollinearOffsetTolMm = item.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = item.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = item.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = item.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = item.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = item.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = item.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = item.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = item.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = item.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = item.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = item.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = item.WallDoubleLineAdaptiveExtendMaxMm
            };
        }

        private static void ApplyUiDefaults(PreviewPaneLayerItem item)
        {
            if (item == null)
            {
                return;
            }

            item.EnableLayerOverride = true;
            if (!item.WallHeightMm.HasValue) item.WallHeightMm = 4000.0;
            if (!item.WallBaseOffsetMm.HasValue) item.WallBaseOffsetMm = 0.0;
            if (!item.MinWallLengthMm.HasValue) item.MinWallLengthMm = 100.0;
            if (!item.WallThicknessTolMm.HasValue) item.WallThicknessTolMm = 20.0;
            if (!item.WallMaxWallThicknessMm.HasValue) item.WallMaxWallThicknessMm = 500.0;
            if (!item.DefaultSingleWallThicknessMm.HasValue) item.DefaultSingleWallThicknessMm = 200.0;
            if (!item.WallParallelAngleTolDeg.HasValue) item.WallParallelAngleTolDeg = 2.0;
            if (!item.WallEndpointMergeTolMm.HasValue) item.WallEndpointMergeTolMm = 50.0;
            if (!item.WallArcThicknessTolMm.HasValue) item.WallArcThicknessTolMm = 20.0;
            if (!item.WallEndpointClusterTolMm.HasValue) item.WallEndpointClusterTolMm = 15.0;
            if (!item.WallExtendSearchTolMm.HasValue) item.WallExtendSearchTolMm = 30.0;
            if (!item.WallDuplicateTolMm.HasValue) item.WallDuplicateTolMm = 6.0;
            if (!item.WallAngleSnapDeg.HasValue) item.WallAngleSnapDeg = 0.5;
            if (!item.WallEnableOrthogonalSnap.HasValue) item.WallEnableOrthogonalSnap = true;
            if (!item.WallEnableExtendToIntersection.HasValue) item.WallEnableExtendToIntersection = true;
            if (!item.WallEnableEndpointClustering.HasValue) item.WallEnableEndpointClustering = true;
            if (!item.WallEnableDuplicateRemoval.HasValue) item.WallEnableDuplicateRemoval = true;
            if (!item.WallEnableExtendCollinear.HasValue) item.WallEnableExtendCollinear = false;
            if (!item.WallEnableMergeCollinear.HasValue) item.WallEnableMergeCollinear = false;
            if (!item.WallExtendCollinearTolMm.HasValue) item.WallExtendCollinearTolMm = 150.0;
            if (!item.WallCollinearOffsetTolMm.HasValue) item.WallCollinearOffsetTolMm = 30.0;
            if (!item.WallExtendProjectionTolMm.HasValue) item.WallExtendProjectionTolMm = 80.0;
            if (!item.WallUseDirectionalClustering.HasValue) item.WallUseDirectionalClustering = false;
            if (!item.WallEnableAutoDoubleLineThickness.HasValue) item.WallEnableAutoDoubleLineThickness = true;
            if (!item.WallAutoThicknessTopK.HasValue) item.WallAutoThicknessTopK = 20;
            if (!item.WallAutoThicknessBinMm.HasValue) item.WallAutoThicknessBinMm = 10.0;
            if (!item.WallMinDoubleLineThicknessMm.HasValue) item.WallMinDoubleLineThicknessMm = 60.0;
            if (!item.WallMinDoubleLineOverlapLenMm.HasValue) item.WallMinDoubleLineOverlapLenMm = 300.0;
            if (string.IsNullOrWhiteSpace(item.WallDoubleLineSingleWallPlaceMode)) item.WallDoubleLineSingleWallPlaceMode = AdvancedSettingsRow.WallPlaceModeInsideFaceOnCadLine;
            if (string.IsNullOrWhiteSpace(item.WallDoubleLineLengthPolicy)) item.WallDoubleLineLengthPolicy = AdvancedSettingsRow.WallDoubleLineLengthPolicyUnion;
            if (!item.WallDoubleLineAdaptiveContainTolMm.HasValue) item.WallDoubleLineAdaptiveContainTolMm = 100.0;
            if (!item.WallDoubleLineAdaptiveExtendMaxMm.HasValue) item.WallDoubleLineAdaptiveExtendMaxMm = 600.0;

            if (!item.MinDoorWidthMm.HasValue) item.MinDoorWidthMm = 600.0;
            if (!item.MaxDoorWidthMm.HasValue) item.MaxDoorWidthMm = 3000.0;
            if (!item.DoorWallMatchTolMm.HasValue) item.DoorWallMatchTolMm = 300.0;
            if (!item.DoorHeightMm.HasValue) item.DoorHeightMm = 2100.0;
            if (!item.DoorSillHeightMm.HasValue) item.DoorSillHeightMm = 0.0;
            if (!item.UseFixedDoorWidth.HasValue) item.UseFixedDoorWidth = false;
            if (!item.PreferGeometryOpeningWidth.HasValue) item.PreferGeometryOpeningWidth = true;

            if (!item.WindowHeightMm.HasValue) item.WindowHeightMm = 1500.0;
            if (!item.WindowSillHeightMm.HasValue) item.WindowSillHeightMm = 900.0;
            if (!item.WindowUseSillPlusHeight.HasValue) item.WindowUseSillPlusHeight = true;

            if (!item.BeamMinLengthMm.HasValue) item.BeamMinLengthMm = 800.0;
            if (!item.BeamElevationOffsetMm.HasValue) item.BeamElevationOffsetMm = 3000.0;

            if (!item.ColumnHeightMm.HasValue) item.ColumnHeightMm = 4000.0;
            if (string.IsNullOrWhiteSpace(item.ColumnClusterAlgorithm)) item.ColumnClusterAlgorithm = "EndpointGraph";
            if (!item.ColumnClusterTolMm.HasValue) item.ColumnClusterTolMm = 350.0;
            if (!item.ColumnMinGroupSegments.HasValue) item.ColumnMinGroupSegments = 8;
            if (!item.ColumnMinSizeMm.HasValue) item.ColumnMinSizeMm = 200.0;
            if (!item.ColumnMaxSizeMm.HasValue) item.ColumnMaxSizeMm = 1200.0;
            if (!item.ColumnEnableLongLineFilter.HasValue) item.ColumnEnableLongLineFilter = true;
            if (!item.ColumnMaxSegmentLengthMm.HasValue) item.ColumnMaxSegmentLengthMm = 2000.0;
            if (!item.ColumnEnableMerge.HasValue) item.ColumnEnableMerge = true;
            if (!item.ColumnMergeTolMm.HasValue) item.ColumnMergeTolMm = 300.0;
            if (string.IsNullOrWhiteSpace(item.ColumnMergeStrategy)) item.ColumnMergeStrategy = "KeepBest";
            if (!item.ColumnEndpointTolMm.HasValue) item.ColumnEndpointTolMm = 30.0;
            if (!item.ColumnGapTolMm.HasValue) item.ColumnGapTolMm = 50.0;
            if (!item.ColumnMinAreaM2.HasValue) item.ColumnMinAreaM2 = 0.04;
            if (!item.ColumnMaxAspectRatio.HasValue) item.ColumnMaxAspectRatio = 4.0;
            if (!item.ColumnMinFillRatio.HasValue) item.ColumnMinFillRatio = 0.25;
            if (!item.ColumnDedupePlacedTolMm.HasValue) item.ColumnDedupePlacedTolMm = 150.0;
            if (!item.ColumnAreaWeight.HasValue) item.ColumnAreaWeight = 1.0;
            if (!item.ColumnSegmentCountWeight.HasValue) item.ColumnSegmentCountWeight = 0.6;
            if (!item.ColumnRectnessWeight.HasValue) item.ColumnRectnessWeight = 0.8;
            if (!item.ColumnLongLinePenalty.HasValue) item.ColumnLongLinePenalty = 1.2;
            if (!item.ColumnIrregularEnable.HasValue) item.ColumnIrregularEnable = true;
            if (!item.ColumnIrregularMaxSizeMm.HasValue) item.ColumnIrregularMaxSizeMm = 1800.0;
            if (!item.ColumnIrregularGapTolMm.HasValue) item.ColumnIrregularGapTolMm = 30.0;
            if (!item.ColumnIrregularMinAreaM2.HasValue) item.ColumnIrregularMinAreaM2 = 0.03;
            if (!item.ColumnAttachToWallEnable.HasValue) item.ColumnAttachToWallEnable = true;
            if (!item.ColumnAttachToWallSnapTolMm.HasValue) item.ColumnAttachToWallSnapTolMm = 250.0;
            if (string.IsNullOrWhiteSpace(item.ColumnAttachToWallTarget)) item.ColumnAttachToWallTarget = "WallCenterline";
            if (!item.ColumnAttachToWallAllowOverlap.HasValue) item.ColumnAttachToWallAllowOverlap = false;
            if (!item.ColumnDebugDrawCandidates.HasValue) item.ColumnDebugDrawCandidates = false;
            if (!item.ColumnDebugDrawClusterId.HasValue) item.ColumnDebugDrawClusterId = false;
            if (!item.ColumnDebugDrawRejectReason.HasValue) item.ColumnDebugDrawRejectReason = false;
            if (!item.ColumnDebugExportReport.HasValue) item.ColumnDebugExportReport = true;
        }

        private static void ApplySettings(AdvancedSettingsRow target, AdvancedSettingsRow source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.EnableLayerOverride = source.EnableLayerOverride;
            target.ApplyAsCategoryDefault = source.ApplyAsCategoryDefault;
            target.WallMinWallLengthMm = source.WallMinWallLengthMm;
            target.WallDefaultSingleWallThicknessMm = source.WallDefaultSingleWallThicknessMm;
            target.WallHeightMm = source.WallHeightMm;
            target.WallBaseOffsetMm = source.WallBaseOffsetMm;
            target.WallThicknessTolMm = source.WallThicknessTolMm;
            target.WallMaxWallThicknessMm = source.WallMaxWallThicknessMm;
            target.WallParallelAngleTolDeg = source.WallParallelAngleTolDeg;
            target.WallEndpointMergeTolMm = source.WallEndpointMergeTolMm;
            target.WallArcThicknessTolMm = source.WallArcThicknessTolMm;
            target.WallEndpointClusterTolMm = source.WallEndpointClusterTolMm;
            target.WallExtendSearchTolMm = source.WallExtendSearchTolMm;
            target.WallDuplicateTolMm = source.WallDuplicateTolMm;
            target.WallAngleSnapDeg = source.WallAngleSnapDeg;
            target.WallEnableOrthogonalSnap = source.WallEnableOrthogonalSnap;
            target.WallEnableExtendToIntersection = source.WallEnableExtendToIntersection;
            target.WallEnableEndpointClustering = source.WallEnableEndpointClustering;
            target.WallEnableDuplicateRemoval = source.WallEnableDuplicateRemoval;
            target.WallEnableExtendCollinear = source.WallEnableExtendCollinear;
            target.WallEnableMergeCollinear = source.WallEnableMergeCollinear;
            target.WallExtendCollinearTolMm = source.WallExtendCollinearTolMm;
            target.WallCollinearOffsetTolMm = source.WallCollinearOffsetTolMm;
            target.WallExtendProjectionTolMm = source.WallExtendProjectionTolMm;
            target.WallUseDirectionalClustering = source.WallUseDirectionalClustering;
            target.WallEnableAutoDoubleLineThickness = source.WallEnableAutoDoubleLineThickness;
            target.WallAutoThicknessTopK = source.WallAutoThicknessTopK;
            target.WallAutoThicknessBinMm = source.WallAutoThicknessBinMm;
            target.WallMinDoubleLineThicknessMm = source.WallMinDoubleLineThicknessMm;
            target.WallMinDoubleLineOverlapLenMm = source.WallMinDoubleLineOverlapLenMm;
            target.WallForceSingleLineMode = source.WallForceSingleLineMode;
            target.WallDoubleLineSingleWallPlaceMode = source.WallDoubleLineSingleWallPlaceMode;
            target.WallDoubleLineLengthPolicy = source.WallDoubleLineLengthPolicy;
            target.WallDoubleLineAdaptiveContainTolMm = source.WallDoubleLineAdaptiveContainTolMm;
            target.WallDoubleLineAdaptiveExtendMaxMm = source.WallDoubleLineAdaptiveExtendMaxMm;
            target.DoorExpectedWidthMm = source.DoorExpectedWidthMm;
            target.MinDoorWidthMm = source.MinDoorWidthMm;
            target.MaxDoorWidthMm = source.MaxDoorWidthMm;
            target.DoorWallMatchTolMm = source.DoorWallMatchTolMm;
            target.DoorHeightMm = source.DoorHeightMm;
            target.DoorSillHeightMm = source.DoorSillHeightMm;
            target.UseFixedDoorWidth = source.UseFixedDoorWidth;
            target.PreferGeometryOpeningWidth = source.PreferGeometryOpeningWidth;
        }

        private static HashSet<string> LoadDwgLayerNames(Document doc)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return names;
            }

            List<CadSegment> segments = CadSegmentBuilder.BuildSegments(doc, import, null).Segments ?? new List<CadSegment>();
            foreach (string name in segments
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RawLayerName))
                .Select(x => x.RawLayerName)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }

            return names;
        }

        private const string UnknownFamilyTypePlaceholder = "Please select";

        private static bool IsGeneratableCategory(MapCategory category)
        {
            return category != MapCategory.Ignore &&
                category != MapCategory.Unknown &&
                category != MapCategory.NotForBuild;
        }

        private static MapCategory ResolveSavedCategory(PreviewPaneLayerItem item)
        {
            if (item == null || !item.Category.HasValue)
            {
                return MapCategory.Ignore;
            }

            if (!item.IsSelected && IsGeneratableCategory(item.Category.Value))
            {
                return MapCategory.Ignore;
            }

            return item.Category.Value;
        }

        private static bool IsInvalidStandardLayer(string rawLayerName)
        {
            if (string.IsNullOrWhiteSpace(rawLayerName))
            {
                return false;
            }

            try
            {
                LayerStandardAnalyzeResult analysis = LayerStandardAnalyzer.AnalyzeLayers(new[] { rawLayerName });
                LayerStandardMatchItem match = analysis != null
                    ? analysis.Matches.FirstOrDefault(x => x != null && string.Equals(x.LayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                    : null;
                return match != null && !match.IsValid;
            }
            catch
            {
                return false;
            }
        }

        private static MapCategory InferDefaultCategoryFromLayerName(string rawLayerName)
        {
            if (string.IsNullOrWhiteSpace(rawLayerName))
            {
                return MapCategory.Ignore;
            }

            try
            {
                LayerStandardAnalyzeResult analysis = LayerStandardAnalyzer.AnalyzeLayers(new[] { rawLayerName });
                LayerStandardMatchItem match = analysis != null
                    ? analysis.Matches.FirstOrDefault(x => x != null && string.Equals(x.LayerName, rawLayerName, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (match == null)
                {
                    return MapCategory.NotForBuild;
                }

                if (!match.IsValid)
                {
                    return MapCategory.Unknown;
                }

                // For custom department rules, the rule explicitly decides the Revit generation
                // category. For the built-in EMSD rule, LayerStandardAnalyzer preserves the
                // previous inference behavior and supplies the same suggested category here.
                return match.SuggestedMapCategory;
            }
            catch
            {
                return MapCategory.NotForBuild;
            }
        }

        private static bool IsNotForBuildLayer(string rawLayerName, string matchedStandard)
        {
            return ContainsIgnoreCase(rawLayerName, "Text") ||
                ContainsIgnoreCase(rawLayerName, "Grid") ||
                ContainsIgnoreCase(rawLayerName, "Dimension") ||
                ContainsIgnoreCase(rawLayerName, "Axis") ||
                ContainsIgnoreCase(rawLayerName, "Stair") ||
                ContainsIgnoreCase(rawLayerName, "Ramp") ||
                ContainsIgnoreCase(matchedStandard, "Text") ||
                ContainsIgnoreCase(matchedStandard, "Grid") ||
                ContainsIgnoreCase(matchedStandard, "Dimension") ||
                ContainsIgnoreCase(matchedStandard, "Axis") ||
                ContainsIgnoreCase(matchedStandard, "Stair") ||
                ContainsIgnoreCase(matchedStandard, "Ramp");
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<MapCategory, List<(string Name, ElementId Id)>> BuildFamilyCatalog(Document doc)
        {
            Dictionary<MapCategory, List<(string Name, ElementId Id)>> data = new Dictionary<MapCategory, List<(string Name, ElementId Id)>>();
            data[MapCategory.Walls] = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>()
                .Where(x => x != null && !IsHiddenWallFamilyTypeName(x.Name))
                .Select(x => (x.Name, x.Id)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            data[MapCategory.Floors] = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                .Select(x => (x.Name, x.Id)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            data[MapCategory.Ceilings] = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>()
                .Select(x => (x.Name, x.Id)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            data[MapCategory.Doors] = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
                .Select(x => (x.FamilyName + " : " + x.Name, x.Id)).OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
            data[MapCategory.Windows] = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows)
                .Select(x => (x.FamilyName + " : " + x.Name, x.Id)).OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
            data[MapCategory.Columns] = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(x => x.Category != null && (x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Columns || x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns))
                .Select(x => (x.FamilyName + " : " + x.Name, x.Id)).OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
            data[MapCategory.Beams] = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                .Select(x => (x.FamilyName + " : " + x.Name, x.Id)).OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList();
            return data;
        }

        private static bool IsHiddenWallFamilyTypeName(string name)
        {
            return string.Equals(name, "M_Exterior Glazing", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "_Not Defined", StringComparison.OrdinalIgnoreCase);
        }

        private static ElementId ResolveTypeId(Dictionary<MapCategory, List<(string Name, ElementId Id)>> catalog, MapCategory category, string typeName)
        {
            if (catalog == null || !catalog.TryGetValue(category, out List<(string Name, ElementId Id)> options))
            {
                return ElementId.InvalidElementId;
            }

            (string Name, ElementId Id) matched = options.FirstOrDefault(x => string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (matched.Id != null && matched.Id != ElementId.InvalidElementId)
            {
                return matched.Id;
            }

            return options.Count > 0 ? options[0].Id : ElementId.InvalidElementId;
        }

        private static ImportInstance ResolveCurrentImportInstance(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            DwgSessionInfo session = DwgSessionManager.Get(doc);
            if (session?.LinkInstanceId != null && session.LinkInstanceId != ElementId.InvalidElementId)
            {
                ImportInstance current = doc.GetElement(session.LinkInstanceId) as ImportInstance;
                if (current != null)
                {
                    return current;
                }
            }

            List<ImportInstance> linkedInstances = DwgImportService.GetLinkedImportInstances(doc)
                .OrderByDescending(x => x.Id.IntegerValue)
                .ToList();
            if (linkedInstances.Count > 0)
            {
                return linkedInstances[0];
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .OrderByDescending(x => x.Id.IntegerValue)
                .FirstOrDefault();
        }

        private static string BuildDockableContextSignature(Document doc)
        {
            ImportInstance import = ResolveCurrentImportInstance(doc);
            Level level = ResolveLevel(doc);
            return WizardSessionCache.BuildContextSignature(import?.Id, level?.Id, ResolveEffectiveSourceUnit(doc));
        }

        private static SourceUnit ResolveEffectiveSourceUnit(Document doc)
        {
            DwgSessionInfo session = DwgSessionManager.Get(doc);
            if (session != null && session.SourceUnit != SourceUnit.Auto)
            {
                return session.SourceUnit;
            }

            DiagnosticRecorder.AppendDebug("WARNING: DWG SourceUnit missing from session. Fallback to Millimeter.");
            return SourceUnit.Millimeter;
        }

        private static UnitContext BuildRevitImportInstanceUnitContext(Document doc)
        {
            SourceUnit sourceUnit = ResolveEffectiveSourceUnit(doc);
            DwgSessionInfo session = DwgSessionManager.Get(doc);
            return new UnitContext
            {
                SourceUnit = sourceUnit,
                ScaleToFeet = 1.0,
                Confidence = 1.0,
                Evidence = session != null && !string.IsNullOrWhiteSpace(session.SourceUnitEvidence)
                    ? session.SourceUnitEvidence
                    : "PreviewPaneSessionFallback"
            };
        }

        private static Level ResolveLevel(Document doc)
        {
            Level fromView = doc.ActiveView != null ? doc.ActiveView.GenLevel : null;
            if (fromView != null)
            {
                return fromView;
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static WallType ResolveWallType(Document doc)
        {
            List<WallType> wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .ToList();

            List<WallType> basicTypes = wallTypes.Where(x => x != null && x.Kind == WallKind.Basic).ToList();

            string cultureName = (LocalizationService.CurrentCulture == null ? string.Empty : LocalizationService.CurrentCulture.Name) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cultureName))
            {
                cultureName = (System.Globalization.CultureInfo.CurrentUICulture == null ? string.Empty : System.Globalization.CultureInfo.CurrentUICulture.Name) ?? string.Empty;
            }
            if (cultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                WallType preferred6Inch = basicTypes.FirstOrDefault(IsPreferred6InchWallType);
                if (preferred6Inch != null)
                {
                    return preferred6Inch;
                }

                WallType preferred140 = basicTypes.FirstOrDefault(IsPreferred140WallType);
                if (preferred140 != null)
                {
                    return preferred140;
                }
            }
            else
            {
                WallType preferred140 = basicTypes.FirstOrDefault(IsPreferred140WallType);
                if (preferred140 != null)
                {
                    return preferred140;
                }
            }

            WallType basic = basicTypes.FirstOrDefault();
            return basic ?? wallTypes.FirstOrDefault();
        }

        private static string ResolvePreferredFamilyTypeName(MapCategory? category, IList<string> options)
        {
            if (!category.HasValue || options == null || options.Count == 0)
            {
                return null;
            }

            if (category.Value == MapCategory.Doors)
            {
                foreach (string name in options)
                {
                    if (!string.IsNullOrWhiteSpace(name) &&
                        name.IndexOf("Passage-Single", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return name;
                    }
                }

                return null;
            }

            if (category.Value != MapCategory.Walls)
            {
                return null;
            }

            string[] preferredPatterns =
            {
                @"^\s*Generic\s*-\s*100\s*mm\b",
                @"^\s*Generic\s*-\s*150\s*mm\b",
                @"^\s*Generic\s*-\s*200\s*mm\b"
            };

            foreach (string pattern in preferredPatterns)
            {
                foreach (string name in options)
                {
                    if (!string.IsNullOrWhiteSpace(name) && Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase))
                    {
                        return name;
                    }
                }
            }

            return null;
        }

        private static bool IsPreferred140WallType(WallType wallType)
        {
            if (wallType == null)
            {
                return false;
            }

            string name = wallType.Name ?? string.Empty;
            string familyName = wallType.FamilyName ?? string.Empty;
            return name.IndexOf("140", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   familyName.IndexOf("140", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsPreferred6InchWallType(WallType wallType)
        {
            if (wallType == null)
            {
                return false;
            }

            string name = wallType.Name ?? string.Empty;
            string familyName = wallType.FamilyName ?? string.Empty;
            // Match exact 6" token and avoid matching 16" / 26".
            return Regex.IsMatch(name, @"(^|[^0-9])6""($|[^0-9])") ||
                   Regex.IsMatch(familyName, @"(^|[^0-9])6""($|[^0-9])");
        }

        private static AdvancedSettingsRow TryGetWallCategoryDefaults(Document doc)
        {
            LayerOverrideStoreData store = LoadDocScopedOverrides(doc);
            if (store != null &&
                store.CategoryDefaults != null &&
                store.CategoryDefaults.TryGetValue(MapCategory.Walls, out AdvancedSettingsRow row))
            {
                return row;
            }

            return null;
        }

        private static HashSet<string> GetWallRawLayers(Document doc, CadToRevit.Models.Cad.CadDataset scaled)
        {
            HashSet<string> layers = GetWallRawLayers(doc, scaled != null ? scaled.Segments : null);
            if (layers.Count > 0)
            {
                return layers;
            }

            foreach (string layer in (scaled != null ? scaled.SegmentsByRawLayer?.Keys : null) ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(layer))
                {
                    layers.Add(layer);
                }
            }

            return layers;
        }

        private static HashSet<string> GetWallRawLayers(Document doc, IEnumerable<CadSegment> segments)
        {
            HashSet<string> selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string contextSignature = BuildDockableContextSignature(doc);
            bool hasPersistedRows = WizardStateStoreService.TryLoad(doc, contextSignature, out List<MapRow> persistedRows);
            if (!hasPersistedRows)
            {
                hasPersistedRows = WizardSessionCache.TryLoad(doc, contextSignature, out persistedRows);
            }

            if (hasPersistedRows && persistedRows != null)
            {
                foreach (MapRow row in persistedRows)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                    {
                        continue;
                    }

                    if (row.Category == MapCategory.Walls)
                    {
                        selected.Add(row.RawLayerName);
                    }
                }
            }

            if (selected.Count > 0)
            {
                return selected;
            }

            LayerOverrideStoreData store = LoadDocScopedOverrides(doc);
            if (store != null && store.LayerOverrides != null)
            {
                foreach (KeyValuePair<string, AdvancedSettingsRow> kv in store.LayerOverrides)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null && kv.Value.EnableLayerOverride)
                    {
                        selected.Add(kv.Key);
                    }
                }
            }

            if (selected.Count > 0)
            {
                return selected;
            }

            foreach (CadSegment seg in segments ?? Enumerable.Empty<CadSegment>())
            {
                if (seg == null || string.IsNullOrWhiteSpace(seg.RawLayerName))
                {
                    continue;
                }

                if (string.Equals(seg.SemanticLayer, "WALL", StringComparison.OrdinalIgnoreCase))
                {
                    selected.Add(seg.RawLayerName);
                }
            }

            return selected;
        }

        private static LayerOverrideStoreData LoadDocScopedOverrides(Document doc)
        {
            LayerOverrideStoreData store = LayerOverrideStoreService.Load(doc);
            if (store == null)
            {
                return new LayerOverrideStoreData();
            }

            // Prevent global AppData history from leaking into a fresh/unsaved document.
            // Accept only RVT-backed payload so unsaved docs can still keep in-memory project settings.
            if (!string.Equals(store.LoadSource, "RVT", StringComparison.OrdinalIgnoreCase))
            {
                return new LayerOverrideStoreData();
            }

            return store;
        }

        private static GlobalGenerationSettings LoadGlobalGenerationSettings(Document doc)
        {
            LayerOverrideStoreData store = LoadDocScopedOverrides(doc);
            return GlobalGenerationSettings.Clone(store != null ? store.GlobalGenerationSettings : null);
        }

        private static bool ComputeCadVisibleInView(Document doc, View view)
        {
            ImportInstance import = ResolveCurrentImportInstance(doc);
            if (import == null)
            {
                return true;
            }

            return !IsElementHiddenInView(doc, view, import.Id);
        }

        private static bool ComputeGeneratedBuildingElementsVisibleInView(Document doc, View view)
        {
            List<ElementId> ids = CollectGeneratedBuildingElementsInView(doc, view);
            if (ids.Count == 0)
            {
                return true;
            }

            return ids.Any(id => !IsElementHiddenInView(doc, view, id));
        }

        private static List<ElementId> CollectGeneratedBuildingElementsInView(Document doc, View view)
        {
            List<BuiltInCategory> categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_GenericModel
            };

            ElementMulticategoryFilter filter = new ElementMulticategoryFilter(categories.Select(x => new ElementId((int)x)).ToList());
            List<ElementId> ids = new List<ElementId>();
            // Collect from whole document (not view-scoped collector), otherwise hidden elements
            // may be skipped and cannot be restored by the next toggle.
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType().WherePasses(filter))
            {
                if (element == null)
                {
                    continue;
                }

                if (!IsGeneratedByCadToRevit(element))
                {
                    continue;
                }

                if (!element.CanBeHidden(view))
                {
                    continue;
                }

                ids.Add(element.Id);
            }

            return ids.Distinct().ToList();
        }

        private static bool IsGeneratedByCadToRevit(Element element)
        {
            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments == null || comments.StorageType != StorageType.String)
            {
                return false;
            }

            string text = comments.AsString();
            return !string.IsNullOrWhiteSpace(text) && text.IndexOf("CadToRevit|RowKey=", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ClearRoomVisualizationBeforeLayerOperation(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            try
            {
                RoomPointProbeService.ClearProbePreview(doc);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] Clear probe room preview skipped. Error=" + ex.Message);
            }

            try
            {
                Room3DVisualizationService.Clear(doc);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PreviewPane] Clear room visualization skipped. Error=" + ex.Message);
            }
        }

        private static void MarkRoutePlannerDirty(Document doc, string reason)
        {
            try
            {
                RoutePlannerSessionCacheService.MarkDirty(doc, reason);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoutePlannerSession] MarkDirty skipped. Error=" + ex.Message);
            }
        }

        private static bool IsElementHiddenInView(Document doc, View view, ElementId id)
        {
            Element element = doc.GetElement(id);
            if (element == null)
            {
                return false;
            }

            return element.IsHidden(view);
        }

        private static bool HasPersistentDocIdentity(Document doc)
        {
            return doc != null && !string.IsNullOrWhiteSpace(doc.PathName);
        }

        private static void WarnIfCadRuntimeUnavailable(UIApplication uiApp)
        {
            CadRuntimeInfo cadRuntime = CadRuntimeDetector.Detect(forceRefresh: true);
            if (cadRuntime != null && cadRuntime.IsReady)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[ModelGenerator] CAD runtime unavailable. Continue model generation with reduced room/lift text recognition. " +
                (cadRuntime != null ? cadRuntime.ToString() : string.Empty));
            CadRuntimeUserMessage.ShowWarningOnce(uiApp, cadRuntime);
        }

        private static BoundingBoxXYZ CollectModelBoundingBox(Document doc)
        {
            HashSet<int> categories = new HashSet<int>
            {
                (int)BuiltInCategory.OST_Walls,
                (int)BuiltInCategory.OST_Doors,
                (int)BuiltInCategory.OST_Windows,
                (int)BuiltInCategory.OST_Columns,
                (int)BuiltInCategory.OST_StructuralColumns,
                (int)BuiltInCategory.OST_StructuralFraming,
                (int)BuiltInCategory.OST_Ceilings,
                (int)BuiltInCategory.OST_GenericModel
            };

            BoundingBoxXYZ total = null;
            IEnumerable<Element> elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .Where(x => x.Category != null && categories.Contains(x.Category.Id.IntegerValue));
            foreach (Element e in elements)
            {
                BoundingBoxXYZ box = e.get_BoundingBox(null);
                if (box == null)
                {
                    continue;
                }

                total = UnionBoundingBox(total, box);
            }

            return total;
        }

        private static BoundingBoxXYZ CollectImportBoundingBox(Document doc)
        {
            BoundingBoxXYZ total = null;
            foreach (ImportInstance instance in new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>())
            {
                BoundingBoxXYZ box = instance.get_BoundingBox(null);
                if (box == null)
                {
                    continue;
                }

                total = UnionBoundingBox(total, box);
            }

            return total;
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

            BoundingBoxXYZ c = new BoundingBoxXYZ();
            c.Min = new XYZ(
                Math.Min(a.Min.X, b.Min.X),
                Math.Min(a.Min.Y, b.Min.Y),
                Math.Min(a.Min.Z, b.Min.Z));
            c.Max = new XYZ(
                Math.Max(a.Max.X, b.Max.X),
                Math.Max(a.Max.Y, b.Max.Y),
                Math.Max(a.Max.Z, b.Max.Z));
            return c;
        }

        private static OverrideGraphicSettings TryGetElementOverrides(View view, ElementId elementId)
        {
            if (view == null || elementId == null || elementId == ElementId.InvalidElementId)
            {
                return new OverrideGraphicSettings();
            }

            try
            {
                return view.GetElementOverrides(elementId);
            }
            catch
            {
                return new OverrideGraphicSettings();
            }
        }

        private static string ResolveElementTypeName(Element element)
        {
            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null && familyInstance.Symbol != null)
            {
                return familyInstance.Symbol.Name ?? string.Empty;
            }

            ElementType type = element != null && element.Document != null
                ? element.Document.GetElement(element.GetTypeId()) as ElementType
                : null;
            return type != null ? type.Name ?? string.Empty : string.Empty;
        }

        private sealed class DetachTarget
        {
            public Element Element { get; set; }

            public GeneratedElementFullMetadataSnapshot Original { get; set; }

            public string UniqueId { get; set; }

            public string OriginalFamilyType { get; set; }

            public OverrideGraphicSettings OriginalViewOverride { get; set; }

            public ElementId ViewId { get; set; }
        }
    }
}
