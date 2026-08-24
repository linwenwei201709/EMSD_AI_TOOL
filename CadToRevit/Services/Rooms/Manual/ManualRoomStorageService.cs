using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.Manual
{
    public static class ManualRoomStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("7F73CF48-8C0B-4F4A-A7C0-92F0E4D0C3B7");
        private const string SchemaName = "CadToRevitManualRoomStore";
        private const string FieldName = "JsonPayload";

        public static List<ManualRoomRecord> Load(Document doc)
        {
            string raw = ReadRaw(doc);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<ManualRoomRecord>();
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ManualRoomStorePayload));
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(raw)))
                {
                    ManualRoomStorePayload payload = serializer.ReadObject(ms) as ManualRoomStorePayload;
                    return FromDtos(payload != null ? payload.Rooms : null);
                }
            }
            catch
            {
                return new List<ManualRoomRecord>();
            }
        }

        public static void Upsert(Document doc, ManualRoomRecord room)
        {
            if (doc == null || room == null || string.IsNullOrWhiteSpace(room.Key))
            {
                return;
            }

            List<ManualRoomRecord> rooms = Load(doc);
            rooms.RemoveAll(x => x == null || string.Equals(x.Key, room.Key, StringComparison.OrdinalIgnoreCase));
            rooms.Add(room);
            Save(doc, rooms);
        }

        public static void Save(Document doc, IList<ManualRoomRecord> rooms)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            ManualRoomStorePayload payload = new ManualRoomStorePayload
            {
                Version = "1.0",
                Rooms = ToDtos(rooms)
            };

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

        private static string ReadRaw(Document doc)
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
            return field != null ? entity.Get<string>(field) ?? string.Empty : string.Empty;
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

        private static string Serialize(ManualRoomStorePayload payload)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ManualRoomStorePayload));
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static List<ManualRoomRecordDto> ToDtos(IList<ManualRoomRecord> rooms)
        {
            return (rooms ?? new List<ManualRoomRecord>())
                .Where(x => x != null)
                .Select(x => new ManualRoomRecordDto
                {
                    Key = x.Key ?? string.Empty,
                    RoomName = x.RoomName ?? string.Empty,
                    RoomNumber = x.RoomNumber ?? string.Empty,
                    RoomType = x.RoomType ?? string.Empty,
                    SourceType = x.SourceType ?? "Manual",
                    LevelIdValue = x.LevelIdValue,
                    LevelName = x.LevelName ?? string.Empty,
                    BoundarySignature = x.BoundarySignature ?? string.Empty,
                    AreaM2 = x.AreaM2,
                    Centroid = ToPointDto(x.Centroid),
                    BBox = ToBoxDto(x.BBox),
                    LoopPoints = (x.LoopPoints ?? new List<XYZ>()).ConvertAll(ToPointDto),
                    BoundaryWalls = (x.BoundaryWalls ?? new List<RoomBoundaryWallReference>()).ConvertAll(ToWallDto),
                    CreatedAt = x.CreatedAt ?? string.Empty
                })
                .ToList();
        }

        private static List<ManualRoomRecord> FromDtos(IList<ManualRoomRecordDto> rooms)
        {
            return (rooms ?? new List<ManualRoomRecordDto>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .Select(x => new ManualRoomRecord
                {
                    Key = x.Key ?? string.Empty,
                    RoomName = x.RoomName ?? string.Empty,
                    RoomNumber = x.RoomNumber ?? string.Empty,
                    RoomType = x.RoomType ?? string.Empty,
                    SourceType = string.IsNullOrWhiteSpace(x.SourceType) ? "Manual" : x.SourceType,
                    LevelIdValue = x.LevelIdValue,
                    LevelName = x.LevelName ?? string.Empty,
                    BoundarySignature = x.BoundarySignature ?? string.Empty,
                    AreaM2 = x.AreaM2,
                    Centroid = FromPointDto(x.Centroid),
                    BBox = FromBoxDto(x.BBox),
                    LoopPoints = (x.LoopPoints ?? new List<PointDto>()).ConvertAll(FromPointDto),
                    BoundaryWalls = (x.BoundaryWalls ?? new List<RoomBoundaryWallReferenceDto>()).ConvertAll(FromWallDto),
                    CreatedAt = x.CreatedAt ?? string.Empty
                })
                .ToList();
        }

        private static PointDto ToPointDto(XYZ point)
        {
            return point == null ? new PointDto() : new PointDto { X = point.X, Y = point.Y, Z = point.Z };
        }

        private static XYZ FromPointDto(PointDto point)
        {
            return point == null ? XYZ.Zero : new XYZ(point.X, point.Y, point.Z);
        }

        private static BoundingBoxDto ToBoxDto(BoundingBoxXYZ box)
        {
            return box == null ? new BoundingBoxDto() : new BoundingBoxDto { Min = ToPointDto(box.Min), Max = ToPointDto(box.Max) };
        }

        private static BoundingBoxXYZ FromBoxDto(BoundingBoxDto box)
        {
            if (box == null)
            {
                return null;
            }

            return new BoundingBoxXYZ
            {
                Min = FromPointDto(box.Min),
                Max = FromPointDto(box.Max)
            };
        }

        private static RoomBoundaryWallReferenceDto ToWallDto(RoomBoundaryWallReference wall)
        {
            return new RoomBoundaryWallReferenceDto
            {
                ElementId = wall != null ? wall.ElementId : -1,
                UniqueId = wall != null ? wall.UniqueId ?? string.Empty : string.Empty,
                DisplayName = wall != null ? wall.DisplayName ?? string.Empty : string.Empty,
                RevitName = wall != null ? wall.RevitName ?? string.Empty : string.Empty,
                LengthMm = wall != null ? wall.LengthMm : 0.0
            };
        }

        private static RoomBoundaryWallReference FromWallDto(RoomBoundaryWallReferenceDto wall)
        {
            return new RoomBoundaryWallReference
            {
                ElementId = wall != null ? wall.ElementId : -1,
                UniqueId = wall != null ? wall.UniqueId ?? string.Empty : string.Empty,
                DisplayName = wall != null ? wall.DisplayName ?? string.Empty : string.Empty,
                RevitName = wall != null ? wall.RevitName ?? string.Empty : string.Empty,
                LengthMm = wall != null ? wall.LengthMm : 0.0
            };
        }

        [DataContract]
        private sealed class ManualRoomStorePayload
        {
            [DataMember(Name = "Version")]
            public string Version { get; set; }

            [DataMember(Name = "Rooms")]
            public List<ManualRoomRecordDto> Rooms { get; set; } = new List<ManualRoomRecordDto>();
        }

        [DataContract]
        private sealed class ManualRoomRecordDto
        {
            [DataMember] public string Key { get; set; }
            [DataMember] public string RoomName { get; set; }
            [DataMember] public string RoomNumber { get; set; }
            [DataMember] public string RoomType { get; set; }
            [DataMember] public string SourceType { get; set; }
            [DataMember] public int LevelIdValue { get; set; }
            [DataMember] public string LevelName { get; set; }
            [DataMember] public string BoundarySignature { get; set; }
            [DataMember] public double AreaM2 { get; set; }
            [DataMember] public PointDto Centroid { get; set; }
            [DataMember] public BoundingBoxDto BBox { get; set; }
            [DataMember] public List<PointDto> LoopPoints { get; set; } = new List<PointDto>();
            [DataMember] public List<RoomBoundaryWallReferenceDto> BoundaryWalls { get; set; } = new List<RoomBoundaryWallReferenceDto>();
            [DataMember] public string CreatedAt { get; set; }
        }

        [DataContract]
        private sealed class PointDto
        {
            [DataMember] public double X { get; set; }
            [DataMember] public double Y { get; set; }
            [DataMember] public double Z { get; set; }
        }

        [DataContract]
        private sealed class BoundingBoxDto
        {
            [DataMember] public PointDto Min { get; set; } = new PointDto();
            [DataMember] public PointDto Max { get; set; } = new PointDto();
        }

        [DataContract]
        private sealed class RoomBoundaryWallReferenceDto
        {
            [DataMember] public int ElementId { get; set; }
            [DataMember] public string UniqueId { get; set; }
            [DataMember] public string DisplayName { get; set; }
            [DataMember] public string RevitName { get; set; }
            [DataMember] public double LengthMm { get; set; }
        }
    }
}
