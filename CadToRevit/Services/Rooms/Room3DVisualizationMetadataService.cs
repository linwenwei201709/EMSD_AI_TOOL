using Autodesk.Revit.DB;
using System;

namespace CadToRevit.Services.Rooms
{
    internal static class Room3DVisualizationMetadataService
    {
        internal static string BuildRegionName(string roomKey)
        {
            return Room3DVisualizationConstants.RegionNamePrefix + SanitizeRevitName(roomKey);
        }

        internal static string BuildMarkerName(string roomKey)
        {
            return Room3DVisualizationConstants.MarkerNamePrefix + SanitizeRevitName(roomKey);
        }

        internal static string BuildRegionDataId(string roomKey)
        {
            return Room3DVisualizationConstants.RegionDataPrefix + (roomKey ?? string.Empty);
        }

        internal static string BuildMarkerDataId(string roomKey)
        {
            return Room3DVisualizationConstants.MarkerDataPrefix + (roomKey ?? string.Empty);
        }

        internal static string BuildTextName(string roomKey)
        {
            return Room3DVisualizationConstants.TextNamePrefix + SanitizeRevitName(roomKey);
        }

        internal static string BuildTextDataId(string roomKey)
        {
            return Room3DVisualizationConstants.TextDataPrefix + (roomKey ?? string.Empty);
        }

        internal static bool IsManagedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return name.StartsWith(Room3DVisualizationConstants.RegionNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(Room3DVisualizationConstants.MarkerNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(Room3DVisualizationConstants.TextNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(Room3DVisualizationConstants.LegacyTagNamePrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsManagedMarkerName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                   name.StartsWith(Room3DVisualizationConstants.MarkerNamePrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryGetRoomKeyFromName(string name, out string roomKey)
        {
            roomKey = string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (name.StartsWith(Room3DVisualizationConstants.RegionNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                roomKey = name.Substring(Room3DVisualizationConstants.RegionNamePrefix.Length);
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.MarkerNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                roomKey = name.Substring(Room3DVisualizationConstants.MarkerNamePrefix.Length);
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.LegacyTagNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                roomKey = name.Substring(Room3DVisualizationConstants.LegacyTagNamePrefix.Length);
                return true;
            }

            if (name.StartsWith(Room3DVisualizationConstants.TextNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                roomKey = name.Substring(Room3DVisualizationConstants.TextNamePrefix.Length);
                return true;
            }

            return false;
        }

        internal static void ApplyMetadata(DirectShape shape, string name, string applicationDataId)
        {
            if (shape == null)
            {
                return;
            }

            shape.ApplicationId = Room3DVisualizationConstants.ApplicationId;
            shape.ApplicationDataId = applicationDataId ?? string.Empty;
            shape.Name = name ?? string.Empty;
        }

        internal static void ApplyMetadata(Element element, string name, string applicationDataId)
        {
            if (element == null)
            {
                return;
            }

            // For non-DirectShape elements (for example text family instances), store plugin metadata
            // in comments so refresh/highlight/clear can trace ownership safely.
            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments != null && !comments.IsReadOnly)
            {
                comments.Set(applicationDataId ?? string.Empty);
            }
        }

        internal static bool IsManagedTextElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            string data = comments != null ? comments.AsString() : string.Empty;
            return !string.IsNullOrWhiteSpace(data) &&
                   data.StartsWith(Room3DVisualizationConstants.TextDataPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeRevitName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string result = value;

            // Keep only safe characters for Revit element names.
            char[] invalidChars = new[] { '|', ':', ';', '<', '>', '?', '[', ']', '{', '}', '/', '\\' };
            foreach (char c in invalidChars)
            {
                result = result.Replace(c, '_');
            }

            return result.Trim();
        }
    }
}
