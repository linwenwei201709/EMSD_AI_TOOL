namespace CadToRevit.Models.Settings
{
    /// <summary>
    /// Stores project-level generation settings and field-level global overrides.
    /// </summary>
    public sealed class GlobalGenerationSettings
    {
        public const double DefaultHeadRoomMm = 0.0;
        public const double DefaultWallHeightMm = 4000.0;
        public const double DefaultDoorHeightMm = 2100.0;
        public const double DefaultDoorSillHeightMm = 0.0;

        public bool SafeModeEnabled { get; set; } = true;

        public bool AutoJoinWallsAfterCreate { get; set; } = true;

        public double HeadRoomMm { get; set; } = DefaultHeadRoomMm;

        public bool UseGlobalWallHeightOverride { get; set; }

        public double GlobalWallHeightMm { get; set; } = DefaultWallHeightMm;

        public bool UseGlobalDoorHeightOverride { get; set; }

        public double GlobalDoorHeightMm { get; set; } = DefaultDoorHeightMm;

        public bool UseGlobalDoorSillHeightOverride { get; set; }

        public double GlobalDoorSillHeightMm { get; set; } = DefaultDoorSillHeightMm;

        /// <summary>
        /// When true, CAD door layers create wall openings only instead of Door family instances.
        /// Default is false so normal Door family instances are preserved unless the user explicitly enables opening-only mode.
        /// </summary>
        public bool CreateDoorOpeningOnly { get; set; } = false;

        public static GlobalGenerationSettings CreateDefault()
        {
            return new GlobalGenerationSettings();
        }

        public static GlobalGenerationSettings Clone(GlobalGenerationSettings source)
        {
            if (source == null)
            {
                return CreateDefault();
            }

            return new GlobalGenerationSettings
            {
                SafeModeEnabled = source.SafeModeEnabled,
                AutoJoinWallsAfterCreate = source.AutoJoinWallsAfterCreate,
                HeadRoomMm = source.HeadRoomMm >= 0 ? source.HeadRoomMm : DefaultHeadRoomMm,
                UseGlobalWallHeightOverride = source.UseGlobalWallHeightOverride,
                GlobalWallHeightMm = source.GlobalWallHeightMm > 0 ? source.GlobalWallHeightMm : DefaultWallHeightMm,
                UseGlobalDoorHeightOverride = source.UseGlobalDoorHeightOverride,
                GlobalDoorHeightMm = source.GlobalDoorHeightMm > 0 ? source.GlobalDoorHeightMm : DefaultDoorHeightMm,
                UseGlobalDoorSillHeightOverride = source.UseGlobalDoorSillHeightOverride,
                GlobalDoorSillHeightMm = source.GlobalDoorSillHeightMm >= 0 ? source.GlobalDoorSillHeightMm : DefaultDoorSillHeightMm,
                CreateDoorOpeningOnly = source.CreateDoorOpeningOnly
            };
        }
    }
}
