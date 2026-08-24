using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;

namespace CadToRevit.Services
{
    public sealed class GeneratedElementMetadataSnapshot
    {
        public int Id { get; set; }

        public string RowKey { get; set; }

        public int CategoryId { get; set; }

        public int HostId { get; set; } = Autodesk.Revit.DB.ElementId.InvalidElementId.IntegerValue;
    }

    public sealed class GeneratedElementFullMetadataSnapshot
    {
        public string RowKey { get; set; }

        public string GenerationBatchId { get; set; }

        public string RawLayerName { get; set; }

        public string Category { get; set; }

        public int LevelId { get; set; }

        public int DwgId { get; set; }
    }

    public static class GeneratedElementMetadataService
    {
        private static readonly Guid SchemaGuid = new Guid("C88D668D-12C0-4F79-98AE-7757A7D0142F");
        private const string SchemaName = "CadToRevitGeneratedElementMetadata";

        private const string FieldSourcePlugin = "SourcePlugin";
        private const string FieldRowKey = "RowKey";
        private const string FieldGenerationBatchId = "GenerationBatchId";
        private const string FieldRawLayerName = "RawLayerName";
        private const string FieldCategory = "Category";
        private const string FieldLevelId = "LevelId";
        private const string FieldDwgId = "DwgId";

        public static void WriteBatch(
            Document doc,
            IEnumerable<ElementId> elementIds,
            string rowKey,
            string batchId,
            string rawLayerName,
            string category,
            int levelId,
            int dwgId)
        {
            if (doc == null || elementIds == null)
            {
                return;
            }

            Schema schema = EnsureSchema();
            if (schema == null)
            {
                return;
            }

            foreach (ElementId id in elementIds)
            {
                Element elem = doc.GetElement(id);
                if (elem == null)
                {
                    continue;
                }

                Entity entity = new Entity(schema);
                entity.Set(schema.GetField(FieldSourcePlugin), "CadToRevit");
                entity.Set(schema.GetField(FieldRowKey), rowKey ?? string.Empty);
                entity.Set(schema.GetField(FieldGenerationBatchId), batchId ?? string.Empty);
                entity.Set(schema.GetField(FieldRawLayerName), rawLayerName ?? string.Empty);
                entity.Set(schema.GetField(FieldCategory), category ?? string.Empty);
                entity.Set(schema.GetField(FieldLevelId), levelId);
                entity.Set(schema.GetField(FieldDwgId), dwgId);
                elem.SetEntity(entity);

                // Fallback marker for quick inspection in Revit UI.
                Parameter comments = elem.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && !comments.IsReadOnly)
                {
                    comments.Set("CadToRevit|RowKey=" + (rowKey ?? string.Empty) + "|Batch=" + (batchId ?? string.Empty));
                }
            }
        }

        public static bool TryGetRowKey(Element element, out string rowKey)
        {
            rowKey = null;
            if (element == null)
            {
                return false;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                Entity entity = element.GetEntity(schema);
                if (entity.IsValid())
                {
                    Field field = schema.GetField(FieldRowKey);
                    if (field != null)
                    {
                        string value = entity.Get<string>(field);
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            rowKey = value.Trim();
                            return true;
                        }
                    }
                }
            }

            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            string text = comments != null ? comments.AsString() : null;
            if (TryParseRowKeyFromComments(text, out string parsed))
            {
                rowKey = parsed;
                return true;
            }

