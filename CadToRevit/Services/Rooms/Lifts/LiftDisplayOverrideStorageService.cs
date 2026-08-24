using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.Lifts
{
    [DataContract]
    public sealed class LiftDisplayOverride
    {
        [DataMember]
        public string LiftKey { get; set; }

        [DataMember]
        public double? InternalLengthMm { get; set; }

        [DataMember]
        public double? InternalWidthMm { get; set; }

        [DataMember]
        public double? InternalHeightMm { get; set; }

        [DataMember]
        public double? DoorWidthMm { get; set; }

        [DataMember]
        public double? DoorHeightMm { get; set; }

        [DataMember]
        public double? CapacityKg { get; set; }

        [DataMember]
        public string UpdatedAt { get; set; }
    }

    public static class LiftDisplayOverrideStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("60FA9A93-0AB4-4D35-9DF3-C9B563498B1D");
        private const string SchemaName = "CadToRevitLiftDisplayOverrideStore";
        private const string FieldName = "JsonPayload";

        public static Dictionary<string, LiftDisplayOverride> Load(Document doc)
        {
            Dictionary<string, LiftDisplayOverride> result =
                new Dictionary<string, LiftDisplayOverride>(StringComparer.OrdinalIgnoreCase);
            if (doc == null || doc.ProjectInformation == null)
            {
                return result;
            }

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

            LiftDisplayOverridePayload payload = Deserialize(entity.Get<string>(field) ?? string.Empty);
            foreach (LiftDisplayOverride entry in payload.Lifts ?? new List<LiftDisplayOverride>())
            {
                if (!string.IsNullOrWhiteSpace(entry.LiftKey))
                {
                    result[entry.LiftKey] = entry;
                }
            }

            return result;
        }

        public static void Save(Document doc, IDictionary<string, LiftDisplayOverride> data)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            LiftDisplayOverridePayload payload = new LiftDisplayOverridePayload();
            foreach (KeyValuePair<string, LiftDisplayOverride> pair in data ?? new Dictionary<string, LiftDisplayOverride>())
            {
                LiftDisplayOverride value = pair.Value;
                if (value == null || string.IsNullOrWhiteSpace(value.LiftKey))
                {
                    continue;
                }

                payload.Lifts.Add(value);
            }

            Schema schema = EnsureSchema();
            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return;
            }

            Entity entity = new Entity(schema);
            entity.Set(field, Serialize(payload));
            doc.ProjectInformation.SetEntity(entity);
        }

        public static void Upsert(Document doc, LiftDisplayOverride displayOverride)
        {
            if (displayOverride == null || string.IsNullOrWhiteSpace(displayOverride.LiftKey))
            {
                return;
            }

            Dictionary<string, LiftDisplayOverride> data = Load(doc);
            displayOverride.UpdatedAt = DateTime.UtcNow.ToString("o");
            data[displayOverride.LiftKey] = displayOverride;
            Save(doc, data);
        }

        public static void Delete(Document doc, string liftKey)
        {
            Dictionary<string, LiftDisplayOverride> data = Load(doc);
            data.Remove(liftKey ?? string.Empty);
            Save(doc, data);
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

        private static string Serialize(LiftDisplayOverridePayload payload)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LiftDisplayOverridePayload));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, payload ?? new LiftDisplayOverridePayload());
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static LiftDisplayOverridePayload Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new LiftDisplayOverridePayload();
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LiftDisplayOverridePayload));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(stream) as LiftDisplayOverridePayload ?? new LiftDisplayOverridePayload();
                }
            }
            catch
            {
                return new LiftDisplayOverridePayload();
            }
        }

        [DataContract]
        private sealed class LiftDisplayOverridePayload
        {
            [DataMember]
            public List<LiftDisplayOverride> Lifts { get; set; } = new List<LiftDisplayOverride>();
        }
    }
}
