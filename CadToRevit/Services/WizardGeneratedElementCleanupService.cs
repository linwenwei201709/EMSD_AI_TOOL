using Autodesk.Revit.DB;
using CadToRevit.Models.Mapping;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class WizardGeneratedElementCleanupService
    {
        public static CleanupRowResult DeleteRowGeneratedElements(
            Document doc,
            WizardGenerationRowRecord record,
            List<string> errors)
        {
            CleanupRowResult result = new CleanupRowResult
            {
                RowKey = record?.RowKey ?? string.Empty,
                RequestedCount = record?.ElementIds?.Count ?? 0
            };

            if (doc == null || record == null || record.ElementIds == null || record.ElementIds.Count == 0)
            {
                return result;
            }

            DiagnosticRecorder.AppendDebug(
                "[RowDeletePlan] TargetRowKey=" + (record.RowKey ?? string.Empty) +
                ", LayerName=" + (record.RawLayerName ?? string.Empty) +
                ", RequestedDeleteCount=" + result.RequestedCount);

            string targetRowKey = WizardGenerationTrackingStoreService.NormalizeRowKey(record.RowKey);
            Dictionary<int, GeneratedElementMetadataSnapshot> generatedSnapshotIndex = GeneratedElementMetadataService.BuildGeneratedSnapshotIndex(doc);
            List<ElementId> idsToDelete = new List<ElementId>();
            HashSet<int> targetWallIds = new HashSet<int>();
            foreach (int id in record.ElementIds.Distinct())
            {
                Element elem = doc.GetElement(new ElementId(id));
                if (elem == null)
                {
                    result.MissingElementIds.Add(id);
                    continue;
                }

                if (DetachedGeneratedElementMetadataService.IsDetached(elem))
                {
                    result.SkippedDetachedElementIds.Add(id);
                    DiagnosticRecorder.AppendDebug("[RowDeleteSkipDetached] ElementId=" + id + ", RowKey=" + (record.RowKey ?? string.Empty));
                    continue;
                }

                if (generatedSnapshotIndex.TryGetValue(id, out GeneratedElementMetadataSnapshot actualSnapshot))
                {
                    string normalizedActual = WizardGenerationTrackingStoreService.NormalizeRowKey(actualSnapshot.RowKey);
                    if (!string.Equals(normalizedActual, targetRowKey, StringComparison.OrdinalIgnoreCase))
                    {
                        result.SkippedForeignElementIds.Add(id);
                        continue;
                    }
                }

                idsToDelete.Add(elem.Id);
                if (elem.Category != null && elem.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Walls)
                {
                    targetWallIds.Add(elem.Id.IntegerValue);
                }
            }

            result.ExistingCount = idsToDelete.Count;
            if (result.SkippedForeignElementIds.Count > 0)
            {
                result.HasWarning = true;
                result.WarningMessage =
                    "[RowDeleteWarning] RowKey=" + result.RowKey +
                    ", RequestedDeleteCount=" + result.RequestedCount +
                    ", ActualDeletedCount=0" +
                    ", Warning=Skipped foreign tracked elements before delete" +
                    ", ForeignCount=" + result.SkippedForeignElementIds.Count;
            }

            if (idsToDelete.Count == 0)
            {
                return result;
            }

            try
            {
                Action deleteAction = () =>
                {
                    using (SubTransaction st = new SubTransaction(doc))
                    {
                        st.Start();
                        ICollection<ElementId> deleted = doc.Delete(idsToDelete);
                        List<ElementId> deletedIds = deleted == null ? new List<ElementId>() : deleted.ToList();
                        int deletedCount = deletedIds.Count;

                        result.DeletedElementIds = deletedIds.Select(x => x.IntegerValue).Distinct().ToList();
                        foreach (ElementId id in deletedIds)
                        {
                            if (!generatedSnapshotIndex.TryGetValue(id.IntegerValue, out GeneratedElementMetadataSnapshot relatedSnapshot))
                            {
                                continue;
                            }

                            string normalizedRelated = WizardGenerationTrackingStoreService.NormalizeRowKey(relatedSnapshot.RowKey);
                            if (string.Equals(normalizedRelated, targetRowKey, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            bool isAllowedDependent = IsAllowedDependentDelete(relatedSnapshot, targetWallIds);
                            string decision = isAllowedDependent ? "AllowedDependentDelete" : "DangerousCrossRowDelete";
                            string decisionLog =
                                "[RowDeleteForeign] TargetRowKey=" + result.RowKey +
                                ", DeletedElementId=" + id.IntegerValue +
                                ", DeletedRowKey=" + normalizedRelated +
                                ", DeletedCategory=" + relatedSnapshot.CategoryId +
                                ", HostId=" + relatedSnapshot.HostId +
                                ", Decision=" + decision;
                            result.ForeignDeleteDecisionLogs.Add(decisionLog);

                            if (isAllowedDependent)
                            {
                                result.AllowedDependentDeletedElementIds.Add(id.IntegerValue);
                            }
                            else
                            {
                                result.DangerousForeignDeletedElementIds.Add(id.IntegerValue);
                            }
                        }

                        if (result.DangerousForeignDeletedElementIds.Count > 0)
                        {
                            st.RollBack();
                            result.DeletedCount = 0;
                            result.DeletedElementIds.Clear();
                            result.HasWarning = true;
                            result.WarningMessage =
                                "[RowDeleteWarning] RowKey=" + result.RowKey +
                                ", RequestedDeleteCount=" + result.RequestedCount +
                                ", ActualDeletedCount=0" +
                                ", Warning=Cross-row delete detected and rollback executed" +
                                ", AllowedDependentDeleteCount=" + result.AllowedDependentDeletedElementIds.Distinct().Count() +
                                ", DangerousForeignDeleteCount=" + result.DangerousForeignDeletedElementIds.Distinct().Count();
                            foreach (int id in result.DangerousForeignDeletedElementIds.Distinct())
                            {
                                result.SkippedForeignElementIds.Add(id);
                            }

                            return;
                        }

                        st.Commit();
                        result.DeletedCount = deletedCount;
                    }
                };

                if (doc.IsModifiable)
                {
                    deleteAction();
                }
                else
                {
                    using (Transaction tx = new Transaction(doc, "CadToRevit Cleanup Generated Row"))
                    {
                        tx.Start();
                        deleteAction();
                        tx.Commit();
                    }
                }
            }
            catch (Exception ex)
            {
                string error = "[RowDelete] RowKey=" + result.RowKey + ", Error=" + ex.Message;
                errors?.Add(error);
                DiagnosticRecorder.AppendDebug(error);
            }

            if (result.DeletedCount > result.RequestedCount)
            {
                result.HasWarning = true;
                result.WarningMessage =
                    "[RowDeleteWarning] RowKey=" + result.RowKey +
                    ", RequestedDeleteCount=" + result.RequestedCount +
                    ", ActualDeletedCount=" + result.DeletedCount +
                    ", Warning=ActualDeletedCount exceeds RequestedDeleteCount";
            }

            DiagnosticRecorder.AppendDebug(
                "[RowDeleteResult] RowKey=" + result.RowKey +
                ", LayerName=" + (record.RawLayerName ?? string.Empty) +
                ", RequestedDeleteCount=" + result.RequestedCount +
                ", ActualDeletedCount=" + result.DeletedCount +
                ", AllowedDependentDeleteCount=" + result.AllowedDependentDeletedElementIds.Distinct().Count() +
                ", DangerousForeignDeleteCount=" + result.DangerousForeignDeletedElementIds.Distinct().Count());

            return result;
        }

        private static bool IsAllowedDependentDelete(GeneratedElementMetadataSnapshot snapshot, HashSet<int> targetWallIds)
        {
            if (snapshot == null)
            {
                return false;
            }

            bool isDoorOrWindow =
                snapshot.CategoryId == (int)BuiltInCategory.OST_Doors ||
                snapshot.CategoryId == (int)BuiltInCategory.OST_Windows;
            if (!isDoorOrWindow)
            {
                return false;
            }

            if (snapshot.HostId <= 0 || targetWallIds == null || targetWallIds.Count == 0)
            {
                return false;
            }

            return targetWallIds.Contains(snapshot.HostId);
        }
    }
}
