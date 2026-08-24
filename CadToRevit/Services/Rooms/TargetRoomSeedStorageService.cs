using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms
{
    public static class TargetRoomSeedStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("6E1D7B3A-361A-4475-A1C4-5087F4C2E2A9");
        private const string SchemaName = "CadToRevitTargetRoomSeedStore";
        private const string FieldName = "JsonPayload";

        public static void SaveSeeds(Document doc, IList<TargetRoomSeed> seeds)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            // Store plain JSON so schema evolution stays simple across plugin versions.
            SeedStorePayload payload = new SeedStorePayload
            {
                Version = "1.0",
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Seeds = ToDtos(seeds)
            };

            string json = Serialize(payload);
            Schema schema = EnsureSchema();
            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return;
            }

            Entity entity = new Entity(schema);
            entity.Set(field, json ?? string.Empty);
            doc.ProjectInformation.SetEntity(entity);
        }

        public static List<TargetRoomSeed> LoadSeeds(Document doc)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return new List<TargetRoomSeed>();
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return new List<TargetRoomSeed>();
            }

            Entity entity = doc.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid())
            {
                return new List<TargetRoomSeed>();
            }

            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return new List<TargetRoomSeed>();
            }

            // Fallback to empty payload if data is missing or incompatible.
            string json = entity.Get<string>(field) ?? string.Empty;
            SeedStorePayload payload = Deserialize(json);
            return FromDtos(payload != null ? payload.Seeds : null);
        }

        public static void ClearSeeds(Document doc)
        {
            SaveSeeds(doc, new List<TargetRoomSeed>());
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

        private static string Serialize(SeedStorePayload payload)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SeedStorePayload));
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static SeedStorePayload Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SeedStorePayload();
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SeedStorePayload));
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(ms) as SeedStorePayload ?? new SeedStorePayload();
                }
            }
            catch
            {
                return new SeedStorePayload();
            }
        }

        private static List<TargetRoomSeedDto> ToDtos(IList<TargetRoomSeed> seeds)
        {
            List<TargetRoomSeedDto> result = new List<TargetRoomSeedDto>();
            foreach (TargetRoomSeed seed in seeds ?? new List<TargetRoomSeed>())
            {
                if (seed == null || seed.Position == null)
                {
                    continue;
                }

                result.Add(new TargetRoomSeedDto
                {
                    Key = seed.Key ?? string.Empty,
                    RoomName = seed.RoomName ?? string.Empty,
                    TargetRoomType = seed.TargetRoomType ?? string.Empty,
                    X = seed.Position.X,
                    Y = seed.Position.Y,
                    Z = seed.Position.Z,
                    LevelId = seed.LevelId != null ? seed.LevelId.IntegerValue : -1,
                    SourceLayer = seed.SourceLayer ?? string.Empty,
                    RawText = seed.RawText ?? string.Empty
                });
            }

            return result;
        }

        private static List<TargetRoomSeed> FromDtos(IList<TargetRoomSeedDto> dtos)
        {
            List<TargetRoomSeed> result = new List<TargetRoomSeed>();
            foreach (TargetRoomSeedDto dto in dtos ?? new List<TargetRoomSeedDto>())
            {
                if (dto == null)
                {
                    continue;
                }

                result.Add(new TargetRoomSeed
                {
                    Key = dto.Key ?? string.Empty,
                    RoomName = dto.RoomName ?? string.Empty,
                    TargetRoomType = dto.TargetRoomType ?? string.Empty,
                    Position = new XYZ(dto.X, dto.Y, dto.Z),
                    LevelId = dto.LevelId > 0 ? new ElementId(dto.LevelId) : ElementId.InvalidElementId,
                    SourceLayer = dto.SourceLayer ?? string.Empty,
                    RawText = dto.RawText ?? string.Empty
                });
            }

            return result;
        }

        [DataContract]
        private sealed class SeedStorePayload
        {
            [DataMember(Name = "version")]
            public string Version { get; set; }

            [DataMember(Name = "savedAt")]
            public string SavedAt { get; set; }

            [DataMember(Name = "seeds")]
            public List<TargetRoomSeedDto> Seeds { get; set; } = new List<TargetRoomSeedDto>();
        }

        [DataContract]
        private sealed class TargetRoomSeedDto
        {
            [DataMember(Name = "key")]
            public string Key { get; set; }

            [DataMember(Name = "roomName")]
            public string RoomName { get; set; }

            [DataMember(Name = "targetRoomType")]
            public string TargetRoomType { get; set; }

            [DataMember(Name = "x")]
            public double X { get; set; }

            [DataMember(Name = "y")]
            public double Y { get; set; }

            [DataMember(Name = "z")]
            public double Z { get; set; }

            [DataMember(Name = "levelId")]
            public int LevelId { get; set; }

            [DataMember(Name = "sourceLayer")]
            public string SourceLayer { get; set; }

            [DataMember(Name = "rawText")]
            public string RawText { get; set; }
        }
    }
}
