using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms
{
    [DataContract]
    internal sealed class RoomCustomFamilyCatalogDto
    {
        [DataMember(Name = "schemaVersion")]
        public int SchemaVersion { get; set; } = 5;

        [DataMember(Name = "libraryName")]
        public string LibraryName { get; set; }

        [DataMember(Name = "familyFolderName")]
        public string FamilyFolderName { get; set; }

        [DataMember(Name = "families")]
        public List<RoomCustomFamilyCatalogItemDto> Families { get; set; } = new List<RoomCustomFamilyCatalogItemDto>();
    }

    [DataContract]
    internal sealed class RoomCustomFamilyCatalogItemDto
    {
        [DataMember(Name = "key")]
        public string Key { get; set; }

        [DataMember(Name = "displayName")]
        public string DisplayName { get; set; }

        [DataMember(Name = "originalFileName", EmitDefaultValue = false)]
        public string OriginalFileName { get; set; }

        [DataMember(Name = "storedFileName", EmitDefaultValue = false)]
        public string StoredFileName { get; set; }

        // Keep reading legacy catalogs that only store fileName.
        [DataMember(Name = "fileName", EmitDefaultValue = false)]
        public string LegacyFileName { get; set; }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "sortOrder")]
        public int SortOrder { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        [DataMember(Name = "airflowM3s")]
        public double AirflowM3s { get; set; }

        [DataMember(Name = "mbLengthMm")]
        public int MbLengthMm { get; set; }

        [DataMember(Name = "filterLengthMm")]
        public int FilterLengthMm { get; set; }

        [DataMember(Name = "coilLengthMm")]
        public int CoilLengthMm { get; set; }

        [DataMember(Name = "fanLengthMm")]
        public int FanLengthMm { get; set; }

        [DataMember(Name = "totalLengthMm")]
        public int TotalLengthMm { get; set; }

        [DataMember(Name = "heightMm")]
        public int HeightMm { get; set; }

        [DataMember(Name = "widthMm")]
        public int WidthMm { get; set; }

        [DataMember(Name = "weightKg")]
        public int WeightKg { get; set; }

        [DataMember(Name = "requiredMaintenanceSpaceMm")]
        public int RequiredMaintenanceSpaceMm { get; set; }

        [DataMember(Name = "requiredMaintenanceSpaceSide")]
        public string RequiredMaintenanceSpaceSide { get; set; }

        [DataMember(Name = "valveChamberLengthMm")]
        public int ValveChamberLengthMm { get; set; }

        [DataMember(Name = "valveChamberWidthMm")]
        public int ValveChamberWidthMm { get; set; }

        [DataMember(Name = "elChamberLengthMm")]
        public int ElChamberLengthMm { get; set; }

        [DataMember(Name = "elChamberWidthMm")]
        public int ElChamberWidthMm { get; set; }

        [DataMember(Name = "maintenanceDoorSideMm")]
        public int MaintenanceDoorSideMm { get; set; }

        [DataMember(Name = "maintenanceOtherSideMm")]
        public int MaintenanceOtherSideMm { get; set; }

        [DataMember(Name = "maintenanceFrontBackMm")]
        public int MaintenanceFrontBackMm { get; set; }

        [DataMember(Name = "subModules", EmitDefaultValue = false)]
        public List<RoomCustomFamilySubModuleDto> SubModules { get; set; } =
            new List<RoomCustomFamilySubModuleDto>();

        [DataMember(Name = "maintenanceSpaces", EmitDefaultValue = false)]
        public List<RoomCustomFamilyMaintenanceSpaceDto> MaintenanceSpaces { get; set; } =
            new List<RoomCustomFamilyMaintenanceSpaceDto>();

        internal string GetOriginalFileName()
        {
            return string.IsNullOrWhiteSpace(OriginalFileName) ? LegacyFileName : OriginalFileName;
        }

        internal string GetStoredFileName()
        {
            string storedFileName = string.IsNullOrWhiteSpace(StoredFileName) ? LegacyFileName : StoredFileName;
            return storedFileName;
        }

        internal void NormalizeFileNames()
        {
            if (string.IsNullOrWhiteSpace(OriginalFileName))
            {
                OriginalFileName = LegacyFileName;
            }

            if (string.IsNullOrWhiteSpace(StoredFileName))
            {
                StoredFileName = LegacyFileName;
            }

            LegacyFileName = null;
        }
    }

    [DataContract]
    internal sealed class RoomCustomFamilySubModuleDto
    {
        [DataMember(Name = "sequence")]
        public int Sequence { get; set; }

        [DataMember(Name = "moduleCode")]
        public string ModuleCode { get; set; }

        [DataMember(Name = "gridRow")]
        public int GridRow { get; set; }

        [DataMember(Name = "gridColumn")]
        public int GridColumn { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "lengthMm")]
        public int LengthMm { get; set; }

        [DataMember(Name = "widthMm")]
        public int WidthMm { get; set; }

        [DataMember(Name = "heightMm")]
        public int HeightMm { get; set; }

        [DataMember(Name = "weightKg")]
        public int WeightKg { get; set; }

        [DataMember(Name = "photo", EmitDefaultValue = false)]
        public string Photo { get; set; }
    }

    [DataContract]
    internal sealed class RoomCustomFamilyMaintenanceSpaceDto
    {
        [DataMember(Name = "sequence")]
        public int Sequence { get; set; }

        [DataMember(Name = "maintenanceCode")]
        public string MaintenanceCode { get; set; }

        [DataMember(Name = "side")]
        public string Side { get; set; }

        [DataMember(Name = "dimensionMm")]
        public int DimensionMm { get; set; }

        [DataMember(Name = "isWallSide")]
        public bool IsWallSide { get; set; }

        [DataMember(Name = "isDoorSide")]
        public bool IsDoorSide { get; set; }
    }

    internal sealed class RoomCustomFamilyCatalogMutationResult
    {
        public bool Success { get; set; }

        public string Message { get; set; }
    }

    internal sealed class RoomCustomFamilyAhuParameters
    {
        internal double AirflowM3s { get; set; }

        internal int MbLengthMm { get; set; }

        internal int FilterLengthMm { get; set; }

        internal int CoilLengthMm { get; set; }

        internal int FanLengthMm { get; set; }

        internal int TotalLengthMm { get; set; }

        internal int HeightMm { get; set; }

        internal int WidthMm { get; set; }

        internal int WeightKg { get; set; }

        internal int RequiredMaintenanceSpaceMm { get; set; }

        internal string RequiredMaintenanceSpaceSide { get; set; }

        internal int ValveChamberLengthMm { get; set; }

        internal int ValveChamberWidthMm { get; set; }

        internal int ElChamberLengthMm { get; set; }

        internal int ElChamberWidthMm { get; set; }

        internal int MaintenanceDoorSideMm { get; set; }

        internal int MaintenanceOtherSideMm { get; set; }

        internal int MaintenanceFrontBackMm { get; set; }
    }
}
