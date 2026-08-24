using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Mapping;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services
{
    public static class WizardIdempotencyStoreService
    {
        private static readonly Guid SchemaGuid = new Guid("5F5A7BAA-1A1A-4A53-9F66-7D340BF5E8CF");
        private const string SchemaName = "CadToRevitWizardIdempotencyStore";
        private const string FieldName = "JsonPayload";

        public static string BuildRowKey(string rawLayer, MapCategory category, ElementId levelId, ElementId dwgId)
        {
            string layer = string.IsNullOrWhiteSpace(rawLayer) ? string.Empty : rawLayer.Trim();
            int level = levelId != null ? levelId.IntegerValue : -1;
            int dwg = dwgId != null ? dwgId.IntegerValue : -1;
            return layer + "|L" + level + "|D" + dwg;
        }

        public static bool Contains(Document doc, string key)
        {
            if (doc == null || doc.ProjectInformation == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            HashSet<string> keys = LoadKeys(doc);
            return keys.Contains(key);
        }

        public static void MarkCreated(Document doc, string key)
        {
            if (doc == null || doc.ProjectInformation == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            try
            {
                HashSet<string> keys = LoadKeys(doc);
                if (!keys.Add(key))
                {
                    return;
                }

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

                    IdempotencyStoreDto dto = new IdempotencyStoreDto
                    {
                        SchemaVersion = 1,
                        UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
                        CreatedKeys = keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()
                    };
                    Entity entity = new Entity(schema);
                    entity.Set(field, Serialize(dto));
                    doc.ProjectInformation.SetEntity(entity);
                };

                if (doc.IsModifiable)
                {
                    write();
                    return;
                }

                using (Transaction tx = new Transaction(doc, "CadToRevit Save Idempotency"))
                {
                    tx.Start();
                    write();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[IdempotencyStore] Mark failed: " + ex.Message);
            }
        }

        private static HashSet<string> LoadKeys(Document doc)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || doc.ProjectInformation == null)
            {
                return result;
            }

            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null)
                {
                    return result;
                }

                Entity entity = doc.ProjectInformation.GetEntity(schema);
                if (!entity.IsValid())
                {
                    return result;
                }

                Field field = schema.GetField(FieldName);
                if (field == null)
                {
                    return result;
                }

                string payload = entity.Get<string>(field);
                IdempotencyStoreDto dto = Deserialize(payload);
                foreach (string key in dto?.CreatedKeys ?? new List<string>())
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        result.Add(key);
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[IdempotencyStore] Load failed: " + ex.Message);
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
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }

        private static string Serialize(IdempotencyStoreDto dto)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(IdempotencyStoreDto));
                serializer.WriteObject(ms, dto ?? new IdempotencyStoreDto());
                ms.Position = 0;
                using (StreamReader reader = new StreamReader(ms))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static IdempotencyStoreDto Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new IdempotencyStoreDto();
            }

            try
            {
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(IdempotencyStoreDto));
                    return serializer.ReadObject(ms) as IdempotencyStoreDto;
                }
            }
            catch
            {
                return new IdempotencyStoreDto();
            }
        }

        [DataContract]
        private sealed class IdempotencyStoreDto
        {
            [DataMember(Name = "SchemaVersion")]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "UpdatedAtUtc")]
            public string UpdatedAtUtc { get; set; }

            [DataMember(Name = "CreatedKeys")]
            public List<string> CreatedKeys { get; set; } = new List<string>();
        }
    }
}
