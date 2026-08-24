using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using System;

namespace CadToRevit.UI.Dockable
{
    public static class DeliveryRoutePaneRuntime
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("7386F728-C718-4326-A065-34450429AA8B"));

        public static DeliveryRoutePaneViewModel ViewModel { get; } = new DeliveryRoutePaneViewModel();

        public static void Show(UIApplication uiApp)
        {
            RefreshOptionsFromRecognitionState();
            RoomRecognitionPaneRuntime.RefreshDeliveryRouteRecordsSnapshotFromDocument(uiApp);
            ViewModel.RefreshSavedRoutes();
            TryHidePropertiesPalette(uiApp);
            TryHidePane(uiApp, RoomRecognitionPaneRuntime.RightPaneId);
            TryShowPane(uiApp, PaneId);
        }

        public static void RefreshOptionsFromRecognitionState()
        {
            ViewModel.SetOptions(
                RoomRecognitionPaneRuntime.GetDeliveryRouteLiftOptionsSnapshot(),
                RoomRecognitionPaneRuntime.GetDeliveryRouteRoomOptionsSnapshot());
        }

        public static void Hide(UIApplication uiApp)
        {
            TryHidePane(uiApp, PaneId);
        }

        private static void TryHidePropertiesPalette(UIApplication uiApp)
        {
            try
            {
                DockablePane pane = uiApp?.GetDockablePane(
                    Autodesk.Revit.UI.DockablePanes.BuiltInDockablePanes.PropertiesPalette);
                pane?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRoutePane] Hide properties palette failed: " + ex.Message);
            }
        }

        private static void TryShowPane(UIApplication uiApp, DockablePaneId paneId)
        {
            try
            {
                DockablePane pane = uiApp?.GetDockablePane(paneId);
                pane?.Show();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRoutePane] Show pane failed: " + ex.Message);
            }
        }

        private static void TryHidePane(UIApplication uiApp, DockablePaneId paneId)
        {
            try
            {
                DockablePane pane = uiApp?.GetDockablePane(paneId);
                pane?.Hide();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[DeliveryRoutePane] Hide pane failed: " + ex.Message);
            }
        }
    }
}
