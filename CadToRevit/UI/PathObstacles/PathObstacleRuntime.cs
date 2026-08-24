using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using CadToRevit.Commands;
using CadToRevit.Models;
using CadToRevit.Services.Diagnostics;
using CadToRevit.UI.Dockable;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace CadToRevit.UI.PathObstacles
{
    public static class PathObstacleRuntime
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("597DC78A-05F4-4B4A-839E-2672C6743B4D"));

        private static PathObstacleExternalEventHandler _handler;
        private static ExternalEvent _externalEvent;
        private static PathObstacleDrawingSession _drawingSession;

        public static PathObstaclePaneViewModel ViewModel { get; } = new PathObstaclePaneViewModel();

        internal static bool IsDrawingActive
        {
            get
            {
                PathObstacleDrawingSession session = _drawingSession;
                return session != null && session.IsActive;
            }
        }

        public static void InitializeExternalEvent()
        {
            if (_externalEvent != null)
            {
                return;
            }

            _handler = new PathObstacleExternalEventHandler();
            _externalEvent = ExternalEvent.Create(_handler);
        }

        public static void ShowManager(UIApplication uiApp)
        {
            ShowPane(uiApp);
        }

        public static void ShowPane(UIApplication uiApp)
        {
            InitializeExternalEvent();

            // Restricted Area and Room Management share the left side of Revit.
            // Only hide the Room & Lift pane here; keep right-side Layout Plan /
            // Delivery Route panes untouched.
            RoomRecognitionPaneRuntime.HideRoomAndLiftPane(uiApp);
            TryHidePreviewPane(uiApp);
            TryHidePropertiesPalette(uiApp);
            TryShowPane(uiApp, PaneId);
            RequestRefresh();
        }

        public static void HidePane(UIApplication uiApp)
        {
            PathObstacleDrawingSession session = _drawingSession;
            if (session != null && session.IsActive)
            {
                RequestCancelDrawing();
            }

            TryHidePane(uiApp, PaneId);
        }

        internal static void UpdateRecords(IEnumerable<PathObstacleRecord> records)
        {
            ViewModel.SetRecords(records);
            PathObstacleManagerWindow.UpdateRecords(records);
        }

        internal static void RequestRefresh()
        {
            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.Refresh });
        }

        internal static void RequestLocate(PathObstacleRecord record)
        {
            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.Locate, Record = record });
        }

        internal static void RequestDelete(PathObstacleRecord record)
        {
            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.Delete, Record = record });
        }

        internal static void RequestDeleteAll()
        {
            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.DeleteAll });
        }

        internal static void RequestRename(PathObstacleRecord record, string newName)
        {
            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.Rename, Record = record, NewName = newName });
        }

        internal static void RequestBeginDrawing()
        {
            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.BeginDrawing });
        }

        internal static void RequestFinishDrawing()
        {
            PathObstacleDrawingSession session = _drawingSession;
            if (session == null || !session.IsActive)
            {
                return;
            }

            session.FinishRequested = true;
            session.CancelRequested = false;

            // PickPoint() blocks the current Revit ExternalEvent. A second
            // ExternalEvent cannot run until that PickPoint returns. Therefore
            // finish/cancel must first interrupt the active pick with ESC, then
            // the same ExternalEvent completes the requested action.
            if (session.IsPicking)
            {
                InterruptActivePick(session);
                return;
            }

            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.FinishDrawing });
        }

        internal static void RequestCancelDrawing()
        {
            PathObstacleDrawingSession session = _drawingSession;
            if (session == null || !session.IsActive)
            {
                return;
            }

            session.CancelRequested = true;
            session.FinishRequested = false;

            if (session.IsPicking)
            {
                InterruptActivePick(session);
                return;
            }

            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.CancelDrawing });
        }

        internal static void ExecuteBeginDrawing(UIApplication app)
        {
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (doc == null)
            {
                return;
            }

            EndDrawing(app, true);

            _drawingSession = new PathObstacleDrawingSession
            {
                IsActive = true,
                App = app,
                BaseElevation = DrawPathObstacleCommand.PrepareDrawing(doc)
            };
            ViewModel.IsDrawing = true;

            PathObstacleDrawingBarWindow bar = new PathObstacleDrawingBarWindow();
            bar.AttachToRevit(app);
            bar.Show();
            _drawingSession.BarWindow = bar;
            SchedulePickNextPoint();
        }

        internal static void ExecutePickNextPoint(UIApplication app)
        {
            PathObstacleDrawingSession session = _drawingSession;
            UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
            if (session == null || !session.IsActive || uiDoc == null || session.CancelRequested || session.FinishRequested)
            {
                return;
            }

            try
            {
                session.IsPicking = true;
                XYZ point = DrawPathObstacleCommand.PickSinglePoint(uiDoc, session.BaseElevation, session.TemporaryMarkerIds);
                if (point != null)
                {
                    session.PickedPoints.Add(point);
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // ESC can come from the user directly, or from the Drawing Bar
                // when Finish/Cancel is clicked. Preserve the already selected
                // action instead of converting every ESC into Cancel.
                if (!session.FinishRequested && !session.CancelRequested)
                {
                    session.CancelRequested = true;
                }
            }
            finally
            {
                session.IsPicking = false;
            }

            if (session.CancelRequested)
            {
                ExecuteCancelDrawing(app);
                return;
            }

            if (session.FinishRequested)
            {
                ExecuteFinishDrawing(app);
                return;
            }

            SchedulePickNextPoint();
        }

        internal static void ExecuteFinishDrawing(UIApplication app)
        {
            PathObstacleDrawingSession session = _drawingSession;
            if (session == null || !session.IsActive || session.IsPicking)
            {
                return;
            }

            if (session.PickedPoints.Count < 3)
            {
                session.FinishRequested = false;
                UpdateDrawingInstruction(session, "Please select at least 3 points before finishing.", true);
                SchedulePickNextPoint();
                return;
            }

            ElementId createdId;
            string message;
            if (!DrawPathObstacleCommand.TryCreateRestrictedAreaFromPoints(app, session.PickedPoints, session.TemporaryMarkerIds, out createdId, out message))
            {
                session.FinishRequested = false;

                // Do not stack modal dialogs on top of an active PickPoint
                // workflow. Show the validation error inside the Drawing Bar
                // and reset the points so the user can redraw cleanly.
                UIDocument uiDoc = app != null ? app.ActiveUIDocument : null;
                Document doc = uiDoc != null ? uiDoc.Document : null;
                DrawPathObstacleCommand.ClearTemporaryPickMarkers(doc, session.TemporaryMarkerIds);
                session.PickedPoints.Clear();

                UpdateDrawingInstruction(
                    session,
                    string.IsNullOrWhiteSpace(message)
                        ? "The selected points could not form a valid restricted area. Please redraw the polygon."
                        : message,
                    true);

                SchedulePickNextPoint();
                return;
            }

            EndDrawing(app, true);
            RequestRefresh();
        }

        internal static void ExecuteCancelDrawing(UIApplication app)
        {
            EndDrawing(app, true);
        }

        internal static void AbortDrawingAfterUnexpectedFailure(UIApplication app)
        {
            EndDrawing(app, true);
        }

        private static void EndDrawing(UIApplication app, bool clearMarkers)
        {
            PathObstacleDrawingSession session = _drawingSession;
            _drawingSession = null;
            ViewModel.IsDrawing = false;

            if (session == null)
            {
                return;
            }

            if (clearMarkers)
            {
                UIDocument uiDoc = app != null ? app.ActiveUIDocument : session.App != null ? session.App.ActiveUIDocument : null;
                Document doc = uiDoc != null ? uiDoc.Document : null;
                DrawPathObstacleCommand.ClearTemporaryPickMarkers(doc, session.TemporaryMarkerIds);
            }

            PathObstacleDrawingBarWindow window = session.BarWindow;
            if (window != null)
            {
                window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (window.IsVisible)
                    {
                        window.Close();
                    }
                }));
            }
        }

        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int VkEscape = 0x1B;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static void InterruptActivePick(PathObstacleDrawingSession session)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                IntPtr mainWindowHandle = session.App != null
                    ? session.App.MainWindowHandle
                    : IntPtr.Zero;

                if (mainWindowHandle == IntPtr.Zero)
                {
                    return;
                }

                SetForegroundWindow(mainWindowHandle);
                PostMessage(mainWindowHandle, WmKeyDown, new IntPtr(VkEscape), IntPtr.Zero);
                PostMessage(mainWindowHandle, WmKeyUp, new IntPtr(VkEscape), IntPtr.Zero);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstacleDrawing] Failed to interrupt PickPoint: " + ex.Message);
            }
        }

        private static void UpdateDrawingInstruction(
            PathObstacleDrawingSession session,
            string message,
            bool isError)
        {
            PathObstacleDrawingBarWindow bar = session != null ? session.BarWindow : null;
            if (bar == null)
            {
                return;
            }

            bar.SetInstruction(message, isError);
        }

        private static void SchedulePickNextPoint()
        {
            Application application = Application.Current;
            if (application != null && application.Dispatcher != null)
            {
                application.Dispatcher.BeginInvoke(new Action(delegate
                {
                    Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.PickNextPoint });
                }));
                return;
            }

            Raise(new PathObstacleExternalEventRequest { Type = PathObstacleRequestType.PickNextPoint });
        }

        private static void Raise(PathObstacleExternalEventRequest request)
        {
            if (_handler == null || _externalEvent == null)
            {
                InitializeExternalEvent();
            }

            if (_handler == null || _externalEvent == null)
            {
                return;
            }

            _handler.SetRequest(request);
            _externalEvent.Raise();
        }

        private static void TryShowPane(UIApplication uiApp, DockablePaneId paneId)
        {
            try
            {
                uiApp?.GetDockablePane(paneId)?.Show();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstaclePane] Show pane failed: " + ex.Message);
            }
        }

        private static void TryHidePane(UIApplication uiApp, DockablePaneId paneId)
        {
            try
            {
                uiApp?.GetDockablePane(paneId)?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstaclePane] Hide pane failed: " + ex.Message);
            }
        }

        private static void TryHidePreviewPane(UIApplication uiApp)
        {
            try
            {
                uiApp?.GetDockablePane(PreviewPaneRuntime.PaneId)?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstaclePane] Hide preview pane failed: " + ex.Message);
            }
        }

        private static void TryHidePropertiesPalette(UIApplication uiApp)
        {
            try
            {
                uiApp?.GetDockablePane(Autodesk.Revit.UI.DockablePanes.BuiltInDockablePanes.PropertiesPalette)?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathObstaclePane] Hide properties palette failed: " + ex.Message);
            }
        }

        private sealed class PathObstacleDrawingSession
        {
            public bool IsActive { get; set; }
            public bool IsPicking { get; set; }
            public bool FinishRequested { get; set; }
            public bool CancelRequested { get; set; }
            public double BaseElevation { get; set; }
            public UIApplication App { get; set; }
            public List<XYZ> PickedPoints { get; } = new List<XYZ>();
            public List<ElementId> TemporaryMarkerIds { get; } = new List<ElementId>();
            public PathObstacleDrawingBarWindow BarWindow { get; set; }
        }
    }
}
