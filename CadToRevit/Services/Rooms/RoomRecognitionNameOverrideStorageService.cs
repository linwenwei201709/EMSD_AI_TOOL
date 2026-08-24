using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms
{
    public sealed class RoomRecognitionNameOverrideData
    {
        public Dictionary<string, string> RoomNames { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> LiftNames { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static class RoomRecognitionNameOverrideStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("9A6D03D9-99E3-4E25-95D1-2B053AB61E3B");
        private const string SchemaName = "CadToRevitRoomRecognitionNameOverrideStore";
        private const string FieldName = "JsonPayload";

        public static RoomRecognitionNameOverrideData Load(Document doc)
        {
            RoomRecognitionNameOverrideData result = new RoomRecognitionNameOverrideData();
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

            NameOverridePayload payload = Deserialize(entity.Get<string>(field) ?? string.Empty);
            foreach (NameOverrideEntry entry in payload.RoomNames ?? new List<NameOverrideEntry>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    result.RoomNames[entry.Key] = entry.Value.Trim();
                }
            }

            foreach (NameOverrideEntry entry in payload.LiftNames ?? new List<NameOverrideEntry>())
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    result.LiftNames[entry.Key] = entry.Value.Trim();
                }
            }

            return result;
        }

        public static void Save(Document doc, RoomRecognitionNameOverrideData data)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            NameOverridePayload payload = new NameOverridePayload();
            foreach (KeyValuePair<string, string> pair in data?.RoomNames ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    payload.RoomNames.Add(new NameOverrideEntry { Key = pair.Key, Value = pair.Value.Trim() });
                }
            }

            foreach (KeyValuePair<string, string> pair in data?.LiftNames ?? new Dictionary<string, string>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    payload.LiftNames.Add(new NameOverrideEntry { Key = pair.Key, Value = pair.Value.Trim() });
                }
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

        public static void UpsertRoomName(Document doc, string roomKey, string customName)
        {
            if (string.IsNullOrWhiteSpace(roomKey) || string.IsNullOrWhiteSpace(customName))
            {
                return;
            }

            RoomRecognitionNameOverrideData data = Load(doc);
            data.RoomNames[roomKey] = customName.Trim();
            Save(doc, data);
        }

        public static void UpsertLiftName(Document doc, string liftKey, string customName)
        {
            if (string.IsNullOrWhiteSpace(liftKey) || string.IsNullOrWhiteSpace(customName))
            {
                return;
            }

            RoomRecognitionNameOverrideData data = Load(doc);
            data.LiftNames[liftKey] = customName.Trim();
            Save(doc, data);
        }

        public static void DeleteRoomNameOverride(Document doc, string roomKey)
        {
            RoomRecognitionNameOverrideData data = Load(doc);
            data.RoomNames.Remove(roomKey ?? string.Empty);
            Save(doc, data);
        }

        public static void DeleteLiftNameOverride(Document doc, string liftKey)
        {
            RoomRecognitionNameOverrideData data = Load(doc);
            data.LiftNames.Remove(liftKey ?? string.Empty);
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

        private static string Serialize(NameOverridePayload payload)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(NameOverridePayload));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, payload ?? new NameOverridePayload());
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static NameOverridePayload Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new NameOverridePayload();
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(NameOverridePayload));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(stream) as NameOverridePayload ?? new NameOverridePayload();
                }
            }
            catch
            {
                return new NameOverridePayload();
            }
        }

        [DataContract]
        private sealed class NameOverridePayload
        {
            [DataMember]
            public List<NameOverrideEntry> RoomNames { get; set; } = new List<NameOverrideEntry>();

            [DataMember]
            public List<NameOverrideEntry> LiftNames { get; set; } = new List<NameOverrideEntry>();
        }

        [DataContract]
        private sealed class NameOverrideEntry
        {
            [DataMember]
            public string Key { get; set; }

            [DataMember]
            public string Value { get; set; }
        }
    }
}
