using Autodesk.Revit.DB;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Units;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Dwg
{
    public sealed class DwgContextResetOptions
    {
        public bool DeleteGeneratedElements { get; set; } = true;

        public bool ClearMappingState { get; set; } = true;

        public bool ClearTrackingState { get; set; } = true;
    }

    public static class DwgContextResetService
    {
        public static void ResetBeforeImport(Document doc, DwgContextResetOptions options)
        {
            if (doc == null)
            {
                return;
            }

            DwgContextResetOptions normalized = options ?? new DwgContextResetOptions();
            DwgSessionInfo current = DwgSessionManager.Get(doc);
            int currentDwgId = current?.LinkInstanceId?.IntegerValue ?? ElementId.InvalidElementId.IntegerValue;
            DiagnosticRecorder.AppendDebug("[DwgReset.Before] Start, CurrentDwgId=" + currentDwgId);

            List<WizardGenerationRowRecord> allRows = WizardGenerationTrackingStoreService.Load(doc);
            List<WizardGenerationRowRecord> relatedRows = GetRowsForCurrentDwg(allRows, current);

            if (normalized.DeleteGeneratedElements && relatedRows.Count > 0)
            {
                List<string> errors = new List<string>();
                int deletedCount = 0;
                using (Transaction tx = new Transaction(doc, "CadToRevit Reset Old Generated Elements"))
                {
                    tx.Start();
                    foreach (WizardGenerationRowRecord row in relatedRows)
                    {
                        CleanupRowResult cleanup = WizardGeneratedElementCleanupService.DeleteRowGeneratedElements(doc, row, errors);
                        deletedCount += cleanup.DeletedCount;
                    }

                    tx.Commit();
                }

                foreach (string err in errors.Take(30))
                {
                    DiagnosticRecorder.AppendDebug("[DwgReset.Before] CleanupError: " + err);
                }

                DiagnosticRecorder.AppendDebug(
                    "[DwgReset.Before] DeletedGeneratedElements=" + deletedCount +
                    ", RowCount=" + relatedRows.Count +
                    ", Errors=" + errors.Count);
            }

            if (normalized.ClearTrackingState)
            {
                List<WizardGenerationRowRecord> remained = allRows
                    .Where(x => x != null && !relatedRows.Contains(x))
                    .ToList();
                WizardGenerationTrackingStoreService.Save(doc, remained);
                DiagnosticRecorder.AppendDebug("[DwgReset.Before] TrackingRowsSaved=" + remained.Count);
            }

            if (normalized.ClearMappingState)
            {
                // Keep only current runtime context after import; stale mapping/session must be cleared now.
                WizardStateStoreService.Clear(doc);
                WizardSessionCache.Clear(doc);
                DiagnosticRecorder.AppendDebug("[DwgReset.Before] Cleared mapping state and session cache.");
            }

            DwgSessionManager.Clear(doc);
            DiagnosticRecorder.AppendDebug("[DwgReset.Before] Session cleared.");
        }

        public static void ResetAfterImport(
            Document doc,
            ElementId newImportId,
            string filePath,
            IEnumerable<string> layers,
            SourceUnit sourceUnit,
            string sourceUnitEvidence)
        {
            if (doc == null || newImportId == null || newImportId == ElementId.InvalidElementId)
            {
                return;
            }

            List<string> currentLayers = (layers ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            DwgSessionManager.Set(doc, new DwgSessionInfo
            {
                LinkInstanceId = newImportId,
                FilePath = filePath ?? string.Empty,
                ImportTime = DateTime.Now,
                DwgLayers = currentLayers,
                SourceUnit = sourceUnit,
                SourceUnitEvidence = sourceUnitEvidence ?? string.Empty
            });
            DwgSessionManager.ApplyFileFingerprint(DwgSessionManager.Get(doc), filePath);

            // Ensure no stale context signature can be reused after DWG switch.
            WizardStateStoreService.Clear(doc);
            WizardSessionCache.Clear(doc);

            Level level = ResolveLevel(doc);
            string contextSignature = WizardSessionCache.BuildContextSignature(newImportId, level?.Id, sourceUnit);
            DiagnosticRecorder.AppendDebug(
                "[DwgReset.After] Session set, DwgId=" + newImportId.IntegerValue +
                ", Layers=" + currentLayers.Count +
                ", Context=" + contextSignature);
        }

        private static List<WizardGenerationRowRecord> GetRowsForCurrentDwg(
            List<WizardGenerationRowRecord> allRows,
            DwgSessionInfo session)
        {
            if (allRows == null || allRows.Count == 0)
            {
                return new List<WizardGenerationRowRecord>();
            }

            int sessionDwgId = session?.LinkInstanceId?.IntegerValue ?? ElementId.InvalidElementId.IntegerValue;
            if (sessionDwgId > 0)
            {
                return allRows.Where(x => x != null && x.DwgId == sessionDwgId).ToList();
            }

            // Single-context strategy: if current DWG id is unknown, treat all tracked rows as stale.
            return allRows.Where(x => x != null).ToList();
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
    }
}
