using CadToRevit.Models.Rooms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomCustomFamilyCatalogValidator
    {
        internal static void ValidateCatalog(RoomCustomFamilyCatalogDto catalog)
        {
            if (catalog == null)
            {
                throw new InvalidOperationException("Family catalog is null.");
            }

            if (catalog.SchemaVersion <= 0)
            {
                throw new InvalidOperationException("Family catalog schemaVersion must be greater than 0.");
            }

            if (!string.Equals(catalog.FamilyFolderName, RoomCustomFamilyCatalogService.FamilyFolderName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Family catalog folder name does not match the expected family folder.");
            }

            if (catalog.Families == null)
            {
                throw new InvalidOperationException("Family catalog families collection is missing.");
            }

            List<RoomCustomFamilyCatalogItemDto> items = catalog.Families.Where(x => x != null).ToList();
            if (items.Count != catalog.Families.Count)
            {
                throw new InvalidOperationException("Family catalog contains null items.");
            }

            foreach (RoomCustomFamilyCatalogItemDto item in items)
            {
                item.NormalizeFileNames();
            }

            ValidateUnique(items, x => x.Key, "key");
            ValidateUnique(items, x => x.StoredFileName, "storedFileName");
            ValidateFixedAhuKeys(items);
            ValidateUniqueAirflow(items);

            foreach (RoomCustomFamilyCatalogItemDto item in items)
            {
                if (string.IsNullOrWhiteSpace(item.DisplayName))
                {
                    throw new InvalidOperationException("Family displayName cannot be empty.");
                }

                if (string.IsNullOrWhiteSpace(item.OriginalFileName))
                {
                    throw new InvalidOperationException("Family originalFileName cannot be empty.");
                }

                if (string.IsNullOrWhiteSpace(item.StoredFileName))
                {
                    throw new InvalidOperationException("Family storedFileName cannot be empty.");
                }

                if (!string.Equals(Path.GetExtension(item.OriginalFileName), ".rfa", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Family originalFileName must use the .rfa extension.");
                }

                if (!string.Equals(Path.GetExtension(item.StoredFileName), ".rfa", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Family storedFileName must use the .rfa extension.");
                }

                if (item.OriginalFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidOperationException("Family originalFileName contains invalid characters.");
                }

                if (item.StoredFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    throw new InvalidOperationException("Family storedFileName contains invalid characters.");
                }

                ValidateAhuParameters(item);
                ValidateSubModules(item);
                ValidateMaintenanceSpaces(item);
            }
        }

        private static void ValidateSubModules(RoomCustomFamilyCatalogItemDto item)
        {
            if (item.SubModules == null)
            {
                item.SubModules = new List<RoomCustomFamilySubModuleDto>();
                return;
            }

            List<RoomCustomFamilySubModuleDto> rows = item.SubModules
                .Where(x => x != null)
                .OrderBy(x => x.Sequence)
                .ToList();

            if (rows.Count != item.SubModules.Count)
            {
                throw new InvalidOperationException("Family subModules cannot contain null items.");
            }

            HashSet<string> occupiedCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Count; i++)
            {
                RoomCustomFamilySubModuleDto row = rows[i];
                int expectedSequence = i + 1;
                if (row.Sequence != expectedSequence)
                {
                    throw new InvalidOperationException(
                        "Family subModules sequence must be continuous from S1.");
                }

                string expectedCode = "S" + expectedSequence;
                if (string.IsNullOrWhiteSpace(row.ModuleCode))
                {
                    row.ModuleCode = expectedCode;
                }
                else if (!string.Equals(row.ModuleCode.Trim(), expectedCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Family subModules moduleCode must match its sequence: " + expectedCode + ".");
                }

                if (row.GridRow < 0 || row.GridRow >= 4 ||
                    row.GridColumn < 0 || row.GridColumn >= 6)
                {
                    throw new InvalidOperationException(
                        "Family subModules grid position must be inside the 6 x 4 layout.");
                }

                string cellKey = row.GridRow + ":" + row.GridColumn;
                if (!occupiedCells.Add(cellKey))
                {
                    throw new InvalidOperationException(
                        "Family subModules cannot occupy the same grid cell.");
                }

                if (i > 0)
                {
                    RoomCustomFamilySubModuleDto previous = rows[i - 1];
                    int manhattan =
                        Math.Abs(row.GridRow - previous.GridRow) +
                        Math.Abs(row.GridColumn - previous.GridColumn);
                    if (manhattan != 1)
                    {
                        throw new InvalidOperationException(
                            row.ModuleCode + " must be adjacent to " + previous.ModuleCode + ".");
                    }
                }

                ValidateNonNegative(row.LengthMm, "subModules.lengthMm");
                ValidateNonNegative(row.WidthMm, "subModules.widthMm");
                ValidateNonNegative(row.HeightMm, "subModules.heightMm");
                ValidateNonNegative(row.WeightKg, "subModules.weightKg");
                row.Name = row.Name ?? string.Empty;
                row.Photo = row.Photo ?? string.Empty;
            }
        }


        private static void ValidateMaintenanceSpaces(RoomCustomFamilyCatalogItemDto item)
        {
            if (item.MaintenanceSpaces == null)
            {
                item.MaintenanceSpaces = new List<RoomCustomFamilyMaintenanceSpaceDto>();
                return;
            }

            List<RoomCustomFamilyMaintenanceSpaceDto> rows = item.MaintenanceSpaces
                .Where(x => x != null)
                .OrderBy(x => x.Sequence)
                .ToList();

            if (rows.Count != item.MaintenanceSpaces.Count)
            {
                throw new InvalidOperationException("Family maintenanceSpaces cannot contain null items.");
            }

            if (rows.Count > 4)
            {
                throw new InvalidOperationException("Family maintenanceSpaces cannot contain more than four sides.");
            }

            if (rows.Count > 0 && (item.SubModules == null || item.SubModules.Count == 0))
            {
                throw new InvalidOperationException("Family maintenanceSpaces require at least one Sub-Module.");
            }

            HashSet<string> occupiedSides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int doorSideCount = 0;
            int wallSideCount = 0;

            for (int i = 0; i < rows.Count; i++)
            {
                RoomCustomFamilyMaintenanceSpaceDto row = rows[i];
                int expectedSequence = i + 1;
                if (row.Sequence != expectedSequence)
                {
                    throw new InvalidOperationException(
                        "Family maintenanceSpaces sequence must be continuous from M1.");
                }

                string expectedCode = "M" + expectedSequence;
                if (string.IsNullOrWhiteSpace(row.MaintenanceCode))
                {
                    row.MaintenanceCode = expectedCode;
                }
                else if (!string.Equals(row.MaintenanceCode.Trim(), expectedCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Family maintenanceSpaces maintenanceCode must match its sequence: " + expectedCode + ".");
                }

                string side = NormalizeMaintenanceSide(row.Side);
                if (string.IsNullOrWhiteSpace(side))
                {
                    throw new InvalidOperationException(
                        "Family maintenanceSpaces side must be Top, Bottom, Left, or Right.");
                }

                row.Side = side;
                if (!occupiedSides.Add(side))
                {
                    throw new InvalidOperationException(
                        "Family maintenanceSpaces cannot contain duplicate side: " + side + ".");
                }

                ValidatePositive(row.DimensionMm, "maintenanceSpaces.dimensionMm");

                if (row.IsWallSide && row.IsDoorSide)
                {
                    throw new InvalidOperationException(
                        row.MaintenanceCode + " cannot be both Wall Side and Door Side.");
                }

                if (row.IsDoorSide)
                {
                    doorSideCount++;
                }

                if (row.IsWallSide)
                {
                    wallSideCount++;
                }
            }

            if (doorSideCount > 1)
            {
                throw new InvalidOperationException(
                    "Only one maintenance space can be marked as Door Side.");
            }

            if (wallSideCount > 3)
            {
                throw new InvalidOperationException(
                    "At most three maintenance spaces can be marked as Wall Side.");
            }
        }

        private static string NormalizeMaintenanceSide(string side)
        {
            string value = (side ?? string.Empty).Trim();
            if (string.Equals(value, "Top", StringComparison.OrdinalIgnoreCase)) return "Top";
            if (string.Equals(value, "Bottom", StringComparison.OrdinalIgnoreCase)) return "Bottom";
            if (string.Equals(value, "Left", StringComparison.OrdinalIgnoreCase)) return "Left";
            if (string.Equals(value, "Right", StringComparison.OrdinalIgnoreCase)) return "Right";
            return string.Empty;
        }

        private static void ValidateFixedAhuKeys(List<RoomCustomFamilyCatalogItemDto> items)
        {
            List<string> expectedKeys = RoomCustomFamilyCatalogDefaults.FixedKeys
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> actualKeys = items
                .Select(x => Normalize(x.Key))
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (actualKeys.Count != expectedKeys.Count ||
                actualKeys.Where((key, index) => !string.Equals(key, expectedKeys[index], StringComparison.OrdinalIgnoreCase)).Any())
            {
                throw new InvalidOperationException("Family catalog must contain exactly the fixed AHU keys ahu_001 to ahu_010.");
            }
        }

        private static void ValidateUniqueAirflow(List<RoomCustomFamilyCatalogItemDto> items)
        {
            double duplicate = items
                .GroupBy(x => x.AirflowM3s)
                .Where(x => x.Key > 0 && x.Count() > 1)
                .Select(x => x.Key)
                .FirstOrDefault();

            if (duplicate > 0)
            {
                throw new InvalidOperationException("Duplicate AHU airflowM3s: " + duplicate.ToString("0.###"));
            }
        }

        private static void ValidateAhuParameters(RoomCustomFamilyCatalogItemDto item)
        {
            if (item.AirflowM3s <= 0)
            {
                throw new InvalidOperationException("Family airflowM3s must be greater than 0.");
            }

            ValidatePositive(item.TotalLengthMm, "totalLengthMm");
            ValidatePositive(item.HeightMm, "heightMm");
            ValidatePositive(item.WidthMm, "widthMm");
            ValidateNonNegative(item.WeightKg, "weightKg");
            ValidateNonNegative(item.RequiredMaintenanceSpaceMm, "requiredMaintenanceSpaceMm");
            if (string.IsNullOrWhiteSpace(item.RequiredMaintenanceSpaceSide))
            {
                item.RequiredMaintenanceSpaceSide = "Access Side";
            }

            ValidateNonNegative(item.MbLengthMm, "mbLengthMm");
            ValidateNonNegative(item.FilterLengthMm, "filterLengthMm");
            ValidateNonNegative(item.CoilLengthMm, "coilLengthMm");
            ValidateNonNegative(item.FanLengthMm, "fanLengthMm");
            ValidateNonNegative(item.ValveChamberLengthMm, "valveChamberLengthMm");
            ValidateNonNegative(item.ValveChamberWidthMm, "valveChamberWidthMm");
            ValidateNonNegative(item.ElChamberLengthMm, "elChamberLengthMm");
            ValidateNonNegative(item.ElChamberWidthMm, "elChamberWidthMm");
            ValidateNonNegative(item.MaintenanceDoorSideMm, "maintenanceDoorSideMm");
            ValidateNonNegative(item.MaintenanceOtherSideMm, "maintenanceOtherSideMm");
            ValidateNonNegative(item.MaintenanceFrontBackMm, "maintenanceFrontBackMm");
        }

        private static void ValidatePositive(int value, string fieldName)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("Family " + fieldName + " must be greater than 0.");
            }
        }

        private static void ValidateNonNegative(int value, string fieldName)
        {
            if (value < 0)
            {
                throw new InvalidOperationException("Family " + fieldName + " cannot be negative.");
            }
        }

        private static void ValidateUnique(
            IEnumerable<RoomCustomFamilyCatalogItemDto> items,
            Func<RoomCustomFamilyCatalogItemDto, string> selector,
            string fieldName)
        {
            string duplicate = items
                .GroupBy(x => Normalize(selector(x)), StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Count() > 1)
                .Select(x => x.Key)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(duplicate))
            {
                throw new InvalidOperationException("Duplicate family " + fieldName + ": " + duplicate);
            }

            if (items.Any(x => string.IsNullOrWhiteSpace(selector(x))))
            {
                throw new InvalidOperationException("Family " + fieldName + " cannot be empty.");
            }
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
