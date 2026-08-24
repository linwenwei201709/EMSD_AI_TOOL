# Colleague Frontend Compatibility Patch

Date: 2026-08-24  
Branch: `compat/current-features-20260824`  
Base: colleague repository commit `1cc5bb989c14f83b59b94110a114e8a366bd039f`  
Reference behavior: local current frontend commit `541d8f2`

## Intent

Keep the colleague UI layout and existing interaction flow. Add only the
contracts needed for the current backend/frontend behavior: IFC millimetre
room-fit requests, Excel six-module geometry, feasible placement feedback,
route restrictions, and failed-route red-zone visualization.

## Changes

### Room-fit compatibility

- `Models/Rooms/EquipmentValidation/AhuPlacementValidationRequest.cs`
  - Added evaluation mode, physical/maintenance evaluation flags, door-side
    options, door direction, and typed restricted-area fields.
- `Services/Rooms/EquipmentValidation/AhuPlacementValidationService.cs`
  - Sends `evaluation_mode`, `room_height_mm`, `evaluate_maintenance_space`,
    door direction/options, maintenance semantics, six-module polygons, and
    restricted areas.
  - Reads physical fit separately from maintenance fit and maps backend
    feasible placement coordinates back to Revit insertion.
  - Keeps legacy response fields and messages as fallback.
- `Models/Rooms/EquipmentValidation/AhuPlacementValidationResult.cs`
  - Added current-placement, feasible-placement, maintenance-reason and
    status-contract fields without removing legacy fields.
- `UI/Dockable/RoomRecognitionPaneRuntime.cs`
  - Supplies the detected door direction and current restricted-area list.
- `UI/Dockable/RoomDetailPaneViewModel.cs`
  - Uses a backend-suggested feasible placement point when returned.
  - Prefers the backend Excel layout catalogue, then falls back to the
    colleague's persisted family catalogue.
- `Services/Rooms/EquipmentValidation/AhuEquipmentLayoutCatalogService.cs`
  - New optional adapter for `/api/equipment/catalog`; failures fall back to
    the existing local catalog, so the colleague UI is not blocked when the
    API is offline.

### Route request/response compatibility

- `Services/PathPreview/CalculatePathApiService.cs`
  - Sends both plural `restricted_areas` and legacy singular
    `restricted_area` for cut-and-replan compatibility.
  - Adds the current 300 mm handling/safety defaults, explicit envelope and
    auto-goal flags, and `allow_disassembly=false` while retaining the existing
    public builder signatures.
  - When the backend returns independently verified transport groups, draws
    each group's IFC path with its own dimensions instead of rendering only
    the whole-AHU fallback path.
  - Maps `failure_type`, `applied_restrictions`, `need_cut`, and strategy
    metadata into the existing execution result.

### Failed-route red zones

- `Models/Path/RedZonePoint3D.cs` (new)
- `Services/PathPreview/PathPreviewConstants.cs`
- `Services/PathPreview/PathPreviewMaterialService.cs`
- `Services/PathPreview/Path3DVisualizationService.cs`
- `Services/PathPreview/CalculatePathApiService.cs`

  Failed responses containing `red_zones` are converted from IFC millimetres
  to Revit feet only at the drawing boundary, drawn as red Generic Model
  DirectShapes, framed in the preview view, and reported in the failure
  message. Successful-route drawing remains unchanged.

### Coordinate boundary

- `Services/Api/IfcMillimeterCoordinateAdapter.cs` (new)
  - Centralizes the existing 304.8 Revit-foot/IFC-mm conversion and angle sign
    convention. Only the room-fit and route API boundaries use it in this
    patch; unrelated colleague model calculations were not rewritten.

## Verification

- `git diff --check`: passed (only normal line-ending warnings).
- C# syntax pass with Roslyn: no syntax/duplicate-member errors in the changed
  files. Full compile is pending the .NET Framework 4.8 Developer Pack and
  Revit 2024/ObjectARX reference assemblies, which are not installed in this
  environment.
- The changed path-preview files were also compiled as a focused subset against
  the installed Revit 2023 API reference; no syntax or selected semantic errors
  were reported. This is not a substitute for the required Revit 2024 build.
- Revit UI installation/runtime testing has not been claimed by this patch.

## Deliberately not changed

- The colleague's XAML/UI layout and button flow.
- The colleague's original route-planning algorithm.
- IFC source files and Revit family definitions.
- The original GitHub `main` branch.
