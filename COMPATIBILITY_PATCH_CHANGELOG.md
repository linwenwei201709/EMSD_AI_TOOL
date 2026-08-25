# Colleague Frontend Compatibility Patch

## 2026-08-25 - Feed imported-DWG door candidates into room fit

- `UI/Dockable/RoomRecognitionPaneRuntime.cs` now keeps the existing native
  Revit door/opening resolver as the first choice and, only when it has no
  usable result, detects candidates from the active imported DWG `DOOR` layer
  through `DoorCandidateDetector`.
- DWG candidates are matched to the selected room boundary, and their opening
  center, width and wall direction are used for door-facing orientation. The
  active DWG import is cached per document/import/fingerprint so every room
  does not re-parse the CAD geometry.
- DWG candidates are marked with `source=DWG`; an unknown CAD door height is
  left as unavailable (`0`/`-` in the UI) rather than being fabricated.
- Native Revit door priority, the existing UI flow, coordinate conversion and
  route algorithm remain unchanged.
- Added a bounded raw-layer fallback for door-named layers such as `A-DOOR`
  and `ARCH-DOOR`, with center/width de-duplication. Generic non-door layers
  are not scanned.

## 2026-08-25 - Preserve the resolved room-door metadata

- Added optional door metadata to the room-fit preparation result and request:
  Revit element id, width/height, IFC-mm center, source and `found` state.
- The existing `DoorMetricCandidate` resolver is still used once per
  preparation. Its existing priority and fallback rules are unchanged.
- `/api/check_room_fit` requests now include an additive `door_info` object.
  Older backends can ignore it; it prevents future clients from needing to
  re-scan IFC or the Revit document to identify the selected door.
- When the candidate is valid, its dimensions take precedence over stale or
  empty display text. If it is unavailable, the previous display/request
  fallback is retained.
- Added `[AhuRoomFit] doorInfo ...` diagnostics for troubleshooting.

No UI layout, route algorithm, coordinate conversion, maintenance-side
selection, or door-candidate ordering was changed.

## 2026-08-24 - Accept cached verified transport groups

- Updated `CadToRevit/Services/PathPreview/CalculatePathApiService.cs` to accept both `independently_verified` and `verified_exact_input_cache` transport-group verification states.
- This preserves the backend's validated minimum-disassembly route when a group reuses an exact-input cached path; no coordinate conversion, route planning, or module-layout logic was changed.

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
    auto-goal flags. The original compatibility snapshot used
    `allow_disassembly=false`; the 2026-08-24 follow-up below changes this to
    `true` and attaches the validated six-module layout.
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
## 2026-08-24 - Restore validated modular route planning

### Changed files

- `CadToRevit/Services/PathPreview/CalculatePathApiService.cs`
  - Changed `cut_and_replan` requests from `allow_disassembly: false` to
    `allow_disassembly: true`.
  - Added `sub_modules` to the request. The payload is built from the backend
    workbook-backed `/api/equipment/catalog` layout for the selected
    `original_model_id`, including module names, dimensions, heights and local
    millimetre polygon points.
  - If the optional catalog cannot be read or does not contain a valid six-
    module layout, the request sends `sub_modules: []` and keeps the backend's
    legacy three-envelope fallback instead of inventing geometry.
  - Added diagnostic records for catalog fallback, invalid module geometry and
    successful module attachment.

- `CadToRevit/UI/Dockable/RoomDetailPaneViewModel.cs`
  - Replaced the hard-coded route model ID `1` with the confirmed equipment's
    family/model ID.
  - Stops with a clear message if the confirmed equipment model cannot be
    resolved, preventing the wrong sizing layout from being sent.

### Behaviour and compatibility notes

- Whole-unit routing remains the first attempt. Modular splitting is only
  considered by the backend after the rigid whole-unit route fails.
- The UI response handling was not changed; existing `need_cut`, group paths
  and disassembly result rendering continue to be used.
- This change is specifically for restoring the previously verified modular
  route behaviour (for example, the Service Lift to AHU Room 2 case). It does
  not alter IFC coordinate conversion, clearance values or collision rules.
