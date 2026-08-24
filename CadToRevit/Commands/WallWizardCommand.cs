using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Models;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Models.Settings;
using CadToRevit.Models.Units;
using CadToRevit.Services;
using CadToRevit.Services.Cad;
using CadToRevit.Services.CadRuntime;
using CadToRevit.Services.Columns;
using CadToRevit.Services.Config;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Dwg;
using CadToRevit.Services.Join;
using CadToRevit.Services.Preview;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Rooms.Lifts;
using CadToRevit.Services.Units;
using CadToRevit.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CadToRevit.Commands
{
    internal enum PreflightDecision
    {
        Continue,
        AdjustFilter,
        ViewRangeOnly,
        Cancel
    }

    internal sealed class GenerationExecutionOptions
    {
        public double MinLengthMm { get; set; }

        public int BatchSize { get; set; }

        public bool OnlyViewRange { get; set; }

        public bool SafeMode { get; set; }
    }

    internal sealed class AnalyzeSummaryInfo
    {
        public string DwgName { get; set; }

        public int ImportInstanceId { get; set; }

        public string UnitText { get; set; }

        public int LayerCount { get; set; }

        public int ValidLayerCount { get; set; }

        public int SegmentCount { get; set; }

        public int ArcCount { get; set; }

        public int PolylineCount { get; set; }

        public double TimeSeconds { get; set; }

        public string Status { get; set; }

        public string Error { get; set; }
    }

    internal sealed class ArcWallCreateCandidate
    {
        public Curve Curve { get; set; }

        public double ThicknessMm { get; set; }
    }

    internal sealed class CreateElementsExecutionSummary
    {
        public string Message { get; set; }

        public int CreatedCount { get; set; }

        public int JoinedCount { get; set; }

        public int FailureCount { get; set; }

        public List<string> Errors { get; set; } = new List<string>();

        public List<int> CreatedElementIds { get; set; } = new List<int>();
    }

    internal interface IGenerationProgressReporter
    {
        bool IsCancellationRequested { get; }

        void UpdateProgress(string stage, int current, int total, string detail);
    }

    internal sealed class WinFormsGenerationProgressReporter : IGenerationProgressReporter
    {
        private readonly GenerationProgressForm _form;

        public WinFormsGenerationProgressReporter(GenerationProgressForm form)
        {
            _form = form;
        }

        public bool IsCancellationRequested => _form != null && _form.IsCancellationRequested;

        public void UpdateProgress(string stage, int current, int total, string detail)
        {
            _form?.UpdateProgress(stage, current, total, detail);
        }
    }

    [Transaction(TransactionMode.Manual)]
    public class WallWizardCommand : IExternalCommand
    {
        private const double DefaultWallHeightMm = 4000.0;
        private const string StructuralColumnPrefix = "结构柱 | ";
        private const string ArchitecturalColumnPrefix = "建筑柱 | ";
        private const string DefaultRoomNameLayer = "ROOMNAME";
        private const double RoomTextMarkerHalfSizeMm = 120.0;
        private const int RoomTextMarkerMaxCount = 200;
        private const int RoomBoundaryMarkerMaxRooms = 80;
        private const int RoomBoundaryMarkerMaxSegments = 4000;


        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            CadRuntimeInfo cadRuntime = CadRuntimeDetector.Detect(forceRefresh: true);
            if (cadRuntime == null || !cadRuntime.IsReady)
            {
                DiagnosticRecorder.AppendDebug(
                    "[ModelGenerator] CAD runtime unavailable. Continue model generation with reduced room/lift text recognition. " +
                    (cadRuntime != null ? cadRuntime.ToString() : string.Empty));
                CadRuntimeUserMessage.ShowWarningOnce(commandData.Application, cadRuntime);
            }

            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;
            List<ImportInstance> allImports = GetAllImportInstances(doc);
            if (allImports.Count == 0)
            {
                TaskDialog.Show("M9-3", "No CAD Link (ImportInstance) found.");
                return Result.Cancelled;
            }

            List<Level> levels = GetAllLevels(doc);
            List<WallType> wallTypes = GetSupportedWallTypes(doc);
            List<string> columnTypeNames = GetColumnFamilyTypeNames(doc);
            List<string> doorTypeNames = GetFamilySymbolTypeNames(doc, BuiltInCategory.OST_Doors);
            List<string> windowTypeNames = GetFamilySymbolTypeNames(doc, BuiltInCategory.OST_Windows);
            List<string> beamTypeNames = GetFamilySymbolTypeNames(doc, BuiltInCategory.OST_StructuralFraming);
            List<ParameterOption> parameterOptions = BuildWallParameterOptions(doc, wallTypes);
            if (levels.Count == 0 || wallTypes.Count == 0)
            {
                TaskDialog.Show("M9-3", "No level or wall type available.");
                return Result.Cancelled;
            }

            ImportInstance selectedImport = GetSelectedImportInstance(uiDoc) ?? allImports.First();
            ElementId selectedDwgId = selectedImport.Id;
            ElementId selectedLevelId = ResolveDefaultLevelId(doc, levels);
            SourceUnit selectedUnit = ResolveEffectiveSourceUnit(doc, SourceUnit.Auto, out _);
            GlobalGenerationSettings projectGlobalSettings = LoadGlobalGenerationSettings(doc);
            bool joinWallsAfterCreate = projectGlobalSettings.AutoJoinWallsAfterCreate;
            bool safeModeEnabled = projectGlobalSettings.SafeModeEnabled;
            List<MapRow> mapRows = new List<MapRow>();
            VerticalDimensionSettings verticalSettings = VerticalDimensionStoreService.Load(doc);
            AnalyzeSummaryInfo lastAnalyze = null;
            string lastAnalyzeTimeText = "Last Analyze: N/A";
            string initContextSignature = WizardSessionCache.BuildContextSignature(selectedDwgId, selectedLevelId, selectedUnit);
            List<MapRow> cachedRows;
            if (WizardSessionCache.TryLoad(doc, initContextSignature, out cachedRows))
            {
                mapRows = DeduplicateMapRows(cachedRows);
            }
            else
            {
                List<MapRow> persistedRows;
                if (WizardStateStoreService.TryLoad(doc, initContextSignature, out persistedRows) && persistedRows.Count > 0)
                {
                    mapRows = DeduplicateMapRows(persistedRows);
                }
            }

            while (true)
            {
                ImportInstance activeImport = doc.GetElement(selectedDwgId) as ImportInstance;
                if (activeImport == null)
                {
                    activeImport = allImports.First();
                    selectedDwgId = activeImport.Id;
                }

                UnitContext unitContext;
                CadDataset scaled;
                AnalyzeSummaryInfo analyzeSummary;
                BuildAnalyzeContext(doc, activeImport, selectedUnit, out unitContext, out scaled, out analyzeSummary);
                if (analyzeSummary != null)
                {
                    lastAnalyze = analyzeSummary;
                }
                if (scaled == null || scaled.Segments.Count == 0)
                {
                    TaskDialog.Show("M9-3", "No segments found from CAD geometry.");
                    return Result.Cancelled;
                }

                List<string> layerOptions = BuildLayerOptions(scaled);

                using (CadToRevitHelixForm form = new CadToRevitHelixForm(
                    allImports,
                    selectedDwgId,
                    levels,
                    selectedLevelId,
                    selectedUnit,
                    wallTypes.Select(x => x.Name),
                    columnTypeNames,
                    doorTypeNames,
                    windowTypeNames,
                    beamTypeNames,
                    parameterOptions,
                    layerOptions,
                    mapRows,
                    joinWallsAfterCreate,
                    safeModeEnabled,
                    verticalSettings,
                    BuildAnalyzeSummaryText(lastAnalyze),
                    lastAnalyzeTimeText))
                {
                    System.Windows.Forms.DialogResult dialogResult = form.ShowDialog();
                    selectedDwgId = form.SelectedDwgId;
                    selectedLevelId = form.SelectedLevelId;
                    selectedUnit = form.SelectedUnit;
                    joinWallsAfterCreate = form.JoinWallsAfterCreate;
                    safeModeEnabled = form.SafeModeEnabled;
                    mapRows = DeduplicateMapRows(form.GetMapRows());
                    verticalSettings = form.VerticalSettings;
                    string contextSignature = WizardSessionCache.BuildContextSignature(selectedDwgId, selectedLevelId, selectedUnit);
                    LayerOverrideStoreData beforeSaveOverrides = LayerOverrideStoreService.Load(doc);
                    if (mapRows.Count == 0)
                    {
                        WizardSessionCache.Save(doc, contextSignature, new List<MapRow>());
                        WizardStateStoreService.Save(doc, contextSignature, new List<MapRow>());
                        SaveLayerOverridesWithMessage(doc, new List<MapRow>(), beforeSaveOverrides);
                        DiagnosticRecorder.AppendDebug("[Analyze] CacheSaved=True, Rows=0");
                    }
                    else
                    {
                        WizardSessionCache.Save(doc, contextSignature, mapRows);
                        WizardStateStoreService.Save(doc, contextSignature, mapRows);
                        SaveLayerOverridesWithMessage(doc, mapRows, beforeSaveOverrides);
                        DiagnosticRecorder.AppendDebug("[Analyze] CacheSaved=True, Rows=" + mapRows.Count);
                    }

                    VerticalDimensionStoreService.Save(doc, verticalSettings);

                    if (dialogResult != System.Windows.Forms.DialogResult.OK || form.Action == HelixWizardAction.Cancel)
                    {
                        return Result.Cancelled;
                    }

                    if (form.Action == HelixWizardAction.Analyze)
                    {
                        lastAnalyzeTimeText = "Last Analyze: " + DateTime.Now.ToString("HH:mm:ss");
                        ShowAnalyzeResultDialog(lastAnalyze);
                        continue;
                    }

                    if (form.Action == HelixWizardAction.Preview)
                    {
                        HashSet<string> previewLayers = form.GetPreviewRawLayers();
                        if (previewLayers.Count == 0)
                        {
                            TaskDialog.Show("M9-3", "Please select at least one layer in Map Board.");
                            continue;
                        }

                        PreviewResult preview = PreviewService.ShowLayerSegments(uiDoc, scaled, previewLayers);
                        if (!string.IsNullOrWhiteSpace(preview != null ? preview.Message : null))
                        {
                            TaskDialog.Show("Preview Result", preview.Message);
                        }
                        continue;
                    }

                    if (form.Action == HelixWizardAction.CreateElements)
                    {
                        PreviewService.Clear(doc);
                        GenerationGuardConfig guard = GenerationGuardConfigProvider.Load();
                        GenerationExecutionOptions options = new GenerationExecutionOptions
                        {
                            MinLengthMm = guard.DefaultMinLengthMm > 0 ? guard.DefaultMinLengthMm : 200.0,
                            BatchSize = guard.BatchSize > 0 ? guard.BatchSize : 200,
                            OnlyViewRange = false,
                            SafeMode = safeModeEnabled
                        };
                        HashSet<string> wallLayers = new HashSet<string>(
                            mapRows
                                .Where(x => x != null && x.Category == MapCategory.Walls && !string.IsNullOrWhiteSpace(x.RawLayerName))
                                .Select(x => x.RawLayerName),
                            StringComparer.OrdinalIgnoreCase);
                        if (wallLayers.Count > 0)
                        {
                            double preflightMinLengthMm = ResolvePreflightMinLengthMm(mapRows, options.MinLengthMm);
                            PreflightReport preflight = PreflightAnalyzer.Analyze(
                                scaled,
                                wallLayers,
                                preflightMinLengthMm,
                                guard.MaxSegmentsPreview,
                                guard.MaxSegmentsHardStop,
                                guard.MaxEstimatedWalls);
                            PreflightDecision decision = ShowPreflightDialog(preflight, guard);
                            if (preflight.Workload == WorkloadLevel.Extreme || preflight.RawSegmentCount > guard.MaxSegmentsPreview)
                            {
                                // 高复杂度自动触发安全模式，降低弹窗和失败概率。
                                options.SafeMode = true;
                            }
                            if (decision == PreflightDecision.Cancel)
                            {
                                continue;
                            }

                            if (decision == PreflightDecision.AdjustFilter)
                            {
                                options.MinLengthMm = Math.Max(options.MinLengthMm, guard.HighRiskMinLengthMm);
                            }
                            else if (decision == PreflightDecision.ViewRangeOnly)
                            {
                                options.OnlyViewRange = true;
                                options.MinLengthMm = Math.Max(options.MinLengthMm, guard.HighRiskMinLengthMm);
                            }
                        }

                        CadDataset runDataset = options.OnlyViewRange
                            ? FilterDatasetByViewRange(scaled, uiDoc.ActiveView)
                            : scaled;
                        if (options.SafeMode && options.BatchSize > 100)
                        {
                            options.BatchSize = 100;
                        }
                        int created;
                        int joined;
                        int totalCenterlines;
                        List<string> failures;
                        List<WallRecognitionResult> recognizeDetails;
                        DoorCreateResult doorSummary;
                        WindowCreateResult windowSummary;
                        GenerationProfilingData profiling = new GenerationProfilingData();
                        CancellationTokenSource cts = new CancellationTokenSource();
                        bool isCanceled = false;
                        using (GenerationProgressForm progress = new GenerationProgressForm())
                        {
                            IGenerationProgressReporter progressReporter = new WinFormsGenerationProgressReporter(progress);
                            progress.Show();
                            progress.UpdateProgress(Loc.T("Progress.StagePreparing"), 0, 100, Loc.T("Progress.InitializingTask"));
                            try
                            {
                                CreateElementsByMapRows(
                                    doc,
                                    runDataset,
                                    mapRows,
                                    wallTypes,
                                    levels,
                                    selectedLevelId,
                                    selectedDwgId,
                                    joinWallsAfterCreate && !options.SafeMode,
                                    verticalSettings,
                                    options,
                                    progressReporter,
                                    cts,
                                    profiling,
                                    true,
                                    out created,
                                    out joined,
                                    out totalCenterlines,
                                    out failures,
                                    out recognizeDetails,
                                    out doorSummary,
                                    out windowSummary);
                            }
                            catch (OperationCanceledException)
                            {
                                isCanceled = true;
                                created = 0;
                                joined = 0;
                                totalCenterlines = 0;
                                failures = new List<string> { "Operation canceled by user." };
                                recognizeDetails = new List<WallRecognitionResult>();
                                doorSummary = new DoorCreateResult();
                                windowSummary = new WindowCreateResult();
                                profiling.IsCanceled = true;
                            }
                            finally
                            {
                                progress.Close();
                            }
                        }

                        string profilingPath = ProfilingLogService.Write(profiling);
                        if (isCanceled)
                        {
                            TaskDialog.Show("M11.3", "Operation canceled.\nProfiling: " + profilingPath);
                            continue;
                        }

                        HashSet<string> selectedLayers = new HashSet<string>(
                            mapRows.Where(x => !string.IsNullOrWhiteSpace(x.RawLayerName)).Select(x => x.RawLayerName),
                            StringComparer.OrdinalIgnoreCase);
                        WallRecognitionResult summarize = MergeResults(recognizeDetails);
                        string diagPath = DiagnosticRecorder.Write(
                            unitContext,
                            selectedLayers,
                            summarize,
                            created,
                            failures);
                        string verticalLogPath = VerticalDimensionLogService.Write(verticalSettings, doorSummary, windowSummary);
                        int savedSeedCount = TrySaveTargetRoomSeeds(doc, runDataset, selectedLevelId);

                        TaskDialog.Show(
                            "M9-3",
                            "Map Rows: " + mapRows.Count + "\n" +
                            "Centerlines: " + totalCenterlines + "\n" +
                            "Merged: " + summarize.MergedWalls + "\n" +
                            "Refined: " + summarize.RefinedWalls + "\n" +
                            "Endpoint Clustered: " + summarize.ClusteredEndpointCount + "\n" +
                            "Endpoint Extended: " + summarize.ExtendedEndpointCount + "\n" +
                            "Duplicate Removed: " + summarize.DuplicateRemovedCount + "\n" +
                            "Collinear Merged: " + summarize.CollinearMergedCount + "\n" +
                            "Off-axis Snapped: " + summarize.OffAxisSnappedCount + "\n" +
                            "Created Elements: " + created + "\n" +
                            "Joined Pairs: " + joined + "\n" +
                            "SafeMode: " + (options.SafeMode ? "On" : "Off") + "\n" +
                            "Failures: " + failures.Count + "\n" +
                            "TargetRoomSeeds: " + savedSeedCount + "\n" +
                            "Diagnostic: " + diagPath + "\n" +
                            "VerticalStats: " + verticalLogPath + "\n" +
                            "Profiling: " + profilingPath);
                        return Result.Succeeded;
                    }
                }
            }
        }


        internal static CreateElementsExecutionSummary ExecuteForDockable(
            Document doc,
            ElementId dwgId,
            ElementId levelId,
            List<MapRow> mapRows,
            bool joinWallsAfterCreate,
            bool safeModeEnabled,
            VerticalDimensionSettings verticalSettings,
            bool enableIdempotencySkip = true)
        {
            CreateElementsExecutionSummary summary = new CreateElementsExecutionSummary();
            if (doc == null)
            {
                summary.Message = "No active document.";
                summary.Errors.Add(summary.Message);
                return summary;
            }

            List<ImportInstance> imports = GetAllImportInstances(doc);
            ImportInstance selectedImport = doc.GetElement(dwgId) as ImportInstance ?? imports.FirstOrDefault();
            if (selectedImport == null)
            {
                summary.Message = "No CAD Link found.";
                summary.Errors.Add(summary.Message);
                return summary;
            }

            List<Level> levels = GetAllLevels(doc);
            List<WallType> wallTypes = GetSupportedWallTypes(doc);
            if (levels.Count == 0 || wallTypes.Count == 0)
            {
                summary.Message = "No level or wall type available.";
                summary.Errors.Add(summary.Message);
                return summary;
            }

            ElementId resolvedLevelId = levelId;
            if (resolvedLevelId == null || resolvedLevelId == ElementId.InvalidElementId || doc.GetElement(resolvedLevelId) == null)
            {
                resolvedLevelId = ResolveDefaultLevelId(doc, levels);
            }

            List<MapRow> effectiveRows = DeduplicateMapRows(mapRows)
                .Where(x => x != null && x.Category != MapCategory.Ignore && x.Category != MapCategory.Unknown && x.Category != MapCategory.NotForBuild && !string.IsNullOrWhiteSpace(x.RawLayerName))
                .ToList();
            if (effectiveRows.Count == 0)
            {
                summary.Message = "No selected mapping rows to generate.";
                summary.Errors.Add(summary.Message);
                return summary;
            }

            PreviewService.Clear(doc);
            GenerationGuardConfig guard = GenerationGuardConfigProvider.Load();
            GenerationExecutionOptions options = new GenerationExecutionOptions
            {
                MinLengthMm = guard.DefaultMinLengthMm > 0 ? guard.DefaultMinLengthMm : 200.0,
                BatchSize = guard.BatchSize > 0 ? guard.BatchSize : 200,
                OnlyViewRange = false,
                SafeMode = safeModeEnabled
            };

            HashSet<string> wallLayers = new HashSet<string>(
                effectiveRows
                    .Where(x => x.Category == MapCategory.Walls && !string.IsNullOrWhiteSpace(x.RawLayerName))
                    .Select(x => x.RawLayerName),
                StringComparer.OrdinalIgnoreCase);
            if (wallLayers.Count > 0)
            {
                SourceUnit generationSourceUnit = ResolveEffectiveSourceUnit(doc, SourceUnit.Auto, out _);
                UnitContext preflightUnitContext;
                CadDataset preflightDataset;
                AnalyzeSummaryInfo preflightSummary;
                BuildAnalyzeContext(doc, selectedImport, generationSourceUnit, out preflightUnitContext, out preflightDataset, out preflightSummary);
                if (preflightDataset != null)
                {
                    double preflightMinLengthMm = ResolvePreflightMinLengthMm(effectiveRows, options.MinLengthMm);
                    PreflightReport preflight = PreflightAnalyzer.Analyze(
                        preflightDataset,
                        wallLayers,
                        preflightMinLengthMm,
                        guard.MaxSegmentsPreview,
                        guard.MaxSegmentsHardStop,
                        guard.MaxEstimatedWalls);
                    if (preflight.Workload == WorkloadLevel.Extreme || preflight.RawSegmentCount > guard.MaxSegmentsPreview)
                    {
                        options.SafeMode = true;
                        options.MinLengthMm = Math.Max(options.MinLengthMm, guard.HighRiskMinLengthMm);
                    }
                }
            }

            UnitContext unitContext;
            CadDataset scaled;
            AnalyzeSummaryInfo analyzeSummary;
            BuildAnalyzeContext(doc, selectedImport, ResolveEffectiveSourceUnit(doc, SourceUnit.Auto, out _), out unitContext, out scaled, out analyzeSummary);
            CadDataset runDataset = options.OnlyViewRange ? FilterDatasetByViewRange(scaled, doc.ActiveView) : scaled;
            if (options.SafeMode && options.BatchSize > 100)
            {
                options.BatchSize = 100;
            }

            int created;
            int joined;
            int totalCenterlines;
            List<string> failures;
            List<WallRecognitionResult> recognizeDetails;
            DoorCreateResult doorSummary;
            WindowCreateResult windowSummary;
            GenerationProfilingData profiling = new GenerationProfilingData();
            CancellationTokenSource cts = new CancellationTokenSource();
            try
            {
                using (CadToRevit.UI.Dockable.PreviewGenerationProgressWindow progressWindow = new CadToRevit.UI.Dockable.PreviewGenerationProgressWindow())
                {
                    progressWindow.Show();
                    progressWindow.UpdateProgress(Loc.T("Progress.StageRecognizeGenerate"), 0, 100, Loc.T("Progress.DetailDefault"));
                    CreateElementsByMapRows(
                        doc,
                        runDataset,
                        effectiveRows,
                        wallTypes,
                        levels,
                        resolvedLevelId,
                        selectedImport.Id,
                        joinWallsAfterCreate && !options.SafeMode,
                        verticalSettings,
                        options,
                        progressWindow,
                        cts,
                        profiling,
                        enableIdempotencySkip,
                        out created,
                        out joined,
                        out totalCenterlines,
                        out failures,
                        out recognizeDetails,
                        out doorSummary,
                        out windowSummary);
                }
            }
            catch (OperationCanceledException)
            {
                summary.Message = "Operation canceled by user.";
                summary.Errors.Add(summary.Message);
                return summary;
            }

            HashSet<string> selectedLayers = new HashSet<string>(
                effectiveRows.Where(x => !string.IsNullOrWhiteSpace(x.RawLayerName)).Select(x => x.RawLayerName),
                StringComparer.OrdinalIgnoreCase);
            int extractedSeeds = TrySaveTargetRoomSeeds(doc, runDataset, resolvedLevelId);

            WallRecognitionResult merged = MergeResults(recognizeDetails);
            DiagnosticRecorder.Write(unitContext, selectedLayers, merged, created, failures);
            VerticalDimensionLogService.Write(verticalSettings, doorSummary, windowSummary);
            ProfilingLogService.Write(profiling);

            summary.CreatedCount = created;
            summary.JoinedCount = joined;
            summary.FailureCount = failures == null ? 0 : failures.Count;
            summary.Errors = (failures ?? new List<string>()).Take(20).ToList();
            summary.CreatedElementIds = (doorSummary != null ? doorSummary.CreatedElementIds : new List<int>())
                .Where(x => x > 0)
                .Distinct()
                .ToList();
            summary.Message = "Created Elements: " + created + ", Joined: " + joined + ", Failures: " + summary.FailureCount;
            if (extractedSeeds > 0)
            {
                summary.Message += " | TargetRoomSeeds:" + extractedSeeds;
            }
            return summary;
        }

        private static void CreateElementsByMapRows(
            Document doc,
            CadDataset scaled,
            List<MapRow> mapRows,
            List<WallType> wallTypes,
            List<Level> levels,
            ElementId levelId,
            ElementId dwgId,
            bool joinWallsAfterCreate,
            VerticalDimensionSettings verticalSettings,
            GenerationExecutionOptions options,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            GenerationProfilingData profiling,
            bool enableIdempotencySkip,
            out int createdCount,
            out int joinedCount,
            out int totalCenterlines,
            out List<string> failureMessages,
            out List<WallRecognitionResult> recognizeDetails,
            out DoorCreateResult doorSummary,
            out WindowCreateResult windowSummary)
        {
            createdCount = 0;
            joinedCount = 0;
            totalCenterlines = 0;
            failureMessages = new List<string>();
            recognizeDetails = new List<WallRecognitionResult>();
            doorSummary = new DoorCreateResult();
            windowSummary = new WindowCreateResult();
            profiling = profiling ?? new GenerationProfilingData();
            mapRows = DeduplicateMapRows(mapRows);
            if (scaled == null || mapRows == null || mapRows.Count == 0)
            {
                return;
            }

            options = options ?? new GenerationExecutionOptions { MinLengthMm = 200.0, BatchSize = 200 };
            GlobalGenerationSettings globalSettings = LoadGlobalGenerationSettings(doc);
            int batchSize = options.BatchSize > 0 ? options.BatchSize : 200;
            Stopwatch totalWatch = Stopwatch.StartNew();
            profiling.LayerSummary = string.Join(", ", mapRows.Where(x => x != null).Select(x => x.RawLayerName).Distinct(StringComparer.OrdinalIgnoreCase));
            profiling.RawSegmentCount = scaled.Segments == null ? 0 : scaled.Segments.Count;
            profiling.BuildMs = 0;
            profiling.SafeModeEnabled = options.SafeMode;
            DynamicWallTypeService.ClearCache();

            Level level = doc.GetElement(levelId) as Level;
            if (level == null)
            {
                failureMessages.Add("Target level is invalid.");
                return;
            }

            Dictionary<string, WallType> typeByName = wallTypes
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FamilySymbol> structuralColumnSymbolByName = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralColumns)
                .GroupBy(x => x.FamilyName + " : " + x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FamilySymbol> architecturalColumnSymbolByName = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Columns)
                .GroupBy(x => x.FamilyName + " : " + x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FamilySymbol> doorSymbolByName = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Doors)
                .GroupBy(x => x.FamilyName + " : " + x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FamilySymbol> windowSymbolByName = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Windows)
                .GroupBy(x => x.FamilyName + " : " + x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, FamilySymbol> beamSymbolByName = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)BuiltInCategory.OST_StructuralFraming)
                .GroupBy(x => x.FamilyName + " : " + x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ElementId> levelIdByName = levels
                .Where(x => x != null && x.Id != null && !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            List<Wall> createdWalls = new List<Wall>();
            Stopwatch filterWatch = Stopwatch.StartNew();
            int phaseCurrent = 0;
            int phaseTotal = mapRows.Count + (joinWallsAfterCreate ? 1 : 0);
            int filteredSegmentCount = 0;
            int candidateDouble = 0;
            int candidateSingle = 0;

            using (TransactionGroup tg = new TransactionGroup(doc, "Create+Join Walls (M9-3)"))
            {
                tg.Start();
                foreach (MapRow mapRow in mapRows)
                {
                    phaseCurrent++;
                    UpdateProgressAndCheckCancel(
                        progress,
                        cancellationSource,
                        Loc.T("Progress.StageRecognizeGenerate"),
                        phaseCurrent,
                        phaseTotal,
                        Loc.T("Progress.DetailLayerFormat", mapRow == null ? string.Empty : mapRow.RawLayerName));
                    if (mapRow == null ||
                        string.IsNullOrWhiteSpace(mapRow.RawLayerName))
                    {
                        continue;
                    }

                    string idempotencyKey = WizardIdempotencyStoreService.BuildRowKey(
                        mapRow.RawLayerName,
                        mapRow.Category,
                        levelId,
                        dwgId);
                    if (enableIdempotencySkip && WizardIdempotencyStoreService.Contains(doc, idempotencyKey))
                    {
                        if (failureMessages.Count < 50)
                        {
                            failureMessages.Add("Skipped duplicate row: " + mapRow.RawLayerName + " [" + mapRow.Category + "]");
                        }

                        continue;
                    }

                    int rowCreatedBefore = createdCount;

                    if (mapRow.Category == MapCategory.Walls)
                    {
                        double rowMinLengthMm = ResolveWallRowMinLengthMm(mapRow, options.MinLengthMm);
                        List<CadSegment> filteredSegments = FilterSegmentsByLayerAndLength(scaled.Segments, mapRow.RawLayerName, rowMinLengthMm);
                        filteredSegmentCount += filteredSegments.Count;
                        List<string> layerStats = filteredSegments
                            .GroupBy(s => s.RawLayerName)
                            .Select(g => g.Key + ":" + g.Count())
                            .OrderByDescending(x => x)
                            .ToList();
                        DiagnosticRecorder.AppendDebug(
                            "[CreatePhase] RowLayer=" + mapRow.RawLayerName +
                            ", FilteredSegmentsLayerStats=" + string.Join(", ", layerStats) +
                            ", MinLengthMm=" + rowMinLengthMm.ToString("F1"));

                        HashSet<string> layerOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            mapRow.RawLayerName
                        };
                        WallType templateWallType = ResolveWallType(typeByName, mapRow.RevitTypeName);
                        if (templateWallType == null)
                        {
                            failureMessages.Add("No wall template type for row layer: " + mapRow.RawLayerName);
                            continue;
                        }

                        // Dynamic wall thickness for single-line recognition now comes only from row/global defaults.
                        WallRecognitionConfig cfg = WallRecognitionConfigProvider.Load();
                        double? expectedWidthMm =
                            (mapRow.Settings != null ? mapRow.Settings.WallDefaultSingleWallThicknessMm : null)
                            ?? cfg.DefaultSingleWallThicknessMm;
                        if (!expectedWidthMm.HasValue || expectedWidthMm.Value <= 0)
                        {
                            failureMessages.Add("Invalid wall thickness for row layer: " + mapRow.RawLayerName);
                            continue;
                        }

                        Stopwatch recognizeWatch = Stopwatch.StartNew();
                        List<CadSegment> lineSegments = filteredSegments.Where(x => x != null && !x.IsArc).ToList();
                        List<CadSegment> arcSegments = filteredSegments.Where(x => x != null && x.IsArc).ToList();
                        WallRecognitionResult recognize = WallRecognitionEngine.RecognizeWalls(lineSegments, layerOnly, mapRow.Settings, expectedWidthMm);
                        recognizeWatch.Stop();
                        profiling.RecognizeMs += recognizeWatch.ElapsedMilliseconds;
                        List<ArcWallCreateCandidate> arcWallCandidates = BuildArcWallCandidates(
                            arcSegments,
                            mapRow.Settings,
                            expectedWidthMm.Value);
                        double snapTolMm = options.SafeMode ? 5.0 : 3.0;
                        recognize.Centerlines = CleanCenterlineCandidates(recognize.Centerlines, snapTolMm, rowMinLengthMm);
                        arcWallCandidates = DedupArcCandidates(arcWallCandidates, snapTolMm, rowMinLengthMm);
                        recognize.TypeDArcWalls = arcWallCandidates.Count;
                        recognizeDetails.Add(recognize);
                        totalCenterlines += recognize.Centerlines.Count + arcWallCandidates.Count;
                        candidateDouble += recognize.TypeADoubleLineWalls;
                        candidateSingle += recognize.TypeBSingleLineWalls;

                        int startCount = createdWalls.Count;
                        Stopwatch createWatch = Stopwatch.StartNew();
                        bool isCurtainWall = templateWallType.Kind == WallKind.Curtain;
                        if (isCurtainWall)
                        {
                            DiagnosticRecorder.AppendDebug(
                                "[CurtainWallCreate] Layer=" + mapRow.RawLayerName +
                                ", Type=" + templateWallType.Name +
                                ", Centerlines=" + recognize.Centerlines.Count +
                                ", Arcs=" + arcWallCandidates.Count);
                            CreateWallsFromCenterlinesInBatches(
                                doc,
                                recognize.Centerlines,
                                templateWallType.Id,
                                level.Id,
                                mapRow.RawLayerName,
                                createdWalls,
                                mapRow.Settings,
                                batchSize,
                                progress,
                                cancellationSource,
                                profiling.CommitBatchMs,
                                ref createdCount,
                                failureMessages);
                            CreateArcWallsInBatches(
                                doc,
                                arcWallCandidates,
                                templateWallType.Id,
                                level.Id,
                                mapRow.RawLayerName,
                                createdWalls,
                                mapRow.Settings,
                                batchSize,
                                progress,
                                cancellationSource,
                                profiling.CommitBatchMs,
                                ref createdCount,
                                failureMessages);
                        }
                        else
                        {
                            Dictionary<int, ElementId> wallTypeIdsByThickness = PrepareTemplateWallTypeIds(
                                doc,
                                templateWallType,
                                recognize.Centerlines,
                                arcWallCandidates);
                            CreateWallsFromCenterlinesByThicknessGroups(
                                doc,
                                recognize.Centerlines,
                                wallTypeIdsByThickness,
                                level.Id,
                                mapRow.RawLayerName,
                                createdWalls,
                                mapRow.Settings,
                                batchSize,
                                progress,
                                cancellationSource,
                                profiling.CommitBatchMs,
                                ref createdCount,
                                failureMessages);
                            CreateArcWallsByThicknessGroups(
                                doc,
                                arcWallCandidates,
                                wallTypeIdsByThickness,
                                level.Id,
                                mapRow.RawLayerName,
                                createdWalls,
                                mapRow.Settings,
                                batchSize,
                                progress,
                                cancellationSource,
                                profiling.CommitBatchMs,
                                ref createdCount,
                                failureMessages);
                        }
                        createWatch.Stop();
                        profiling.CreateMs += createWatch.ElapsedMilliseconds;

                        if (mapRow.Settings != null && mapRow.Settings.ParameterMappings != null && mapRow.Settings.ParameterMappings.Count > 0)
                        {
                            ApplyParameterMappings(
                                createdWalls.Skip(startCount).Cast<Element>().ToList(),
                                mapRow.Settings.ParameterMappings,
                                levelIdByName,
                                failureMessages);
                        }
                    }
                    else if (mapRow.Category == MapCategory.Columns)
                    {
                        FamilySymbol columnSymbol = ResolveColumnSymbol(
                            structuralColumnSymbolByName,
                            architecturalColumnSymbolByName,
                            mapRow.RevitTypeName);
                        if (columnSymbol == null)
                        {
                            failureMessages.Add("No column type for row layer: " + mapRow.RawLayerName);
                            continue;
                        }

                        ColumnDetectionResult detect = ColumnCandidateDetector.DetectByRawLayer(
                            scaled.Segments,
                            mapRow.RawLayerName,
                            mapRow.Settings,
                            doc);
                        ColumnRecognitionDefaults columnSettings = ColumnRecognitionConfigProvider.ResolveForLayer(mapRow.RawLayerName, mapRow.Settings);
                        List<ColumnCandidate> candidates = detect.Candidates;
                        if (detect.RejectedCandidates.Count > 0)
                        {
                            DiagnosticRecorder.AppendDebug(
                                "[ColumnReject] Layer=" + mapRow.RawLayerName +
                                ", Rejected=" + detect.RejectedCandidates.Count);
                        }

                        if (!string.IsNullOrWhiteSpace(detect.ReportPath))
                        {
                            DiagnosticRecorder.AppendDebug(
                                "[ColumnReport] Layer=" + mapRow.RawLayerName +
                                ", Path=" + detect.ReportPath);
                        }
                        double columnHeightMm = ResolveColumnHeightMm(mapRow.Settings, LoadGlobalGenerationSettings(doc));
                        using (Transaction tx = new Transaction(doc, "Create Columns"))
                        {
                            tx.Start();
                            ConfigureFailureHandling(tx);
                            List<Element> placed = ColumnPlacementService.PlaceColumns(
                                doc,
                                candidates,
                                columnSymbol,
                                level,
                                columnHeightMm,
                                columnSettings == null ? null : columnSettings.Orientation);
                            createdCount += placed.Count;
                            if (mapRow.Settings != null && mapRow.Settings.ParameterMappings != null && mapRow.Settings.ParameterMappings.Count > 0)
                            {
                                ApplyParameterMappings(
                                    placed,
                                    mapRow.Settings.ParameterMappings,
                                    levelIdByName,
                                    failureMessages);
                            }

                            tx.Commit();
                        }
                    }
                    else if (mapRow.Category == MapCategory.Doors)
                    {
                        List<Wall> hostWalls = GetHostWallsForDoor(doc, createdWalls);
                        DoorDetectSettings doorDetectSettings = BuildDoorDetectSettings(mapRow);
                        DoorDetectResult detect = DoorCandidateDetector.DetectByRawLayerFromSegments(
                            scaled.Segments,
                            doorDetectSettings,
                            mapRow.RawLayerName,
                            hostWalls);
                        DiagnosticRecorder.AppendDebug(
                            "[DoorStats] Layer=" + mapRow.RawLayerName +
                            ", DoorSegments=" + detect.DoorSegmentsTotal +
                            ", ArcSegments=" + detect.ArcSegmentsTotal +
                            ", RuleR3=" + detect.Rule3Count +
                            ", RuleR1=" + detect.Rule1Count +
                            ", RuleR2=" + detect.Rule2Count +
                            ", Candidates=" + (detect.Candidates == null ? 0 : detect.Candidates.Count));
                        DoorCandidateLogWriter.Write(detect);
                        FamilySymbol doorSymbol = ResolveDoorSymbol(doorSymbolByName, mapRow.RevitTypeName);
                        if (doorSymbol == null)
                        {
                            failureMessages.Add("No door type for row layer: " + mapRow.RawLayerName);
                            continue;
                        }

                        VerticalDimensionSettings rowVertical = BuildVerticalByMapRow(mapRow, verticalSettings, globalSettings);
                        DoorCreateResult doorCreate = DoorCreatorService.CreateDoors(doc, detect.Candidates, doorSymbol, hostWalls, true, rowVertical, mapRow.Settings);
                        doorSummary.CreatedDoors += doorCreate.CreatedDoors;
                        doorSummary.SkippedDoors += doorCreate.SkippedDoors;
                        doorSummary.DoorCandidates += doorCreate.DoorCandidates;
                        doorSummary.WidthSetSuccessCount += doorCreate.WidthSetSuccessCount;
                        doorSummary.WidthSetFailedCount += doorCreate.WidthSetFailedCount;
                        doorSummary.HeightSetSuccessCount += doorCreate.HeightSetSuccessCount;
                        doorSummary.HeightSetFailedCount += doorCreate.HeightSetFailedCount;
                        if (doorCreate.CreatedElementIds != null && doorCreate.CreatedElementIds.Count > 0)
                        {
                            foreach (int id in doorCreate.CreatedElementIds)
                            {
                                if (id > 0 && !doorSummary.CreatedElementIds.Contains(id))
                                {
                                    doorSummary.CreatedElementIds.Add(id);
                                }
                            }
                        }

                        if (doorCreate.CreatedAuxWallElementIds != null && doorCreate.CreatedAuxWallElementIds.Count > 0)
                        {
                            foreach (int id in doorCreate.CreatedAuxWallElementIds)
                            {
                                if (id > 0 && !doorSummary.CreatedAuxWallElementIds.Contains(id))
                                {
                                    doorSummary.CreatedAuxWallElementIds.Add(id);
                                }
                            }
                        }

                        DiagnosticRecorder.AppendDebug(
                            "[DoorCreateStats] Layer=" + mapRow.RawLayerName +
                            ", Created=" + doorCreate.CreatedDoors +
                            ", Skipped=" + doorCreate.SkippedDoors +
                            ", TrackedIds=" + (doorCreate.CreatedElementIds == null ? 0 : doorCreate.CreatedElementIds.Count) +
                            ", TrackedAuxWalls=" + (doorCreate.CreatedAuxWallElementIds == null ? 0 : doorCreate.CreatedAuxWallElementIds.Count) +
                            ", WidthOk=" + doorCreate.WidthSetSuccessCount +
                            ", WidthFail=" + doorCreate.WidthSetFailedCount +
                            ", HeightOk=" + doorCreate.HeightSetSuccessCount +
                            ", HeightFail=" + doorCreate.HeightSetFailedCount);
                        createdCount += doorCreate.CreatedDoors;
                        foreach (string reason in doorCreate.SkipReasons)
                        {
                            if (failureMessages.Count < 30)
                            {
                                failureMessages.Add("Door: " + reason);
                            }
                        }
                    }
                    else if (mapRow.Category == MapCategory.Windows)
                    {
                        FamilySymbol windowSymbol = ResolveWindowSymbol(windowSymbolByName, mapRow.RevitTypeName);
                        if (windowSymbol == null)
                        {
                            failureMessages.Add("No window type for row layer: " + mapRow.RawLayerName);
                            continue;
                        }

                        WindowCreateSettings windowSettings = new WindowCreateSettings
                        {
                            DefaultSillHeightMm = verticalSettings?.WindowSillHeightMm ?? 900.0
                        };
                        List<WindowCandidate> windowCandidates = WindowCandidateBuilder.BuildByRawLayer(scaled.Segments, mapRow.RawLayerName, windowSettings);
                        List<Wall> hostWalls = GetHostWallsForDoor(doc, createdWalls);
                        WindowCreateResult windowCreate = WindowCreatorService.Create(
                            doc,
                            windowCandidates,
                            windowSettings,
                            BuildVerticalByMapRow(mapRow, verticalSettings, globalSettings),
                            windowSymbol,
                            hostWalls,
                            true);
                        windowSummary.CreatedCount += windowCreate.CreatedCount;
                        windowSummary.SkippedCount += windowCreate.SkippedCount;
                        windowSummary.TotalCandidates += windowCreate.TotalCandidates;
                        windowSummary.HeightSetSuccessCount += windowCreate.HeightSetSuccessCount;
                        windowSummary.HeightSetFailedCount += windowCreate.HeightSetFailedCount;
                        createdCount += windowCreate.CreatedCount;
                        DiagnosticRecorder.AppendDebug(
                            "[WindowCreateStats] Layer=" + mapRow.RawLayerName +
                            ", Created=" + windowCreate.CreatedCount +
                            ", Skipped=" + windowCreate.SkippedCount +
                            ", HeightOk=" + windowCreate.HeightSetSuccessCount +
                            ", HeightFail=" + windowCreate.HeightSetFailedCount);
                    }
                    else if (mapRow.Category == MapCategory.Beams)
                    {
                        FamilySymbol beamSymbol = ResolveBeamSymbol(beamSymbolByName, mapRow.RevitTypeName);
                        if (beamSymbol == null)
                        {
                            failureMessages.Add("No beam type for row layer: " + mapRow.RawLayerName);
                            continue;
                        }

                        int createdBeams = BeamCreatorService.CreateByRawLayer(
                            doc,
                            scaled.Segments,
                            mapRow.RawLayerName,
                            mapRow.Settings,
                            beamSymbol,
                            level,
                            failureMessages);
                        createdCount += createdBeams;
                    }

                    if (enableIdempotencySkip && createdCount > rowCreatedBefore)
                    {
                        WizardIdempotencyStoreService.MarkCreated(doc, idempotencyKey);
                    }
                }

                filterWatch.Stop();
                profiling.FilterMs = filterWatch.ElapsedMilliseconds;
                profiling.FilteredSegmentCount = filteredSegmentCount;
                profiling.CandidateDoubleLineCount = candidateDouble;
                profiling.CandidateSingleLineCount = candidateSingle;

                if (joinWallsAfterCreate)
                {
                    Stopwatch joinWatch = Stopwatch.StartNew();
                    UpdateProgressAndCheckCancel(
                        progress,
                        cancellationSource,
                        Loc.T("Progress.StageJoinPost"),
                        phaseTotal,
                        phaseTotal,
                        Loc.T("Progress.JoiningWalls"));
                    using (Transaction txJoin = new Transaction(doc, "Join Walls (M9-3)"))
                    {
                        txJoin.Start();
                        ConfigureFailureHandling(txJoin);
                        joinedCount = WallJoinService.JoinNearbyWalls(doc, createdWalls);
                        txJoin.Commit();
                    }

                    joinWatch.Stop();
                    profiling.JoinMs = joinWatch.ElapsedMilliseconds;
                }

                tg.Assimilate();
            }

            profiling.WallCreatedCount = createdWalls.Count;
            profiling.SkippedCount = failureMessages.Count;
            profiling.SkipReasons = failureMessages.ToList();
            totalWatch.Stop();
            profiling.TotalMs = totalWatch.ElapsedMilliseconds;
            if (progress != null)
            {
                progress.UpdateProgress(Loc.T("Progress.StageCompleted"), 100, 100, Loc.T("Progress.Completed"));
            }
        }


        private static void CreateWallsFromCenterlinesInBatches(
            Document doc,
            List<WallCenterlineCandidate> centerlines,
            ElementId wallTypeId,
            ElementId levelId,
            string expectedRawLayerName,
            List<Wall> createdWalls,
            AdvancedSettingsRow rowSettings,
            int batchSize,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            List<long> commitBatchMs,
            ref int createdCount,
            List<string> failureMessages)
        {
            if (centerlines == null || centerlines.Count == 0)
            {
                return;
            }

            double wallHeightMm = ResolveWallHeightMm(rowSettings, LoadGlobalGenerationSettings(doc));
            double baseOffsetMm = rowSettings != null && rowSettings.WallBaseOffsetMm.HasValue
                ? rowSettings.WallBaseOffsetMm.Value
                : 0.0;
            double heightFt = UnitUtils.ConvertToInternalUnits(wallHeightMm, UnitTypeId.Millimeters);
            double baseOffsetFt = UnitUtils.ConvertToInternalUnits(baseOffsetMm, UnitTypeId.Millimeters);
            int safeBatchSize = batchSize <= 0 ? 200 : batchSize;
            int batchIndex = 0;
            while (batchIndex < centerlines.Count)
            {
                UpdateProgressAndCheckCancel(
                    progress,
                    cancellationSource,
                    Loc.T("Progress.StageCreateWalls"),
                    batchIndex,
                    centerlines.Count,
                    Loc.T("Progress.BatchSizeFormat", safeBatchSize));
                int count = Math.Min(safeBatchSize, centerlines.Count - batchIndex);
                Stopwatch batchWatch = Stopwatch.StartNew();
                List<WallCenterlineCandidate> batch = centerlines.Skip(batchIndex).Take(count).ToList();
                CreateWallsChunkWithSplit(
                    doc,
                    batch,
                    wallTypeId,
                    levelId,
                    expectedRawLayerName,
                    rowSettings,
                    heightFt,
                    baseOffsetFt,
                    createdWalls,
                    progress,
                    cancellationSource,
                    ref createdCount,
                    failureMessages);

                batchWatch.Stop();
                if (commitBatchMs != null)
                {
                    commitBatchMs.Add(batchWatch.ElapsedMilliseconds);
                }

                batchIndex += count;
            }
        }

        private static void CreateWallsFromCenterlinesByThicknessGroups(
            Document doc,
            List<WallCenterlineCandidate> centerlines,
            Dictionary<int, ElementId> wallTypeIdsByThickness,
            ElementId levelId,
            string expectedRawLayerName,
            List<Wall> createdWalls,
            AdvancedSettingsRow rowSettings,
            int batchSize,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            List<long> commitBatchMs,
            ref int createdCount,
            List<string> failureMessages)
        {
            foreach (IGrouping<int, WallCenterlineCandidate> group in (centerlines ?? new List<WallCenterlineCandidate>())
                .Where(x => x != null)
                .GroupBy(x => DynamicWallTypeService.NormalizeThicknessMm(x.ThicknessMm))
                .OrderBy(x => x.Key))
            {
                ElementId wallTypeId;
                if (!wallTypeIdsByThickness.TryGetValue(group.Key, out wallTypeId))
                {
                    if (failureMessages.Count < 200)
                    {
                        failureMessages.Add("Missing dynamic wall type for thickness: " + group.Key + " mm");
                    }

                    continue;
                }

                CreateWallsFromCenterlinesInBatches(
                    doc,
                    group.ToList(),
                    wallTypeId,
                    levelId,
                    expectedRawLayerName,
                    createdWalls,
                    rowSettings,
                    batchSize,
                    progress,
                    cancellationSource,
                    commitBatchMs,
                    ref createdCount,
                    failureMessages);
            }
        }


        private static string ResolveSourceRawLayer(WallCenterlineCandidate candidate, string fallbackLayer)
        {
            if (candidate != null)
            {
                if (candidate.SideA != null && !string.IsNullOrWhiteSpace(candidate.SideA.RawLayerName))
                {
                    return candidate.SideA.RawLayerName;
                }

                if (candidate.SideB != null && !string.IsNullOrWhiteSpace(candidate.SideB.RawLayerName))
                {
                    return candidate.SideB.RawLayerName;
                }
            }

            return string.IsNullOrWhiteSpace(fallbackLayer) ? "(unknown)" : fallbackLayer;
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

        private static void CreateArcWallsInBatches(
            Document doc,
            List<ArcWallCreateCandidate> candidates,
            ElementId wallTypeId,
            ElementId levelId,
            string expectedRawLayerName,
            List<Wall> createdWalls,
            AdvancedSettingsRow rowSettings,
            int batchSize,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            List<long> commitBatchMs,
            ref int createdCount,
            List<string> failureMessages)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return;
            }

            double wallHeightMm = ResolveWallHeightMm(rowSettings, LoadGlobalGenerationSettings(doc));
            double baseOffsetMm = rowSettings != null && rowSettings.WallBaseOffsetMm.HasValue
                ? rowSettings.WallBaseOffsetMm.Value
                : 0.0;
            double heightFt = UnitUtils.ConvertToInternalUnits(wallHeightMm, UnitTypeId.Millimeters);
            double baseOffsetFt = UnitUtils.ConvertToInternalUnits(baseOffsetMm, UnitTypeId.Millimeters);

            int safeBatchSize = batchSize <= 0 ? 200 : batchSize;
            int batchIndex = 0;
            while (batchIndex < candidates.Count)
            {
                UpdateProgressAndCheckCancel(
                    progress,
                    cancellationSource,
                    Loc.T("Progress.StageCreateArcWalls"),
                    batchIndex,
                    candidates.Count,
                    Loc.T("Progress.BatchSizeFormat", safeBatchSize));
                int count = Math.Min(safeBatchSize, candidates.Count - batchIndex);
                Stopwatch batchWatch = Stopwatch.StartNew();
                List<ArcWallCreateCandidate> batch = candidates.Skip(batchIndex).Take(count).ToList();
                CreateArcWallsChunkWithSplit(
                    doc,
                    batch,
                    wallTypeId,
                    levelId,
                    expectedRawLayerName,
                    heightFt,
                    baseOffsetFt,
                    createdWalls,
                    progress,
                    cancellationSource,
                    ref createdCount,
                    failureMessages);

                batchWatch.Stop();
                if (commitBatchMs != null)
                {
                    commitBatchMs.Add(batchWatch.ElapsedMilliseconds);
                }

                batchIndex += count;
            }
        }

        private static void CreateArcWallsByThicknessGroups(
            Document doc,
            List<ArcWallCreateCandidate> candidates,
            Dictionary<int, ElementId> wallTypeIdsByThickness,
            ElementId levelId,
            string expectedRawLayerName,
            List<Wall> createdWalls,
            AdvancedSettingsRow rowSettings,
            int batchSize,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            List<long> commitBatchMs,
            ref int createdCount,
            List<string> failureMessages)
        {
            foreach (IGrouping<int, ArcWallCreateCandidate> group in (candidates ?? new List<ArcWallCreateCandidate>())
                .Where(x => x != null)
                .GroupBy(x => DynamicWallTypeService.NormalizeThicknessMm(x.ThicknessMm))
                .OrderBy(x => x.Key))
            {
                ElementId wallTypeId;
                if (!wallTypeIdsByThickness.TryGetValue(group.Key, out wallTypeId))
                {
                    if (failureMessages.Count < 200)
                    {
                        failureMessages.Add("Missing dynamic arc wall type for thickness: " + group.Key + " mm");
                    }

                    continue;
                }

                CreateArcWallsInBatches(
                    doc,
                    group.ToList(),
                    wallTypeId,
                    levelId,
                    expectedRawLayerName,
                    createdWalls,
                    rowSettings,
                    batchSize,
                    progress,
                    cancellationSource,
                    commitBatchMs,
                    ref createdCount,
                    failureMessages);
            }
        }

        private static Dictionary<int, ElementId> PrepareTemplateWallTypeIds(
            Document doc,
            WallType templateWallType,
            List<WallCenterlineCandidate> centerlines,
            List<ArcWallCreateCandidate> arcWallCandidates)
        {
            HashSet<int> normalizedThicknesses = new HashSet<int>();
            foreach (WallCenterlineCandidate centerline in centerlines ?? new List<WallCenterlineCandidate>())
            {
                if (centerline != null)
                {
                    normalizedThicknesses.Add(DynamicWallTypeService.NormalizeThicknessMm(centerline.ThicknessMm));
                }
            }

            foreach (ArcWallCreateCandidate candidate in arcWallCandidates ?? new List<ArcWallCreateCandidate>())
            {
                if (candidate != null)
                {
                    normalizedThicknesses.Add(DynamicWallTypeService.NormalizeThicknessMm(candidate.ThicknessMm));
                }
            }

            Dictionary<int, ElementId> result = new Dictionary<int, ElementId>();
            if (normalizedThicknesses.Count == 0)
            {
                return result;
            }

            using (Transaction tx = new Transaction(doc, "Prepare Dynamic Wall Types"))
            {
                tx.Start();
                foreach (int normalizedThicknessMm in normalizedThicknesses.OrderBy(x => x))
                {
                    string dynamicTypeName;
                    WallType wallType = DynamicWallTypeService.GetOrCreateTemplateThicknessWallType(
                        doc,
                        templateWallType,
                        normalizedThicknessMm,
                        out dynamicTypeName);
                    result[normalizedThicknessMm] = wallType.Id;
                }

                tx.Commit();
            }

            return result;
        }

        private static WallType ResolveWallType(Dictionary<string, WallType> typeByName, string typeName)
        {
            if (typeByName == null || typeByName.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(typeName))
            {
                WallType mapped;
                if (typeByName.TryGetValue(typeName, out mapped))
                {
                    return mapped;
                }
            }

            return null;
        }

        private static void CreateWallsChunkWithSplit(
            Document doc,
            List<WallCenterlineCandidate> chunk,
            ElementId wallTypeId,
            ElementId levelId,
            string expectedRawLayerName,
            AdvancedSettingsRow rowSettings,
            double heightFt,
            double baseOffsetFt,
            List<Wall> createdWalls,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            ref int createdCount,
            List<string> failureMessages)
        {
            if (chunk == null || chunk.Count == 0)
            {
                return;
            }

            if (TryCreateWallsChunk(doc, chunk, wallTypeId, levelId, expectedRawLayerName, rowSettings, heightFt, baseOffsetFt, createdWalls, ref createdCount, failureMessages))
            {
                return;
            }

            if (chunk.Count == 1)
            {
                WallCenterlineCandidate bad = chunk[0];
                string reason = "Skip bad wall curve.";
                if (bad != null && bad.CenterLine != null)
                {
                    reason = "Skip bad wall curve, lenFt=" + bad.CenterLine.Length.ToString("F4");
                }

                if (failureMessages.Count < 200)
                {
                    failureMessages.Add(reason);
                }

                return;
            }

            int mid = chunk.Count / 2;
            List<WallCenterlineCandidate> left = chunk.Take(mid).ToList();
            List<WallCenterlineCandidate> right = chunk.Skip(mid).ToList();
            UpdateProgressAndCheckCancel(
                progress,
                cancellationSource,
                Loc.T("Progress.StageRetrySplit"),
                0,
                1,
                Loc.T("Progress.SplitFailedWallBatch"));
            CreateWallsChunkWithSplit(doc, left, wallTypeId, levelId, expectedRawLayerName, rowSettings, heightFt, baseOffsetFt, createdWalls, progress, cancellationSource, ref createdCount, failureMessages);
            CreateWallsChunkWithSplit(doc, right, wallTypeId, levelId, expectedRawLayerName, rowSettings, heightFt, baseOffsetFt, createdWalls, progress, cancellationSource, ref createdCount, failureMessages);
        }

        private static bool TryCreateWallsChunk(
            Document doc,
            List<WallCenterlineCandidate> chunk,
            ElementId wallTypeId,
            ElementId levelId,
            string expectedRawLayerName,
            AdvancedSettingsRow rowSettings,
            double heightFt,
            double baseOffsetFt,
            List<Wall> createdWalls,
            ref int createdCount,
            List<string> failureMessages)
        {
            List<Wall> localCreated = new List<Wall>();
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Walls Chunk"))
                {
                    tx.Start();
                    ConfigureFailureHandling(tx);
                    foreach (WallCenterlineCandidate c in chunk)
                    {
                        if (c == null || c.CenterLine == null || c.CenterLine.Length <= 1e-6)
                        {
                            continue;
                        }

                        Wall wall = Wall.Create(doc, c.CenterLine, wallTypeId, levelId, heightFt, baseOffsetFt, false, false);
                        ApplySingleWallPlacementMode(wall, c, rowSettings);
                        DisallowWallAutoJoin(wall);
                        localCreated.Add(wall);
                        XYZ p0 = c.CenterLine.GetEndPoint(0);
                        XYZ p1 = c.CenterLine.GetEndPoint(1);
                        XYZ mid = new XYZ((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5, (p0.Z + p1.Z) * 0.5);
                        string sourceLayer = ResolveSourceRawLayer(c, expectedRawLayerName);
                        DiagnosticRecorder.AppendDebug(
                            "[WallCreated] Id=" + wall.Id.IntegerValue +
                            ", Mid=(" + mid.X.ToString("F4") + "," + mid.Y.ToString("F4") + "," + mid.Z.ToString("F4") + ")" +
                            ", SourceRawLayer=" + sourceLayer +
                            ", ThicknessMm=" + c.ThicknessMm.ToString("F2"));
                    }

                    TransactionStatus status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[WallChunkRollback] " + ex.Message);
                return false;
            }

            createdWalls.AddRange(localCreated.Where(x => x != null && x.IsValidObject));
            createdCount += localCreated.Count;
            return true;
        }

        private static void CreateArcWallsChunkWithSplit(
            Document doc,
            List<ArcWallCreateCandidate> chunk,
            ElementId wallTypeId,
            ElementId levelId,
            string expectedRawLayerName,
            double heightFt,
            double baseOffsetFt,
            List<Wall> createdWalls,
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            ref int createdCount,
            List<string> failureMessages)
        {
            if (chunk == null || chunk.Count == 0)
            {
                return;
            }

            if (TryCreateArcWallsChunk(doc, chunk, wallTypeId, levelId, expectedRawLayerName, heightFt, baseOffsetFt, createdWalls, ref createdCount))
            {
                return;
            }

            if (chunk.Count == 1)
            {
                if (failureMessages.Count < 200)
                {
                    failureMessages.Add("Skip bad arc wall curve.");
                }

                return;
            }

            int mid = chunk.Count / 2;
            List<ArcWallCreateCandidate> left = chunk.Take(mid).ToList();
            List<ArcWallCreateCandidate> right = chunk.Skip(mid).ToList();
            UpdateProgressAndCheckCancel(
                progress,
                cancellationSource,
                Loc.T("Progress.StageRetrySplit"),
                0,
                1,
                Loc.T("Progress.SplitFailedArcWallBatch"));
            CreateArcWallsChunkWithSplit(doc, left, wallTypeId, levelId, expectedRawLayerName, heightFt, baseOffsetFt, createdWalls, progress, cancellationSource, ref createdCount, failureMessages);
            CreateArcWallsChunkWithSplit(doc, right, wallTypeId, levelId, expectedRawLayerName, heightFt, baseOffsetFt, createdWalls, progress, cancellationSource, ref createdCount, failureMessages);
        }

        private static bool TryCreateArcWallsChunk(
            Document doc,
            List<ArcWallCreateCandidate> chunk,
            ElementId wallTypeId,
            ElementId levelId,
            string expectedRawLayerName,
            double heightFt,
            double baseOffsetFt,
            List<Wall> createdWalls,
            ref int createdCount)
        {
            List<Wall> localCreated = new List<Wall>();
            try
            {
                using (Transaction tx = new Transaction(doc, "Create Arc Walls Chunk"))
                {
                    tx.Start();
                    ConfigureFailureHandling(tx);
                    foreach (ArcWallCreateCandidate c in chunk)
                    {
                        if (c == null || c.Curve == null || c.Curve.Length <= 1e-6)
                        {
                            continue;
                        }

                        Wall wall = Wall.Create(doc, c.Curve, wallTypeId, levelId, heightFt, baseOffsetFt, false, false);
                        DisallowWallAutoJoin(wall);
                        localCreated.Add(wall);
                        DiagnosticRecorder.AppendDebug(
                            "[ArcWallCreated] Id=" + wall.Id.IntegerValue +
                            ", SourceRawLayer=" + expectedRawLayerName +
                            ", ThicknessMm=" + c.ThicknessMm.ToString("F2") +
                            ", LengthFt=" + c.Curve.Length.ToString("F4"));
                    }

                    TransactionStatus status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[ArcWallChunkRollback] " + ex.Message);
                return false;
            }

            createdWalls.AddRange(localCreated.Where(x => x != null && x.IsValidObject));
            createdCount += localCreated.Count;
            return true;
        }

        private static void UpdateProgressAndCheckCancel(
            IGenerationProgressReporter progress,
            CancellationTokenSource cancellationSource,
            string stage,
            int current,
            int total,
            string detail)
        {
            if (progress != null)
            {
                progress.UpdateProgress(stage, current, total, detail);
                if (progress.IsCancellationRequested && cancellationSource != null && !cancellationSource.IsCancellationRequested)
                {
                    cancellationSource.Cancel();
                }
            }

            if (cancellationSource != null && cancellationSource.IsCancellationRequested)
            {
                throw new OperationCanceledException();
            }
        }

        private static void ConfigureFailureHandling(Transaction tx)
        {
            if (tx == null)
            {
                return;
            }

            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new WallBatchFailuresPreprocessor());
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
        }

        private static void ConfigureNonCriticalFailureHandling(Transaction tx, string scope)
        {
            if (tx == null)
            {
                return;
            }

            FailureHandlingOptions options = tx.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new NonCriticalWarningsPreprocessor(scope));
            options.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(options);
        }

        private static double ResolvePreflightMinLengthMm(List<MapRow> mapRows, double fallbackMinLengthMm)
        {
            List<double> configured = (mapRows ?? new List<MapRow>())
                .Where(x => x != null && x.Category == MapCategory.Walls && x.Settings != null)
                .Select(x => x.Settings.WallMinWallLengthMm)
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x.Value)
                .ToList();
            if (configured.Count == 0)
            {
                return fallbackMinLengthMm;
            }

            return configured.Min();
        }

        private static double ResolveWallRowMinLengthMm(MapRow mapRow, double fallbackMinLengthMm)
        {
            if (mapRow != null &&
                mapRow.Settings != null &&
                mapRow.Settings.WallMinWallLengthMm.HasValue &&
                mapRow.Settings.WallMinWallLengthMm.Value > 0)
            {
                return mapRow.Settings.WallMinWallLengthMm.Value;
            }

            return fallbackMinLengthMm;
        }

        private static List<WallCenterlineCandidate> CleanCenterlineCandidates(
            List<WallCenterlineCandidate> source,
            double snapTolMm,
            double minLenMm)
        {
            double snapTolFt = snapTolMm / 304.8;
            double minLenFt = minLenMm / 304.8;
            List<WallCenterlineCandidate> result = new List<WallCenterlineCandidate>();
            Dictionary<string, WallCenterlineCandidate> dedup = new Dictionary<string, WallCenterlineCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (WallCenterlineCandidate item in source ?? new List<WallCenterlineCandidate>())
            {
                if (item == null || item.CenterLine == null || item.CenterLine.Length < minLenFt)
                {
                    continue;
                }

                XYZ p0 = SnapPoint2D(item.CenterLine.GetEndPoint(0), snapTolFt);
                XYZ p1 = SnapPoint2D(item.CenterLine.GetEndPoint(1), snapTolFt);
                if (p0.DistanceTo(p1) < minLenFt)
                {
                    continue;
                }

                Line normalized = Line.CreateBound(p0, p1);
                string key = BuildLineDedupKey(normalized, snapTolFt);
                if (!dedup.ContainsKey(key))
                {
                    WallCenterlineCandidate clone = new WallCenterlineCandidate
                    {
                        CenterLine = normalized,
                        ThicknessMm = item.ThicknessMm,
                        SideA = item.SideA,
                        SideB = item.SideB,
                        OverlapLengthMm = item.OverlapLengthMm
                    };
                    dedup[key] = clone;
                    result.Add(clone);
                }
            }

            return result;
        }

        private static List<ArcWallCreateCandidate> DedupArcCandidates(
            List<ArcWallCreateCandidate> source,
            double snapTolMm,
            double minLenMm)
        {
            double snapTolFt = snapTolMm / 304.8;
            double minLenFt = minLenMm / 304.8;
            List<ArcWallCreateCandidate> result = new List<ArcWallCreateCandidate>();
            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ArcWallCreateCandidate item in source ?? new List<ArcWallCreateCandidate>())
            {
                if (item == null || item.Curve == null || item.Curve.Length < minLenFt)
                {
                    continue;
                }

                string key = BuildCurveDedupKey(item.Curve, snapTolFt);
                if (keys.Add(key))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        private static string BuildLineDedupKey(Line line, double tolFt)
        {
            XYZ a = line.GetEndPoint(0);
            XYZ b = line.GetEndPoint(1);
            XYZ p0 = SortPoint(a, b) ? a : b;
            XYZ p1 = SortPoint(a, b) ? b : a;
            return QuantizePoint(p0, tolFt) + "|" + QuantizePoint(p1, tolFt);
        }

        private static string BuildCurveDedupKey(Curve curve, double tolFt)
        {
            XYZ p0 = curve.GetEndPoint(0);
            XYZ p1 = curve.GetEndPoint(1);
            if (!SortPoint(p0, p1))
            {
                XYZ t = p0;
                p0 = p1;
                p1 = t;
            }

            return curve.GetType().Name + "|" + QuantizePoint(p0, tolFt) + "|" + QuantizePoint(p1, tolFt) + "|" + Math.Round(curve.Length / Math.Max(tolFt, 1e-6), 0);
        }

        private static bool SortPoint(XYZ a, XYZ b)
        {
            if (Math.Abs(a.X - b.X) > 1e-9)
            {
                return a.X < b.X;
            }

            if (Math.Abs(a.Y - b.Y) > 1e-9)
            {
                return a.Y < b.Y;
            }

            return a.Z < b.Z;
        }

        private static XYZ SnapPoint2D(XYZ p, double tolFt)
        {
            if (p == null)
            {
                return null;
            }

            double qx = Math.Round(p.X / Math.Max(tolFt, 1e-9)) * tolFt;
            double qy = Math.Round(p.Y / Math.Max(tolFt, 1e-9)) * tolFt;
            return new XYZ(qx, qy, p.Z);
        }

        private static string QuantizePoint(XYZ p, double tolFt)
        {
            double qx = Math.Round(p.X / Math.Max(tolFt, 1e-9));
            double qy = Math.Round(p.Y / Math.Max(tolFt, 1e-9));
            double qz = Math.Round(p.Z / Math.Max(tolFt, 1e-9));
            return qx + "," + qy + "," + qz;
        }

        private static List<CadSegment> FilterSegmentsByLayerAndLength(
            List<CadSegment> source,
            string rawLayerName,
            double minLengthMm)
        {
            double minLengthFt = minLengthMm / 304.8;
            return (source ?? new List<CadSegment>())
                .Where(s => s != null &&
                            !string.IsNullOrWhiteSpace(s.RawLayerName) &&
                            string.Equals(s.RawLayerName, rawLayerName, StringComparison.OrdinalIgnoreCase) &&
                            GetSegmentLengthFt(s) >= minLengthFt)
                .ToList();
        }

        private static double GetSegmentLengthFt(CadSegment segment)
        {
            if (segment == null)
            {
                return 0.0;
            }

            if (segment.IsArc && segment.RadiusFeet > 1e-9 && Math.Abs(segment.SweepAngleRad) > 1e-9)
            {
                return Math.Abs(segment.RadiusFeet * segment.SweepAngleRad);
            }

            if (segment.P0 != null && segment.P1 != null)
            {
                return segment.P0.DistanceTo(segment.P1);
            }

            return 0.0;
        }

        private static CadDataset FilterDatasetByViewRange(CadDataset source, View activeView)
        {
            if (source == null || activeView == null || !activeView.CropBoxActive || activeView.CropBox == null)
            {
                return source;
            }

            BoundingBoxXYZ box = activeView.CropBox;
            XYZ min = box.Min;
            XYZ max = box.Max;
            Transform tf = box.Transform ?? Transform.Identity;
            List<CadSegment> filtered = (source.Segments ?? new List<CadSegment>())
                .Where(s => s != null && s.P0 != null && s.P1 != null)
                .Where(s =>
                {
                    XYZ mid = new XYZ((s.P0.X + s.P1.X) * 0.5, (s.P0.Y + s.P1.Y) * 0.5, (s.P0.Z + s.P1.Z) * 0.5);
                    XYZ local = tf.Inverse.OfPoint(mid);
                    return local.X >= min.X && local.X <= max.X &&
                           local.Y >= min.Y && local.Y <= max.Y &&
                           local.Z >= min.Z && local.Z <= max.Z;
                })
                .ToList();

            return new CadDataset
            {
                Segments = filtered,
                SegmentsByRawLayer = filtered
                    .Where(s => !string.IsNullOrWhiteSpace(s.RawLayerName))
                    .GroupBy(s => s.RawLayerName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase)
            };
        }

        private static PreflightDecision ShowPreflightDialog(PreflightReport report, GenerationGuardConfig guard)
        {
            TaskDialog td = new TaskDialog("M11.3 Preflight");
            td.MainInstruction = "Wall generation preflight report";
            td.MainContent =
                "RawSegmentCount: " + report.RawSegmentCount + "\n" +
                "AfterMinLengthCount: " + report.AfterMinLengthCount + "\n" +
                "Length(mm) min/p50/p90/max: " +
                report.MinLengthMm.ToString("F1") + " / " +
                report.P50LengthMm.ToString("F1") + " / " +
                report.P90LengthMm.ToString("F1") + " / " +
                report.MaxLengthMm.ToString("F1") + "\n" +
                "Extents(mm) W x H: " +
                report.ExtentWidthMm.ToString("F0") + " x " + report.ExtentHeightMm.ToString("F0") + "\n" +
                "EstimatedWorkload: " + report.Workload + "\n" +
                "EstimatedWalls: " + report.EstimatedWallCount + "\n\n" +
                "Thresholds: Preview>" + guard.MaxSegmentsPreview +
                ", HardStop>" + guard.MaxSegmentsHardStop +
                ", MaxEstimatedWalls>" + guard.MaxEstimatedWalls;
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Continue (not recommended)");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Adjust filter parameter");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Only process active view range");
            td.CommonButtons = TaskDialogCommonButtons.Cancel;
            TaskDialogResult result = td.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                return PreflightDecision.Continue;
            }

            if (result == TaskDialogResult.CommandLink2)
            {
                return PreflightDecision.AdjustFilter;
            }

            if (result == TaskDialogResult.CommandLink3)
            {
                return PreflightDecision.ViewRangeOnly;
            }

            return PreflightDecision.Cancel;
        }

        private static List<ArcWallCreateCandidate> BuildArcWallCandidates(
            List<CadSegment> arcSegments,
            AdvancedSettingsRow rowSettings,
            double defaultSingleThicknessMm)
        {
            List<ArcWallCreateCandidate> result = new List<ArcWallCreateCandidate>();
            if (arcSegments == null || arcSegments.Count == 0)
            {
                return result;
            }

            double ftToMm = 304.8;
            double minThicknessMm = 60.0;
            double maxThicknessMm = rowSettings != null && rowSettings.WallMaxWallThicknessMm.HasValue && rowSettings.WallMaxWallThicknessMm.Value > 0
                ? rowSettings.WallMaxWallThicknessMm.Value
                : 600.0;
            double centerTolMm = rowSettings != null && rowSettings.WallEndpointMergeTolMm.HasValue && rowSettings.WallEndpointMergeTolMm.Value > 0
                ? rowSettings.WallEndpointMergeTolMm.Value
                : 5.0;
            double arcThicknessTolMm = rowSettings != null && rowSettings.WallArcThicknessTolMm.HasValue && rowSettings.WallArcThicknessTolMm.Value > 0
                ? rowSettings.WallArcThicknessTolMm.Value
                : 20.0;
            bool[] used = new bool[arcSegments.Count];
            int pairedCount = 0;
            int singleCount = 0;

            for (int i = 0; i < arcSegments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                CadSegment a = arcSegments[i];
                if (a == null || a.Center == null || a.MidPoint == null || a.P0 == null || a.P1 == null)
                {
                    continue;
                }

                int matched = -1;
                for (int j = i + 1; j < arcSegments.Count; j++)
                {
                    if (used[j])
                    {
                        continue;
                    }

                    CadSegment b = arcSegments[j];
                    if (!CanArcPair(a, b, centerTolMm / ftToMm, minThicknessMm / ftToMm, maxThicknessMm / ftToMm, arcThicknessTolMm / ftToMm))
                    {
                        continue;
                    }

                    matched = j;
                    break;
                }

                if (matched >= 0)
                {
                    CadSegment b = arcSegments[matched];
                    Curve centerCurve = TryBuildCenterArcCurve(a, b);
                    double thicknessMm = Math.Abs(a.RadiusFeet - b.RadiusFeet) * ftToMm;
                    if (centerCurve != null && centerCurve.Length > 1e-6)
                    {
                        result.Add(new ArcWallCreateCandidate
                        {
                            Curve = centerCurve,
                            ThicknessMm = thicknessMm
                        });
                        pairedCount++;
                        used[i] = true;
                        used[matched] = true;
                    }
                }
            }

            for (int i = 0; i < arcSegments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                CadSegment a = arcSegments[i];
                Curve curve = TryBuildArcCurve(a);
                if (curve == null || curve.Length <= 1e-6)
                {
                    continue;
                }

                result.Add(new ArcWallCreateCandidate
                {
                    Curve = curve,
                    ThicknessMm = defaultSingleThicknessMm
                });
                singleCount++;
            }

            DiagnosticRecorder.AppendDebug(
                "[ArcWallCandidates] TotalArcSegments=" + arcSegments.Count +
                ", Paired=" + pairedCount +
                ", Single=" + singleCount +
                ", OutputCurves=" + result.Count);

            return result;
        }

        private static bool CanArcPair(
            CadSegment a,
            CadSegment b,
            double centerTolFt,
            double minThicknessFt,
            double maxThicknessFt,
            double arcThicknessTolFt)
        {
            if (a == null || b == null || a.Center == null || b.Center == null)
            {
                return false;
            }

            if (a.Center.DistanceTo(b.Center) > centerTolFt)
            {
                return false;
            }

            double thicknessFt = Math.Abs(a.RadiusFeet - b.RadiusFeet);
            if (thicknessFt < minThicknessFt || thicknessFt > maxThicknessFt)
            {
                return false;
            }

            double sweepA = Math.Abs(a.SweepAngleRad);
            double sweepB = Math.Abs(b.SweepAngleRad);
            double deltaSweep = Math.Abs(sweepA - sweepB);
            if (deltaSweep > Math.Max(arcThicknessTolFt, 0.1))
            {
                return false;
            }

            return true;
        }

        private static Curve TryBuildCenterArcCurve(CadSegment a, CadSegment b)
        {
            if (a == null || b == null || a.P0 == null || a.P1 == null || a.MidPoint == null || b.P0 == null || b.P1 == null || b.MidPoint == null)
            {
                return null;
            }

            bool sameDir = a.P0.DistanceTo(b.P0) + a.P1.DistanceTo(b.P1) <= a.P0.DistanceTo(b.P1) + a.P1.DistanceTo(b.P0);
            XYZ bp0 = sameDir ? b.P0 : b.P1;
            XYZ bp1 = sameDir ? b.P1 : b.P0;
            XYZ c0 = MidPoint(a.P0, bp0);
            XYZ c1 = MidPoint(a.P1, bp1);
            XYZ cm = MidPoint(a.MidPoint, b.MidPoint);

            try
            {
                Arc arc = Arc.Create(c0, c1, cm);
                if (arc != null && arc.Length > 1e-6)
                {
                    return arc;
                }
            }
            catch
            {
            }

            try
            {
                return Line.CreateBound(c0, c1);
            }
            catch
            {
                return null;
            }
        }

        private static Curve TryBuildArcCurve(CadSegment segment)
        {
            if (segment == null || segment.P0 == null || segment.P1 == null || segment.MidPoint == null)
            {
                return null;
            }

            try
            {
                Arc arc = Arc.Create(segment.P0, segment.P1, segment.MidPoint);
                if (arc != null && arc.Length > 1e-6)
                {
                    return arc;
                }
            }
            catch
            {
            }

            try
            {
                return Line.CreateBound(segment.P0, segment.P1);
            }
            catch
            {
                return null;
            }
        }

        private static XYZ MidPoint(XYZ a, XYZ b)
        {
            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static void DisallowWallAutoJoin(Wall wall)
        {
            if (wall == null)
            {
                return;
            }

            try
            {
                WallUtils.DisallowWallJoinAtEnd(wall, 0);
                WallUtils.DisallowWallJoinAtEnd(wall, 1);
            }
            catch
            {
            }
        }


        private static void ApplyParameterMappings(
            List<Element> elements,
            List<ParameterMapping> mappings,
            Dictionary<string, ElementId> levelIdByName,
            List<string> failureMessages)
        {
            if (elements == null || mappings == null || mappings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < elements.Count; i++)
            {
                Element element = elements[i];
                if (element == null)
                {
                    continue;
                }

                foreach (ParameterMapping mapping in mappings)
                {
                    if (mapping == null || string.IsNullOrWhiteSpace(mapping.ParameterName))
                    {
                        continue;
                    }

                    try
                    {
                        Parameter parameter = element.LookupParameter(mapping.ParameterName);
                        if (parameter == null || parameter.IsReadOnly)
                        {
                            continue;
                        }

                        SetParameterValue(parameter, mapping, levelIdByName);
                    }
                    catch (Exception ex)
                    {
                        if (failureMessages.Count < 30)
                        {
                            failureMessages.Add("Parameter mapping failed: " + mapping.ParameterName + " - " + ex.Message);
                        }
                    }
                }
            }
        }


        private static void SetParameterValue(
            Parameter parameter,
            ParameterMapping mapping,
            Dictionary<string, ElementId> levelIdByName)
        {
            string textValue = mapping.Value == null ? string.Empty : mapping.Value.ToString();
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    parameter.Set(textValue ?? string.Empty);
                    break;
                case StorageType.Integer:
                    int intValue;
                    if (bool.TryParse(textValue, out bool boolValue))
                    {
                        parameter.Set(boolValue ? 1 : 0);
                        return;
                    }

                    if (int.TryParse(textValue, out intValue))
                    {
                        parameter.Set(intValue);
                    }
                    break;
                case StorageType.Double:
                    double doubleValue;
                    if (double.TryParse(textValue, out doubleValue))
                    {
                        parameter.Set(doubleValue);
                    }
                    break;
                case StorageType.ElementId:
                    if (!string.IsNullOrWhiteSpace(textValue))
                    {
                        ElementId id;
                        if (levelIdByName != null && levelIdByName.TryGetValue(textValue, out id))
                        {
                            parameter.Set(id);
                            return;
                        }

                        int idInt;
                        if (int.TryParse(textValue, out idInt))
                        {
                            parameter.Set(new ElementId(idInt));
                        }
                    }
                    break;
                default:
                    break;
            }
        }



        private static FamilySymbol ResolveDoorSymbol(Dictionary<string, FamilySymbol> symbolByName, string typeName)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                FamilySymbol mapped;
                if (symbolByName.TryGetValue(typeName, out mapped))
                {
                    return mapped;
                }
            }

            return symbolByName.Count > 0 ? symbolByName.Values.First() : null;
        }


        private static FamilySymbol ResolveWindowSymbol(Dictionary<string, FamilySymbol> symbolByName, string typeName)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                FamilySymbol mapped;
                if (symbolByName.TryGetValue(typeName, out mapped))
                {
                    return mapped;
                }
            }

            return symbolByName.Count > 0 ? symbolByName.Values.First() : null;
        }


        private static FamilySymbol ResolveBeamSymbol(Dictionary<string, FamilySymbol> symbolByName, string typeName)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                FamilySymbol mapped;
                if (symbolByName.TryGetValue(typeName, out mapped))
                {
                    return mapped;
                }
            }

            return symbolByName.Count > 0 ? symbolByName.Values.First() : null;
        }


        private static FamilySymbol ResolveColumnSymbol(
            Dictionary<string, FamilySymbol> structuralSymbolByName,
            Dictionary<string, FamilySymbol> architecturalSymbolByName,
            string typeName)
        {
            structuralSymbolByName = structuralSymbolByName ?? new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
            architecturalSymbolByName = architecturalSymbolByName ?? new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);

            string normalizedTypeName = NormalizeColumnTypeName(typeName);
            bool preferArchitectural = !string.IsNullOrWhiteSpace(typeName) &&
                                       typeName.StartsWith(ArchitecturalColumnPrefix, StringComparison.OrdinalIgnoreCase);
            if (preferArchitectural)
            {
                FamilySymbol archMapped;
                if (!string.IsNullOrWhiteSpace(normalizedTypeName) &&
                    architecturalSymbolByName.TryGetValue(normalizedTypeName, out archMapped))
                {
                    return archMapped;
                }

                FamilySymbol structuralFallback;
                if (!string.IsNullOrWhiteSpace(normalizedTypeName) &&
                    structuralSymbolByName.TryGetValue(normalizedTypeName, out structuralFallback))
                {
                    return structuralFallback;
                }
            }
            else
            {
                FamilySymbol structuralMapped;
                if (!string.IsNullOrWhiteSpace(normalizedTypeName) &&
                    structuralSymbolByName.TryGetValue(normalizedTypeName, out structuralMapped))
                {
                    return structuralMapped;
                }

                FamilySymbol architecturalFallback;
                if (!string.IsNullOrWhiteSpace(normalizedTypeName) &&
                    architecturalSymbolByName.TryGetValue(normalizedTypeName, out architecturalFallback))
                {
                    return architecturalFallback;
                }
            }

            return structuralSymbolByName.Count > 0
                ? structuralSymbolByName.Values.First()
                : (architecturalSymbolByName.Count > 0 ? architecturalSymbolByName.Values.First() : null);
        }


        private static VerticalDimensionSettings BuildVerticalByMapRow(MapRow mapRow, VerticalDimensionSettings fallback, GlobalGenerationSettings globalSettings)
        {
            VerticalDimensionSettings global = fallback ?? new VerticalDimensionSettings();
            GlobalGenerationSettings projectGlobal = GlobalGenerationSettings.Clone(globalSettings);
            AdvancedSettingsRow s = mapRow != null ? mapRow.Settings : null;
            return new VerticalDimensionSettings
            {
                WallHeightMm = projectGlobal.UseGlobalWallHeightOverride && projectGlobal.GlobalWallHeightMm > 0
                    ? projectGlobal.GlobalWallHeightMm
                    : (s != null && s.WallHeightMm.HasValue && s.WallHeightMm.Value > 0 ? s.WallHeightMm.Value : global.WallHeightMm),
                WallBaseOffsetMm = s != null && s.WallBaseOffsetMm.HasValue ? s.WallBaseOffsetMm.Value : global.WallBaseOffsetMm,
                DoorHeightMm = projectGlobal.UseGlobalDoorHeightOverride && projectGlobal.GlobalDoorHeightMm > 0
                    ? projectGlobal.GlobalDoorHeightMm
                    : (s != null && s.DoorHeightMm.HasValue && s.DoorHeightMm.Value > 0 ? s.DoorHeightMm.Value : global.DoorHeightMm),
                DoorSillHeightMm = projectGlobal.UseGlobalDoorSillHeightOverride && projectGlobal.GlobalDoorSillHeightMm >= 0
                    ? projectGlobal.GlobalDoorSillHeightMm
                    : (s != null && s.DoorSillHeightMm.HasValue && s.DoorSillHeightMm.Value >= 0 ? s.DoorSillHeightMm.Value : global.DoorSillHeightMm),
                WindowHeightMm = s != null && s.WindowHeightMm.HasValue && s.WindowHeightMm.Value > 0 ? s.WindowHeightMm.Value : global.WindowHeightMm,
                WindowSillHeightMm = s != null && s.WindowSillHeightMm.HasValue && s.WindowSillHeightMm.Value >= 0 ? s.WindowSillHeightMm.Value : global.WindowSillHeightMm,
                WindowHeadHeightMm = global.WindowHeadHeightMm,
                PreferSillPlusHeight = s != null && s.WindowUseSillPlusHeight.HasValue ? s.WindowUseSillPlusHeight.Value : global.PreferSillPlusHeight
            };
        }

        private static GlobalGenerationSettings LoadGlobalGenerationSettings(Document doc)
        {
            LayerOverrideStoreData store = LayerOverrideStoreService.Load(doc);
            if (store == null)
            {
                return GlobalGenerationSettings.CreateDefault();
            }

            return GlobalGenerationSettings.Clone(store.GlobalGenerationSettings);
        }

        private static double ResolveWallHeightMm(AdvancedSettingsRow rowSettings, GlobalGenerationSettings globalSettings)
        {
            GlobalGenerationSettings projectGlobal = GlobalGenerationSettings.Clone(globalSettings);
            if (projectGlobal.UseGlobalWallHeightOverride && projectGlobal.GlobalWallHeightMm > 0)
            {
                return projectGlobal.GlobalWallHeightMm;
            }

            if (rowSettings != null && rowSettings.WallHeightMm.HasValue && rowSettings.WallHeightMm.Value > 0)
            {
                return rowSettings.WallHeightMm.Value;
            }

            return DefaultWallHeightMm;
        }

        private static double ResolveColumnHeightMm(AdvancedSettingsRow rowSettings, GlobalGenerationSettings globalSettings)
        {
            if (rowSettings != null && rowSettings.ColumnHeightMm.HasValue && rowSettings.ColumnHeightMm.Value > 0)
            {
                return rowSettings.ColumnHeightMm.Value;
            }

            GlobalGenerationSettings projectGlobal = GlobalGenerationSettings.Clone(globalSettings);
            if (projectGlobal.UseGlobalWallHeightOverride && projectGlobal.GlobalWallHeightMm > 0)
            {
                return projectGlobal.GlobalWallHeightMm;
            }

            return DefaultWallHeightMm;
        }


        private static List<Wall> GetHostWallsForDoor(Document doc, List<Wall> createdWalls)
        {
            List<Wall> all = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .ToList();

            foreach (Wall wall in createdWalls ?? new List<Wall>())
            {
                if (wall == null)
                {
                    continue;
                }

                if (!all.Any(x => x != null && x.Id.IntegerValue == wall.Id.IntegerValue))
                {
                    all.Add(wall);
                }
            }

            return all;
        }


        private static List<MapRow> DeduplicateMapRows(IEnumerable<MapRow> source)
        {
            List<MapRow> result = new List<MapRow>();
            if (source == null)
            {
                return result;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MapRow row in source)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                {
                    continue;
                }

                string key = row.RawLayerName.Trim() + "|" + row.Category.ToString();
                if (keys.Add(key))
                {
                    result.Add(row);
                }
            }

            return result;
        }


        private static SourceUnit ResolveEffectiveSourceUnit(Document doc, SourceUnit selectedUnit, out string evidence)
        {
            DwgSessionInfo session = DwgSessionManager.Get(doc);
            if (session != null && session.SourceUnit != SourceUnit.Auto)
            {
                evidence = string.IsNullOrWhiteSpace(session.SourceUnitEvidence)
                    ? "DwgSession"
                    : session.SourceUnitEvidence;
                return session.SourceUnit;
            }

            if (selectedUnit != SourceUnit.Auto)
            {
                evidence = "SelectedUnitWithoutSession";
                DiagnosticRecorder.AppendDebug(
                    "WARNING: DWG SourceUnit missing from session. Use selected unit " + selectedUnit + ".");
                return selectedUnit;
            }

            evidence = "AnalyzeSessionFallback";
            DiagnosticRecorder.AppendDebug("WARNING: DWG SourceUnit missing from session. Fallback to Millimeter.");
            return SourceUnit.Millimeter;
        }

        private static UnitContext BuildRevitImportInstanceUnitContext(SourceUnit sourceUnit, string evidence)
        {
            return new UnitContext
            {
                SourceUnit = sourceUnit,
                ScaleToFeet = 1.0,
                Confidence = 1.0,
                Evidence = "Revit ImportInstance geometry is already feet; " + (evidence ?? string.Empty)
            };
        }


        private static void BuildAnalyzeContext(
            Document doc,
            ImportInstance importInstance,
            SourceUnit selectedUnit,
            out UnitContext unitContext,
            out CadDataset scaled,
            out AnalyzeSummaryInfo summary)
        {
            string sourceUnitEvidence;
            SourceUnit effectiveSourceUnit = ResolveEffectiveSourceUnit(doc, selectedUnit, out sourceUnitEvidence);
            summary = new AnalyzeSummaryInfo
            {
                DwgName = importInstance != null ? (importInstance.Name ?? string.Empty) : string.Empty,
                ImportInstanceId = importInstance != null && importInstance.Id != null ? importInstance.Id.IntegerValue : -1,
                Status = "Failed",
                Error = string.Empty
            };

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                CadSegmentBuildResult buildResult = CadSegmentBuilder.BuildSegments(doc, importInstance, null);
                CadDataset original = CadDatasetBuilder.Build(buildResult);
                string dwgPath = DwgPathResolver.TryGetDwgPath(doc, importInstance, msg => DiagnosticRecorder.AppendDebug(msg));
                Transform cadToRevit = DwgTransformResolver.GetCadToRevitTransform(importInstance, msg => DiagnosticRecorder.AppendDebug(msg));
                DwgSourceUnitContext textUnitContext = DwgSourceUnitContextFactory.Create(effectiveSourceUnit, sourceUnitEvidence);

                DiagnosticRecorder.AppendDebug(
                    "[Analyze] DWG Session Source Unit: SourceUnit=" + effectiveSourceUnit +
                    ", Evidence=" + (sourceUnitEvidence ?? string.Empty));

                CadRuntimeInfo cadRuntime = CadRuntimeDetector.Detect();
                DiagnosticRecorder.AppendDebug("[CAD] Runtime detect => " + cadRuntime);

                List<CadText> texts = null;
                if (cadRuntime.IsReady)
                {
                    texts = DwgTextReader.ReadRoomNameTexts(
                        dwgPath,
                        string.Empty,
                        cadToRevit,
                        textUnitContext,
                        msg => DiagnosticRecorder.AppendDebug(msg));
                }
                else
                {
                    // Skip AutoCAD API path when runtime is not ready.
                    DiagnosticRecorder.AppendDebug("[CAD] Skip DwgTextReader because CAD runtime is not ready. Reason=" + (cadRuntime.Message ?? string.Empty));
                }

                if (texts == null || texts.Count == 0)
                {
                    DiagnosticRecorder.AppendDebug("[RoomText] Fallback to Revit-geometry text extraction.");
                    texts = CadTextBuilder.Extract(doc, importInstance);
                }
                original.Texts = texts ?? new List<CadText>();
                original.TextsByRawLayer = (texts ?? new List<CadText>())
                    .Where(x => x != null)
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.RawLayerName) ? "UNKNOWN" : x.RawLayerName, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
                unitContext = BuildRevitImportInstanceUnitContext(effectiveSourceUnit, sourceUnitEvidence);
                scaled = CadDatasetScaler.Scale(original, unitContext);

                summary.UnitText = unitContext != null ? unitContext.SourceUnit.ToString() : "Unknown";
                summary.LayerCount = scaled != null && scaled.SegmentsByRawLayer != null ? scaled.SegmentsByRawLayer.Count : 0;
                summary.ValidLayerCount = scaled != null && scaled.SegmentsByRawLayer != null
                    ? scaled.SegmentsByRawLayer.Count(x => x.Value != null && x.Value.Count > 0)
                    : 0;
                summary.SegmentCount = scaled != null && scaled.Segments != null ? scaled.Segments.Count : 0;
                summary.ArcCount = scaled != null && scaled.Segments != null ? scaled.Segments.Count(x => x != null && x.IsArc) : 0;
                summary.PolylineCount = scaled != null && scaled.Segments != null
                    ? scaled.Segments.Count(x => x != null && x.SourceType == CadCurveSourceType.PolyLineSegment)
                    : 0;
                summary.Status = "Success";
                summary.Error = string.Empty;
            }
            catch (Exception ex)
            {
                unitContext = BuildRevitImportInstanceUnitContext(effectiveSourceUnit, sourceUnitEvidence);
                scaled = new CadDataset();
                summary.Error = ex.Message;
            }
            finally
            {
                sw.Stop();
                summary.TimeSeconds = sw.Elapsed.TotalSeconds;
                DiagnosticRecorder.AppendDebug(
                    "[Analyze] DWG=" + (summary.DwgName ?? string.Empty) +
                    ", ImportId=" + summary.ImportInstanceId +
                    ", Unit=" + (summary.UnitText ?? string.Empty) +
                    ", Layers=" + summary.LayerCount +
                    ", ValidLayers=" + summary.ValidLayerCount +
                    ", Segments=" + summary.SegmentCount +
                    ", Arc=" + summary.ArcCount +
                    ", Polyline=" + summary.PolylineCount +
                    ", TimeSec=" + summary.TimeSeconds.ToString("F2") +
                    ", Status=" + (summary.Status ?? string.Empty) +
                    (string.IsNullOrWhiteSpace(summary.Error) ? string.Empty : (", Error=" + summary.Error)));
            }
        }

        private static string BuildAnalyzeSummaryText(AnalyzeSummaryInfo summary)
        {
            if (summary == null)
            {
                return "Status: Not analyzed";
            }

            if (!string.Equals(summary.Status, "Success", StringComparison.OrdinalIgnoreCase))
            {
                return "Status: Failed | Error: " + (summary.Error ?? string.Empty);
            }

            return "DWG: " + (summary.DwgName ?? string.Empty) +
                   " | Unit: " + (summary.UnitText ?? string.Empty) +
                   " | Layers: " + summary.LayerCount +
                   " | Valid: " + summary.ValidLayerCount +
                   " | Segments: " + summary.SegmentCount +
                   " | Arc: " + summary.ArcCount +
                   " | Polyline: " + summary.PolylineCount +
                   " | Time: " + summary.TimeSeconds.ToString("F2") + " s" +
                   " | Status: Success";
        }

        private static void ShowAnalyzeResultDialog(AnalyzeSummaryInfo summary)
        {
            AnalyzeSummaryInfo s = summary ?? new AnalyzeSummaryInfo
            {
                Status = "Failed",
                Error = "No analyze data."
            };

            TaskDialog td = new TaskDialog("Analyze Result");
            bool success = string.Equals(s.Status, "Success", StringComparison.OrdinalIgnoreCase);
            td.MainInstruction = success ? "Analyze completed successfully." : "Analyze failed.";
            td.MainContent =
                "Layers: " + s.LayerCount + Environment.NewLine +
                "Valid Layers: " + s.ValidLayerCount + Environment.NewLine +
                "Segments: " + s.SegmentCount + Environment.NewLine +
                "Unit: " + (s.UnitText ?? string.Empty) + Environment.NewLine +
                "Time: " + s.TimeSeconds.ToString("F2") + " s" +
                (success ? string.Empty : (Environment.NewLine + "Error: " + (s.Error ?? string.Empty)));
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "OK");
            td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Copy Log");
            TaskDialogResult result = td.Show();
            if (result == TaskDialogResult.CommandLink2)
            {
                string copied = BuildAnalyzeSummaryText(s);
                try
                {
                    System.Windows.Forms.Clipboard.SetText(copied);
                }
                catch
                {
                }
            }
        }


        private static List<string> BuildLayerOptions(CadDataset scaled)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (scaled != null)
            {
                foreach (CadSegment s in scaled.Segments)
                {
                    if (s != null && !string.IsNullOrWhiteSpace(s.RawLayerName))
                    {
                        set.Add(s.RawLayerName);
                    }
                }
            }

            return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string ResolveRoomNameLayer(CadDataset dataset)
        {
            if (dataset == null)
            {
                return string.Empty;
            }

            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string layer in dataset.SegmentsByRawLayer.Keys)
            {
                if (!string.IsNullOrWhiteSpace(layer))
                {
                    layers.Add(layer);
                }
            }

            foreach (string layer in dataset.TextsByRawLayer.Keys)
            {
                if (!string.IsNullOrWhiteSpace(layer))
                {
                    layers.Add(layer);
                }
            }

            if (layers.Contains("ROOMNAME"))
            {
                return "ROOMNAME";
            }

            return layers.FirstOrDefault(x =>
                       x.IndexOf("ROOM", StringComparison.OrdinalIgnoreCase) >= 0 &&
                       x.IndexOf("NAME", StringComparison.OrdinalIgnoreCase) >= 0) ??
                   layers.FirstOrDefault(x =>
                       x.IndexOf("ROOM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       x.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0) ??
                   string.Empty;
        }

        private static RoomRecognitionSettings ResolveRoomRecognitionSettings(Document doc)
        {
            try
            {
                LayerOverrideStoreData store = LayerOverrideStoreService.Load(doc);
                return RoomRecognitionSettings.Clone(store != null ? store.RoomRecognitionSettings : null);
            }
            catch
            {
                return RoomRecognitionSettings.CreateDefault();
            }
        }

        // Extract and persist target-room text seeds for the model-based recognition stage.
        private static int TrySaveTargetRoomSeeds(Document doc, CadDataset dataset, ElementId levelId)
        {
            if (doc == null || dataset == null)
            {
                return 0;
            }

            RoomRecognitionSettings roomSettings = ResolveRoomRecognitionSettings(doc);
            List<string> targetKeywords = ResolveConfiguredTargetKeywords(roomSettings);
            string roomNameLayer = string.Empty;
            List<TargetRoomSeed> seeds = TargetRoomSeedExtractor.ExtractFromDataset(
                dataset,
                roomNameLayer,
                targetKeywords,
                levelId);
            List<LiftRecognitionRecord> lifts = LiftSeedExtractor.ExtractFromDataset(
                dataset,
                roomNameLayer,
                levelId,
                roomSettings);
            if (seeds.Count == 0 && lifts.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[TargetSeed] No target room seed or lift found in ALL_TEXTS mode.");
                return 0;
            }

            using (Transaction tx = new Transaction(doc, "Save Target Room Seeds"))
            {
                tx.Start();
                ConfigureNonCriticalFailureHandling(tx, "TargetSeedSave");
                TargetRoomSeedStorageService.SaveSeeds(doc, seeds);
                LiftRecognitionStorageService.Save(doc, lifts);
                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug(
                "[TargetSeed] Saved=" + seeds.Count +
                ", LiftSaved=" + lifts.Count +
                ", LevelId=" + (levelId != null ? levelId.IntegerValue : -1) +
                ", TextScope=" + (string.IsNullOrWhiteSpace(roomNameLayer) ? "ALL_TEXTS" : roomNameLayer) +
                ", Keywords=[" + string.Join(", ", targetKeywords) + "]" +
                ", SeedLayers=[" + BuildSeedLayerPreview(seeds) + "]");
            return seeds.Count;
        }

        private static string BuildSeedLayerPreview(List<TargetRoomSeed> seeds)
        {
            return string.Join(", ",
                (seeds ?? new List<TargetRoomSeed>())
                    .Where(x => x != null)
                    .GroupBy(
                        x => string.IsNullOrWhiteSpace(x.SourceLayer) ? "UNKNOWN" : x.SourceLayer,
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count())
                    .Take(8)
                    .Select(g => g.Key + ":" + g.Count()));
        }

        private static string ResolveConfiguredRoomNameLayer(CadDataset dataset, RoomRecognitionSettings roomSettings)
        {
            RoomRecognitionSettings settings = RoomRecognitionSettings.Clone(roomSettings);
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dataset != null)
            {
                foreach (string layer in (dataset.TextsByRawLayer ?? new Dictionary<string, List<CadText>>(StringComparer.OrdinalIgnoreCase)).Keys)
                {
                    if (!string.IsNullOrWhiteSpace(layer))
                    {
                        layers.Add(layer);
                    }
                }

                foreach (string layer in (dataset.SegmentsByRawLayer ?? new Dictionary<string, List<CadSegment>>(StringComparer.OrdinalIgnoreCase)).Keys)
                {
                    if (!string.IsNullOrWhiteSpace(layer))
                    {
                        layers.Add(layer);
                    }
                }
            }

            foreach (string configuredLayer in settings.GetConfiguredRoomTextLayers())
            {
                if (layers.Contains(configuredLayer))
                {
                    return configuredLayer;
                }
            }

            return string.Empty;
        }

        private static List<string> ResolveConfiguredTargetKeywords(RoomRecognitionSettings roomSettings)
        {
            RoomRecognitionSettings settings = RoomRecognitionSettings.Clone(roomSettings);
            List<string> keywords = settings.GetConfiguredTargetKeywords();
            return keywords.Count > 0
                ? keywords
                : new List<string> { "A/C", "AHU", "PAU" };
        }

        private static void LogRoomSemanticInput(
            CadDataset dataset,
            HashSet<string> boundaryLayers,
            string roomNameLayer,
            RoomSemanticConfig cfg)
        {
            int segmentCount = dataset != null && dataset.Segments != null ? dataset.Segments.Count : 0;
            int textCount = dataset != null && dataset.Texts != null ? dataset.Texts.Count : 0;
            int segmentLayerCount = dataset != null && dataset.SegmentsByRawLayer != null ? dataset.SegmentsByRawLayer.Count : 0;
            int textLayerCount = dataset != null && dataset.TextsByRawLayer != null ? dataset.TextsByRawLayer.Count : 0;
            int roomLayerTextCount = 0;
            if (dataset != null &&
                dataset.TextsByRawLayer != null &&
                !string.IsNullOrWhiteSpace(roomNameLayer) &&
                dataset.TextsByRawLayer.TryGetValue(roomNameLayer, out List<CadText> roomLayerTexts) &&
                roomLayerTexts != null)
            {
                roomLayerTextCount = roomLayerTexts.Count;
            }

            List<string> textLayerPreview = (dataset != null && dataset.TextsByRawLayer != null
                    ? dataset.TextsByRawLayer
                    : new Dictionary<string, List<CadText>>(StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Value != null ? x.Value.Count : 0)
                .Take(8)
                .Select(x => (x.Key ?? string.Empty) + ":" + (x.Value != null ? x.Value.Count : 0))
                .ToList();
            string textLayerPreviewText = textLayerPreview.Count > 0 ? string.Join(", ", textLayerPreview) : "(none)";
            string boundaryText = boundaryLayers != null && boundaryLayers.Count > 0
                ? string.Join(", ", boundaryLayers.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                : "(none)";

            DiagnosticRecorder.AppendDebug(
                "[RoomDiag] Input: Segments=" + segmentCount +
                ", SegmentLayers=" + segmentLayerCount +
                ", Texts=" + textCount +
                ", TextLayers=" + textLayerCount +
                ", BoundaryLayers=[" + boundaryText + "]" +
                ", RoomNameLayer=" + (string.IsNullOrWhiteSpace(roomNameLayer) ? "(empty)" : roomNameLayer) +
                ", RoomLayerTextCount=" + roomLayerTextCount +
                ", CloseTolMm=" + (cfg != null ? cfg.CloseTolMm.ToString("F1") : "-") +
                ", MaxPatchMm=" + (cfg != null ? cfg.MaxPatchMm.ToString("F1") : "-") +
                ", DoorGapMaxMm=" + (cfg != null ? cfg.DoorGapMaxMm.ToString("F1") : "-") +
                ", SmallGapPatchMaxMm=" + (cfg != null ? cfg.SmallGapPatchMaxMm.ToString("F1") : "-") +
                ", TargetKeywords=[" + (cfg != null && cfg.TargetKeywords != null ? string.Join(", ", cfg.TargetKeywords) : string.Empty) + "]" +
                ", MinAreaM2=" + (cfg != null ? cfg.MinAreaM2.ToString("F2") : "-"));
            DiagnosticRecorder.AppendDebug("[RoomDiag] TextLayerTop: " + textLayerPreviewText);
        }

        private static void LogRoomSemanticOutput(RoomSemanticRunResult run)
        {
            if (run == null)
            {
                DiagnosticRecorder.AppendDebug("[RoomDiag] Output: run is null.");
                return;
            }

            Dictionary<string, int> statusCounts =
                (run.Rooms ?? new List<CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord>())
                .Where(x => x != null)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Status) ? "(empty)" : x.Status, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            string statusText = statusCounts.Count > 0
                ? string.Join(", ", statusCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => x.Key + "=" + x.Value))
                : "(none)";

            List<string> unmatchedLabelPreview = (run.UnmatchedLabels ?? new List<CadToRevit.Models.Rooms.Semantic.RoomLabel>())
                .Where(x => x != null)
                .Take(8)
                .Select(x =>
                    (string.IsNullOrWhiteSpace(x.RoomNumber) ? "-" : x.RoomNumber) + "|" +
                    (string.IsNullOrWhiteSpace(x.RoomName) ? "-" : x.RoomName) + "|" +
                    (string.IsNullOrWhiteSpace(x.SourceLayer) ? "-" : x.SourceLayer) + "|" +
                    (x.Position != null ? ("(" + x.Position.X.ToString("F2") + "," + x.Position.Y.ToString("F2") + ")") : "(null)"))
                .ToList();
            string unmatchedPreviewText = unmatchedLabelPreview.Count > 0 ? string.Join("; ", unmatchedLabelPreview) : "(none)";

            List<string> matchedPreview =
                (run.Rooms ?? new List<CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord>())
                .Where(x => x != null &&
                            x.Status != null &&
                            x.Status.StartsWith("Matched", StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .Select(x =>
                    (string.IsNullOrWhiteSpace(x.RoomNumber) ? "-" : x.RoomNumber) + "|" +
                    (string.IsNullOrWhiteSpace(x.RoomName) ? "-" : x.RoomName) + "|" +
                    (x.Centroid != null ? ("(" + x.Centroid.X.ToString("F2") + "," + x.Centroid.Y.ToString("F2") + ")") : "(null)"))
                .ToList();
            string matchedPreviewText = matchedPreview.Count > 0 ? string.Join("; ", matchedPreview) : "(none)";

            DiagnosticRecorder.AppendDebug(
                "[TargetMatchDiag] TargetLabels=" + run.Total +
                ", MatchedTargetRooms=" + run.Matched +
                ", UnmatchedTargetLabels=" + run.UnmatchedLabel +
                ", StatusMap={" + statusText + "}");
            DiagnosticRecorder.AppendDebug("[TargetMatchDiag] MatchedPreview: " + matchedPreviewText);
            DiagnosticRecorder.AppendDebug("[TargetMatchDiag] UnmatchedPreview: " + unmatchedPreviewText);
        }

        private static void LogRoomSpatialProbe(CadDataset dataset, RoomSemanticRunResult run, string roomNameLayer)
        {
            if (run == null || run.Matched > 0)
            {
                return;
            }

            List<CadText> labels = (dataset != null ? dataset.Texts : null) ?? new List<CadText>();
            CadText firstLabel = labels.FirstOrDefault(x =>
                x != null &&
                x.Position != null &&
                string.Equals(x.RawLayerName, roomNameLayer, StringComparison.OrdinalIgnoreCase));
            CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord firstRoom =
                (run.Rooms ?? new List<CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord>())
                .FirstOrDefault(x => x != null && x.BBox != null);
            if (firstLabel == null || firstRoom == null || firstRoom.BBox == null)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug(
                "[RoomDiag] SpatialProbe: Label=(" +
                firstLabel.Position.X.ToString("F2") + "," + firstLabel.Position.Y.ToString("F2") + "), " +
                "RoomBBoxMin=(" + firstRoom.BBox.Min.X.ToString("F2") + "," + firstRoom.BBox.Min.Y.ToString("F2") + "), " +
                "RoomBBoxMax=(" + firstRoom.BBox.Max.X.ToString("F2") + "," + firstRoom.BBox.Max.Y.ToString("F2") + "), " +
                "RoomStatus=" + (firstRoom.Status ?? string.Empty));
        }

        private static int CreateRoomTextMarkers(Document doc, RoomSemanticRunResult run)
        {
            if (doc == null || run == null || run.TargetLabels == null || run.TargetLabels.Count == 0)
            {
                return 0;
            }

            List<CadToRevit.Models.Rooms.Semantic.RoomLabel> targets = run.TargetLabels
                .Where(x => x != null && x.Position != null)
                .Take(RoomTextMarkerMaxCount)
                .ToList();
            if (targets.Count == 0)
            {
                return 0;
            }

            double halfFt = RoomTextMarkerHalfSizeMm / 304.8;
            int created = 0;
            using (Transaction tx = new Transaction(doc, "CadToRevit Room Text Markers"))
            {
                tx.Start();
                foreach (CadToRevit.Models.Rooms.Semantic.RoomLabel t in targets)
                {
                    XYZ p = t.Position;
                    Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, p);
                    SketchPlane sp = SketchPlane.Create(doc, plane);
                    Line d1 = Line.CreateBound(
                        new XYZ(p.X - halfFt, p.Y - halfFt, p.Z),
                        new XYZ(p.X + halfFt, p.Y + halfFt, p.Z));
                    Line d2 = Line.CreateBound(
                        new XYZ(p.X - halfFt, p.Y + halfFt, p.Z),
                        new XYZ(p.X + halfFt, p.Y - halfFt, p.Z));
                    doc.Create.NewModelCurve(d1, sp);
                    doc.Create.NewModelCurve(d2, sp);
                    created++;
                }

                tx.Commit();
            }

            return created;
        }

        private static int CreateMatchedRoomBoundaryMarkers(Document doc, RoomSemanticRunResult run)
        {
            if (doc == null || run == null || run.Rooms == null || run.Rooms.Count == 0)
            {
                return 0;
            }

            View view = doc.ActiveView;
            if (view == null)
            {
                return 0;
            }

            List<CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord> debugRooms = run.Rooms
                .Where(x => x != null &&
                            x.Status != null &&
                            x.Status.StartsWith("Matched", StringComparison.OrdinalIgnoreCase) &&
                            x.LoopPoints != null &&
                            x.LoopPoints.Count >= 3)
                .Take(RoomBoundaryMarkerMaxRooms)
                .ToList();
            if (debugRooms.Count == 0)
            {
                return 0;
            }

            int created = 0;
            using (Transaction tx = new Transaction(doc, "CadToRevit Target Room Boundaries"))
            {
                tx.Start();

                foreach (CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord room in debugRooms)
                {
                    OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                    string targetType = room.TargetRoomType ?? string.Empty;
                    Color color;
                    if (string.Equals(targetType, "AC", StringComparison.OrdinalIgnoreCase))
                    {
                        color = new Color(52, 152, 219); // blue
                    }
                    else if (string.Equals(targetType, "AHU", StringComparison.OrdinalIgnoreCase))
                    {
                        color = new Color(46, 204, 113); // green
                    }
                    else if (string.Equals(targetType, "PAU", StringComparison.OrdinalIgnoreCase))
                    {
                        color = new Color(243, 156, 18); // orange
                    }
                    else
                    {
                        color = new Color(149, 165, 166); // gray
                    }

                    ogs.SetProjectionLineColor(color);
                    ogs.SetProjectionLineWeight(5);
                    List<XYZ> pts = room.LoopPoints;
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        if (created >= RoomBoundaryMarkerMaxSegments)
                        {
                            break;
                        }

                        XYZ a = pts[i];
                        XYZ b = pts[i + 1];
                        if (a == null || b == null || a.DistanceTo(b) < 1e-9)
                        {
                            continue;
                        }

                        try
                        {
                            Line edge = Line.CreateBound(a, b);
                            DetailCurve dc = doc.Create.NewDetailCurve(view, edge);
                            if (dc != null)
                            {
                                view.SetElementOverrides(dc.Id, ogs);
                                created++;
                            }
                        }
                        catch
                        {
                            // Ignore invalid edges and continue to keep generation stable.
                        }
                    }

                    if (created >= RoomBoundaryMarkerMaxSegments)
                    {
                        break;
                    }
                }

                tx.Commit();
            }

            return created;
        }

        private static int CreateDebugRoomBoundaryMarkers(Document doc, RoomSemanticRunResult run)
        {
            if (doc == null || run == null || run.DebugCandidates == null || run.DebugCandidates.Count == 0)
            {
                return 0;
            }

            View view = doc.ActiveView;
            if (view == null)
            {
                return 0;
            }

            List<CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord> debugRooms = run.DebugCandidates
                .Where(x => x != null && x.LoopPoints != null && x.LoopPoints.Count >= 2)
                .Take(RoomBoundaryMarkerMaxRooms)
                .ToList();
            if (debugRooms.Count == 0)
            {
                return 0;
            }

            int created = 0;
            using (Transaction tx = new Transaction(doc, "CadToRevit Target Room Debug Boundaries"))
            {
                tx.Start();

                OverrideGraphicSettings ogs = new OverrideGraphicSettings();
                ogs.SetProjectionLineColor(new Color(231, 76, 60)); // red
                ogs.SetProjectionLineWeight(6);

                foreach (CadToRevit.Models.Rooms.Semantic.RoomSemanticRecord room in debugRooms)
                {
                    List<XYZ> pts = room.LoopPoints;
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        if (created >= RoomBoundaryMarkerMaxSegments)
                        {
                            break;
                        }

                        XYZ a = pts[i];
                        XYZ b = pts[i + 1];
                        if (a == null || b == null || a.DistanceTo(b) < 1e-9)
                        {
                            continue;
                        }

                        try
                        {
                            Line edge = Line.CreateBound(a, b);
                            DetailCurve dc = doc.Create.NewDetailCurve(view, edge);
                            if (dc != null)
                            {
                                view.SetElementOverrides(dc.Id, ogs);
                                created++;
                            }
                        }
                        catch
                        {
                            // Ignore invalid edges and continue to keep generation stable.
                        }
                    }

                    if (created >= RoomBoundaryMarkerMaxSegments)
                    {
                        break;
                    }
                }

                tx.Commit();
            }

            return created;
        }


        private static DoorDetectSettings BuildDoorDetectSettings(MapRow mapRow)
        {
            DoorDetectSettings settings = new DoorDetectSettings();
            if (mapRow == null || mapRow.Settings == null)
            {
                return settings;
            }

            if (mapRow.Settings.MinDoorWidthMm.HasValue && mapRow.Settings.MinDoorWidthMm.Value > 0)
            {
                settings.DoorWidthMinMm = mapRow.Settings.MinDoorWidthMm.Value;
            }

            if (mapRow.Settings.MaxDoorWidthMm.HasValue && mapRow.Settings.MaxDoorWidthMm.Value > 0)
            {
                settings.DoorWidthMaxMm = mapRow.Settings.MaxDoorWidthMm.Value;
            }

            if (mapRow.Settings.DoorWallMatchTolMm.HasValue && mapRow.Settings.DoorWallMatchTolMm.Value > 0)
            {
                settings.WallMatchDistTolMm = mapRow.Settings.DoorWallMatchTolMm.Value;
            }

            return settings;
        }


        private static WallRecognitionResult MergeResults(List<WallRecognitionResult> details)
        {
            WallRecognitionResult result = new WallRecognitionResult();
            foreach (WallRecognitionResult item in details ?? new List<WallRecognitionResult>())
            {
                if (item == null)
                {
                    continue;
                }

                result.TotalWallSegments += item.TotalWallSegments;
                result.TypeADoubleLineWalls += item.TypeADoubleLineWalls;
                result.TypeBSingleLineWalls += item.TypeBSingleLineWalls;
                result.MergedWalls += item.MergedWalls;
                result.RefinedWalls += item.RefinedWalls;
                result.ClusteredEndpointCount += item.ClusteredEndpointCount;
                result.ExtendedEndpointCount += item.ExtendedEndpointCount;
                result.DuplicateRemovedCount += item.DuplicateRemovedCount;
                result.CollinearMergedCount += item.CollinearMergedCount;
                result.OffAxisSnappedCount += item.OffAxisSnappedCount;
                if (item.Centerlines != null)
                {
                    result.Centerlines.AddRange(item.Centerlines);
                }
            }

            return result;
        }


        private static List<ImportInstance> GetAllImportInstances(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();
        }


        private static List<Level> GetAllLevels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ToList();
        }


        private static List<WallType> GetSupportedWallTypes(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(x => x.Kind == WallKind.Basic || x.Kind == WallKind.Curtain)
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        private static List<string> GetFamilySymbolTypeNames(Document doc, BuiltInCategory category)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(x => x.Category != null && x.Category.Id.IntegerValue == (int)category)
                .Select(x => x.FamilyName + " : " + x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        private static List<string> GetColumnFamilyTypeNames(Document doc)
        {
            List<string> structuralNames = GetFamilySymbolTypeNames(doc, BuiltInCategory.OST_StructuralColumns)
                .Select(x => StructuralColumnPrefix + x)
                .ToList();
            List<string> architecturalNames = GetFamilySymbolTypeNames(doc, BuiltInCategory.OST_Columns)
                .Select(x => ArchitecturalColumnPrefix + x)
                .ToList();

            return structuralNames
                .Concat(architecturalNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        private static string NormalizeColumnTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return string.Empty;
            }

            if (typeName.StartsWith(StructuralColumnPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return typeName.Substring(StructuralColumnPrefix.Length).Trim();
            }

            if (typeName.StartsWith(ArchitecturalColumnPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return typeName.Substring(ArchitecturalColumnPrefix.Length).Trim();
            }

            return typeName.Trim();
        }


        private static List<ParameterOption> BuildWallParameterOptions(Document doc, List<WallType> wallTypes)
        {
            Dictionary<string, ParameterOption> dict = new Dictionary<string, ParameterOption>(StringComparer.OrdinalIgnoreCase);

            Wall sampleWall = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .FirstOrDefault();
            if (sampleWall != null)
            {
                AddWritableParameters(dict, sampleWall.Parameters);
            }

            foreach (WallType wallType in wallTypes ?? new List<WallType>())
            {
                if (wallType == null)
                {
                    continue;
                }

                AddWritableParameters(dict, wallType.Parameters);
            }

            EnsureFallback(dict, "Top Constraint", StorageType.ElementId, true);
            EnsureFallback(dict, "Base Constraint", StorageType.ElementId, true);
            EnsureFallback(dict, "Comments", StorageType.String, false);
            EnsureFallback(dict, "Mark", StorageType.String, false);

            return dict.Values
                .OrderBy(x => x.ParameterName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        private static void AddWritableParameters(
            Dictionary<string, ParameterOption> dict,
            ParameterSet parameters)
        {
            if (parameters == null)
            {
                return;
            }

            foreach (Parameter parameter in parameters)
            {
                if (parameter == null || parameter.IsReadOnly || parameter.Definition == null)
                {
                    continue;
                }

                string name = parameter.Definition.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (dict.ContainsKey(name))
                {
                    continue;
                }

                bool isLevelElementId = parameter.StorageType == StorageType.ElementId &&
                                        (name.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         name.IndexOf("Constraint", StringComparison.OrdinalIgnoreCase) >= 0);
                dict[name] = new ParameterOption
                {
                    ParameterName = name,
                    StorageType = parameter.StorageType.ToString(),
                    IsLevelElementId = isLevelElementId
                };
            }
        }


        private static void EnsureFallback(
            Dictionary<string, ParameterOption> dict,
            string name,
            StorageType storageType,
            bool isLevelElementId)
        {
            if (dict.ContainsKey(name))
            {
                return;
            }

            dict[name] = new ParameterOption
            {
                ParameterName = name,
                StorageType = storageType.ToString(),
                IsLevelElementId = isLevelElementId
            };
        }


        private static ElementId ResolveDefaultLevelId(Document doc, List<Level> levels)
        {
            View activeView = doc.ActiveView;
            if (activeView != null && activeView.GenLevel != null)
            {
                return activeView.GenLevel.Id;
            }

            Level level = levels.FirstOrDefault();
            return level != null ? level.Id : ElementId.InvalidElementId;
        }


        private static ImportInstance GetSelectedImportInstance(UIDocument uiDoc)
        {
            ICollection<ElementId> selectedIds = uiDoc.Selection.GetElementIds();
            foreach (ElementId id in selectedIds)
            {
                ImportInstance x = uiDoc.Document.GetElement(id) as ImportInstance;
                if (x != null)
                {
                    return x;
                }
            }

            return null;
        }

        private static void SaveLayerOverridesWithMessage(
            Document doc,
            IEnumerable<MapRow> rows,
            LayerOverrideStoreData beforeSave)
        {
            int beforeCount = GetOverrideEntryCount(beforeSave);
            string savePath = GetLayerOverrideStorePath();
            try
            {
                LayerOverrideStoreService.Save(doc, rows);
                LayerOverrideStoreData afterSave = LayerOverrideStoreService.Load(doc);
                int afterCount = GetOverrideEntryCount(afterSave);
                bool shouldShowSuccess = afterCount > 0 || (beforeCount > 0 && afterCount == 0);
                if (shouldShowSuccess)
                {
                    System.Windows.Forms.MessageBox.Show(
                        "图层覆盖配置已保存。" + Environment.NewLine +
                        "保存条目数：" + afterCount + Environment.NewLine +
                        "路径：" + savePath + Environment.NewLine +
                        "说明：覆盖配置不会修改 ColumnRecognitionConfig.json（该文件仅用于默认参数读取）。",
                        "覆盖配置已保存",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "保存图层覆盖配置失败：" + ex.Message + Environment.NewLine +
                    "请检查文件权限或杀毒软件拦截。",
                    "保存失败",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }

        private static int GetOverrideEntryCount(LayerOverrideStoreData data)
        {
            if (data == null)
            {
                return 0;
            }

            int layerCount = data.LayerOverrides == null ? 0 : data.LayerOverrides.Count;
            int categoryCount = data.CategoryDefaults == null ? 0 : data.CategoryDefaults.Count;
            return layerCount + categoryCount;
        }

        private static string GetLayerOverrideStorePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return System.IO.Path.Combine(appData, "CadToRevit", "HelixWizard", "Overrides", "layer_overrides.json");
        }

    }
}
