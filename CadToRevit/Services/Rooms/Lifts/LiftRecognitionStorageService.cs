using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Rooms.Semantic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.Lifts
{
    public static class LiftRecognitionStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("C165FAAD-C4CF-4AA0-87E7-B8B5F2AEE601");
        private const string SchemaName = "CadToRevitLiftRecognitionStore";
        private const string FieldName = "JsonPayload";

        public static void Save(Document doc, IList<LiftRecognitionRecord> lifts)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            LiftStorePayload payload = new LiftStorePayload
            {
                Version = "1.0",
                SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Lifts = ToDtos(lifts)
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

        public static List<LiftRecognitionRecord> Load(Document doc)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return new List<LiftRecognitionRecord>();
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return new List<LiftRecognitionRecord>();
            }

            Entity entity = doc.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid())
            {
                return new List<LiftRecognitionRecord>();
            }

            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return new List<LiftRecognitionRecord>();
            }

            string json = entity.Get<string>(field) ?? string.Empty;
            LiftStorePayload payload = Deserialize(json);
            return FromDtos(payload != null ? payload.Lifts : null);
        }

        public static void Upsert(Document doc, LiftRecognitionRecord lift)
        {
            if (doc == null || lift == null || string.IsNullOrWhiteSpace(lift.Key))
            {
                return;
            }

            List<LiftRecognitionRecord> lifts = Load(doc) ?? new List<LiftRecognitionRecord>();
            lifts.RemoveAll(x => x == null || string.Equals(x.Key, lift.Key, StringComparison.OrdinalIgnoreCase));
            lifts.Add(lift);
            Save(doc, lifts);
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

        private static string Serialize(LiftStorePayload payload)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LiftStorePayload));
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static LiftStorePayload Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new LiftStorePayload();
            }

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LiftStorePayload));
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(ms) as LiftStorePayload ?? new LiftStorePayload();
                }
            }
            catch
            {
                return new LiftStorePayload();
            }
        }

        private static List<LiftRecognitionRecordDto> ToDtos(IList<LiftRecognitionRecord> lifts)
        {
            List<LiftRecognitionRecordDto> result = new List<LiftRecognitionRecordDto>();
            foreach (LiftRecognitionRecord lift in lifts ?? new List<LiftRecognitionRecord>())
            {
                if (lift == null || lift.Position == null)
                {
                    continue;
                }

                result.Add(new LiftRecognitionRecordDto
                {
                    Key = lift.Key ?? string.Empty,
                    LiftName = lift.LiftName ?? string.Empty,
                    LiftKind = lift.LiftKind ?? string.Empty,
                    X = lift.Position.X,
                    Y = lift.Position.Y,
                    Z = lift.Position.Z,
                    LevelId = lift.LevelId != null ? lift.LevelId.IntegerValue : -1,
                    SourceLayer = lift.SourceLayer ?? string.Empty,
                    RawText = lift.RawText ?? string.Empty,
                    LiftId = lift.LiftId ?? string.Empty,
                    LiftType = lift.LiftType ?? string.Empty,
                    Dimension = lift.Dimension ?? string.Empty,
                    DoorSize = lift.DoorSize ?? string.Empty,
                    Capacity = lift.Capacity ?? string.Empty,
                    BoundaryPoints = ToPointDtos(lift.BoundaryPoints),
                    VirtualDoorStart = ToPointDto(lift.VirtualDoorStart),
                    VirtualDoorEnd = ToPointDto(lift.VirtualDoorEnd),
                    VirtualDoorHostWallId = lift.VirtualDoorHostWallId != null ? lift.VirtualDoorHostWallId.IntegerValue : -1,
                    VirtualDoorWidthMm = lift.VirtualDoorWidthMm,
                    VirtualDoorHeightMm = lift.VirtualDoorHeightMm,
                    VirtualDoorSillMm = lift.VirtualDoorSillMm,
                    GeometrySourceLayer = lift.GeometrySourceLayer ?? string.Empty
                });
            }

            return result;
        }

        private static List<LiftRecognitionRecord> FromDtos(IList<LiftRecognitionRecordDto> dtos)
        {
            List<LiftRecognitionRecord> result = new List<LiftRecognitionRecord>();
            foreach (LiftRecognitionRecordDto dto in dtos ?? new List<LiftRecognitionRecordDto>())
            {
                if (dto == null)
                {
                    continue;
                }

                result.Add(new LiftRecognitionRecord
                {
                    Key = dto.Key ?? string.Empty,
                    LiftName = dto.LiftName ?? string.Empty,
                    LiftKind = dto.LiftKind ?? string.Empty,
                    Position = new XYZ(dto.X, dto.Y, dto.Z),
                    LevelId = dto.LevelId > 0 ? new ElementId(dto.LevelId) : ElementId.InvalidElementId,
                    SourceLayer = dto.SourceLayer ?? string.Empty,
                    RawText = dto.RawText ?? string.Empty,
                    LiftId = dto.LiftId ?? string.Empty,
                    LiftType = dto.LiftType ?? string.Empty,
                    Dimension = dto.Dimension ?? string.Empty,
                    DoorSize = dto.DoorSize ?? string.Empty,
                    Capacity = dto.Capacity ?? string.Empty,
                    BoundaryPoints = FromPointDtos(dto.BoundaryPoints),
                    VirtualDoorStart = FromPointDto(dto.VirtualDoorStart),
                    VirtualDoorEnd = FromPointDto(dto.VirtualDoorEnd),
                    VirtualDoorHostWallId = dto.VirtualDoorHostWallId > 0 ? new ElementId(dto.VirtualDoorHostWallId) : ElementId.InvalidElementId,
                    VirtualDoorWidthMm = dto.VirtualDoorWidthMm,
                    VirtualDoorHeightMm = dto.VirtualDoorHeightMm > 0.0 ? dto.VirtualDoorHeightMm : 2100.0,
                    VirtualDoorSillMm = dto.VirtualDoorSillMm,
                    GeometrySourceLayer = dto.GeometrySourceLayer ?? string.Empty
                });
            }

            return result;
        }

        private static List<PointDto> ToPointDtos(IList<XYZ> points)
        {
            List<PointDto> result = new List<PointDto>();
            foreach (XYZ point in points ?? new List<XYZ>())
            {
                PointDto dto = ToPointDto(point);
                if (dto != null)
                {
                    result.Add(dto);
                }
            }

            return result;
        }

        private static PointDto ToPointDto(XYZ point)
        {
            if (point == null)
            {
                return null;
            }

            return new PointDto { X = point.X, Y = point.Y, Z = point.Z };
        }

        private static List<XYZ> FromPointDtos(IList<PointDto> points)
        {
            List<XYZ> result = new List<XYZ>();
            foreach (PointDto point in points ?? new List<PointDto>())
            {
                XYZ xyz = FromPointDto(point);
                if (xyz != null)
                {
                    result.Add(xyz);
                }
            }

            return result;
        }

        private static XYZ FromPointDto(PointDto point)
        {
            if (point == null)
            {
                return null;
            }

            return new XYZ(point.X, point.Y, point.Z);
        }

        [DataContract]
        private sealed class LiftStorePayload
        {
            [DataMember(Name = "version")]
            public string Version { get; set; }

            [DataMember(Name = "savedAt")]
            public string SavedAt { get; set; }

            [DataMember(Name = "lifts")]
            public List<LiftRecognitionRecordDto> Lifts { get; set; } = new List<LiftRecognitionRecordDto>();
        }

        [DataContract]
        private sealed class LiftRecognitionRecordDto
        {
            [DataMember(Name = "key")]
            public string Key { get; set; }

            [DataMember(Name = "liftName")]
            public string LiftName { get; set; }

            [DataMember(Name = "liftKind")]
            public string LiftKind { get; set; }

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

            [DataMember(Name = "liftId")]
            public string LiftId { get; set; }

            [DataMember(Name = "liftType")]
            public string LiftType { get; set; }

            [DataMember(Name = "dimension")]
            public string Dimension { get; set; }

            [DataMember(Name = "doorSize")]
            public string DoorSize { get; set; }

            [DataMember(Name = "capacity")]
            public string Capacity { get; set; }

            [DataMember(Name = "boundaryPoints")]
            public List<PointDto> BoundaryPoints { get; set; } = new List<PointDto>();

            [DataMember(Name = "virtualDoorStart")]
            public PointDto VirtualDoorStart { get; set; }

            [DataMember(Name = "virtualDoorEnd")]
            public PointDto VirtualDoorEnd { get; set; }

            [DataMember(Name = "virtualDoorHostWallId")]
            public int VirtualDoorHostWallId { get; set; }

            [DataMember(Name = "virtualDoorWidthMm")]
            public double VirtualDoorWidthMm { get; set; }

            [DataMember(Name = "virtualDoorHeightMm")]
            public double VirtualDoorHeightMm { get; set; }

            [DataMember(Name = "virtualDoorSillMm")]
            public double VirtualDoorSillMm { get; set; }

            [DataMember(Name = "geometrySourceLayer")]
            public string GeometrySourceLayer { get; set; }
        }

        [DataContract]
        private sealed class PointDto
        {
            [DataMember(Name = "x")]
            public double X { get; set; }

            [DataMember(Name = "y")]
            public double Y { get; set; }

            [DataMember(Name = "z")]
            public double Z { get; set; }
        }
    }
}