            return false;
        }

        public static bool TryGetFullMetadata(Element element, out GeneratedElementFullMetadataSnapshot snapshot)
        {
            snapshot = null;
            if (element == null)
            {
                return false;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                Entity entity = element.GetEntity(schema);
                if (entity.IsValid())
                {
                    string rowKey = GetString(entity, schema, FieldRowKey);
                    if (!string.IsNullOrWhiteSpace(rowKey))
                    {
                        snapshot = new GeneratedElementFullMetadataSnapshot
                        {
                            RowKey = rowKey.Trim(),
                            GenerationBatchId = GetString(entity, schema, FieldGenerationBatchId),
                            RawLayerName = GetString(entity, schema, FieldRawLayerName),
                            Category = GetString(entity, schema, FieldCategory),
                            LevelId = GetInt(entity, schema, FieldLevelId),
                            DwgId = GetInt(entity, schema, FieldDwgId)
                        };
                        return true;
                    }
                }
            }

            if (TryGetRowKey(element, out string parsedRowKey) && !string.IsNullOrWhiteSpace(parsedRowKey))
            {
                snapshot = new GeneratedElementFullMetadataSnapshot
                {
                    RowKey = parsedRowKey.Trim()
                };
                return false;
            }

            return false;
        }

        public static void ClearGeneratedBinding(Element element)
        {
            if (element == null)
            {
                return;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                Entity entity = element.GetEntity(schema);
                if (entity.IsValid())
                {
                    element.DeleteEntity(schema);
                }
            }

            Parameter comments = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments == null || comments.IsReadOnly)
            {
                return;
            }

            string text = comments.AsString();
            if (string.IsNullOrWhiteSpace(text) ||
                text.IndexOf("CadToRevit|RowKey=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            comments.Set(RemoveCadToRevitCommentMarker(text));
        }

        public static Dictionary<int, string> BuildGeneratedRowKeyIndex(Document doc)
        {
            Dictionary<int, string> result = new Dictionary<int, string>();
            if (doc == null)
            {
                return result;
            }

            List<BuiltInCategory> categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings
            };

            ElementMulticategoryFilter filter = new ElementMulticategoryFilter(categories);
            foreach (Element element in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType().ToElements())
            {
                if (element == null)
                {
                    continue;
                }

                if (TryGetRowKey(element, out string rowKey) && !string.IsNullOrWhiteSpace(rowKey))
                {
                    result[element.Id.IntegerValue] = rowKey;
                }
            }

            return result;
        }

        public static Dictionary<int, GeneratedElementMetadataSnapshot> BuildGeneratedSnapshotIndex(Document doc)
        {
            Dictionary<int, GeneratedElementMetadataSnapshot> result = new Dictionary<int, GeneratedElementMetadataSnapshot>();
            if (doc == null)
            {
                return result;
            }

            List<BuiltInCategory> categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Columns,
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_GenericModel,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings
            };

            ElementMulticategoryFilter filter = new ElementMulticategoryFilter(categories);
            foreach (Element element in new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType().ToElements())
            {
                if (element == null)
                {
                    continue;
                }

                if (!TryGetRowKey(element, out string rowKey) || string.IsNullOrWhiteSpace(rowKey))
                {
                    continue;
                }

                int hostId = ElementId.InvalidElementId.IntegerValue;
                FamilyInstance fi = element as FamilyInstance;
                if (fi != null && fi.Host != null)
                {
                    hostId = fi.Host.Id.IntegerValue;
                }

                result[element.Id.IntegerValue] = new GeneratedElementMetadataSnapshot
                {
                    Id = element.Id.IntegerValue,
                    RowKey = rowKey,
                    CategoryId = element.Category != null ? element.Category.Id.IntegerValue : ElementId.InvalidElementId.IntegerValue,
                    HostId = hostId
                };
            }

            return result;
        }

        private static Schema EnsureSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null)
            {
                return schema;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId("EMSD");
            builder.AddSimpleField(FieldSourcePlugin, typeof(string));
            builder.AddSimpleField(FieldRowKey, typeof(string));
            builder.AddSimpleField(FieldGenerationBatchId, typeof(string));
            builder.AddSimpleField(FieldRawLayerName, typeof(string));
            builder.AddSimpleField(FieldCategory, typeof(string));
            builder.AddSimpleField(FieldLevelId, typeof(int));
            builder.AddSimpleField(FieldDwgId, typeof(int));
            return builder.Finish();
        }

        private static bool TryParseRowKeyFromComments(string text, out string rowKey)
        {
            rowKey = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int start = text.IndexOf("CadToRevit|RowKey=", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return false;
            }

            start += "CadToRevit|RowKey=".Length;
            int end = text.IndexOf("|Batch=", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                end = text.Length;
            }

            string value = text.Substring(start, end - start).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            rowKey = value;
            return true;
        }

        private static string GetString(Entity entity, Schema schema, string fieldName)
        {
            Field field = schema != null ? schema.GetField(fieldName) : null;
            if (field == null)
            {
                return string.Empty;
            }

            try
            {
                return entity.Get<string>(field) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int GetInt(Entity entity, Schema schema, string fieldName)
        {
            Field field = schema != null ? schema.GetField(fieldName) : null;
            if (field == null)
            {
                return ElementId.InvalidElementId.IntegerValue;
            }

            try
            {
                return entity.Get<int>(field);
            }
            catch
            {
                return ElementId.InvalidElementId.IntegerValue;
            }
        }

        private static string RemoveCadToRevitCommentMarker(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            List<string> kept = new List<string>();
            string[] parts = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (string part in parts)
            {
                if (part != null &&
                    part.IndexOf("CadToRevit|RowKey=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                kept.Add(part);
            }

            return string.Join(Environment.NewLine, kept).Trim();
        }
    }
}
