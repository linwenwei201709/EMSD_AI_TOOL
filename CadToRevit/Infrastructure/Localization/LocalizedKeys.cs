namespace CadToRevit.Infrastructure.Localization
{
    /// <summary>
    /// Centralized resource key constants.
    /// </summary>
    public static class LocalizedKeys
    {
        public static class Common
        {
            public const string Ok = "Common.OK";
            public const string Yes = "Common.Yes";
            public const string No = "Common.No";
            public const string Cancel = "Common.Cancel";
            public const string Save = "Common.Save";
            public const string Preview = "Common.Preview";
            public const string Export = "Common.Export";
        }

        public static class Ribbon
        {
            public const string TabCadToRevit = "Ribbon.Tab.CadToRevit";
            public const string PanelTools = "Ribbon.Panel.Tools";
            public const string PanelPathTools = "Ribbon.Panel.PathTools";
            public const string PanelIntegrationTools = "Ribbon.Panel.IntegrationTools";
            public const string PanelTestTools = "Ribbon.Panel.TestTools";
            public const string PanelConfig = "Ribbon.Panel.Config";
            public const string ButtonGlobalSettings = "Ribbon.Button.GlobalSettings";
            public const string ButtonGlobalSettingsTooltip = "Ribbon.Button.GlobalSettings.Tooltip";
            public const string ButtonFamilyLibraryManager = "Ribbon.Button.FamilyLibraryManager";
            public const string ButtonFamilyLibraryManagerTooltip = "Ribbon.Button.FamilyLibraryManager.Tooltip";
        }

        public static class Dialog
        {
            public const string InfoTitle = "Dialog.Info.Title";
            public const string WarningTitle = "Dialog.Warning.Title";
            public const string ErrorTitle = "Dialog.Error.Title";
        }

        public static class GlobalSettings
        {
            public const string Title = "GlobalSettings.Title";
            public const string TabGeneral = "GlobalSettings.Tab.General";
            public const string TabWalls = "GlobalSettings.Tab.Walls";
            public const string TabDoors = "GlobalSettings.Tab.Doors";
            public const string TabRooms = "GlobalSettings.Tab.Rooms";
            public const string SafeMode = "GlobalSettings.SafeMode";
            public const string AutoJoinWalls = "GlobalSettings.AutoJoinWalls";
            public const string HeadRoom = "GlobalSettings.HeadRoom";
            public const string UseGlobalWallHeight = "GlobalSettings.UseGlobalWallHeight";
            public const string GlobalWallHeight = "GlobalSettings.GlobalWallHeight";
            public const string UseGlobalDoorHeight = "GlobalSettings.UseGlobalDoorHeight";
            public const string GlobalDoorHeight = "GlobalSettings.GlobalDoorHeight";
            public const string UseGlobalDoorSillHeight = "GlobalSettings.UseGlobalDoorSillHeight";
            public const string GlobalDoorSillHeight = "GlobalSettings.GlobalDoorSillHeight";
            public const string RecognitionWindow = "GlobalSettings.RecognitionWindow";
            public const string TargetKeywords = "GlobalSettings.TargetKeywords";
            public const string DoorGapMax = "GlobalSettings.DoorGapMax";
            public const string SmallGapPatch = "GlobalSettings.SmallGapPatch";
            public const string ValidationPositive = "GlobalSettings.Validation.Positive";
            public const string ValidationNonNegative = "GlobalSettings.Validation.NonNegative";
            public const string NoActiveDocument = "GlobalSettings.NoActiveDocument";
        }

        public static class RoomProbe
        {
            public const string RibbonButton = "Ribbon.Button.ProbeRoom";
            public const string RibbonButtonTooltip = "Ribbon.Button.ProbeRoom.Tooltip";
            public const string DialogTitle = "RoomProbe.Dialog.Title";
            public const string PickPointPrompt = "RoomProbe.PickPointPrompt";
            public const string NoRoomFound = "RoomProbe.NoRoomFound";
        }

        public static class FamilyLibrary
        {
            public const string Title = "FamilyLibrary.Title";
            public const string Subtitle = "FamilyLibrary.Subtitle";
            public const string AddButton = "FamilyLibrary.Button.Add";
            public const string DeleteButton = "FamilyLibrary.Button.Delete";
            public const string RefreshButton = "FamilyLibrary.Button.Refresh";
            public const string EditorTitle = "FamilyLibrary.Editor.Title";
            public const string ColumnDisplayName = "FamilyLibrary.Column.DisplayName";
            public const string ColumnFileName = "FamilyLibrary.Column.FileName";
            public const string ColumnEnabled = "FamilyLibrary.Column.Enabled";
            public const string ColumnSortOrder = "FamilyLibrary.Column.SortOrder";
            public const string ColumnDescription = "FamilyLibrary.Column.Description";
            public const string FieldDisplayName = "FamilyLibrary.Field.DisplayName";
            public const string FieldFileName = "FamilyLibrary.Field.FileName";
            public const string FieldEnabled = "FamilyLibrary.Field.Enabled";
            public const string FieldSortOrder = "FamilyLibrary.Field.SortOrder";
            public const string FieldDescription = "FamilyLibrary.Field.Description";
            public const string FieldCatalogPath = "FamilyLibrary.Field.CatalogPath";
            public const string OpenFileFilter = "FamilyLibrary.OpenFile.Filter";
            public const string DeleteSelectionRequired = "FamilyLibrary.Delete.SelectionRequired";
            public const string DeleteConfirm = "FamilyLibrary.Delete.Confirm";
            public const string DeleteRemovedUnsaved = "FamilyLibrary.Delete.RemovedUnsaved";
            public const string DeletePendingChanges = "FamilyLibrary.Delete.PendingChanges";
            public const string DeleteNotFoundInCatalog = "FamilyLibrary.Delete.NotFoundInCatalog";
            public const string DeleteSuccess = "FamilyLibrary.Delete.Success";
            public const string UnsavedChangesRefresh = "FamilyLibrary.UnsavedChanges.Refresh";
            public const string UnsavedChangesClose = "FamilyLibrary.UnsavedChanges.Close";
            public const string StatusSaved = "FamilyLibrary.Status.Saved";
            public const string StatusUnsaved = "FamilyLibrary.Status.Unsaved";
            public const string SelectedFileMissing = "FamilyLibrary.Error.SelectedFileMissing";
            public const string OnlyRfaFiles = "FamilyLibrary.Error.OnlyRfaFiles";
            public const string SaveMissingSourceFile = "FamilyLibrary.Error.SaveMissingSourceFile";
            public const string SaveDestinationExists = "FamilyLibrary.Error.SaveDestinationExists";
            public const string DisplayNameRequired = "FamilyLibrary.Error.DisplayNameRequired";
            public const string SaveSuccess = "FamilyLibrary.Save.Success";
        }
    }
}
