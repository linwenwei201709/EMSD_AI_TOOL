using Autodesk.Revit.DB;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Units;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Dwg
{
    public sealed class DwgRefreshResult
    {
        public bool Success { get; set; }

        public bool HasChanged { get; set; }

        public bool ReloadExecuted { get; set; }

        public string Message { get; set; }

        public string FilePath { get; set; }
    }

    public static class DwgRefreshService
    {
        public static DwgRefreshResult RefreshCurrentLink(Document doc)
        {
            DwgRefreshResult result = new DwgRefreshResult();
            if (doc == null)
            {
                result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat", "No active document.");
                return result;
            }

            DwgSessionInfo existingSession = DwgSessionManager.Get(doc);
            ImportInstance import = ResolveCurrentImportInstance(doc);
            string filePath = ResolveRefreshFilePath(doc, import, existingSession);
            result.FilePath = filePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat",
                    import == null ? Loc.T("Dialog.DwgRefresh.NoLink") : Loc.T("Dialog.DwgRefresh.PathResolveFailed"));
                return result;
            }

            if (!File.Exists(filePath))
            {
                result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat", Loc.T("Dialog.DwgRefresh.FileMissing"));
                return result;
            }

            if (!DwgSessionManager.TryCaptureFileFingerprint(filePath, out string currentFingerprint, out long currentFileSize, out long currentWriteTicks))
            {
                result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat", Loc.T("Dialog.DwgRefresh.FileMissing"));
                return result;
            }

            DwgSessionInfo session = EnsureSession(doc, import, filePath);
            SourceUnit sourceUnit = session != null && IsSupportedFinalSourceUnit(session.SourceUnit)
                ? session.SourceUnit
                : SourceUnit.Millimeter;
            string evidence = session != null && !string.IsNullOrWhiteSpace(session.SourceUnitEvidence)
                ? session.SourceUnitEvidence
                : "RefreshFromSessionFallback";
            if (session == null || !IsSupportedFinalSourceUnit(session.SourceUnit))
            {
                DiagnosticRecorder.AppendDebug("WARNING: DWG SourceUnit missing or unsupported in session. Fallback to Millimeter.");
            }

            // Refresh updates the geometry of the same DWG source. Preserve the user's
            // layer/category/family mapping before ResetAfterImport clears the old context.
            DwgRefreshMappingSnapshot mappingSnapshot = CaptureMappingSnapshot(
                doc,
                import,
                session,
                sourceUnit);

            string lastKnownFingerprint = session.LastKnownFingerprint;
            if (import != null && string.IsNullOrWhiteSpace(lastKnownFingerprint))
            {
                UpdateSessionSnapshot(doc, session, import, filePath, currentFingerprint, currentFileSize, currentWriteTicks);
                result.Success = true;
                result.HasChanged = false;
                result.Message = Loc.T("Dialog.DwgRefresh.NoChange");
                return result;
            }

            if (import != null && string.Equals(lastKnownFingerprint, currentFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                UpdateSessionSnapshot(doc, session, import, filePath, currentFingerprint, currentFileSize, currentWriteTicks);
                result.Success = true;
                result.HasChanged = false;
                result.Message = Loc.T("Dialog.DwgRefresh.NoChange");
                return result;
            }

            try
            {
                if (import != null)
                {
                    try
                    {
                        ExecuteReload(doc, import);
                        TryRegenerateDocument(doc, "reload");
                        RebuildDwgContextAfterRefresh(
                            doc,
                            import,
                            filePath,
                            sourceUnit,
                            evidence,
                            currentFingerprint,
                            currentFileSize,
                            currentWriteTicks,
                            mappingSnapshot);

                        DiagnosticRecorder.AppendDebug(
                            "[DwgRefresh] Reload succeeded. ImportId=" + import.Id.IntegerValue +
                            ", File=" + filePath +
                            ", SourceUnit=" + sourceUnit);

                        result.Success = true;
                        result.HasChanged = true;
                        result.ReloadExecuted = true;
                        result.Message = Loc.T("Dialog.DwgRefresh.Updated");
                        return result;
                    }
                    catch (Exception reloadEx)
                    {
                        DiagnosticRecorder.AppendDebug("[DwgRefresh] Reload failed, fallback to re-import. Error=" + reloadEx.Message);
                    }
                }

                DwgImportResult importResult = DwgImportService.ImportLink(
                    doc,
                    filePath,
                    true,
                    sourceUnit,
                    evidence);
                if (!importResult.Success)
                {
                    result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat", importResult.ErrorMessage ?? "Import failed.");
                    return result;
                }

                TryRegenerateDocument(doc, "fallback re-import");
                ImportInstance refreshedImport = ResolveImportedInstance(doc, importResult, filePath);
                if (refreshedImport == null)
                {
                    result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat", "Reload fallback imported the DWG, but no ImportInstance could be resolved.");
                    return result;
                }

                RebuildDwgContextAfterRefresh(
                    doc,
                    refreshedImport,
                    filePath,
                    sourceUnit,
                    evidence,
                    currentFingerprint,
                    currentFileSize,
                    currentWriteTicks,
                    mappingSnapshot);

                DiagnosticRecorder.AppendDebug(
                    "[DwgRefresh] Fallback re-import succeeded. ImportId=" + refreshedImport.Id.IntegerValue +
                    ", File=" + filePath +
                    ", SourceUnit=" + sourceUnit);

                result.Success = true;
                result.HasChanged = true;
                result.ReloadExecuted = true;
                result.Message = Loc.T("Dialog.DwgRefresh.Updated");
                return result;
            }
            catch (Exception ex)
            {
                result.Message = Loc.T("Dialog.DwgRefresh.FailedFormat", ex.Message);
                return result;
            }
        }

        private static bool IsSupportedFinalSourceUnit(SourceUnit sourceUnit)
        {
            return sourceUnit == SourceUnit.Millimeter || sourceUnit == SourceUnit.Inch;
        }

        private static void ExecuteReload(Document doc, ImportInstance import)
        {
            ElementId typeId = import != null ? import.GetTypeId() : ElementId.InvalidElementId;
            if (typeId == null || typeId == ElementId.InvalidElementId)
            {
                throw new InvalidOperationException(Loc.T("Dialog.DwgRefresh.ReloadUnsupported"));
            }

            CADLinkType cadLinkType = doc.GetElement(typeId) as CADLinkType;
            if (cadLinkType == null)
            {
                throw new InvalidOperationException(Loc.T("Dialog.DwgRefresh.ReloadUnsupported"));
            }

            try
            {
                cadLinkType.Reload();
                return;
            }
            catch (Exception directReloadEx)
            {
                DiagnosticRecorder.AppendDebug("[DwgRefresh] Direct CADLinkType.Reload failed, retry with transaction. Error=" + directReloadEx.Message);
            }

            using (Transaction tx = new Transaction(doc, "CadToRevit Refresh DWG Link"))
            {
                tx.Start();
                cadLinkType.Reload();
                tx.Commit();
            }
        }

        private static void TryRegenerateDocument(Document doc, string context)
        {
            if (doc == null)
            {
                return;
            }

            try
            {
                if (doc.IsModifiable)
                {
                    doc.Regenerate();
                    return;
                }

                using (Transaction tx = new Transaction(doc, "CadToRevit Regenerate DWG Refresh"))
                {
                    tx.Start();
                    doc.Regenerate();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DwgRefresh] Regenerate skipped after " + context + ". Error=" + ex.Message);
            }
        }

        private static string ResolveRefreshFilePath(Document doc, ImportInstance import, DwgSessionInfo session)
        {
            string fromImport = import != null ? DwgPathResolver.TryGetDwgPath(doc, import) : null;
            if (!string.IsNullOrWhiteSpace(fromImport))
            {
                return fromImport;
            }

            return ResolveSessionFilePath(doc, session);
        }

        private static string ResolveSessionFilePath(Document doc, DwgSessionInfo session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.FilePath))
            {
                return null;
            }

            string raw = session.FilePath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (raw.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) && File.Exists(raw))
            {
                return Path.GetFullPath(raw);
            }

            if (!raw.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string docPath = doc != null ? doc.PathName : null;
            string docDir = string.IsNullOrWhiteSpace(docPath) ? null : Path.GetDirectoryName(docPath);
            if (string.IsNullOrWhiteSpace(docDir))
            {
                return null;
            }

            string combined = Path.Combine(docDir, raw);
            return File.Exists(combined) ? Path.GetFullPath(combined) : null;
        }

        private static void RebuildDwgContextAfterRefresh(
            Document doc,
            ImportInstance import,
            string filePath,
            SourceUnit sourceUnit,
            string evidence,
            string fingerprint,
            long fileSize,
            long writeTicks,
            DwgRefreshMappingSnapshot mappingSnapshot)
        {
            List<string> layers = ReadLayers(doc, import);
            DwgContextResetService.ResetAfterImport(
                doc,
                import.Id,
                filePath,
                layers,
                sourceUnit,
                evidence);

            RestoreMappingSnapshot(
                doc,
                import.Id,
                sourceUnit,
                mappingSnapshot);

            DwgSessionInfo refreshedSession = DwgSessionManager.Get(doc);
            if (refreshedSession == null)
            {
                return;
            }

            refreshedSession.LinkInstanceId = import.Id;
            refreshedSession.FilePath = filePath ?? string.Empty;
            refreshedSession.DwgLayers = layers;
            refreshedSession.SourceUnit = sourceUnit;
            refreshedSession.SourceUnitEvidence = evidence ?? string.Empty;
            refreshedSession.LastKnownFingerprint = fingerprint;
            refreshedSession.LastKnownFileSize = fileSize;
            refreshedSession.LastKnownWriteTimeUtcTicks = writeTicks;
            DwgSessionManager.Set(doc, refreshedSession);
        }

        private static DwgRefreshMappingSnapshot CaptureMappingSnapshot(
            Document doc,
            ImportInstance import,
            DwgSessionInfo session,
            SourceUnit sourceUnit)
        {
            DwgRefreshMappingSnapshot snapshot = new DwgRefreshMappingSnapshot
            {
                OldImportId = import != null
                    ? import.Id.IntegerValue
                    : (session?.LinkInstanceId?.IntegerValue ?? ElementId.InvalidElementId.IntegerValue)
            };

            if (doc == null || snapshot.OldImportId <= 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DwgRefresh.Mapping] Snapshot skipped. Reason=NoValidCurrentImport");
                return snapshot;
            }

            Level level = ResolveLevel(doc);
            string oldContextSignature = WizardSessionCache.BuildContextSignature(
                new ElementId(snapshot.OldImportId),
                level?.Id,
                sourceUnit);
            snapshot.OldContextSignature = oldContextSignature;

            List<MapRow> mapRows;
            bool loaded = WizardStateStoreService.TryLoad(
                doc,
                oldContextSignature,
                out mapRows);
            string source = "WizardStateStore";

            if (!loaded)
            {
                loaded = WizardSessionCache.TryLoad(
                    doc,
                    oldContextSignature,
                    out mapRows);
                source = "WizardSessionCache";
            }

            if (loaded && mapRows != null && mapRows.Count > 0)
            {
                snapshot.MapRows = mapRows;
                snapshot.Source = source;
            }

            DiagnosticRecorder.AppendDebug(
                "[DwgRefresh.Mapping] Snapshot captured. Context=" + oldContextSignature +
                ", Rows=" + snapshot.MapRows.Count +
                ", Source=" + (string.IsNullOrWhiteSpace(snapshot.Source) ? "None" : snapshot.Source));

            return snapshot;
        }

        private static void RestoreMappingSnapshot(
            Document doc,
            ElementId newImportId,
            SourceUnit sourceUnit,
            DwgRefreshMappingSnapshot snapshot)
        {
            if (doc == null ||
                newImportId == null ||
                newImportId == ElementId.InvalidElementId ||
                snapshot == null ||
                snapshot.MapRows == null ||
                snapshot.MapRows.Count == 0)
            {
                DiagnosticRecorder.AppendDebug(
                    "[DwgRefresh.Mapping] Restore skipped. Reason=NoSavedMappingRows");
                return;
            }

            Level level = ResolveLevel(doc);
            string newContextSignature = WizardSessionCache.BuildContextSignature(
                newImportId,
                level?.Id,
                sourceUnit);

            // ResetAfterImport intentionally clears the old context. Save the snapshot under
            // the refreshed context so existing layer names keep Category and Family Type,
            // while new layers still use the normal inference rules in PreviewPaneDataService.
            WizardStateStoreService.Save(doc, newContextSignature, snapshot.MapRows);
            WizardSessionCache.Save(doc, newContextSignature, snapshot.MapRows);

            MigrateGenerationTrackingForReimport(
                doc,
                snapshot.OldImportId,
                newImportId);

            DiagnosticRecorder.AppendDebug(
                "[DwgRefresh.Mapping] Snapshot restored. OldContext=" +
                (snapshot.OldContextSignature ?? string.Empty) +
                ", NewContext=" + newContextSignature +
                ", Rows=" + snapshot.MapRows.Count +
                ", OldImportId=" + snapshot.OldImportId +
                ", NewImportId=" + newImportId.IntegerValue);
        }

        private static void MigrateGenerationTrackingForReimport(
            Document doc,
            int oldImportId,
            ElementId newImportId)
        {
            if (doc == null ||
                oldImportId <= 0 ||
                newImportId == null ||
                newImportId == ElementId.InvalidElementId ||
                oldImportId == newImportId.IntegerValue)
            {
                return;
            }

            List<WizardGenerationRowRecord> trackingRows =
                WizardGenerationTrackingStoreService.Load(doc);
            if (trackingRows == null || trackingRows.Count == 0)
            {
                return;
            }

            int migratedCount = 0;
            foreach (WizardGenerationRowRecord row in trackingRows)
            {
                if (row == null || row.DwgId != oldImportId)
                {
                    continue;
                }

                MapCategory category;
                if (!Enum.TryParse(row.Category ?? string.Empty, true, out category))
                {
                    category = MapCategory.Ignore;
                }

                row.DwgId = newImportId.IntegerValue;
                row.RowKey = WizardGenerationTrackingStoreService.BuildRowKey(
                    row.RawLayerName,
                    category,
                    new ElementId(row.LevelId),
                    newImportId);
                migratedCount++;
            }

            if (migratedCount <= 0)
            {
                return;
            }

            WizardGenerationTrackingStoreService.Save(doc, trackingRows);
            DiagnosticRecorder.AppendDebug(
                "[DwgRefresh.Mapping] Generation tracking migrated. OldImportId=" + oldImportId +
                ", NewImportId=" + newImportId.IntegerValue +
                ", Rows=" + migratedCount);
        }

        private static Level ResolveLevel(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            Level fromView = doc.ActiveView?.GenLevel;
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

        private static ImportInstance ResolveImportedInstance(Document doc, DwgImportResult importResult, string filePath)
        {
            if (doc == null)
            {
                return null;
            }

            if (importResult != null && importResult.LinkInstanceId != null && importResult.LinkInstanceId != ElementId.InvalidElementId)
            {
                ImportInstance byResultId = doc.GetElement(importResult.LinkInstanceId) as ImportInstance;
                if (byResultId != null)
                {
                    return byResultId;
                }

                ImportInstance byTypeId = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .Cast<ImportInstance>()
                    .Where(x => x != null && x.GetTypeId() != null && x.GetTypeId().IntegerValue == importResult.LinkInstanceId.IntegerValue)
                    .OrderByDescending(x => x.Id.IntegerValue)
                    .FirstOrDefault();
                if (byTypeId != null)
                {
                    return byTypeId;
                }
            }

            List<ImportInstance> linkedInstances = DwgImportService.GetLinkedImportInstances(doc)
                .OrderByDescending(x => x.Id.IntegerValue)
                .ToList();
            ImportInstance matchedByPath = linkedInstances.FirstOrDefault(x => IsSamePath(DwgPathResolver.TryGetDwgPath(doc, x), filePath));
            if (matchedByPath != null)
            {
                return matchedByPath;
            }

            List<ImportInstance> allInstances = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .OrderByDescending(x => x.Id.IntegerValue)
                .ToList();
            ImportInstance allMatchedByPath = allInstances.FirstOrDefault(x => IsSamePath(DwgPathResolver.TryGetDwgPath(doc, x), filePath));
            return allMatchedByPath ?? linkedInstances.FirstOrDefault() ?? allInstances.FirstOrDefault();
        }

        private static bool IsSamePath(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static DwgSessionInfo EnsureSession(Document doc, ImportInstance import, string filePath)
        {
            DwgSessionInfo session = DwgSessionManager.Get(doc);
            if (session == null)
            {
                session = new DwgSessionInfo();
            }

            session.LinkInstanceId = import != null ? import.Id : session.LinkInstanceId;
            session.FilePath = filePath ?? session.FilePath ?? string.Empty;
            session.ImportTime = DateTime.Now;
            if (session.DwgLayers == null)
            {
                session.DwgLayers = new List<string>();
            }

            DwgSessionManager.Set(doc, session);
            return session;
        }

        private static void UpdateSessionSnapshot(
            Document doc,
            DwgSessionInfo session,
            ImportInstance import,
            string filePath,
            string fingerprint,
            long fileSize,
            long writeTicks)
        {
            session.LinkInstanceId = import != null ? import.Id : session.LinkInstanceId;
            session.FilePath = filePath ?? string.Empty;
            session.ImportTime = DateTime.Now;
            session.LastKnownFingerprint = fingerprint;
            session.LastKnownFileSize = fileSize;
            session.LastKnownWriteTimeUtcTicks = writeTicks;
            session.DwgLayers = import != null ? ReadLayers(doc, import) : (session.DwgLayers ?? new List<string>());
            DwgSessionManager.Set(doc, session);
        }

        private static ImportInstance ResolveCurrentImportInstance(Document doc)
        {
            DwgSessionInfo session = DwgSessionManager.Get(doc);
            if (session?.LinkInstanceId != null && session.LinkInstanceId != ElementId.InvalidElementId)
            {
                ImportInstance current = doc.GetElement(session.LinkInstanceId) as ImportInstance;
                if (current != null)
                {
                    return current;
                }
            }

            return DwgImportService.GetLinkedImportInstances(doc).FirstOrDefault();
        }

        private static List<string> ReadLayers(Document doc, ImportInstance import)
        {
            return CadGeometryReader.ReadGeometryItems(doc, import)
                .Select(x => string.IsNullOrWhiteSpace(x.RawLayerName) ? x.LayerName : x.RawLayerName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private sealed class DwgRefreshMappingSnapshot
        {
            public int OldImportId { get; set; } = ElementId.InvalidElementId.IntegerValue;

            public string OldContextSignature { get; set; } = string.Empty;

            public string Source { get; set; } = string.Empty;

            public List<MapRow> MapRows { get; set; } = new List<MapRow>();
        }
    }
}
