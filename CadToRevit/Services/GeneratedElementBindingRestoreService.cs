using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Mapping;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services
{
    public sealed class RestoreBindingResult
    {
        public int RequestedCount { get; set; }

        public int RestoredCount { get; set; }

        public List<int> SkippedElementIds { get; set; } = new List<int>();

        public List<string> Errors { get; set; } = new List<string>();
    }

    public static class GeneratedElementBindingRestoreService
    {
        public static int CountRestorableSelectedBindings(UIDocument uiDoc)
        {
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null || uiDoc.Selection == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ElementId id in uiDoc.Selection.GetElementIds())
            {
                Element element = doc.GetElement(id);
                if (DetachedGeneratedElementMetadataService.IsDetached(element))
                {
                    count++;
                }
            }

            return count;
        }

        public static int CountDetachableSelectedElements(UIDocument uiDoc)
        {
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null || uiDoc.Selection == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ElementId id in uiDoc.Selection.GetElementIds())
            {
                Element element = doc.GetElement(id);
                if (element == null || DetachedGeneratedElementMetadataService.IsDetached(element))
                {
                    continue;
                }

                bool hasFullMetadata = GeneratedElementMetadataService.TryGetFullMetadata(element, out GeneratedElementFullMetadataSnapshot snapshot) &&
                    snapshot != null &&
                    !string.IsNullOrWhiteSpace(snapshot.RowKey);
                if (hasFullMetadata)
                {
                    count++;
                }
            }

            return count;
        }

        public static RestoreBindingResult RestoreSelectedBindings(UIDocument uiDoc)
        {
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null || uiDoc.Selection == null)
            {
                RestoreBindingResult result = new RestoreBindingResult();
                result.Errors.Add("No active document.");
                return result;
            }

            List<ElementId> selectedIds = uiDoc.Selection.GetElementIds().ToList();
            return RestoreDetachedElements(doc, selectedIds, null, "Restore CAD Generated Element Binding");
        }

        public static RestoreBindingResult RestoreDetachedElements(
            Document doc,
            IEnumerable<ElementId> elementIds,
            IEnumerable<ElementOverrideRestoreInfo> originalOverrides,
            string transactionName)
        {
            RestoreBindingResult result = new RestoreBindingResult();
            if (doc == null)
            {
                result.Errors.Add("No active document.");
                return result;
            }

            List<ElementId> selectedIds = (elementIds ?? Enumerable.Empty<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct(new ElementIdComparer())
                .ToList();
            result.RequestedCount = selectedIds.Count;
            if (selectedIds.Count == 0)
            {
                return result;
            }

            List<RestoreTarget> targets = new List<RestoreTarget>();
            foreach (ElementId id in selectedIds)
            {
                Element element = doc.GetElement(id);
                if (element == null)
                {
                    result.SkippedElementIds.Add(id.IntegerValue);
                    continue;
                }

                if (!DetachedGeneratedElementMetadataService.TryGetDetachedSnapshot(element, out DetachedGeneratedElementSnapshot snapshot))
                {
                    result.SkippedElementIds.Add(id.IntegerValue);
                    continue;
                }

                targets.Add(new RestoreTarget
                {
                    ElementId = id,
                    Element = element,
                    Snapshot = snapshot
                });
            }

            if (targets.Count == 0)
            {
                return result;
            }

            List<ElementOverrideRestoreInfo> overrideInfos = (originalOverrides ?? Enumerable.Empty<ElementOverrideRestoreInfo>())
                .Where(x => x != null && x.ElementId != null && x.ElementId != ElementId.InvalidElementId)
                .ToList();

            List<WizardGenerationRowRecord> rows = WizardGenerationTrackingStoreService.Load(doc)
                .Where(x => x != null)
                .ToList();

            using (Transaction tx = new Transaction(doc, string.IsNullOrWhiteSpace(transactionName) ? "Restore CAD Generated Element Binding" : transactionName))
            {
                tx.Start();
                foreach (RestoreTarget target in targets)
                {
                    try
                    {
                        GeneratedElementMetadataService.WriteBatch(
                            doc,
                            new[] { target.ElementId },
                            target.Snapshot.OriginalRowKey,
                            target.Snapshot.OriginalGenerationBatchId,
                            target.Snapshot.OriginalRawLayerName,
                            target.Snapshot.OriginalCategory,
                            target.Snapshot.OriginalLevelId,
                            target.Snapshot.OriginalDwgId);

                        DetachedGeneratedElementMetadataService.ClearDetachedSnapshot(target.Element);
                        AddBackToTrackingRows(rows, target);
                        result.RestoredCount++;
                    }
                    catch (Exception ex)
                    {
                        result.SkippedElementIds.Add(target.ElementId.IntegerValue);
                        result.Errors.Add("Element " + target.ElementId.IntegerValue.ToString(CultureInfo.InvariantCulture) + ": " + ex.Message);
                    }
                }

                WizardGenerationTrackingStoreService.Save(doc, rows);
                if (overrideInfos.Count > 0)
                {
                    DetachedElementVisualOverrideService.RestoreOriginalOverrides(doc, overrideInfos);
                }
                else
                {
                    DetachedElementVisualOverrideService.ClearDetachedOverride(doc, targets.Select(x => x.ElementId));
                }
                tx.Commit();
            }

            return result;
        }

        private static void AddBackToTrackingRows(List<WizardGenerationRowRecord> rows, RestoreTarget target)
        {
            DetachedGeneratedElementSnapshot snapshot = target.Snapshot;
            string normalizedRowKey = WizardGenerationTrackingStoreService.NormalizeRowKey(snapshot.OriginalRowKey);
            WizardGenerationRowRecord row = rows.FirstOrDefault(x =>
                string.Equals(
                    WizardGenerationTrackingStoreService.NormalizeRowKey(x.RowKey),
                    normalizedRowKey,
                    StringComparison.OrdinalIgnoreCase));

            if (row == null)
            {
                row = new WizardGenerationRowRecord
                {
                    RowKey = snapshot.OriginalRowKey,
                    RawLayerName = snapshot.OriginalRawLayerName,
                    Category = snapshot.OriginalCategory,
                    LevelId = snapshot.OriginalLevelId,
                    DwgId = snapshot.OriginalDwgId,
                    GenerationBatchId = snapshot.OriginalGenerationBatchId,
                    LastSyncAction = "RestoreBinding",
                    LastSyncReason = "Restored detached element binding",
                    LastSyncedAt = DateTime.UtcNow.ToString("o"),
                    ElementIds = new List<int>()
                };
                rows.Add(row);
            }

            if (row.ElementIds == null)
            {
                row.ElementIds = new List<int>();
            }

            int id = target.ElementId.IntegerValue;
            if (!row.ElementIds.Contains(id))
            {
                row.ElementIds.Add(id);
            }

            row.ElementIds = row.ElementIds.Distinct().OrderBy(x => x).ToList();
            row.GeneratedCount = row.ElementIds.Count;
            row.LastSyncAction = "RestoreBinding";
            row.LastSyncReason = "Restored detached element binding";
            row.LastSyncedAt = DateTime.UtcNow.ToString("o");
        }

        private sealed class RestoreTarget
        {
            public ElementId ElementId { get; set; }

            public Element Element { get; set; }

            public DetachedGeneratedElementSnapshot Snapshot { get; set; }
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                int left = x != null ? x.IntegerValue : ElementId.InvalidElementId.IntegerValue;
                int right = y != null ? y.IntegerValue : ElementId.InvalidElementId.IntegerValue;
                return left == right;
            }

            public int GetHashCode(ElementId obj)
            {
                return obj != null ? obj.IntegerValue.GetHashCode() : 0;
            }
        }
    }
}
