using Autodesk.Revit.UI;
using CadToRevit.Services.Common;
using System;
using System.Collections.Concurrent;

namespace CadToRevit.UI.Dockable
{
    public sealed class RoomRecognitionExternalEventHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<RoomRecognitionPaneRequest> _queue = new ConcurrentQueue<RoomRecognitionPaneRequest>();

        public void Enqueue(RoomRecognitionPaneRequest request)
        {
            if (request == null)
            {
                return;
            }

            _queue.Enqueue(request);
        }

        public void Execute(UIApplication app)
        {
            RoomRecognitionPaneRequest request;
            while (_queue.TryDequeue(out request))
            {
                try
                {
                    bool ok = false;
                    if (request.Type == RoomRecognitionPaneRequestType.AutoDetectRooms)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteAutoDetectRooms(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.AutoDetectLifts)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteAutoDetectLifts(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CreateManualRoom)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCreateManualRoom(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.FinishManualRoomSelection)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteFinishManualRoomSelection(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CancelManualRoomSelection)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCancelManualRoomSelection(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CreateManualLift)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCreateManualLift(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.RenameRoom)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteRenameRoom(app, request.RoomKey, request.NewName);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.RenameLift)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteRenameLift(app, request.LiftKey, request.NewName);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.SaveLiftDisplayInfo)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteSaveLiftDisplayInfo(
                            app,
                            request.LiftKey,
                            request.NewName,
                            request.LiftDisplayOverride);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DeleteRoom)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteDeleteRoomFromCurrentList(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DeleteLift)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteDeleteLiftFromCurrentList(app, request.LiftKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.FocusRoom)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteFocusRoom(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.FocusLift)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteFocusLift(app, request.LiftKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.FocusLiftPreserveView)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteFocusLiftPreserveView(app, request.LiftKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ClearRoomFocus)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteClearRoomFocus(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.HighlightRoomOnly)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteHighlightRoomOnly(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.HighlightLiftOnly)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteHighlightLiftOnly(app, request.LiftKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ClearLeftSelectionHighlight)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteClearLeftSelectionHighlight(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.SetRoomCustomFamily)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteSetRoomCustomFamily(
                            app,
                            request.RoomKey,
                            request.FamilyKey,
                            request.FamilyPath,
                            request.UseCustomFamilyPlacementPoint,
                            request.CustomFamilyPlacementXmm,
                            request.CustomFamilyPlacementYmm,
                            request.UseCustomFamilyOrientation,
                            request.CustomFamilyOrientationDeg);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.RestoreProbePreview)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteRestoreProbePreview(app, request.StableRoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.PickPipeWallPoint)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecutePickPipeWallPoint(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CreatePipeSystem)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCreatePipeSystem(app, request.RoomKey, request.PipeDiameterText);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.PickDuctWallPoint)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecutePickDuctWallPoint(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CreateDuctSystem)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCreateDuctSystem(app, request.RoomKey, request.PipeDiameterText);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CreateDuctWork)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCreateDuctWork(
                            app,
                            request.RoomKey,
                            request.SadDuctSizeText,
                            request.SadWallElementId,
                            request.RadDuctSizeText,
                            request.RadWallElementId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CreatePipeWork)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCreatePipeWork(
                            app,
                            request.RoomKey,
                            request.ChwsPipeSizeText,
                            request.ChwsWallElementId,
                            request.ChwrPipeSizeText,
                            request.ChwrWallElementId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.AddCustomDuctSizeOption)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteAddCustomDuctSizeOption(
                            app,
                            request.DuctLengthMm,
                            request.DuctWidthMm);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.AddCustomPipeSizeOption)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteAddCustomPipeSizeOption(
                            app,
                            request.PipeSizeMm);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.RemoveDuctWork)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteRemoveDuctWork(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.RemovePipeWork)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteRemovePipeWork(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.SelectBoundaryWall)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteSelectBoundaryWall(app, request.BoundaryWallElementId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ClearRoomEquipmentLayout)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteClearRoomEquipmentLayout(app, request.RoomKey);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.SaveLayoutPlan)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteSaveLayoutPlan(
                            app,
                            request.LayoutPlan,
                            request.SubmitLayoutPlan,
                            request.ApplyLayoutPlanActiveState);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.SaveDeliveryRoute)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteSaveDeliveryRoute(app, request.DeliveryRoute);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DeleteDeliveryRoute)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteDeleteDeliveryRoute(app, request.RouteId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ExportDeliveryRoute)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteExportDeliveryRoute(app, request.RouteId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DeleteLayoutPlan)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteDeleteLayoutPlan(app, request.LayoutId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ExportLayoutPlan)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteExportLayoutPlan(app, request.LayoutId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ActivateLayoutPlan)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteActivateLayoutPlan(app, request.LayoutId);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.GenerateDeliveryRoute)
                    {
                        CadToRevit.Services.PathPreview.CalculatePathExecutionResult result = RoomRecognitionPaneRuntime.ExecuteGenerateDeliveryRoute(
                            app,
                            request.StartLiftKey,
                            request.TargetRoomKey);
                        request.TrySetPathExecutionResult(result);
                        ok = result != null && result.Success && result.Drawn;
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.PrepareDeliveryRoute)
                    {
                        DeliveryRoutePreparationResult result = RoomRecognitionPaneRuntime.ExecutePrepareDeliveryRoute(
                            app,
                            request.StartLocationType,
                            request.StartLiftKey,
                            request.StartPointXmm,
                            request.StartPointYmm,
                            request.StartPointZmm,
                            request.TargetRoomKey);
                        request.TrySetPreparationResult(result);
                        ok = result != null && result.Success;
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.BeginDeliveryRouteStartPointSelection)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteBeginDeliveryRouteStartPointSelection(
                            app,
                            request.NewName,
                            request.StartPointXmm,
                            request.StartPointYmm,
                            request.StartPointZmm);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.PickDeliveryRouteStartPoint)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecutePickDeliveryRouteStartPoint(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ConfirmDeliveryRouteStartPointSelection)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteConfirmDeliveryRouteStartPointSelection(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.CancelDeliveryRouteStartPointSelection)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteCancelDeliveryRouteStartPointSelection(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.FocusDeliveryRouteStartPoint)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteFocusDeliveryRouteStartPoint(
                            app,
                            request.StartPointXmm,
                            request.StartPointYmm,
                            request.StartPointZmm);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ClearDeliveryRouteStartPointMarker)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteClearDeliveryRouteStartPointMarker(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.PrepareAhuPlacementValidation)
                    {
                        AhuPlacementValidationPreparationResult result =
                            RoomRecognitionPaneRuntime.ExecutePrepareAhuPlacementValidation(
                                app,
                                request.RoomKey);
                        request.TrySetAhuPlacementPreparationResult(result);
                        ok = result != null && result.Success;
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ClearAhuPlacementPointMarker)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteClearAhuPlacementPointMarker(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DrawDeliveryRoutePath)
                    {
                        CadToRevit.Services.PathPreview.CalculatePathExecutionResult result = RoomRecognitionPaneRuntime.ExecuteDrawDeliveryRoutePath(
                            app,
                            request.ResponseBody);
                        request.TrySetPathExecutionResult(result);
                        ok = result != null && result.Success && result.Drawn;
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.ClearDeliveryRoutePath)
                    {
                        ok = RoomRecognitionPaneRuntime.ExecuteClearDeliveryRoutePath(app);
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DrawDeliveryRouteComparison)
                    {
                        CadToRevit.Services.PathPreview.CalculatePathExecutionResult result =
                            RoomRecognitionPaneRuntime.ExecuteDrawDeliveryRouteComparison(
                                app,
                                request.RouteIds);
                        request.TrySetPathExecutionResult(result);
                        ok = result != null && result.Success && result.Drawn;
                    }
                    else if (request.Type == RoomRecognitionPaneRequestType.DrawLayoutPlanRouteComparison)
                    {
                        CadToRevit.Services.PathPreview.CalculatePathExecutionResult result = RoomRecognitionPaneRuntime.ExecuteDrawLayoutPlanRouteComparison(
                            app,
                            request.LayoutIds);
                        request.TrySetPathExecutionResult(result);
                        ok = result != null && result.Success && result.Drawn;
                    }

                    if (ok && IsFineDetailLevelRequest(request.Type))
                    {
                        ViewDisplayHelper.EnsureFineDetailLevel(app != null && app.ActiveUIDocument != null
                            ? app.ActiveUIDocument.Document
                            : null);
                    }

                    if (request.PathExecutionCompletion == null &&
                        request.PreparationCompletion == null &&
                        request.AhuPlacementPreparationCompletion == null)
                    {
                        request.TrySetResult(ok);
                    }
                }
                catch (Exception ex)
                {
                    request.TrySetException(ex);
                }
            }
        }

        public string GetName()
        {
            return "CadToRevit RoomRecognition ExternalEvent";
        }

        private static bool IsFineDetailLevelRequest(RoomRecognitionPaneRequestType type)
        {
            return type == RoomRecognitionPaneRequestType.GenerateDeliveryRoute ||
                type == RoomRecognitionPaneRequestType.DrawDeliveryRoutePath ||
                type == RoomRecognitionPaneRequestType.DrawDeliveryRouteComparison ||
                type == RoomRecognitionPaneRequestType.DrawLayoutPlanRouteComparison;
        }
    }
}
