using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace CadToRevit.Services
{
    public sealed class DetachedGeneratedElementSnapshot
    {
        public string OriginalRowKey { get; set; }

        public string OriginalGenerationBatchId { get; set; }

        public string OriginalRawLayerName { get; set; }

        public string OriginalCategory { get; set; }

        public int OriginalLevelId { get; set; }

        public int OriginalDwgId { get; set; }

        public string DetachedAtUtc { get; set; }
    }

    public static class DetachedGeneratedElementMetadataService
    {
        private static readonly Guid SchemaGuid = new Guid("B66B6F28-501E-4E33-9A62-9850A7E50F09");
        private const string SchemaName = "CadToRevitDetachedGeneratedElementMetadata";

        private const string FieldSourcePlugin = "SourcePlugin";
        private const string FieldOriginalRowKey = "OriginalRowKey";
        private const string FieldOriginalGenerationBatchId = "OriginalGenerationBatchId";
        private const string FieldOriginalRawLayerName = "OriginalRawLayerName";
        private const string FieldOriginalCategory = "OriginalCategory";
        private const string FieldOriginalLevelId = "OriginalLevelId";
        private const string FieldOriginalDwgId = "OriginalDwgId";
        private const string FieldDetachedAtUtc = "DetachedAtUtc";

        public static void WriteDetachedSnapshot(Element element, GeneratedElementFullMetadataSnapshot original)
        {
            if (element == null || original == null || string.IsNullOrWhiteSpace(original.RowKey))
            {
                return;
            }

            Schema schema = EnsureSchema();
            if (schema == null)
            {
                return;
            }

            Entity entity = new Entity(schema);
            entity.Set(schema.GetField(FieldSourcePlugin), "CadToRevit");
            entity.Set(schema.GetField(FieldOriginalRowKey), original.RowKey ?? string.Empty);
            entity.Set(schema.GetField(FieldOriginalGenerationBatchId), original.GenerationBatchId ?? string.Empty);
            entity.Set(schema.GetField(FieldOriginalRawLayerName), original.RawLayerName ?? string.Empty);
            entity.Set(schema.GetField(FieldOriginalCategory), original.Category ?? string.Empty);
            entity.Set(schema.GetField(FieldOriginalLevelId), original.LevelId);
            entity.Set(schema.GetField(FieldOriginalDwgId), original.DwgId);
            entity.Set(schema.GetField(FieldDetachedAtUtc), DateTime.UtcNow.ToString("o"));
            element.SetEntity(entity);
        }

        public static bool TryGetDetachedSnapshot(Element element, out DetachedGeneratedElementSnapshot snapshot)
        {
            snapshot = null;
            if (element == null)
            {
                return false;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return false;
            }

            Entity entity = element.GetEntity(schema);
            if (!entity.IsValid())
            {
                return false;
            }

            string rowKey = GetString(entity, schema, FieldOriginalRowKey);
            if (string.IsNullOrWhiteSpace(rowKey))
            {
                return false;
            }

            snapshot = new DetachedGeneratedElementSnapshot
            {
                OriginalRowKey = rowKey.Trim(),
                OriginalGenerationBatchId = GetString(entity, schema, FieldOriginalGenerationBatchId),
                OriginalRawLayerName = GetString(entity, schema, FieldOriginalRawLayerName),
                OriginalCategory = GetString(entity, schema, FieldOriginalCategory),
                OriginalLevelId = GetInt(entity, schema, FieldOriginalLevelId),
                OriginalDwgId = GetInt(entity, schema, FieldOriginalDwgId),
                DetachedAtUtc = GetString(entity, schema, FieldDetachedAtUtc)
            };
            return true;
        }

        public static bool IsDetached(Element element)
        {
            return TryGetDetachedSnapshot(element, out DetachedGeneratedElementSnapshot _);
        }

        public static void ClearDetachedSnapshot(Element element)
        {
            if (element == null)
            {
                return;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return;
            }

            Entity entity = element.GetEntity(schema);
            if (entity.IsValid())
            {
                element.DeleteEntity(schema);
            }
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
            builder.AddSimpleField(FieldOriginalRowKey, typeof(string));
            builder.AddSimpleField(FieldOriginalGenerationBatchId, typeof(string));
            builder.AddSimpleField(FieldOriginalRawLayerName, typeof(string));
            builder.AddSimpleField(FieldOriginalCategory, typeof(string));
            builder.AddSimpleField(FieldOriginalLevelId, typeof(int));
            builder.AddSimpleField(FieldOriginalDwgId, typeof(int));
            builder.AddSimpleField(FieldDetachedAtUtc, typeof(string));
            return builder.Finish();
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
    }
}
