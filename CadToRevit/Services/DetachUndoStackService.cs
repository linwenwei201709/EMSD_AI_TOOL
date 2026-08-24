using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public sealed class DetachUndoBatch
    {
        public Guid BatchId { get; set; } = Guid.NewGuid();

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<DetachUndoItem> Items { get; set; } = new List<DetachUndoItem>();
    }

    public sealed class DetachUndoItem
    {
        public ElementId ElementId { get; set; }

        public string UniqueId { get; set; }

        public string OriginalLayerName { get; set; }

        public string OriginalCategory { get; set; }

        public string OriginalFamilyType { get; set; }

        public OverrideGraphicSettings OriginalViewOverride { get; set; }

        public ElementId ViewId { get; set; }
    }

    public static class DetachUndoStackService
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, Stack<DetachUndoBatch>> Stacks =
            new Dictionary<string, Stack<DetachUndoBatch>>(StringComparer.OrdinalIgnoreCase);

        public static void Push(Document doc, DetachUndoBatch batch)
        {
            if (doc == null || batch == null || batch.Items == null || batch.Items.Count == 0)
            {
                return;
            }

            string key = BuildDocKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (SyncRoot)
            {
                if (!Stacks.TryGetValue(key, out Stack<DetachUndoBatch> stack) || stack == null)
                {
                    stack = new Stack<DetachUndoBatch>();
                    Stacks[key] = stack;
                }

                stack.Push(batch);
            }
        }

        public static int GetLatestRestorableCount(Document doc)
        {
            DetachUndoBatch batch = PeekLatestRestorableBatch(doc);
            if (batch == null || batch.Items == null)
            {
                return 0;
            }

            return batch.Items.Count(x => IsItemStillDetached(doc, x));
        }

        public static RestoreBindingResult UndoLastDetachBatch(Document doc)
        {
            RestoreBindingResult empty = new RestoreBindingResult();
            DetachUndoBatch batch = PopLatestRestorableBatch(doc);
            if (doc == null || batch == null || batch.Items == null || batch.Items.Count == 0)
            {
                return empty;
            }

            List<DetachUndoItem> items = batch.Items
                .Where(x => IsItemStillDetached(doc, x))
                .ToList();
            if (items.Count == 0)
            {
                empty.RequestedCount = batch.Items.Count;
                return empty;
            }

            List<ElementId> ids = items
                .Select(x => ResolveElement(doc, x))
                .Where(x => x != null)
                .Select(x => x.Id)
                .ToList();

            List<ElementOverrideRestoreInfo> overrides = items
                .Where(x => x.OriginalViewOverride != null)
                .Select(x => new ElementOverrideRestoreInfo
                {
                    ElementId = ResolveElement(doc, x)?.Id ?? ElementId.InvalidElementId,
                    ViewId = x.ViewId,
                    Override = x.OriginalViewOverride
                })
                .Where(x => x.ElementId != null && x.ElementId != ElementId.InvalidElementId)
                .ToList();

            RestoreBindingResult result = GeneratedElementBindingRestoreService.RestoreDetachedElements(
                doc,
                ids,
                overrides,
                "Undo Detach Elements");
            result.RequestedCount = batch.Items.Count;
            DiagnosticRecorder.AppendDebug("[UndoDetach] BatchId=" + batch.BatchId + ", Requested=" + batch.Items.Count + ", Restored=" + result.RestoredCount);
            return result;
        }

        private static DetachUndoBatch PeekLatestRestorableBatch(Document doc)
        {
            string key = BuildDocKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            lock (SyncRoot)
            {
                if (!Stacks.TryGetValue(key, out Stack<DetachUndoBatch> stack) || stack == null)
                {
                    return null;
                }

                while (stack.Count > 0)
                {
                    DetachUndoBatch batch = stack.Peek();
                    if (batch != null && batch.Items != null && batch.Items.Any(x => IsItemStillDetached(doc, x)))
                    {
                        return batch;
                    }

                    stack.Pop();
                }
            }

            return null;
        }

        private static DetachUndoBatch PopLatestRestorableBatch(Document doc)
        {
            string key = BuildDocKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            lock (SyncRoot)
            {
                if (!Stacks.TryGetValue(key, out Stack<DetachUndoBatch> stack) || stack == null)
                {
                    return null;
                }

                while (stack.Count > 0)
                {
                    DetachUndoBatch batch = stack.Pop();
                    if (batch != null && batch.Items != null && batch.Items.Any(x => IsItemStillDetached(doc, x)))
                    {
                        return batch;
                    }
                }
            }

            return null;
        }

        private static bool IsItemStillDetached(Document doc, DetachUndoItem item)
        {
            Element element = ResolveElement(doc, item);
            return element != null && DetachedGeneratedElementMetadataService.IsDetached(element);
        }

        private static Element ResolveElement(Document doc, DetachUndoItem item)
        {
            if (doc == null || item == null)
            {
                return null;
            }

            if (item.ElementId != null && item.ElementId != ElementId.InvalidElementId)
            {
                Element byId = doc.GetElement(item.ElementId);
                if (byId != null)
                {
                    return byId;
                }
            }

            return !string.IsNullOrWhiteSpace(item.UniqueId) ? doc.GetElement(item.UniqueId) : null;
        }

        private static string BuildDocKey(Document doc)
        {
            if (doc == null)
            {
                return string.Empty;
            }

            return (doc.PathName ?? string.Empty) + "|" + (doc.Title ?? string.Empty) + "|" + doc.GetHashCode();
        }
    }
}
