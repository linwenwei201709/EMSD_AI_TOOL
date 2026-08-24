using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.PathObstacles;
using CadToRevit.Services.PathPreview;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.UI.PathObstacles
{
    internal sealed class PathObstacleExternalEventHandler : IExternalEventHandler
    {
        private readonly object _sync = new object();
        private readonly Queue<PathObstacleExternalEventRequest> _requests = new Queue<PathObstacleExternalEventRequest>();

        public void SetRequest(PathObstacleExternalEventRequest request)
        {
            lock (_sync)
            {
                if (request != null)
                {
                    _requests.Enqueue(request);
                }
            }
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                PathObstacleExternalEventRequest request;
                lock (_sync)
                {
                    request = _requests.Count > 0 ? _requests.Dequeue() : null;
                }

                if (request == null || request.Type == PathObstacleRequestType.None)
                {
                    return;
                }

                try
                {
                    UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
                    Document doc = uiDoc != null ? uiDoc.Document : null;
                    if (doc == null)
                    {
                        PathObstacleRuntime.UpdateRecords(new List<PathObstacleRecord>());
                        return;
                    }

                    switch (request.Type)
                    {
                        case PathObstacleRequestType.Refresh:
                            Refresh(doc);
                            break;

                        case PathObstacleRequestType.Locate:
                            if (!PathObstacleLocateService.Locate(app, request.Record))
                            {
                                PathObstacleNoticeWindow.Show("Restricted Area", "This restricted area no longer exists.");
                                Refresh(doc);
                            }
                            break;

                        case PathObstacleRequestType.Delete:
                            Delete(doc, request.Record);
                            Refresh(doc);
                            break;

                        case PathObstacleRequestType.DeleteAll:
                            DeleteAll(doc);
                            Refresh(doc);
                            break;

                        case PathObstacleRequestType.Rename:
                            Rename(doc, request.Record, request.NewName);
                            Refresh(doc);
                            break;

                        case PathObstacleRequestType.BeginDrawing:
                            PathObstacleRuntime.ExecuteBeginDrawing(app);
                            break;

                        case PathObstacleRequestType.PickNextPoint:
                            PathObstacleRuntime.ExecutePickNextPoint(app);
                            break;

                        case PathObstacleRequestType.FinishDrawing:
                            PathObstacleRuntime.ExecuteFinishDrawing(app);
                            break;

                        case PathObstacleRequestType.CancelDrawing:
                            PathObstacleRuntime.ExecuteCancelDrawing(app);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[PathObstacleManager] ExternalEvent failed: " + ex);

                    // Never stack a modal notice window on top of an active
                    // PickPoint session. Clean the drawing state first so Revit
                    // cannot remain trapped in selection mode.
                    if (PathObstacleRuntime.IsDrawingActive)
                    {
                        PathObstacleRuntime.AbortDrawingAfterUnexpectedFailure(app);
                        return;
                    }

                    PathObstacleNoticeWindow.Show("Restricted Area", ex.Message);
                }
            }
        }

        public string GetName()
        {
            return "CadToRevit PathObstacle ExternalEvent";
        }

        private static void Refresh(Document doc)
        {
            PathObstacleRuntime.UpdateRecords(PathObstacleStoreService.Load(doc));
        }

        private static void Rename(Document doc, PathObstacleRecord record, string newName)
        {
            string value = PathObstacleMetadataService.SanitizeName(newName);
            if (string.IsNullOrWhiteSpace(value))
            {
                PathObstacleNoticeWindow.Show("Restricted Area", "Please enter a restricted area name.");
                return;
            }

            using (Transaction transaction = new Transaction(doc, "Rename Restricted Area"))
            {
                transaction.Start();
                PathObstacleStoreService.Rename(doc, record, value);
                transaction.Commit();
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "Restricted area was renamed.");
        }

        private static void DeleteAll(Document doc)
        {
            IEnumerable<PathObstacleRecord> records =
                PathObstacleStoreService.Load(doc) ?? Enumerable.Empty<PathObstacleRecord>();

            List<ElementId> ids = records
                .Where(record => record != null)
                .Select(record => PathObstacleStoreService.FindElement(doc, record))
                .Where(element => element != null)
                .Select(element => element.Id)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
            {
                return;
            }

            using (Transaction transaction = new Transaction(doc, "Delete All Restricted Areas"))
            {
                transaction.Start();
                foreach (ElementId id in ids)
                {
                    doc.Delete(id);
                }
                transaction.Commit();
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "All restricted areas were deleted.");
        }

        private static void Delete(Document doc, PathObstacleRecord record)
        {
            Element element = PathObstacleStoreService.FindElement(doc, record);
            if (element == null)
            {
                PathObstacleNoticeWindow.Show("Restricted Area", "This restricted area no longer exists.");
                return;
            }

            using (Transaction transaction = new Transaction(doc, "Delete Path Obstacle"))
            {
                transaction.Start();
                doc.Delete(element.Id);
                transaction.Commit();
            }

            RoutePlannerSessionCacheService.MarkDirty(doc, "Path obstacle was deleted.");
        }
    }
}
