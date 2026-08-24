using CadToRevit.Models.Rooms;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms
{
    internal static class RoomCustomFamilyCatalogFileService
    {
        internal static string GetLibraryDirectory()
        {
            return Path.Combine(RoomCustomFamilyCatalogService.ResolveBaseDirectory(), RoomCustomFamilyCatalogService.FamilyFolderName);
        }

        internal static string GetCatalogPath()
        {
            return Path.Combine(GetLibraryDirectory(), "catalog.json");
        }

        internal static RoomCustomFamilyCatalogDto LoadCatalog()
        {
            string catalogPath = GetCatalogPath();
            if (!File.Exists(catalogPath))
            {
                throw new FileNotFoundException("Family catalog file not found.", catalogPath);
            }

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RoomCustomFamilyCatalogDto));
            using (FileStream stream = File.OpenRead(catalogPath))
            {
                RoomCustomFamilyCatalogDto catalog = serializer.ReadObject(stream) as RoomCustomFamilyCatalogDto;
                RoomCustomFamilyCatalogDefaults.ApplyDefaults(catalog);
                RoomCustomFamilyCatalogValidator.ValidateCatalog(catalog);
                return catalog;
            }
        }

        internal static IReadOnlyList<RoomCustomFamilyOption> LoadOptions()
        {
            try
            {
                RoomCustomFamilyCatalogDto catalog = LoadCatalog();
                string libraryDirectory = GetLibraryDirectory();
                return catalog.Families
                    .Where(x => x.Enabled)
                    .Select(x =>
                    {
                        x.NormalizeFileNames();
                        return new RoomCustomFamilyOption(
                            x.Key,
                            x.DisplayName,
                            x.OriginalFileName,
                            x.StoredFileName,
                            Path.Combine(libraryDirectory, x.StoredFileName),
                            x.Enabled,
                            x.SortOrder,
                            x.Description,
                            x.AirflowM3s,
                            x.MbLengthMm,
                            x.FilterLengthMm,
                            x.CoilLengthMm,
                            x.FanLengthMm,
                            x.TotalLengthMm,
                            x.HeightMm,
                            x.WidthMm,
                            x.WeightKg,
                            x.RequiredMaintenanceSpaceMm,
                            x.RequiredMaintenanceSpaceSide,
                            x.ValveChamberLengthMm,
                            x.ValveChamberWidthMm,
                            x.ElChamberLengthMm,
                            x.ElChamberWidthMm,
                            x.MaintenanceDoorSideMm,
                            x.MaintenanceOtherSideMm,
                            x.MaintenanceFrontBackMm);
                    })
                    .Where(FilterExistingOption)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomCustomFamily] Failed to load catalog: " + ex.Message);
                return Array.Empty<RoomCustomFamilyOption>();
            }
        }

        internal static void SaveCatalog(RoomCustomFamilyCatalogDto catalog)
        {
            RoomCustomFamilyCatalogDefaults.ApplyDefaults(catalog);
            if (catalog != null)
            {
                catalog.SchemaVersion = RoomCustomFamilyCatalogService.CurrentSchemaVersion;
            }

            RoomCustomFamilyCatalogValidator.ValidateCatalog(catalog);

            try
            {
                string libraryDirectory = GetLibraryDirectory();
                Directory.CreateDirectory(libraryDirectory);

                string catalogPath = GetCatalogPath();
                string tempPath = catalogPath + ".tmp";

                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RoomCustomFamilyCatalogDto));
                using (FileStream stream = File.Create(tempPath))
                {
                    serializer.WriteObject(stream, catalog);
                }

                string json = File.ReadAllText(tempPath, Encoding.UTF8);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));

                if (File.Exists(catalogPath))
                {
                    File.Delete(catalogPath);
                }

                File.Move(tempPath, catalogPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException(
                    "Unable to save AHU family catalog. Please check the installation folder permission or run Revit as administrator.",
                    ex);
            }
        }

        internal static RoomCustomFamilyCatalogMutationResult SaveCatalogAndReload(RoomCustomFamilyCatalogDto catalog)
        {
            SaveCatalog(catalog);
            RoomCustomFamilyCatalogService.Reload();
            return new RoomCustomFamilyCatalogMutationResult
            {
                Success = true,
                Message = Loc.T(LocalizedKeys.FamilyLibrary.SaveSuccess)
            };
        }

        internal static RoomCustomFamilyCatalogMutationResult DeleteFamilyImmediateAndReload(string familyKey)
        {
            if (string.IsNullOrWhiteSpace(familyKey))
            {
                throw new InvalidOperationException("Family key is required.");
            }

            RoomCustomFamilyCatalogDto catalog = LoadCatalog();
            RoomCustomFamilyCatalogItemDto item = catalog.Families
                .FirstOrDefault(x => string.Equals(x.Key, familyKey, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                throw new InvalidOperationException("The selected family could not be found in the catalog.");
            }

            string libraryDirectory = GetLibraryDirectory();
            item.NormalizeFileNames();
            string originalPath = Path.Combine(libraryDirectory, item.StoredFileName ?? string.Empty);
            string backupPath = File.Exists(originalPath)
                ? originalPath + ".bak." + Guid.NewGuid().ToString("N")
                : null;

            try
            {
                if (!string.IsNullOrWhiteSpace(backupPath))
                {
                    File.Move(originalPath, backupPath);
                }

                catalog.Families.Remove(item);
                SaveCatalog(catalog);

                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                RoomCustomFamilyCatalogService.Reload();
                return new RoomCustomFamilyCatalogMutationResult
                {
                    Success = true,
                    Message = Loc.T(LocalizedKeys.FamilyLibrary.DeleteSuccess)
                };
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    if (File.Exists(originalPath))
                    {
                        File.Delete(originalPath);
                    }

                    File.Move(backupPath, originalPath);
                }

                throw;
            }
        }

        private static bool FilterExistingOption(RoomCustomFamilyOption option)
        {
            if (option == null)
            {
                return false;
            }

            if (File.Exists(option.FullPath))
            {
                return true;
            }

            DiagnosticRecorder.AppendDebug(
                "[RoomCustomFamily] Catalog item hidden because file is missing. Key=" + (option.Key ?? string.Empty) +
                ", StoredFileName=" + (option.StoredFileName ?? string.Empty));
            return false;
        }
    }
}
