using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;

namespace CadToRevit.Services
{
    public static class ProjectSettingsStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("7B9BE82C-53FD-47AA-9A86-8CF46FC4E9AA");
        private const string SchemaName = "CadToRevitLayerOverrideStore";
        private const string FieldName = "JsonPayload";

        public static string Read(Document doc)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return string.Empty;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return string.Empty;
            }

            Entity entity = doc.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid())
            {
                return string.Empty;
            }

            Field field = schema.GetField(FieldName);
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

        public static void Write(Document doc, string json)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            Schema schema = EnsureSchema();
            if (schema == null)
            {
                return;
            }

            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return;
            }

            Entity entity = new Entity(schema);
            entity.Set(field, json ?? string.Empty);
            doc.ProjectInformation.SetEntity(entity);
        }

        private static Schema EnsureSchema()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null)
            {
                return existing;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId("EMSD");
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }
    }
}
