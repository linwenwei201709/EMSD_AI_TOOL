using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models;
using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services
{
    public static class VerticalDimensionStoreService
    {
        private static readonly Guid SchemaGuid = new Guid("8D266A8E-57F0-4CF2-8C9B-9AF94F8A86DD");
        private const string SchemaName = "CadToRevitVerticalDimensions";
        private const string FieldName = "JsonPayload";

        public static VerticalDimensionSettings Load(Document doc)
        {
            VerticalDimensionSettings fromRvt = TryLoadFromRvt(doc);
            if (fromRvt != null)
            {
                return fromRvt;
            }

            VerticalDimensionSettings fromFile = TryLoadFromFile();
            return fromFile ?? new VerticalDimensionSettings();
        }

        public static void Save(Document doc, VerticalDimensionSettings settings)
        {
            VerticalDimensionSettings safe = settings ?? new VerticalDimensionSettings();
            string json = Serialize(safe);
            TryWriteToRvt(doc, json);
            TryWriteToFile(json);
        }

        private static VerticalDimensionSettings TryLoadFromRvt(Document doc)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return null;
            }

            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null)
                {
                    return null;
                }

                Entity entity = doc.ProjectInformation.GetEntity(schema);
                if (!entity.IsValid())
                {
                    return null;
                }

                Field field = schema.GetField(FieldName);
                if (field == null)
                {
                    return null;
                }

                string payload = entity.Get<string>(field);
                return Deserialize(payload);
            }
            catch
            {
                return null;
            }
        }

        private static void TryWriteToRvt(Document doc, string json)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            try
            {
                Action write = () =>
                {
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
                };

                if (doc.IsModifiable)
                {
                    write();
                    return;
                }

                using (Transaction tx = new Transaction(doc, "CadToRevit Save Vertical Settings"))
                {
                    tx.Start();
                    write();
                    tx.Commit();
                }
            }
            catch
            {
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
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }

        private static VerticalDimensionSettings TryLoadFromFile()
        {
            string path = GetStorePath();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                string payload = File.ReadAllText(path);
                return Deserialize(payload);
            }
            catch
            {
                return null;
            }
        }

        private static void TryWriteToFile(string json)
        {
            try
            {
                string path = GetStorePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, json ?? string.Empty);
            }
            catch
            {
            }
        }

        private static string GetStorePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CadToRevit", "HelixWizard", "vertical_settings.json");
        }

        private static string Serialize(VerticalDimensionSettings settings)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(VerticalDimensionSettings));
                serializer.WriteObject(ms, settings ?? new VerticalDimensionSettings());
                ms.Position = 0;
                using (StreamReader sr = new StreamReader(ms))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        private static VerticalDimensionSettings Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(VerticalDimensionSettings));
                    return serializer.ReadObject(ms) as VerticalDimensionSettings;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
