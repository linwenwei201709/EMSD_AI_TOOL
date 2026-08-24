using CadToRevit.Models.Settings;
using CadToRevit.Services;

namespace CadToRevit.UI
{
    /// <summary>
    /// Holds editable project-level settings for the global settings window.
    /// </summary>
    internal sealed class GlobalSettingsViewModel
    {
        public bool SafeModeEnabled { get; set; }

        public bool AutoJoinWallsAfterCreate { get; set; }

        public double HeadRoomMm { get; set; }

        public bool UseGlobalWallHeightOverride { get; set; }

        public double GlobalWallHeightMm { get; set; }

        public bool UseGlobalDoorHeightOverride { get; set; }

        public double GlobalDoorHeightMm { get; set; }

        public bool UseGlobalDoorSillHeightOverride { get; set; }

        public double GlobalDoorSillHeightMm { get; set; }

        public bool CreateDoorOpeningOnly { get; set; }

        public double RoomRecognitionWindowSizeM { get; set; }

        public string TargetKeywordsText { get; set; }

        public string LiftGeometryLayerNames { get; set; }

        public double DoorGapMaxMm { get; set; }

        public double SmallGapPatchMaxMm { get; set; }

        public string RoomTextLayerNames { get; set; }

        public static GlobalSettingsViewModel FromSettings(GlobalGenerationSettings global, RoomRecognitionSettings room)
        {
            GlobalGenerationSettings safeGlobal = GlobalGenerationSettings.Clone(global);
            RoomRecognitionSettings safeRoom = RoomRecognitionSettings.Clone(room);
            return new GlobalSettingsViewModel
            {
                SafeModeEnabled = safeGlobal.SafeModeEnabled,
                AutoJoinWallsAfterCreate = safeGlobal.AutoJoinWallsAfterCreate,
                HeadRoomMm = safeGlobal.HeadRoomMm,
                UseGlobalWallHeightOverride = safeGlobal.UseGlobalWallHeightOverride,
                GlobalWallHeightMm = safeGlobal.GlobalWallHeightMm,
                UseGlobalDoorHeightOverride = safeGlobal.UseGlobalDoorHeightOverride,
                GlobalDoorHeightMm = safeGlobal.GlobalDoorHeightMm,
                UseGlobalDoorSillHeightOverride = safeGlobal.UseGlobalDoorSillHeightOverride,
                GlobalDoorSillHeightMm = safeGlobal.GlobalDoorSillHeightMm,
                CreateDoorOpeningOnly = safeGlobal.CreateDoorOpeningOnly,
                RoomRecognitionWindowSizeM = safeRoom.ModelRecognitionWindowSizeM,
                TargetKeywordsText = safeRoom.TargetKeywordsText,
                LiftGeometryLayerNames = safeRoom.LiftGeometryLayerNames,
                DoorGapMaxMm = safeRoom.DoorGapMaxMm,
                SmallGapPatchMaxMm = safeRoom.SmallGapPatchMaxMm,
                RoomTextLayerNames = safeRoom.RoomTextLayerNames
            };
        }

        public GlobalGenerationSettings BuildGlobalSettings()
        {
            return GlobalGenerationSettings.Clone(new GlobalGenerationSettings
            {
                SafeModeEnabled = SafeModeEnabled,
                AutoJoinWallsAfterCreate = AutoJoinWallsAfterCreate,
                HeadRoomMm = HeadRoomMm,
                UseGlobalWallHeightOverride = UseGlobalWallHeightOverride,
                GlobalWallHeightMm = GlobalWallHeightMm,
                UseGlobalDoorHeightOverride = UseGlobalDoorHeightOverride,
                GlobalDoorHeightMm = GlobalDoorHeightMm,
                UseGlobalDoorSillHeightOverride = UseGlobalDoorSillHeightOverride,
                GlobalDoorSillHeightMm = GlobalDoorSillHeightMm,
                CreateDoorOpeningOnly = CreateDoorOpeningOnly
            });
        }

        public RoomRecognitionSettings BuildRoomRecognitionSettings()
        {
            return RoomRecognitionSettings.Clone(new RoomRecognitionSettings
            {
                RoomTextLayerNames = RoomTextLayerNames,
                DoorGapMaxMm = DoorGapMaxMm,
                SmallGapPatchMaxMm = SmallGapPatchMaxMm,
                TargetKeywordsText = TargetKeywordsText,
                LiftGeometryLayerNames = LiftGeometryLayerNames,
                ModelRecognitionWindowSizeM = RoomRecognitionWindowSizeM,
                HeadRoomMm = HeadRoomMm
            });
        }
    }
}
