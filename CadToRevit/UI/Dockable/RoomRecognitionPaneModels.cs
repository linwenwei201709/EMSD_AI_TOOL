using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms.DeliveryRoutes;
using CadToRevit.Models.Rooms.LayoutPlans;
using CadToRevit.Services.PathPreview;
using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Rooms;
using CadToRevit.Services.Rooms.Lifts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CadToRevit.UI.Dockable
{
    public enum RoomRecognitionPaneRequestType
    {
        None,
        AutoDetectRooms,
        AutoDetectLifts,
        CreateManualRoom,
        FinishManualRoomSelection,
        CancelManualRoomSelection,
        CreateManualLift,
        RenameRoom,
        RenameLift,
        SaveLiftDisplayInfo,
        DeleteRoom,
        DeleteLift,
        FocusRoom,
        FocusLift,
        FocusLiftPreserveView,
        ClearRoomFocus,
        HighlightRoomOnly,
        HighlightLiftOnly,
        ClearLeftSelectionHighlight,
        SetRoomCustomFamily,
        RestoreProbePreview,
        PickPipeWallPoint,
        CreatePipeSystem,
        PickDuctWallPoint,
        CreateDuctSystem,
        CreateDuctWork,
        CreatePipeWork,
        RemoveDuctWork,
        RemovePipeWork,
        SelectBoundaryWall,
        ClearRoomEquipmentLayout,
        SaveLayoutPlan,
        SaveDeliveryRoute,
        DeleteDeliveryRoute,
        ExportDeliveryRoute,
        DeleteLayoutPlan,
        ExportLayoutPlan,
        ActivateLayoutPlan,
        GenerateDeliveryRoute,
        PrepareDeliveryRoute,
        BeginDeliveryRouteStartPointSelection,
        PickDeliveryRouteStartPoint,
        ConfirmDeliveryRouteStartPointSelection,
        CancelDeliveryRouteStartPointSelection,
        FocusDeliveryRouteStartPoint,
        ClearDeliveryRouteStartPointMarker,
        PrepareAhuPlacementValidation,
        ClearAhuPlacementPointMarker,
        DrawDeliveryRoutePath,
        ClearDeliveryRoutePath,
        AddCustomDuctSizeOption,
        AddCustomPipeSizeOption,
        DrawDeliveryRouteComparison,
        DrawLayoutPlanRouteComparison
    }

    public enum RoomRecognitionPaneMode
    {
        None,
        Detect,
        Probe
    }

    public sealed class RoomRecognitionPaneRequest
    {
        public RoomRecognitionPaneRequestType Type { get; set; }

        public string RoomKey { get; set; }

        public string LiftKey { get; set; }

        public string NewName { get; set; }

        public LiftDisplayOverride LiftDisplayOverride { get; set; }

        public string FamilyKey { get; set; }

        public string FamilyPath { get; set; }

        public bool UseCustomFamilyPlacementPoint { get; set; }

        public double CustomFamilyPlacementXmm { get; set; }

        public double CustomFamilyPlacementYmm { get; set; }

        public bool UseCustomFamilyOrientation { get; set; }

        public double CustomFamilyOrientationDeg { get; set; }

        public string StableRoomKey { get; set; }

        public string PipeDiameterText { get; set; }

        public string SadDuctSizeText { get; set; }

        public ElementId SadWallElementId { get; set; } = ElementId.InvalidElementId;

        public string RadDuctSizeText { get; set; }

        public ElementId RadWallElementId { get; set; } = ElementId.InvalidElementId;

        public string ChwsPipeSizeText { get; set; }

        public ElementId ChwsWallElementId { get; set; } = ElementId.InvalidElementId;

        public string ChwrPipeSizeText { get; set; }

        public ElementId ChwrWallElementId { get; set; } = ElementId.InvalidElementId;

        public ElementId BoundaryWallElementId { get; set; } = ElementId.InvalidElementId;

        public double DuctLengthMm { get; set; }

        public double DuctWidthMm { get; set; }

        public double PipeSizeMm { get; set; }

        public RoomLayoutPlanDto LayoutPlan { get; set; }

        public DeliveryRouteRecordDto DeliveryRoute { get; set; }

        public bool SubmitLayoutPlan { get; set; }

        public bool ApplyLayoutPlanActiveState { get; set; }

        public string LayoutId { get; set; }

        public List<string> LayoutIds { get; set; } = new List<string>();

        public string RouteId { get; set; }

        public List<string> RouteIds { get; set; } = new List<string>();

        public string StartLiftKey { get; set; }

        public string StartLocationType { get; set; }

        public double? StartPointXmm { get; set; }

        public double? StartPointYmm { get; set; }

        public double? StartPointZmm { get; set; }

        public string TargetRoomKey { get; set; }

        public string ResponseBody { get; set; }

        public TaskCompletionSource<bool> Completion { get; set; }

        public TaskCompletionSource<CalculatePathExecutionResult> PathExecutionCompletion { get; set; }

        public TaskCompletionSource<DeliveryRoutePreparationResult> PreparationCompletion { get; set; }

        public TaskCompletionSource<AhuPlacementValidationPreparationResult> AhuPlacementPreparationCompletion { get; set; }

        public void TrySetResult(bool value)
        {
            Completion?.TrySetResult(value);
        }

        public void TrySetPathExecutionResult(CalculatePathExecutionResult value)
        {
            PathExecutionCompletion?.TrySetResult(value);
        }

        public void TrySetPreparationResult(DeliveryRoutePreparationResult value)
        {
            PreparationCompletion?.TrySetResult(value);
        }

        public void TrySetAhuPlacementPreparationResult(AhuPlacementValidationPreparationResult value)
        {
            AhuPlacementPreparationCompletion?.TrySetResult(value);
        }

        public void TrySetException(System.Exception ex)
        {
            Completion?.TrySetException(ex);
            PathExecutionCompletion?.TrySetException(ex);
            PreparationCompletion?.TrySetException(ex);
            AhuPlacementPreparationCompletion?.TrySetException(ex);
        }
    }

    public sealed class DeliveryRoutePreparationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public string SessionId { get; set; }

        public double StartXmm { get; set; }

        public double StartYmm { get; set; }

        public double GoalXmm { get; set; }

        public double GoalYmm { get; set; }

        public List<RestrictedAreaRequestItem> RestrictedAreas { get; set; } =
            new List<RestrictedAreaRequestItem>();
    }

    public sealed class AhuPlacementValidationPreparationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public string SessionId { get; set; }

        public string RoomKey { get; set; }

        public double PlacementXmm { get; set; }

        public double PlacementYmm { get; set; }
    }

    public sealed class RoomRecognitionPaneState
    {
        public RoomRecognitionPaneMode Mode { get; set; }

        public TargetRoomModelRecognitionService.RecognitionSummary Summary { get; set; }

        public Dictionary<string, List<ElementId>> RoomRangeElementIds { get; set; } =
            new Dictionary<string, List<ElementId>>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, RoomSemanticRecord> RoomByKey { get; set; } =
            new Dictionary<string, RoomSemanticRecord>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, LiftRecognitionRecord> LiftByKey { get; set; } =
            new Dictionary<string, LiftRecognitionRecord>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> LevelNameByRoomKey { get; set; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> RoomDisplayNameByKey { get; set; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> LiftDisplayNameByKey { get; set; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, LiftDisplayOverride> LiftDisplayOverrideByKey { get; set; } =
            new Dictionary<string, LiftDisplayOverride>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> RoomCustomFamilyKeyByRoomKey { get; set; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ElementId> RoomCustomFamilyInstanceIdByRoomKey { get; set; } =
            new Dictionary<string, ElementId>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<ElementId>> RoomGeneratedDuctElementIdsByRoomKey { get; set; } =
            new Dictionary<string, List<ElementId>>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<ElementId>> RoomGeneratedPipeElementIdsByRoomKey { get; set; } =
            new Dictionary<string, List<ElementId>>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ElementId> PipeWallElementIdByRoomKey { get; set; } =
            new Dictionary<string, ElementId>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, XYZ> PipeWallPointByRoomKey { get; set; } =
            new Dictionary<string, XYZ>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> PipeWallDisplayNameByRoomKey { get; set; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ElementId> DuctWallElementIdByRoomKey { get; set; } =
            new Dictionary<string, ElementId>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, XYZ> DuctWallPointByRoomKey { get; set; } =
            new Dictionary<string, XYZ>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> DuctWallDisplayNameByRoomKey { get; set; } =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ProbeRoomCardState> ProbeRoomByStableKey { get; set; } =
            new Dictionary<string, ProbeRoomCardState>(System.StringComparer.OrdinalIgnoreCase);

        public List<string> ProbeRoomCardOrder { get; set; } = new List<string>();

        public string SelectedProbeRoomStableKey { get; set; }

        public string SelectedLiftKey { get; set; }
    }

    public sealed class DeliveryRouteEquipmentInfo
    {
        public bool Found { get; set; }

        public string RoomKey { get; set; }

        public string FamilyKey { get; set; }

        public int OriginalModelId { get; set; }

        public int RevitElementId { get; set; }

        public string DisplayName { get; set; }

        public double AirflowM3s { get; set; }

        public int TotalLengthMm { get; set; }

        public int WidthMm { get; set; }

        public int HeightMm { get; set; }
    }

    public sealed class ProbeRoomCardState
    {
        public string StableRoomKey { get; set; }

        public bool HitNativeRoom { get; set; }

        public ElementId LevelId { get; set; } = ElementId.InvalidElementId;

        public string LevelName { get; set; }

        public string RoomName { get; set; }

        public string RoomNumber { get; set; }

        public double AreaM2 { get; set; }

        public string Status { get; set; }

        public RoomSemanticRecord SemanticRecord { get; set; }

        public List<XYZ> LoopPoints { get; set; } = new List<XYZ>();
    }
}
