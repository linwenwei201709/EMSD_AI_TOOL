using Autodesk.Revit.DB;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.PathObstacles
{
    public static class PathObstacleStoreService
    {
        public static IList<PathObstacleRecord> Load(Document doc)
        {
            if (doc == null)
            {
                return new List<PathObstacleRecord>();
            }

            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_GenericModel)
                .WhereElementIsNotElementType()
                .Where(PathObstacleMetadataService.IsPathObstacleElement)
                .Select(PathObstacleMetadataService.ReadRecord)
                .Where(record => record != null)
                .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string BuildNextDefaultName(Document doc)
        {
            HashSet<string> existing = new HashSet<string>(
                Load(doc).Select(record => record.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            for (int index = 1; index < 10000; index++)
            {
                string name = "Restricted Area " + index.ToString(CultureInfo.InvariantCulture);
                if (!existing.Contains(name))
                {
                    return name;
                }
            }

            return "Restricted Area " + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        }

        public static PathObstacleRecord Save(Element element, string name)
        {
            if (element == null)
            {
                return null;
            }

            PathObstacleRecord existing = PathObstacleMetadataService.ReadRecord(element);
            PathObstacleRecord record = new PathObstacleRecord
            {
                ObstacleId = existing != null && !string.IsNullOrWhiteSpace(existing.ObstacleId) ? existing.ObstacleId : Guid.NewGuid().ToString("N"),
                Name = PathObstacleMetadataService.SanitizeName(name),
                ElementIdValue = element.Id.IntegerValue,
                UniqueId = element.UniqueId,
                LevelName = existing != null ? existing.LevelName : string.Empty,
                CreatedAt = existing != null && existing.CreatedAt != DateTime.MinValue ? existing.CreatedAt : DateTime.Now
            };

            if (string.IsNullOrWhiteSpace(record.Name))
            {
                record.Name = BuildNextDefaultName(element.Document);
            }

            PathObstacleMetadataService.WriteRecord(element, record);
            return record;
        }

        public static PathObstacleRecord Rename(Document doc, PathObstacleRecord record, string newName)
        {
            Element element = FindElement(doc, record);
            if (element == null)
            {
                return null;
            }

            return Save(element, newName);
        }

        public static Element FindElement(Document doc, PathObstacleRecord record)
        {
            if (doc == null || record == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(record.UniqueId))
            {
                Element byUniqueId = doc.GetElement(record.UniqueId);
                if (byUniqueId != null)
                {
                    return byUniqueId;
                }
            }

            if (record.ElementIdValue > 0)
            {
                Element byId = doc.GetElement(new ElementId(record.ElementIdValue));
                if (byId != null)
                {
                    return byId;
                }
            }

            return null;
        }
    }
}
