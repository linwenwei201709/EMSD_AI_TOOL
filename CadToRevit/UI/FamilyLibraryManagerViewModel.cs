using CadToRevit.Models.Rooms;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Services.Rooms;
using CadToRevit.UI.Dockable;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CadToRevit.UI
{
    internal sealed class FamilyLibraryManagerViewModel : INotifyPropertyChanged
    {
        private const bool AllowFamilyMutation = false;
        private FamilyLibraryManagerItemViewModel _selectedItem;
        private bool _hasUnsavedChanges;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<FamilyLibraryManagerItemViewModel> Items { get; } =
            new ObservableCollection<FamilyLibraryManagerItemViewModel>();

        public string LibraryDirectory => RoomCustomFamilyCatalogFileService.GetLibraryDirectory();

        public string CatalogPath => RoomCustomFamilyCatalogFileService.GetCatalogPath();

        public FamilyLibraryManagerItemViewModel SelectedItem
        {
            get { return _selectedItem; }
            set { Set(ref _selectedItem, value); }
        }

        public bool HasUnsavedChanges
        {
            get { return _hasUnsavedChanges; }
            private set { Set(ref _hasUnsavedChanges, value); }
        }

        public void Reload()
        {
            RoomCustomFamilyCatalogDto catalog = RoomCustomFamilyCatalogFileService.LoadCatalog();

            Items.Clear();
            foreach (RoomCustomFamilyCatalogItemDto family in catalog.Families
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                FamilyLibraryManagerItemViewModel item = new FamilyLibraryManagerItemViewModel
                {
                    Key = family.Key,
                    DisplayName = family.DisplayName,
                    FileName = family.GetOriginalFileName(),
                    StoredFileName = family.GetStoredFileName(),
                    Enabled = family.Enabled,
                    SortOrder = family.SortOrder,
                    Description = family.Description ?? string.Empty,
                    AirflowM3s = family.AirflowM3s,
                    MbLengthMm = family.MbLengthMm,
                    FilterLengthMm = family.FilterLengthMm,
                    CoilLengthMm = family.CoilLengthMm,
                    FanLengthMm = family.FanLengthMm,
                    TotalLengthMm = family.TotalLengthMm,
                    HeightMm = family.HeightMm,
                    WidthMm = family.WidthMm,
                    WeightKg = family.WeightKg,
                    RequiredMaintenanceSpaceMm = family.RequiredMaintenanceSpaceMm,
                    RequiredMaintenanceSpaceSide = string.IsNullOrWhiteSpace(family.RequiredMaintenanceSpaceSide) ? "Access Side" : family.RequiredMaintenanceSpaceSide,
                    ValveChamberLengthMm = family.ValveChamberLengthMm,
                    ValveChamberWidthMm = family.ValveChamberWidthMm,
                    ElChamberLengthMm = family.ElChamberLengthMm,
                    ElChamberWidthMm = family.ElChamberWidthMm,
                    MaintenanceDoorSideMm = family.MaintenanceDoorSideMm,
                    MaintenanceOtherSideMm = family.MaintenanceOtherSideMm,
                    MaintenanceFrontBackMm = family.MaintenanceFrontBackMm,
                    IsNew = false
                };

                item.ReplaceSubModules((family.SubModules ?? new List<RoomCustomFamilySubModuleDto>())
                    .OrderBy(x => x.Sequence)
                    .Select(x => new FamilyLibrarySubModuleItemViewModel
                    {
                        Sequence = x.Sequence,
                        ModuleCode = string.IsNullOrWhiteSpace(x.ModuleCode) ? "S" + x.Sequence : x.ModuleCode,
                        GridRow = x.GridRow,
                        GridColumn = x.GridColumn,
                        Name = x.Name ?? string.Empty,
                        LengthMm = x.LengthMm,
                        WidthMm = x.WidthMm,
                        HeightMm = x.HeightMm,
                        WeightKg = x.WeightKg,
                        Photo = x.Photo ?? string.Empty
                    }));

                item.ReplaceMaintenanceSpaces((family.MaintenanceSpaces ?? new List<RoomCustomFamilyMaintenanceSpaceDto>())
                    .OrderBy(x => x.Sequence)
                    .Select(x => new FamilyLibraryMaintenanceSpaceItemViewModel
                    {
                        Sequence = x.Sequence,
                        MaintenanceCode = string.IsNullOrWhiteSpace(x.MaintenanceCode) ? "M" + x.Sequence : x.MaintenanceCode,
                        Side = x.Side ?? string.Empty,
                        DimensionMm = x.DimensionMm,
                        IsWallSide = x.IsWallSide,
                        IsDoorSide = x.IsDoorSide
                    }));

                AttachItem(item);
                Items.Add(item);
            }

            SelectedItem = Items.FirstOrDefault();
            HasUnsavedChanges = false;
        }

        public void AddFromFile(string sourceFilePath)
        {
            if (!AllowFamilyMutation)
            {
                throw new InvalidOperationException("The AHU family list is fixed. Adding families is disabled in this version.");
            }

            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.SelectedFileMissing));
            }

            string fileName = Path.GetFileName(sourceFilePath);
            if (!string.Equals(Path.GetExtension(fileName), ".rfa", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.OnlyRfaFiles));
            }

            FamilyLibraryManagerItemViewModel item = new FamilyLibraryManagerItemViewModel
            {
                Key = GenerateNextKey(),
                DisplayName = Path.GetFileNameWithoutExtension(fileName),
                FileName = fileName,
                StoredFileName = GenerateStoredFileName(),
                Enabled = true,
                SortOrder = GetNextSortOrder(),
                Description = string.Empty,
                WeightKg = 1500,
                RequiredMaintenanceSpaceMm = 1200,
                RequiredMaintenanceSpaceSide = "Access Side",
                IsNew = true,
                SourceFilePath = sourceFilePath
            };
            AttachItem(item);
            Items.Add(item);
            SelectedItem = item;
            HasUnsavedChanges = true;
        }

        public void DeleteSelected()
        {
            if (!AllowFamilyMutation)
            {
                throw new InvalidOperationException("The AHU family list is fixed. Deleting families is disabled in this version.");
            }

            FamilyLibraryManagerItemViewModel item = SelectedItem;
            if (item == null)
            {
                return;
            }

            Items.Remove(item);
            SelectedItem = Items.FirstOrDefault();
            HasUnsavedChanges = true;
        }

        public string DeleteSelectedImmediate()
        {
            if (!AllowFamilyMutation)
            {
                throw new InvalidOperationException("The AHU family list is fixed. Deleting families is disabled in this version.");
            }

            FamilyLibraryManagerItemViewModel item = SelectedItem;
            if (item == null)
            {
                throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.DeleteSelectionRequired));
            }

            if (item.IsNew)
            {
                Items.Remove(item);
                SelectedItem = Items.FirstOrDefault();
                HasUnsavedChanges = Items.Count > 0;
                return Loc.T(LocalizedKeys.FamilyLibrary.DeleteRemovedUnsaved);
            }

            if (HasUnsavedChanges)
            {
                throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.DeletePendingChanges));
            }

            string filePath = Path.Combine(LibraryDirectory, item.StoredFileName ?? string.Empty);
            RoomCustomFamilyCatalogDto catalog = RoomCustomFamilyCatalogFileService.LoadCatalog();
            RoomCustomFamilyCatalogItemDto existing = catalog.Families
                .FirstOrDefault(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.DeleteNotFoundInCatalog));
            }

            catalog.Families.Remove(existing);
            RoomCustomFamilyCatalogFileService.SaveCatalog(catalog);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            RefreshRoomDetailFamilyOptions();
            Reload();
            return Loc.T(LocalizedKeys.FamilyLibrary.DeleteSuccess);
        }

        public void Save()
        {
            List<FamilyLibraryManagerItemViewModel> currentItems = Items.ToList();
            RoomCustomFamilyCatalogDto catalog = BuildCatalog(currentItems);

            List<string> copiedFiles = new List<string>();

            Directory.CreateDirectory(LibraryDirectory);

            try
            {
                foreach (FamilyLibraryManagerItemViewModel item in currentItems.Where(x => x.IsNew))
                {
                    if (string.IsNullOrWhiteSpace(item.SourceFilePath) || !File.Exists(item.SourceFilePath))
                    {
                        throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.SaveMissingSourceFile));
                    }

                    if (string.IsNullOrWhiteSpace(item.StoredFileName))
                    {
                        item.StoredFileName = GenerateStoredFileName();
                    }

                    string destinationPath = Path.Combine(LibraryDirectory, item.StoredFileName);
                    if (File.Exists(destinationPath))
                    {
                        throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.SaveDestinationExists, item.StoredFileName));
                    }

                    File.Copy(item.SourceFilePath, destinationPath);
                    copiedFiles.Add(destinationPath);
                }

                RoomCustomFamilyCatalogFileService.SaveCatalog(catalog);

                foreach (FamilyLibraryManagerItemViewModel item in currentItems)
                {
                    item.MarkPersisted();
                }

                HasUnsavedChanges = false;
                RefreshRoomDetailFamilyOptions();
                Reload();
            }
            catch
            {
                foreach (string copiedFile in copiedFiles)
                {
                    if (File.Exists(copiedFile))
                    {
                        File.Delete(copiedFile);
                    }
                }

                throw;
            }
        }

        private static void RefreshRoomDetailFamilyOptions()
        {
            RoomCustomFamilyCatalogService.Reload();
            RoomRecognitionPaneRuntime.DetailViewModel.LoadFamilyOptions();
        }

        private RoomCustomFamilyCatalogDto BuildCatalog(List<FamilyLibraryManagerItemViewModel> items)
        {
            foreach (FamilyLibraryManagerItemViewModel item in items)
            {
                if (string.IsNullOrWhiteSpace(item.DisplayName))
                {
                    throw new InvalidOperationException(Loc.T(LocalizedKeys.FamilyLibrary.DisplayNameRequired));
                }
            }

            RoomCustomFamilyCatalogDto catalog = new RoomCustomFamilyCatalogDto
            {
                SchemaVersion = RoomCustomFamilyCatalogService.CurrentSchemaVersion,
                LibraryName = "AHU Family Library",
                FamilyFolderName = RoomCustomFamilyCatalogService.FamilyFolderName,
                Families = items
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new RoomCustomFamilyCatalogItemDto
                    {
                        Key = x.Key,
                        DisplayName = x.DisplayName,
                        OriginalFileName = x.FileName,
                        StoredFileName = x.StoredFileName,
                        Enabled = x.Enabled,
                        SortOrder = x.SortOrder,
                        Description = x.Description ?? string.Empty,
                        AirflowM3s = x.AirflowM3s,
                        MbLengthMm = x.MbLengthMm,
                        FilterLengthMm = x.FilterLengthMm,
                        CoilLengthMm = x.CoilLengthMm,
                        FanLengthMm = x.FanLengthMm,
                        TotalLengthMm = x.TotalLengthMm,
                        HeightMm = x.HeightMm,
                        WidthMm = x.WidthMm,
                        WeightKg = x.WeightKg,
                        RequiredMaintenanceSpaceMm = x.RequiredMaintenanceSpaceMm,
                        RequiredMaintenanceSpaceSide = string.IsNullOrWhiteSpace(x.RequiredMaintenanceSpaceSide) ? "Access Side" : x.RequiredMaintenanceSpaceSide,
                        ValveChamberLengthMm = x.ValveChamberLengthMm,
                        ValveChamberWidthMm = x.ValveChamberWidthMm,
                        ElChamberLengthMm = x.ElChamberLengthMm,
                        ElChamberWidthMm = x.ElChamberWidthMm,
                        MaintenanceDoorSideMm = x.MaintenanceDoorSideMm,
                        MaintenanceOtherSideMm = x.MaintenanceOtherSideMm,
                        MaintenanceFrontBackMm = x.MaintenanceFrontBackMm,
                        SubModules = x.SubModules
                            .OrderBy(m => m.Sequence)
                            .Select(m => new RoomCustomFamilySubModuleDto
                            {
                                Sequence = m.Sequence,
                                ModuleCode = m.ModuleCode,
                                GridRow = m.GridRow,
                                GridColumn = m.GridColumn,
                                Name = m.Name ?? string.Empty,
                                LengthMm = m.LengthMm,
                                WidthMm = m.WidthMm,
                                HeightMm = m.HeightMm,
                                WeightKg = m.WeightKg,
                                Photo = m.Photo ?? string.Empty
                            })
                            .ToList(),
                        MaintenanceSpaces = x.MaintenanceSpaces
                            .OrderBy(m => m.Sequence)
                            .Select(m => new RoomCustomFamilyMaintenanceSpaceDto
                            {
                                Sequence = m.Sequence,
                                MaintenanceCode = m.MaintenanceCode,
                                Side = m.Side,
                                DimensionMm = m.DimensionMm,
                                IsWallSide = m.IsWallSide,
                                IsDoorSide = m.IsDoorSide
                            })
                            .ToList()
                    })
                    .ToList()
            };

            RoomCustomFamilyCatalogValidator.ValidateCatalog(catalog);
            return catalog;
        }

        private void AttachItem(FamilyLibraryManagerItemViewModel item)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        private void OnItemPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            HasUnsavedChanges = true;
        }

        private int GetNextSortOrder()
        {
            int maxSort = Items.Count == 0 ? 0 : Items.Max(x => x.SortOrder);
            return ((maxSort / 10) + 1) * 10;
        }

        private string GenerateNextKey()
        {
            int maxValue = 0;

            foreach (string key in Items.Select(x => x.Key))
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                string digits = new string(key.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out int value) && value > maxValue)
                {
                    maxValue = value;
                }
            }

            return "ahu_" + (maxValue + 1).ToString("000");
        }

        private static string GenerateStoredFileName()
        {
            // Store physical family files with GUID names to avoid collisions.
            return Guid.NewGuid().ToString("N") + ".rfa";
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }

    internal sealed class FamilyLibraryManagerItemViewModel : INotifyPropertyChanged
    {
        private string _key;
        private string _displayName;
        private string _fileName;
        private string _storedFileName;
        private bool _enabled;
        private int _sortOrder;
        private string _description;
        private double _airflowM3s;
        private int _mbLengthMm;
        private int _filterLengthMm;
        private int _coilLengthMm;
        private int _fanLengthMm;
        private int _totalLengthMm;
        private int _heightMm;
        private int _widthMm;
        private int _weightKg;
        private int _requiredMaintenanceSpaceMm;
        private string _requiredMaintenanceSpaceSide;
        private int _valveChamberLengthMm;
        private int _valveChamberWidthMm;
        private int _elChamberLengthMm;
        private int _elChamberWidthMm;
        private int _maintenanceDoorSideMm;
        private int _maintenanceOtherSideMm;
        private int _maintenanceFrontBackMm;

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<FamilyLibrarySubModuleItemViewModel> SubModules { get; } =
            new ObservableCollection<FamilyLibrarySubModuleItemViewModel>();

        public ObservableCollection<FamilyLibraryMaintenanceSpaceItemViewModel> MaintenanceSpaces { get; } =
            new ObservableCollection<FamilyLibraryMaintenanceSpaceItemViewModel>();

        public string TotalSubModuleDimensionsText
        {
            get
            {
                int totalLength = SubModules.Sum(x => x.LengthMm);
                int totalWidth = SubModules.Sum(x => x.WidthMm);
                int totalHeight = SubModules.Sum(x => x.HeightMm);
                return "L:" + totalLength + " x W:" + totalWidth + " x H:" + totalHeight;
            }
        }

        public int TotalSubModuleWeightKg
        {
            get { return SubModules.Sum(x => x.WeightKg); }
        }

        public string SourceFilePath { get; set; }

        public bool IsNew { get; set; }

        public string Key
        {
            get { return _key; }
            set { Set(ref _key, value); }
        }

        public string DisplayName
        {
            get { return _displayName; }
            set { Set(ref _displayName, value); }
        }

        public string FileName
        {
            get { return _fileName; }
            set { Set(ref _fileName, value); }
        }

        public string StoredFileName
        {
            get { return _storedFileName; }
            set { Set(ref _storedFileName, value); }
        }

        public bool Enabled
        {
            get { return _enabled; }
            set { Set(ref _enabled, value); }
        }

        public int SortOrder
        {
            get { return _sortOrder; }
            set { Set(ref _sortOrder, value); }
        }

        public string Description
        {
            get { return _description; }
            set { Set(ref _description, value); }
        }

        public double AirflowM3s
        {
            get { return _airflowM3s; }
            set { Set(ref _airflowM3s, value); }
        }

        public int MbLengthMm
        {
            get { return _mbLengthMm; }
            set { Set(ref _mbLengthMm, value); }
        }

        public int FilterLengthMm
        {
            get { return _filterLengthMm; }
            set { Set(ref _filterLengthMm, value); }
        }

        public int CoilLengthMm
        {
            get { return _coilLengthMm; }
            set { Set(ref _coilLengthMm, value); }
        }

        public int FanLengthMm
        {
            get { return _fanLengthMm; }
            set { Set(ref _fanLengthMm, value); }
        }

        public int TotalLengthMm
        {
            get { return _totalLengthMm; }
            set { Set(ref _totalLengthMm, value); }
        }

        public int HeightMm
        {
            get { return _heightMm; }
            set { Set(ref _heightMm, value); }
        }

        public int WidthMm
        {
            get { return _widthMm; }
            set { Set(ref _widthMm, value); }
        }

        public int WeightKg
        {
            get { return _weightKg; }
            set { Set(ref _weightKg, value); }
        }

        public int RequiredMaintenanceSpaceMm
        {
            get { return _requiredMaintenanceSpaceMm; }
            set { Set(ref _requiredMaintenanceSpaceMm, value); }
        }

        public string RequiredMaintenanceSpaceSide
        {
            get { return _requiredMaintenanceSpaceSide; }
            set { Set(ref _requiredMaintenanceSpaceSide, value); }
        }

        public int ValveChamberLengthMm
        {
            get { return _valveChamberLengthMm; }
            set { Set(ref _valveChamberLengthMm, value); }
        }

        public int ValveChamberWidthMm
        {
            get { return _valveChamberWidthMm; }
            set { Set(ref _valveChamberWidthMm, value); }
        }

        public int ElChamberLengthMm
        {
            get { return _elChamberLengthMm; }
            set { Set(ref _elChamberLengthMm, value); }
        }

        public int ElChamberWidthMm
        {
            get { return _elChamberWidthMm; }
            set { Set(ref _elChamberWidthMm, value); }
        }

        public int MaintenanceDoorSideMm
        {
            get { return _maintenanceDoorSideMm; }
            set { Set(ref _maintenanceDoorSideMm, value); }
        }

        public int MaintenanceOtherSideMm
        {
            get { return _maintenanceOtherSideMm; }
            set { Set(ref _maintenanceOtherSideMm, value); }
        }

        public int MaintenanceFrontBackMm
        {
            get { return _maintenanceFrontBackMm; }
            set { Set(ref _maintenanceFrontBackMm, value); }
        }

        public void ReplaceSubModules(IEnumerable<FamilyLibrarySubModuleItemViewModel> rows)
        {
            foreach (FamilyLibrarySubModuleItemViewModel existing in SubModules.ToList())
            {
                existing.PropertyChanged -= OnSubModulePropertyChanged;
            }

            SubModules.Clear();

            foreach (FamilyLibrarySubModuleItemViewModel row in (rows ?? Enumerable.Empty<FamilyLibrarySubModuleItemViewModel>())
                .OrderBy(x => x.Sequence))
            {
                if (row == null)
                {
                    continue;
                }

                row.PropertyChanged += OnSubModulePropertyChanged;
                SubModules.Add(row);
            }

            RaiseSubModuleSummaryChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubModules)));
        }

        public void AddSubModule(FamilyLibrarySubModuleItemViewModel row)
        {
            if (row == null)
            {
                return;
            }

            row.PropertyChanged += OnSubModulePropertyChanged;
            SubModules.Add(row);
            RaiseSubModuleSummaryChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubModules)));
        }

        public void RemoveSubModulesFromSequence(int sequence)
        {
            List<FamilyLibrarySubModuleItemViewModel> rows = SubModules
                .Where(x => x.Sequence >= sequence)
                .OrderByDescending(x => x.Sequence)
                .ToList();

            foreach (FamilyLibrarySubModuleItemViewModel row in rows)
            {
                row.PropertyChanged -= OnSubModulePropertyChanged;
                SubModules.Remove(row);
            }

            ResequenceSubModules();
            RaiseSubModuleSummaryChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubModules)));
        }

        private void ResequenceSubModules()
        {
            int sequence = 1;
            foreach (FamilyLibrarySubModuleItemViewModel row in SubModules.OrderBy(x => x.Sequence).ToList())
            {
                row.Sequence = sequence;
                row.ModuleCode = "S" + sequence;
                sequence++;
            }
        }

        private void OnSubModulePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            RaiseSubModuleSummaryChanged();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubModules)));
        }

        private void RaiseSubModuleSummaryChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSubModuleDimensionsText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TotalSubModuleWeightKg)));
        }

        public void ReplaceMaintenanceSpaces(IEnumerable<FamilyLibraryMaintenanceSpaceItemViewModel> rows)
        {
            foreach (FamilyLibraryMaintenanceSpaceItemViewModel existing in MaintenanceSpaces.ToList())
            {
                existing.PropertyChanged -= OnMaintenanceSpacePropertyChanged;
            }

            MaintenanceSpaces.Clear();

            foreach (FamilyLibraryMaintenanceSpaceItemViewModel row in
                (rows ?? Enumerable.Empty<FamilyLibraryMaintenanceSpaceItemViewModel>())
                .OrderBy(x => x.Sequence))
            {
                if (row == null)
                {
                    continue;
                }

                row.PropertyChanged += OnMaintenanceSpacePropertyChanged;
                MaintenanceSpaces.Add(row);
            }

            EnforceSingleDoorSide(null);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaintenanceSpaces)));
        }

        public void AddMaintenanceSpace(FamilyLibraryMaintenanceSpaceItemViewModel row)
        {
            if (row == null)
            {
                return;
            }

            row.PropertyChanged += OnMaintenanceSpacePropertyChanged;
            MaintenanceSpaces.Add(row);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaintenanceSpaces)));
        }

        public void RemoveMaintenanceSpaceBySide(string side)
        {
            FamilyLibraryMaintenanceSpaceItemViewModel row = MaintenanceSpaces
                .FirstOrDefault(x => string.Equals(x.Side, side, StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                return;
            }

            row.PropertyChanged -= OnMaintenanceSpacePropertyChanged;
            MaintenanceSpaces.Remove(row);
            ResequenceMaintenanceSpaces();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaintenanceSpaces)));
        }

        private void ResequenceMaintenanceSpaces()
        {
            int sequence = 1;
            foreach (FamilyLibraryMaintenanceSpaceItemViewModel row in
                MaintenanceSpaces.OrderBy(x => x.Sequence).ToList())
            {
                row.Sequence = sequence;
                row.MaintenanceCode = "M" + sequence;
                sequence++;
            }
        }

        private void OnMaintenanceSpacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            FamilyLibraryMaintenanceSpaceItemViewModel changed = sender as FamilyLibraryMaintenanceSpaceItemViewModel;
            if (changed != null &&
                string.Equals(e.PropertyName, nameof(FamilyLibraryMaintenanceSpaceItemViewModel.IsDoorSide), StringComparison.Ordinal) &&
                changed.IsDoorSide)
            {
                EnforceSingleDoorSide(changed);
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MaintenanceSpaces)));
        }

        private void EnforceSingleDoorSide(FamilyLibraryMaintenanceSpaceItemViewModel selected)
        {
            FamilyLibraryMaintenanceSpaceItemViewModel keeper = selected;
            if (keeper == null)
            {
                keeper = MaintenanceSpaces
                    .Where(x => x.IsDoorSide)
                    .OrderBy(x => x.Sequence)
                    .FirstOrDefault();
            }

            foreach (FamilyLibraryMaintenanceSpaceItemViewModel row in MaintenanceSpaces)
            {
                if (!ReferenceEquals(row, keeper) && row.IsDoorSide)
                {
                    row.IsDoorSide = false;
                }
            }
        }

        public void MarkPersisted()
        {
            IsNew = false;
            SourceFilePath = null;
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class FamilyLibrarySubModuleItemViewModel : INotifyPropertyChanged
    {
        private int _sequence;
        private string _moduleCode = string.Empty;
        private int _gridRow;
        private int _gridColumn;
        private string _name = string.Empty;
        private int _lengthMm;
        private int _widthMm;
        private int _heightMm;
        private int _weightKg;
        private string _photo = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Sequence
        {
            get { return _sequence; }
            set { Set(ref _sequence, value); }
        }

        public string ModuleCode
        {
            get { return _moduleCode; }
            set { Set(ref _moduleCode, value ?? string.Empty); }
        }

        public int GridRow
        {
            get { return _gridRow; }
            set { Set(ref _gridRow, value); }
        }

        public int GridColumn
        {
            get { return _gridColumn; }
            set { Set(ref _gridColumn, value); }
        }

        public string Name
        {
            get { return _name; }
            set { Set(ref _name, value ?? string.Empty); }
        }

        public int LengthMm
        {
            get { return _lengthMm; }
            set
            {
                if (Set(ref _lengthMm, value))
                {
                    OnPropertyChanged(nameof(DimensionsMm));
                }
            }
        }

        public int WidthMm
        {
            get { return _widthMm; }
            set
            {
                if (Set(ref _widthMm, value))
                {
                    OnPropertyChanged(nameof(DimensionsMm));
                }
            }
        }

        public int HeightMm
        {
            get { return _heightMm; }
            set
            {
                if (Set(ref _heightMm, value))
                {
                    OnPropertyChanged(nameof(DimensionsMm));
                }
            }
        }

        public int WeightKg
        {
            get { return _weightKg; }
            set { Set(ref _weightKg, value); }
        }

        public string Photo
        {
            get { return _photo; }
            set
            {
                if (Set(ref _photo, value ?? string.Empty))
                {
                    OnPropertyChanged(nameof(PhotoFileName));
                    OnPropertyChanged(nameof(HasPhoto));
                    OnPropertyChanged(nameof(PhotoActionText));
                }
            }
        }

        public string PhotoFileName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Photo))
                {
                    return string.Empty;
                }

                string normalized = Photo.Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFileName(normalized) ?? string.Empty;
            }
        }

        public bool HasPhoto
        {
            get { return !string.IsNullOrWhiteSpace(Photo); }
        }

        public string PhotoActionText
        {
            get { return HasPhoto ? "Replace" : "Upload"; }
        }

        public string DimensionsMm
        {
            get { return "L:" + LengthMm + " x W:" + WidthMm + " x H:" + HeightMm; }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal sealed class FamilyLibraryMaintenanceSpaceItemViewModel : INotifyPropertyChanged
    {
        private int _sequence;
        private string _maintenanceCode = string.Empty;
        private string _side = string.Empty;
        private int _dimensionMm;
        private bool _isWallSide;
        private bool _isDoorSide;

        public event PropertyChangedEventHandler PropertyChanged;

        public int Sequence
        {
            get { return _sequence; }
            set { Set(ref _sequence, value); }
        }

        public string MaintenanceCode
        {
            get { return _maintenanceCode; }
            set { Set(ref _maintenanceCode, value ?? string.Empty); }
        }

        public string Side
        {
            get { return _side; }
            set { Set(ref _side, value ?? string.Empty); }
        }

        public int DimensionMm
        {
            get { return _dimensionMm; }
            set { Set(ref _dimensionMm, value); }
        }

        public bool IsWallSide
        {
            get { return _isWallSide; }
            set
            {
                if (!Set(ref _isWallSide, value))
                {
                    return;
                }

                if (value && _isDoorSide)
                {
                    _isDoorSide = false;
                    OnPropertyChanged(nameof(IsDoorSide));
                }
            }
        }

        public bool IsDoorSide
        {
            get { return _isDoorSide; }
            set
            {
                if (!Set(ref _isDoorSide, value))
                {
                    return;
                }

                if (value && _isWallSide)
                {
                    _isWallSide = false;
                    OnPropertyChanged(nameof(IsWallSide));
                }
            }
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
