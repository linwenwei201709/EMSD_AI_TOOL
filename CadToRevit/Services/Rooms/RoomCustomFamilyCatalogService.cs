using CadToRevit.Models.Rooms;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomCustomFamilyCatalogService
    {
        internal const string FamilyFolderName = "AHUFamilyType";
        internal const int CurrentSchemaVersion = 5;
        private static readonly object SyncRoot = new object();
        private static IReadOnlyList<RoomCustomFamilyOption> _cache;

        internal static IReadOnlyList<RoomCustomFamilyOption> GetOptions(bool forceReload = false)
        {
            lock (SyncRoot)
            {
                if (_cache == null || forceReload)
                {
                    _cache = RoomCustomFamilyCatalogFileService.LoadOptions();
                }

                return _cache;
            }
        }

        internal static RoomCustomFamilyOption GetOption(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return null;
            }

            return GetOptions().FirstOrDefault(x => string.Equals(x.Key, familyKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the fixed document-based default values for S1-S6 of one AHU family.
        /// These defaults are used only when a new Sub-Module row is created. Persisted
        /// catalog.json Sub-Module values are not changed or overwritten by this method.
        /// </summary>
        internal static bool TryGetSubModuleDefault(
            string familyKey,
            int sequence,
            out RoomCustomFamilySubModuleDto subModule)
        {
            subModule = null;

            RoomCustomFamilyAhuParameters ahu;
            if (!RoomCustomFamilyCatalogDefaults.TryGet(familyKey, out ahu) || ahu == null)
            {
                return false;
            }

            string name;
            int lengthMm;
            int widthMm;

            switch (sequence)
            {
                case 1:
                    name = "Mixing Box";
                    lengthMm = ahu.MbLengthMm;
                    widthMm = ahu.WidthMm;
                    break;

                case 2:
                    name = "Filter Chamber";
                    lengthMm = ahu.FilterLengthMm;
                    widthMm = ahu.WidthMm;
                    break;

                case 3:
                    name = "Coil Section";
                    lengthMm = ahu.CoilLengthMm;
                    widthMm = ahu.WidthMm;
                    break;

                case 4:
                    name = "Fan Section";
                    lengthMm = ahu.FanLengthMm;
                    widthMm = ahu.WidthMm;
                    break;

                case 5:
                    name = "Valve Chamber";
                    lengthMm = ahu.ValveChamberLengthMm;
                    widthMm = ahu.ValveChamberWidthMm;
                    break;

                case 6:
                    name = "Electrical Chamber";
                    lengthMm = ahu.ElChamberLengthMm;
                    widthMm = ahu.ElChamberWidthMm;
                    break;

                default:
                    return false;
            }

            subModule = new RoomCustomFamilySubModuleDto
            {
                Sequence = sequence,
                ModuleCode = "S" + sequence,
                Name = name,
                LengthMm = lengthMm,
                WidthMm = widthMm,
                HeightMm = ahu.HeightMm,
                WeightKg = 0,
                Photo = string.Empty
            };

            return true;
        }

        internal static bool TryGetExistingOption(string familyKey, out RoomCustomFamilyOption option)
        {
            option = GetOption(familyKey);
            return option != null && File.Exists(option.FullPath);
        }

        /// <summary>
        /// Returns the latest persisted Sub-Module configuration for one AHU family.
        /// This deliberately reads catalog.json instead of RoomCustomFamilyOption because
        /// the lightweight option model does not contain the 6 x 4 Sub-Module layout.
        /// A copy is returned so placement calculations cannot mutate catalog data.
        /// </summary>
        internal static IReadOnlyList<RoomCustomFamilySubModuleDto> GetSubModules(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return Array.Empty<RoomCustomFamilySubModuleDto>();
            }

            try
            {
                RoomCustomFamilyCatalogDto catalog = RoomCustomFamilyCatalogFileService.LoadCatalog();
                RoomCustomFamilyCatalogItemDto item = catalog != null && catalog.Families != null
                    ? catalog.Families.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.Key, familyKey, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (item == null || item.SubModules == null || item.SubModules.Count == 0)
                {
                    return Array.Empty<RoomCustomFamilySubModuleDto>();
                }

                return item.SubModules
                    .Where(x => x != null)
                    .OrderBy(x => x.Sequence)
                    .Select(x => new RoomCustomFamilySubModuleDto
                    {
                        Sequence = x.Sequence,
                        ModuleCode = x.ModuleCode,
                        GridRow = x.GridRow,
                        GridColumn = x.GridColumn,
                        Name = x.Name,
                        LengthMm = x.LengthMm,
                        WidthMm = x.WidthMm,
                        HeightMm = x.HeightMm,
                        WeightKg = x.WeightKg,
                        Photo = x.Photo
                    })
                    .ToList()
                    .AsReadOnly();
            }
            catch (Exception ex)
            {
                // Sub-Module data is an extension of the existing room-fit request.
                // Do not block the established AHU insertion workflow if the optional
                // layout cannot be read; send the legacy request without sub_modules.
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] subModuleCatalogReadFailed familyKey=" +
                    familyKey + ", error=" + ex.Message);
                return Array.Empty<RoomCustomFamilySubModuleDto>();
            }
        }

        /// <summary>
        /// Returns the complete persisted Maintenance2 configuration for one AHU family.
        /// A copy is returned so API request construction cannot mutate catalog data.
        /// M1-M4 is the click/sequence code; Side is the authoritative AHU-local direction.
        /// </summary>
        internal static IReadOnlyList<RoomCustomFamilyMaintenanceSpaceDto> GetMaintenanceSpaces(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>();
            }

            try
            {
                RoomCustomFamilyCatalogDto catalog = RoomCustomFamilyCatalogFileService.LoadCatalog();
                RoomCustomFamilyCatalogItemDto item = catalog != null && catalog.Families != null
                    ? catalog.Families.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.Key, familyKey, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (item == null || item.MaintenanceSpaces == null || item.MaintenanceSpaces.Count == 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] maintenanceSpaces familyKey=" + familyKey + ", count=0");
                    return Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>();
                }

                List<RoomCustomFamilyMaintenanceSpaceDto> result = item.MaintenanceSpaces
                    .Where(x => x != null)
                    .OrderBy(x => x.Sequence)
                    .Select(x => new RoomCustomFamilyMaintenanceSpaceDto
                    {
                        Sequence = x.Sequence,
                        MaintenanceCode = x.MaintenanceCode,
                        Side = x.Side,
                        DimensionMm = x.DimensionMm,
                        IsWallSide = x.IsWallSide,
                        IsDoorSide = x.IsDoorSide
                    })
                    .ToList();

                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] maintenanceSpaces familyKey=" + familyKey +
                    ", count=" + result.Count);
                return result.AsReadOnly();
            }
            catch (Exception ex)
            {
                // Maintenance2 is an extension of the established room-fit request.
                // Preserve legacy placement if catalog data cannot be read.
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] maintenanceSpacesCatalogReadFailed familyKey=" +
                    familyKey + ", error=" + ex.Message);
                return Array.Empty<RoomCustomFamilyMaintenanceSpaceDto>();
            }
        }

        /// <summary>
        /// Returns the AHU-local side that must face the room door, based on the
        /// latest persisted Maintenance2 configuration.  The M sequence number is
        /// intentionally ignored because M1-M4 reflects click order, not direction.
        /// Returns an empty string when no Door Side is configured or the data is
        /// ambiguous, so the existing Python orientation behaviour remains available.
        /// </summary>
        internal static string GetDoorFacingSide(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return string.Empty;
            }

            try
            {
                RoomCustomFamilyCatalogDto catalog = RoomCustomFamilyCatalogFileService.LoadCatalog();
                RoomCustomFamilyCatalogItemDto item = catalog != null && catalog.Families != null
                    ? catalog.Families.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.Key, familyKey, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (item == null || item.MaintenanceSpaces == null || item.MaintenanceSpaces.Count == 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] doorFacingSide familyKey=" + familyKey + ", value=(none)");
                    return string.Empty;
                }

                List<RoomCustomFamilyMaintenanceSpaceDto> doorRows = item.MaintenanceSpaces
                    .Where(x => x != null && x.IsDoorSide)
                    .OrderBy(x => x.Sequence)
                    .ToList();

                if (doorRows.Count == 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] doorFacingSide familyKey=" + familyKey + ", value=(none)");
                    return string.Empty;
                }

                if (doorRows.Count > 1)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] doorFacingSideInvalid familyKey=" + familyKey +
                        ", reason=More than one Maintenance2 row is marked as Door Side, count=" +
                        doorRows.Count);
                    return string.Empty;
                }

                RoomCustomFamilyMaintenanceSpaceDto doorRow = doorRows[0];
                string side = (doorRow.Side ?? string.Empty).Trim();
                string normalized;
                if (string.Equals(side, "Top", StringComparison.OrdinalIgnoreCase)) normalized = "top";
                else if (string.Equals(side, "Bottom", StringComparison.OrdinalIgnoreCase)) normalized = "bottom";
                else if (string.Equals(side, "Left", StringComparison.OrdinalIgnoreCase)) normalized = "left";
                else if (string.Equals(side, "Right", StringComparison.OrdinalIgnoreCase)) normalized = "right";
                else
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] doorFacingSideInvalid familyKey=" + familyKey +
                        ", maintenance=" + (doorRow.MaintenanceCode ?? string.Empty) +
                        ", side=" + side);
                    return string.Empty;
                }

                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] doorFacingSide familyKey=" + familyKey +
                    ", maintenance=" + (doorRow.MaintenanceCode ?? string.Empty) +
                    ", value=" + normalized);
                return normalized;
            }
            catch (Exception ex)
            {
                // Door-facing information is an optional extension of the established
                // room-fit request.  Never block AHU insertion if catalog reading fails.
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] doorFacingSideCatalogReadFailed familyKey=" +
                    familyKey + ", error=" + ex.Message);
                return string.Empty;
            }
        }

        /// <summary>
        /// Returns the AHU-local sides configured to sit against room walls, based on
        /// the latest persisted Maintenance2 configuration.  The M sequence number is
        /// intentionally ignored because M1-M4 reflects click order, not direction.
        /// Zero to three sides are valid.  An empty list means no Wall Side is configured
        /// (or the persisted data is invalid/ambiguous), preserving the legacy API flow.
        /// </summary>
        internal static IReadOnlyList<string> GetWallFacingSides(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                return Array.Empty<string>();
            }

            try
            {
                RoomCustomFamilyCatalogDto catalog = RoomCustomFamilyCatalogFileService.LoadCatalog();
                RoomCustomFamilyCatalogItemDto item = catalog != null && catalog.Families != null
                    ? catalog.Families.FirstOrDefault(x =>
                        x != null &&
                        string.Equals(x.Key, familyKey, StringComparison.OrdinalIgnoreCase))
                    : null;

                if (item == null || item.MaintenanceSpaces == null || item.MaintenanceSpaces.Count == 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] wallFacingSides familyKey=" + familyKey + ", values=(none)");
                    return Array.Empty<string>();
                }

                List<RoomCustomFamilyMaintenanceSpaceDto> wallRows = item.MaintenanceSpaces
                    .Where(x => x != null && x.IsWallSide)
                    .OrderBy(x => x.Sequence)
                    .ToList();

                if (wallRows.Count == 0)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] wallFacingSides familyKey=" + familyKey + ", values=(none)");
                    return Array.Empty<string>();
                }

                if (wallRows.Count > 3)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] wallFacingSidesInvalid familyKey=" + familyKey +
                        ", reason=More than three Maintenance2 rows are marked as Wall Side, count=" +
                        wallRows.Count);
                    return Array.Empty<string>();
                }

                List<string> result = new List<string>();
                foreach (RoomCustomFamilyMaintenanceSpaceDto wallRow in wallRows)
                {
                    string side = (wallRow.Side ?? string.Empty).Trim();
                    string normalized;
                    if (string.Equals(side, "Top", StringComparison.OrdinalIgnoreCase)) normalized = "top";
                    else if (string.Equals(side, "Bottom", StringComparison.OrdinalIgnoreCase)) normalized = "bottom";
                    else if (string.Equals(side, "Left", StringComparison.OrdinalIgnoreCase)) normalized = "left";
                    else if (string.Equals(side, "Right", StringComparison.OrdinalIgnoreCase)) normalized = "right";
                    else
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[AhuRoomFitApi] wallFacingSidesInvalid familyKey=" + familyKey +
                            ", maintenance=" + (wallRow.MaintenanceCode ?? string.Empty) +
                            ", side=" + side);
                        return Array.Empty<string>();
                    }

                    if (!result.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(normalized);
                    }
                }

                if (result.Count > 3)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[AhuRoomFitApi] wallFacingSidesInvalid familyKey=" + familyKey +
                        ", reason=More than three unique wall-facing sides, count=" + result.Count);
                    return Array.Empty<string>();
                }

                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] wallFacingSides familyKey=" + familyKey +
                    ", values=" + string.Join(",", result));
                return result.AsReadOnly();
            }
            catch (Exception ex)
            {
                // Wall-facing information is an optional extension of the established
                // room-fit request.  Never block AHU insertion if catalog reading fails.
                DiagnosticRecorder.AppendDebug(
                    "[AhuRoomFitApi] wallFacingSidesCatalogReadFailed familyKey=" +
                    familyKey + ", error=" + ex.Message);
                return Array.Empty<string>();
            }
        }

        internal static void Reload()
        {
            GetOptions(true);
        }

        internal static RoomCustomFamilyCatalogMutationResult SaveCatalogAndReload(RoomCustomFamilyCatalogDto catalog)
        {
            return RoomCustomFamilyCatalogFileService.SaveCatalogAndReload(catalog);
        }

        internal static RoomCustomFamilyCatalogMutationResult DeleteFamilyImmediateAndReload(string familyKey)
        {
            return RoomCustomFamilyCatalogFileService.DeleteFamilyImmediateAndReload(familyKey);
        }

        internal static string ResolveBaseDirectory()
        {
            string dllPath = Assembly.GetExecutingAssembly().Location;
            return Path.GetDirectoryName(dllPath) ?? string.Empty;
        }
    }

    internal sealed class RoomCustomFamilyOption
    {
        internal RoomCustomFamilyOption(
            string key,
            string displayName,
            string originalFileName,
            string storedFileName,
            string fullPath,
            bool enabled,
            int sortOrder,
            string description,
            double airflowM3s,
            int mbLengthMm,
            int filterLengthMm,
            int coilLengthMm,
            int fanLengthMm,
            int totalLengthMm,
            int heightMm,
            int widthMm,
            int weightKg,
            int requiredMaintenanceSpaceMm,
            string requiredMaintenanceSpaceSide,
            int valveChamberLengthMm,
            int valveChamberWidthMm,
            int elChamberLengthMm,
            int elChamberWidthMm,
            int maintenanceDoorSideMm,
            int maintenanceOtherSideMm,
            int maintenanceFrontBackMm)
        {
            Key = key;
            OriginalFileName = originalFileName;
            StoredFileName = storedFileName;
            DisplayName = displayName;
            FullPath = fullPath;
            Enabled = enabled;
            SortOrder = sortOrder;
            Description = description ?? string.Empty;
            AirflowM3s = airflowM3s;
            MbLengthMm = mbLengthMm;
            FilterLengthMm = filterLengthMm;
            CoilLengthMm = coilLengthMm;
            FanLengthMm = fanLengthMm;
            TotalLengthMm = totalLengthMm;
            HeightMm = heightMm;
            WidthMm = widthMm;
            WeightKg = weightKg;
            RequiredMaintenanceSpaceMm = requiredMaintenanceSpaceMm;
            RequiredMaintenanceSpaceSide = string.IsNullOrWhiteSpace(requiredMaintenanceSpaceSide) ? "Access Side" : requiredMaintenanceSpaceSide;
            ValveChamberLengthMm = valveChamberLengthMm;
            ValveChamberWidthMm = valveChamberWidthMm;
            ElChamberLengthMm = elChamberLengthMm;
            ElChamberWidthMm = elChamberWidthMm;
            MaintenanceDoorSideMm = maintenanceDoorSideMm;
            MaintenanceOtherSideMm = maintenanceOtherSideMm;
            MaintenanceFrontBackMm = maintenanceFrontBackMm;
        }

        internal string Key { get; }

        internal string DisplayName { get; }

        internal string OriginalFileName { get; }

        internal string StoredFileName { get; }

        internal string FullPath { get; private set; }

        internal bool Enabled { get; }

        internal int SortOrder { get; }

        internal string Description { get; }

        internal double AirflowM3s { get; }

        internal int MbLengthMm { get; }

        internal int FilterLengthMm { get; }

        internal int CoilLengthMm { get; }

        internal int FanLengthMm { get; }

        internal int TotalLengthMm { get; }

        internal int HeightMm { get; }

        internal int WidthMm { get; }

        internal int WeightKg { get; }

        internal int RequiredMaintenanceSpaceMm { get; }

        internal string RequiredMaintenanceSpaceSide { get; }

        internal int ValveChamberLengthMm { get; }

        internal int ValveChamberWidthMm { get; }

        internal int ElChamberLengthMm { get; }

        internal int ElChamberWidthMm { get; }

        internal int MaintenanceDoorSideMm { get; }

        internal int MaintenanceOtherSideMm { get; }

        internal int MaintenanceFrontBackMm { get; }

        internal RoomCustomFamilyOption WithFullPath(string fullPath)
        {
            return new RoomCustomFamilyOption(
                Key,
                DisplayName,
                OriginalFileName,
                StoredFileName,
                fullPath,
                Enabled,
                SortOrder,
                Description,
                AirflowM3s,
                MbLengthMm,
                FilterLengthMm,
                CoilLengthMm,
                FanLengthMm,
                TotalLengthMm,
                HeightMm,
                WidthMm,
                WeightKg,
                RequiredMaintenanceSpaceMm,
                RequiredMaintenanceSpaceSide,
                ValveChamberLengthMm,
                ValveChamberWidthMm,
                ElChamberLengthMm,
                ElChamberWidthMm,
                MaintenanceDoorSideMm,
                MaintenanceOtherSideMm,
                MaintenanceFrontBackMm);
        }

        internal string FileName
        {
            get { return OriginalFileName; }
        }
    }

    internal static class RoomCustomFamilyCatalogDefaults
    {
        private static readonly IReadOnlyDictionary<string, RoomCustomFamilyAhuParameters> Defaults =
            new Dictionary<string, RoomCustomFamilyAhuParameters>(StringComparer.OrdinalIgnoreCase)
            {
                { "ahu_001", Create(1, 700, 700, 1550, 1750, 4700, 1550, 900, 1500, 1200, "Access Side", 1254, 600, 1750, 600, 1200, 600, 600) },
                { "ahu_002", Create(2, 700, 700, 1550, 1750, 4700, 1550, 900, 1500, 1200, "Access Side", 1254, 600, 1750, 600, 1200, 600, 600) },
                { "ahu_003", Create(3, 700, 700, 1550, 1750, 4700, 1550, 1500, 1500, 1200, "Access Side", 1254, 600, 1750, 600, 1500, 600, 600) },
                { "ahu_004", Create(4, 700, 700, 1550, 1750, 4700, 1900, 1500, 1500, 1200, "Access Side", 1254, 600, 1750, 600, 1500, 600, 600) },
                { "ahu_005", Create(5, 850, 700, 1550, 1750, 4850, 1900, 1800, 1500, 1200, "Access Side", 1400, 600, 1750, 600, 1800, 600, 600) },
                { "ahu_006", Create(6, 850, 700, 1550, 1800, 4900, 2200, 1800, 1500, 1200, "Access Side", 1400, 600, 1800, 600, 1800, 600, 600) },
                { "ahu_007", Create(7, 850, 700, 1550, 1800, 4900, 2200, 2100, 1500, 1200, "Access Side", 1400, 600, 1800, 600, 2100, 600, 600) },
                { "ahu_008", Create(8, 850, 700, 1550, 1800, 4900, 2200, 2450, 1500, 1200, "Access Side", 1400, 600, 1800, 600, 2450, 600, 600) },
                { "ahu_009", Create(9, 850, 700, 1550, 1800, 4900, 2200, 2450, 1500, 1200, "Access Side", 1400, 600, 1800, 600, 2450, 600, 600) },
                { "ahu_010", Create(10, 850, 700, 1550, 1800, 4900, 2200, 2750, 1500, 1200, "Access Side", 1400, 600, 1800, 600, 2750, 600, 600) }
            };

        internal static IReadOnlyCollection<string> FixedKeys
        {
            get { return Defaults.Keys.ToList().AsReadOnly(); }
        }

        internal static bool TryGet(string key, out RoomCustomFamilyAhuParameters parameters)
        {
            return Defaults.TryGetValue(key ?? string.Empty, out parameters);
        }

        internal static void ApplyDefaults(RoomCustomFamilyCatalogDto catalog)
        {
            if (catalog == null || catalog.Families == null)
            {
                return;
            }

            catalog.SchemaVersion = RoomCustomFamilyCatalogService.CurrentSchemaVersion;
            foreach (RoomCustomFamilyCatalogItemDto item in catalog.Families.Where(x => x != null))
            {
                if (item.SubModules == null)
                {
                    item.SubModules = new List<RoomCustomFamilySubModuleDto>();
                }

                if (item.MaintenanceSpaces == null)
                {
                    item.MaintenanceSpaces = new List<RoomCustomFamilyMaintenanceSpaceDto>();
                }

                if (TryGet(item.Key, out RoomCustomFamilyAhuParameters defaults))
                {
                    ApplyMissingValues(item, defaults);
                }
            }
        }

        private static void ApplyMissingValues(RoomCustomFamilyCatalogItemDto item, RoomCustomFamilyAhuParameters defaults)
        {
            if (item.AirflowM3s <= 0) item.AirflowM3s = defaults.AirflowM3s;
            if (item.MbLengthMm <= 0) item.MbLengthMm = defaults.MbLengthMm;
            if (item.FilterLengthMm <= 0) item.FilterLengthMm = defaults.FilterLengthMm;
            if (item.CoilLengthMm <= 0) item.CoilLengthMm = defaults.CoilLengthMm;
            if (item.FanLengthMm <= 0) item.FanLengthMm = defaults.FanLengthMm;
            if (item.TotalLengthMm <= 0) item.TotalLengthMm = defaults.TotalLengthMm;
            if (item.HeightMm <= 0) item.HeightMm = defaults.HeightMm;
            if (item.WidthMm <= 0) item.WidthMm = defaults.WidthMm;
            if (item.WeightKg <= 0) item.WeightKg = defaults.WeightKg;
            if (item.RequiredMaintenanceSpaceMm <= 0) item.RequiredMaintenanceSpaceMm = defaults.RequiredMaintenanceSpaceMm;
            if (string.IsNullOrWhiteSpace(item.RequiredMaintenanceSpaceSide)) item.RequiredMaintenanceSpaceSide = defaults.RequiredMaintenanceSpaceSide;
            if (item.ValveChamberLengthMm <= 0) item.ValveChamberLengthMm = defaults.ValveChamberLengthMm;
            if (item.ValveChamberWidthMm <= 0) item.ValveChamberWidthMm = defaults.ValveChamberWidthMm;
            if (item.ElChamberLengthMm <= 0) item.ElChamberLengthMm = defaults.ElChamberLengthMm;
            if (item.ElChamberWidthMm <= 0) item.ElChamberWidthMm = defaults.ElChamberWidthMm;
            if (item.MaintenanceDoorSideMm <= 0) item.MaintenanceDoorSideMm = defaults.MaintenanceDoorSideMm;
            if (item.MaintenanceOtherSideMm <= 0) item.MaintenanceOtherSideMm = defaults.MaintenanceOtherSideMm;
            if (item.MaintenanceFrontBackMm <= 0) item.MaintenanceFrontBackMm = defaults.MaintenanceFrontBackMm;
        }

        private static RoomCustomFamilyAhuParameters Create(
            double airflowM3s,
            int mbLengthMm,
            int filterLengthMm,
            int coilLengthMm,
            int fanLengthMm,
            int totalLengthMm,
            int heightMm,
            int widthMm,
            int weightKg,
            int requiredMaintenanceSpaceMm,
            string requiredMaintenanceSpaceSide,
            int valveChamberLengthMm,
            int valveChamberWidthMm,
            int elChamberLengthMm,
            int elChamberWidthMm,
            int maintenanceDoorSideMm,
            int maintenanceOtherSideMm,
            int maintenanceFrontBackMm)
        {
            return new RoomCustomFamilyAhuParameters
            {
                AirflowM3s = airflowM3s,
                MbLengthMm = mbLengthMm,
                FilterLengthMm = filterLengthMm,
                CoilLengthMm = coilLengthMm,
                FanLengthMm = fanLengthMm,
                TotalLengthMm = totalLengthMm,
                HeightMm = heightMm,
                WidthMm = widthMm,
                WeightKg = weightKg,
                RequiredMaintenanceSpaceMm = requiredMaintenanceSpaceMm,
                RequiredMaintenanceSpaceSide = string.IsNullOrWhiteSpace(requiredMaintenanceSpaceSide) ? "Access Side" : requiredMaintenanceSpaceSide,
                ValveChamberLengthMm = valveChamberLengthMm,
                ValveChamberWidthMm = valveChamberWidthMm,
                ElChamberLengthMm = elChamberLengthMm,
                ElChamberWidthMm = elChamberWidthMm,
                MaintenanceDoorSideMm = maintenanceDoorSideMm,
                MaintenanceOtherSideMm = maintenanceOtherSideMm,
                MaintenanceFrontBackMm = maintenanceFrontBackMm
            };
        }
    }
}
