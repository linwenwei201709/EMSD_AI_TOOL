# EMSD AI Tool Compatibility Project

This repository is a lightweight source snapshot of the Revit front end with
the current room-fit, route-request, restriction, and red-zone compatibility
patches applied. It is intended to be the integration base for the EMSD AI
Tool front end and backend contracts.

The compatibility change list is in
[`COMPATIBILITY_PATCH_CHANGELOG.md`](COMPATIBILITY_PATCH_CHANGELOG.md).

# CadToRevit

Revit plugin project for converting CAD (DWG) content into Revit elements.

## Main Features

- Layer-based mapping for walls, doors, windows, and columns
- Advanced per-layer recognition settings
- Persistence for mapping and override settings
- Debug and diagnostic logs under `CadToRevit/LOG`

## Project Structure

- `CadToRevit.sln`
- `CadToRevit/` source code
- `CadToRevit/LOG/` runtime and debug logs (generated locally; not committed)

## Snapshot scope

The repository intentionally excludes machine-specific or generated payloads
from the initial source snapshot: NuGet `packages/`, Teigha binaries, Revit
family files (`*.rfa`), linked RVT models, and historical log files. Restore
the required Revit/Teigha dependencies from the local development environment
before building or packaging an installer.

## Build

Open `CadToRevit.sln` in Visual Studio and build the `CadToRevit` project.

