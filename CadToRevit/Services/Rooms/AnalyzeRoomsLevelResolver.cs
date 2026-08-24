using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public sealed class AnalyzeRoomsLevelResolveResult
    {
        public Level Level { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public List<ElementId> ContextElementIds { get; set; } = new List<ElementId>();
        public bool Success => Level != null;
    }

    public static class AnalyzeRoomsLevelResolver
    {
        private const string CannotResolve3DMessage = "Cannot determine target level in 3D view. Please select a wall/model group or switch to a floor plan view before running Analyze Rooms.";

        public static AnalyzeRoomsLevelResolveResult Resolve(
            UIDocument uiDoc,
            View activeView,
            IEnumerable<ElementId> contextElementIds,
            bool allowModelFallback)
        {
            Document doc = uiDoc != null ? uiDoc.Document : activeView != null ? activeView.Document : null;
            AnalyzeRoomsLevelResolveResult result = new AnalyzeRoomsLevelResolveResult();
            if (doc == null)
            {
                result.Message = "Analyze Rooms failed: no active document.";
                return result;
            }

            DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] ActiveViewName=" + (activeView != null ? (activeView.Name ?? string.Empty) : string.Empty) +
                ", ActiveViewType=" + (activeView != null ? activeView.ViewType.ToString() : "null"));

            if (activeView != null && !(activeView is View3D) && activeView.GenLevel != null)
            {
                result.Level = doc.GetElement(activeView.GenLevel.Id) as Level;
                result.Reason = "ActiveViewGenLevel";
                result.Message = string.Empty;
                AppendResolved(result);
                return result;
            }

            List<ElementId> ids = BuildContextIds(uiDoc, contextElementIds);
            result.ContextElementIds = ids;
            Level level = ResolveFromElements(doc, ids, out string reason);
            if (level != null)
            {
                result.Level = level;
                result.Reason = reason;
                AppendResolved(result);
                return result;
            }

            if (allowModelFallback)
            {
                level = ResolveDominantWallLevel(doc, GetAllWalls(doc), "ModelWallLevelDistribution", out reason);
                if (level != null)
                {
                    result.Level = level;
                    result.Reason = reason;
                    AppendResolved(result);
                    return result;
                }
            }

            if (activeView is View3D)
            {
                result.Message = CannotResolve3DMessage;
                DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] AnalyzeLevelResolveFailed=" + result.Message);
                return result;
            }

            result.Level = ResolveDefaultLevel(doc);
            result.Reason = "DefaultLevelFallback";
            result.Message = result.Level == null ? "Analyze Rooms failed: no analysis level was found." : string.Empty;
            AppendResolved(result);
            return result;
        }

        private static List<ElementId> BuildContextIds(UIDocument uiDoc, IEnumerable<ElementId> contextElementIds)
        {
            HashSet<int> seen = new HashSet<int>();
            List<ElementId> result = new List<ElementId>();
            foreach (ElementId id in contextElementIds ?? Enumerable.Empty<ElementId>())
            {
                AddId(result, seen, id);
            }

            foreach (ElementId id in uiDoc != null ? uiDoc.Selection.GetElementIds() : Enumerable.Empty<ElementId>())
            {
                AddId(result, seen, id);
            }

            return result;
        }

        private static void AddId(List<ElementId> result, HashSet<int> seen, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId || !seen.Add(id.IntegerValue))
            {
                return;
            }

            result.Add(id);
        }

        private static Level ResolveFromElements(Document doc, List<ElementId> ids, out string reason)
        {
            reason = string.Empty;
            List<Wall> walls = new List<Wall>();
            foreach (ElementId id in ids ?? new List<ElementId>())
            {
                Element element = doc.GetElement(id);
                DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] SelectedElementId=" + id.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                    ", SelectedElementCategory=" + (element != null && element.Category != null ? (element.Category.Name ?? string.Empty) : "null"));
                Wall wall = element as Wall;
                if (wall != null)
                {
                    Level wallLevel = ResolveWallBaseLevel(doc, wall);
                    AppendWallDiagnostics(doc, wall, "SelectedWall");
                    if (wallLevel != null)
                    {
                        reason = "SelectedWallBaseConstraint";
                        return wallLevel;
                    }
                }

                Group group = element as Group;
                if (group != null)
                {
                    Level groupLevel = ResolveElementLevel(doc, group);
                    DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] SelectedModelGroupReferenceLevel=" + FormatLevel(groupLevel));
                    List<ElementId> memberIds = group.GetMemberIds().ToList();
                    List<Wall> groupWalls = memberIds.Select(x => doc.GetElement(x) as Wall).Where(x => x != null).ToList();
                    DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] GroupMemberCount=" + memberIds.Count.ToString(CultureInfo.InvariantCulture) +
                        ", GroupMemberWallCount=" + groupWalls.Count.ToString(CultureInfo.InvariantCulture));
                    Level dominant = ResolveDominantWallLevel(doc, groupWalls, "GroupMemberWallLevelDistribution", out string groupReason);
                    if (dominant != null)
                    {
                        DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] SelectedGroupReferenceLevel=" + FormatLevel(groupLevel) +
                            ", DominantMemberWallLevel=" + FormatLevel(dominant));
                        reason = groupReason;
                        return dominant;
                    }

                    if (groupLevel != null)
                    {
                        reason = "SelectedModelGroupReferenceLevel";
                        return groupLevel;
                    }
                }
            }

            walls.AddRange((ids ?? new List<ElementId>()).Select(x => doc.GetElement(x) as Wall).Where(x => x != null));
            return ResolveDominantWallLevel(doc, walls, "ContextWallLevelDistribution", out reason);
        }

        private static Level ResolveDominantWallLevel(Document doc, List<Wall> walls, string logName, out string reason)
        {
            reason = string.Empty;
            Dictionary<ElementId, int> counts = new Dictionary<ElementId, int>();
            foreach (Wall wall in walls ?? new List<Wall>())
            {
                Level level = ResolveWallBaseLevel(doc, wall);
                if (level == null)
                {
                    continue;
                }

                if (!counts.ContainsKey(level.Id))
                {
                    counts[level.Id] = 0;
                }

                counts[level.Id]++;
            }

            DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] " + logName + ": " + FormatDistribution(doc, counts));
            KeyValuePair<ElementId, int> best = counts.OrderByDescending(x => x.Value).FirstOrDefault();
            if (best.Key == null || best.Key == ElementId.InvalidElementId || best.Value <= 0)
            {
                return null;
            }

            reason = logName.IndexOf("Group", StringComparison.OrdinalIgnoreCase) >= 0
                ? "DominantGroupWallBaseConstraint"
                : "DominantWallBaseConstraint";
            return doc.GetElement(best.Key) as Level;
        }

        private static List<Wall> GetAllWalls(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(x => x != null)
                .ToList();
        }

        internal static Level ResolveWallBaseLevel(Document doc, Wall wall)
        {
            Parameter parameter = wall != null ? wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT) : null;
            ElementId levelId = parameter != null ? parameter.AsElementId() : ElementId.InvalidElementId;
            return levelId != null && levelId != ElementId.InvalidElementId ? doc.GetElement(levelId) as Level : null;
        }

        private static Level ResolveElementLevel(Document doc, Element element)
        {
            if (element == null || element.LevelId == null || element.LevelId == ElementId.InvalidElementId)
            {
                return null;
            }

            return doc.GetElement(element.LevelId) as Level;
        }

        private static Level ResolveDefaultLevel(Document doc)
        {
            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Where(x => x != null)
                .OrderBy(x => x.Elevation)
                .ToList();
            if (levels.Count == 0)
            {
                return null;
            }

            Level l1 = levels.FirstOrDefault(x =>
                string.Equals((x.Name ?? string.Empty).Trim(), "L1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals((x.Name ?? string.Empty).Trim(), "Level 1", StringComparison.OrdinalIgnoreCase));
            return l1 ?? levels.FirstOrDefault();
        }

        private static void AppendWallDiagnostics(Document doc, Wall wall, string prefix)
        {
            Level level = ResolveWallBaseLevel(doc, wall);
            BoundingBoxXYZ box = wall != null ? wall.get_BoundingBox(null) : null;
            DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] " + prefix +
                "BaseConstraint=" + FormatLevel(level) +
                ", BBoxMinZ=" + (box != null && box.Min != null ? box.Min.Z.ToString("F4", CultureInfo.InvariantCulture) : "-") +
                ", BBoxMaxZ=" + (box != null && box.Max != null ? box.Max.Z.ToString("F4", CultureInfo.InvariantCulture) : "-"));
        }

        private static void AppendResolved(AnalyzeRoomsLevelResolveResult result)
        {
            if (result == null || result.Level == null)
            {
                return;
            }

            DiagnosticRecorder.AppendDebug("[AnalyzeRoomsLevel] AnalyzeLevelResolved=" + (result.Level.Name ?? string.Empty) +
                ", AnalyzeLevelId=" + result.Level.Id.IntegerValue.ToString(CultureInfo.InvariantCulture) +
                ", AnalyzeLevelResolveReason=" + (result.Reason ?? string.Empty));
        }

        private static string FormatDistribution(Document doc, Dictionary<ElementId, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return "(empty)";
            }

            return string.Join(", ", counts
                .OrderByDescending(x => x.Value)
                .Select(x => FormatLevel(doc.GetElement(x.Key) as Level) + "=" + x.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private static string FormatLevel(Level level)
        {
            return level != null ? (level.Name ?? string.Empty) : "(none)";
        }
    }
}
