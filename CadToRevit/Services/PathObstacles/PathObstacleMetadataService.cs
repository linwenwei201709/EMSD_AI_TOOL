using Autodesk.Revit.DB;
using CadToRevit.Models;
using System;
using System.Globalization;
using System.Text;

namespace CadToRevit.Services.PathObstacles
{
    public static class PathObstacleMetadataService
    {
        public const string SourcePrefix = "EMSD_PATH_OBSTACLE";
        private const string LegacyComment = "CadToRevit_PathObstacle";
        private const string ObstacleName = "CadToRevit_PathObstacle";

        public static bool IsPathObstacleElement(Element element)
        {
            if (element == null)
            {
                return false;
            }

            if (string.Equals(element.Name, ObstacleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string comments = ReadComments(element);
            return comments.IndexOf(SourcePrefix, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(comments, LegacyComment, StringComparison.OrdinalIgnoreCase);
        }

        public static PathObstacleRecord ReadRecord(Element element)
        {
            if (!IsPathObstacleElement(element))
            {
                return null;
            }

            string comments = ReadComments(element);
            string id = GetToken(comments, "Id");
            string name = GetToken(comments, "Name");
            string createdAtText = GetToken(comments, "CreatedAt");

            DateTime createdAt;
            if (!DateTime.TryParse(createdAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out createdAt))
            {
                createdAt = DateTime.MinValue;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                id = element.UniqueId ?? element.Id.IntegerValue.ToString(CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                Parameter mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                name = mark != null ? mark.AsString() : null;
            }

            return new PathObstacleRecord
            {
                ObstacleId = id,
                Name = string.IsNullOrWhiteSpace(name) ? "Obstacle" : name.Trim(),
                ElementIdValue = element.Id.IntegerValue,
                UniqueId = element.UniqueId,
                LevelName = ResolveLevelName(element),
                CreatedAt = createdAt
            };
        }

        public static void WriteRecord(Element element, PathObstacleRecord record)
        {
            if (element == null || record == null)
            {
                return;
            }

            string existing = ReadComments(element);
            string cleaned = RemoveMetadataToken(existing);
            string metadata = BuildMetadata(record);
            string value = string.IsNullOrWhiteSpace(cleaned) ? metadata : cleaned.Trim() + " " + metadata;
            SetStringParameter(element, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, value);
            SetStringParameter(element, BuiltInParameter.ALL_MODEL_MARK, record.Name);
            SetLookupParameter(element, "IfcName", record.Name);
        }

        public static string SanitizeName(string name)
        {
            string value = (name ?? string.Empty).Trim();
            value = value.Replace("|", " ").Replace("=", " ");
            if (value.Length > 50)
            {
                value = value.Substring(0, 50).Trim();
            }

            return value;
        }

        private static string BuildMetadata(PathObstacleRecord record)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(SourcePrefix);
            builder.Append("|Id=").Append(Escape(record.ObstacleId));
            builder.Append("|Name=").Append(Escape(record.Name));
            builder.Append("|CreatedAt=").Append(record.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string ReadComments(Element element)
        {
            try
            {
                Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                return parameter != null && parameter.StorageType == StorageType.String ? parameter.AsString() ?? string.Empty : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetToken(string comments, string key)
        {
            if (string.IsNullOrWhiteSpace(comments))
            {
                return null;
            }

            int start = comments.IndexOf(SourcePrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            string metadata = comments.Substring(start);
            string[] parts = metadata.Split('|');
            foreach (string part in parts)
            {
                int equals = part.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                string tokenKey = part.Substring(0, equals).Trim();
                if (string.Equals(tokenKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return Unescape(part.Substring(equals + 1).Trim());
                }
            }

            return null;
        }

        private static string RemoveMetadataToken(string comments)
        {
            if (string.IsNullOrWhiteSpace(comments))
            {
                return string.Empty;
            }

            int start = comments.IndexOf(SourcePrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return comments;
            }

            string prefix = comments.Substring(0, start).Trim();
            string metadata = comments.Substring(start);
            int nextSpace = metadata.IndexOf(' ');
            string suffix = nextSpace >= 0 ? metadata.Substring(nextSpace + 1).Trim() : string.Empty;
            return (prefix + " " + suffix).Trim();
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("|", " ").Replace("=", " ");
        }

        private static string Unescape(string value)
        {
            return value ?? string.Empty;
        }

        private static void SetStringParameter(Element element, BuiltInParameter builtInParameter, string value)
        {
            try
            {
                Parameter parameter = element.get_Parameter(builtInParameter);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                }
            }
            catch
            {
            }
        }

        private static void SetLookupParameter(Element element, string parameterName, string value)
        {
            try
            {
                Parameter parameter = element.LookupParameter(parameterName);
                if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.String)
                {
                    parameter.Set(value ?? string.Empty);
                }
            }
            catch
            {
            }
        }

        private static string ResolveLevelName(Element element)
        {
            try
            {
                Document doc = element.Document;
                if (doc == null || element.LevelId == ElementId.InvalidElementId)
                {
                    return string.Empty;
                }

                Element level = doc.GetElement(element.LevelId);
                return level != null ? level.Name : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
