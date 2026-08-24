using Autodesk.Revit.DB;
using System;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewMetadataService
    {
        internal static string BuildSegmentName(string pathId, int index)
        {
            return PathPreviewConstants.SegmentNamePrefix + SanitizeRevitName(pathId) + "__" + index;
        }

        internal static string BuildArrowName(string pathId, int index)
        {
            return PathPreviewConstants.ArrowNamePrefix + SanitizeRevitName(pathId) + "__" + index;
        }

        internal static string BuildNodeName(string pathId, string nodeKind)
        {
            return PathPreviewConstants.NodeNamePrefix + SanitizeRevitName(pathId) + "__" + SanitizeRevitName(nodeKind);
        }

        internal static string BuildSegmentDataId(string pathId, int index)
        {
            return PathPreviewConstants.SegmentDataPrefix + (pathId ?? string.Empty) + "::" + index;
        }

        internal static string BuildArrowDataId(string pathId, int index)
        {
            return PathPreviewConstants.ArrowDataPrefix + (pathId ?? string.Empty) + "::" + index;
        }

        internal static string BuildNodeDataId(string pathId, string nodeKind)
        {
            return PathPreviewConstants.NodeDataPrefix + (pathId ?? string.Empty) + "::" + (nodeKind ?? string.Empty);
        }

        internal static bool IsManagedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return name.StartsWith(PathPreviewConstants.SegmentNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(PathPreviewConstants.ArrowNamePrefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(PathPreviewConstants.NodeNamePrefix, StringComparison.OrdinalIgnoreCase);
        }

        internal static void ApplyMetadata(DirectShape shape, string name, string applicationDataId)
        {
            if (shape == null)
            {
                return;
            }

            shape.ApplicationId = PathPreviewConstants.ApplicationId;
            shape.ApplicationDataId = applicationDataId ?? string.Empty;
            shape.Name = name ?? string.Empty;
        }

        private static string SanitizeRevitName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string result = value;
            char[] invalidChars = new[] { '|', ':', ';', '<', '>', '?', '[', ']', '{', '}', '/', '\\' };
            foreach (char c in invalidChars)
            {
                result = result.Replace(c, '_');
            }

            return result.Trim();
        }
    }
}
