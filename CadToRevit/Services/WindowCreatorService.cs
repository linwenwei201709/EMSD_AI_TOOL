using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class WindowCreatorService
    {
        public static WindowCreateResult Create(
            Document doc,
            List<WindowCandidate> candidates,
            WindowCreateSettings settings)
        {
            return Create(doc, candidates, settings, null, null, null, true);
        }

        public static WindowCreateResult Create(
            Document doc,
            List<WindowCandidate> candidates,
            WindowCreateSettings settings,
            VerticalDimensionSettings vertical,
            FamilySymbol forcedSymbol,
            IList<Wall> hostWalls,
            bool useTransaction)
        {
            WindowCreateResult result = new WindowCreateResult();
            WindowCreateSettings effective = settings ?? new WindowCreateSettings();
            result.TotalCandidates = candidates == null ? 0 : candidates.Count;
            if (doc == null || candidates == null || candidates.Count == 0)
            {
                AddReason(result, "NoWindowSegments");
                return result;
            }

            FamilySymbol symbol = forcedSymbol ?? new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault();
            if (symbol == null)
            {
                AddReason(result, "NoWindowFamilyLoaded");
                result.SkippedCount = result.TotalCandidates;
                return result;
            }

            result.WindowSymbolName = symbol.Name;
            List<Wall> walls = hostWalls == null
                ? new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList()
                : hostWalls.Where(x => x != null).Distinct().ToList();

            Action createCore = () =>
            {
                try
                {
                    if (!symbol.IsActive)
                    {
                        symbol.Activate();
                        doc.Regenerate();
                    }
                }
                catch
                {
                    AddReason(result, "SymbolActivateFailed");
                    result.SkippedCount = result.TotalCandidates;
                    return;
                }

                foreach (WindowCandidate candidate in candidates)
                {
                    Wall wall;
                    XYZ insertPoint;
                    double distMm;
                    if (!NearestWallMatcher.TryMatch(candidate, walls, effective.WindowMatchMaxDistMm, out wall, out insertPoint, out distMm))
                    {
                        candidate.Status = "Skipped";
                        candidate.FailReason = "TooFarFromAnyWall";
                        result.SkippedCount++;
                        AddReason(result, candidate.FailReason);
                        continue;
                    }

                    candidate.HostWallId = wall.Id.IntegerValue;
                    candidate.MatchDistMm = distMm;

                    Level level = ResolveLevel(doc, wall);
                    if (level == null)
                    {
                        candidate.Status = "Skipped";
                        candidate.FailReason = "ProjectToWallFailed";
                        result.SkippedCount++;
                        AddReason(result, candidate.FailReason);
                        continue;
                    }

                    FamilyInstance instance = null;
                    try
                    {
                        instance = doc.Create.NewFamilyInstance(
                            insertPoint,
                            symbol,
                            wall,
                            level,
                            StructuralType.NonStructural);
                        candidate.CreatedElementId = instance.Id.IntegerValue;
                        candidate.Status = "Created";
                        result.CreatedCount++;
                        result.CreatedElementIds.Add(instance.Id.IntegerValue);
                    }
                    catch
                    {
                        candidate.Status = "Failed";
                        candidate.FailReason = "CreateInstanceFailed";
                        result.SkippedCount++;
                        AddReason(result, candidate.FailReason);
                        continue;
                    }

                    if (!TrySetWidth(instance, candidate.WidthMm))
                    {
                        result.WidthSetFailedCount++;
                    }
                    else
                    {
                        result.WidthSetSuccessCount++;
                    }

                    double sillMm = vertical != null && vertical.WindowSillHeightMm > 0
                        ? vertical.WindowSillHeightMm
                        : effective.DefaultSillHeightMm;
                    if (!TrySetSillHeight(instance, sillMm))
                    {
                        AddReason(result, "SetSillFailed");
                    }

                    if (!TryApplyWindowVerticalDimensions(doc, symbol, instance, vertical, result))
                    {
                        result.HeightSetFailedCount++;
                        AddReason(result, "SetWindowHeightFailed");
                    }
                    else
                    {
                        result.HeightSetSuccessCount++;
                    }

                    if (!TryFlipFacing(wall, instance))
                    {
                        AddReason(result, "FlipFailed");
                    }
                }
            };

            if (useTransaction)
            {
                using (Transaction tx = new Transaction(doc, "Create Windows"))
                {
                    tx.Start();
                    createCore();
                    tx.Commit();
                }
            }
            else
            {
                createCore();
            }

            return result;
        }

        private static Level ResolveLevel(Document doc, Wall wall)
        {
            if (wall != null && wall.LevelId != ElementId.InvalidElementId)
            {
                Level byWall = doc.GetElement(wall.LevelId) as Level;
                if (byWall != null)
                {
                    return byWall;
                }
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .FirstOrDefault();
        }

        private static bool TrySetWidth(FamilyInstance instance, double widthMm)
        {
            Parameter p = instance.LookupParameter("Width") ?? instance.LookupParameter("宽度") ?? instance.LookupParameter("寬度");
            if (p == null || p.IsReadOnly)
            {
                return false;
            }

            try
            {
                p.Set(UnitUtils.ConvertToInternalUnits(widthMm, UnitTypeId.Millimeters));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetSillHeight(FamilyInstance instance, double sillMm)
        {
            Parameter p = instance.LookupParameter("Sill Height") ?? instance.LookupParameter("窗台高度");
            if (p == null || p.IsReadOnly)
            {
                return false;
            }

            try
            {
                p.Set(UnitUtils.ConvertToInternalUnits(sillMm, UnitTypeId.Millimeters));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryFlipFacing(Wall wall, FamilyInstance instance)
        {
            LocationCurve loc = wall.Location as LocationCurve;
            Line line = loc?.Curve as Line;
            if (line == null)
            {
                return false;
            }

            try
            {
                XYZ wallDir = line.Direction.Normalize();
                XYZ facing = instance.FacingOrientation.Normalize();
                if (wallDir.DotProduct(facing) < 0)
                {
                    instance.flipFacing();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyWindowVerticalDimensions(
            Document doc,
            FamilySymbol symbol,
            FamilyInstance instance,
            VerticalDimensionSettings vertical,
            WindowCreateResult result)
        {
            if (doc == null || symbol == null || instance == null || vertical == null)
            {
                return false;
            }

            bool ok = false;
            if (vertical.WindowHeightMm > 0)
            {
                FamilySymbol targetType = EnsureWindowTypeWithHeight(doc, symbol, vertical.WindowHeightMm);
                if (targetType != null && instance.Symbol.Id.IntegerValue != targetType.Id.IntegerValue)
                {
                    instance.Symbol = targetType;
                }

                ok = targetType != null;
            }

            if (vertical.WindowHeadHeightMm > 0 || vertical.PreferSillPlusHeight)
            {
                double headMm = vertical.PreferSillPlusHeight
                    ? (vertical.WindowSillHeightMm + vertical.WindowHeightMm)
                    : vertical.WindowHeadHeightMm;
                ok = RevitParameterSetters.TrySetByNames(instance, headMm, "Head Height", "Window Head Height") || ok;
            }

            return ok;
        }

        private static FamilySymbol EnsureWindowTypeWithHeight(Document doc, FamilySymbol source, double targetHeightMm)
        {
            if (doc == null || source == null || targetHeightMm <= 0)
            {
                return source;
            }

            if (HasHeightValue(source, targetHeightMm))
            {
                return source;
            }

            string suffix = "_H" + ((int)Math.Round(targetHeightMm)).ToString();
            string typeName = source.Name + suffix;
            FamilySymbol existing = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Windows)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x != null &&
                                     x.FamilyName == source.FamilyName &&
                                     string.Equals(x.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            try
            {
                FamilySymbol dup = source.Duplicate(typeName) as FamilySymbol;
                if (dup == null)
                {
                    return source;
                }

                bool setOk = RevitParameterSetters.TrySetTypeByNames(dup, targetHeightMm, "Height", "Rough Height", "Window Height");
                return setOk ? dup : source;
            }
            catch
            {
                return source;
            }
        }

        private static bool HasHeightValue(FamilySymbol symbol, double targetHeightMm)
        {
            if (symbol == null || targetHeightMm <= 0)
            {
                return false;
            }

            Parameter p = symbol.LookupParameter("Height") ?? symbol.LookupParameter("Rough Height") ?? symbol.LookupParameter("Window Height");
            if (p == null || p.StorageType != StorageType.Double)
            {
                return false;
            }

            double mm = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters);
            return Math.Abs(mm - targetHeightMm) <= 1.0;
        }

        private static void AddReason(WindowCreateResult result, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            if (!result.SkipByReason.ContainsKey(reason))
            {
                result.SkipByReason[reason] = 0;
            }

            result.SkipByReason[reason]++;
        }
    }
}
