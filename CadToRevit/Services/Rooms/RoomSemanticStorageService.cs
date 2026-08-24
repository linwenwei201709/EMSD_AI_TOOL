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
    public sealed class RoomSemanticStorageMeta
    {
        public int DwgImportId { get; set; }

        public int LevelId { get; set; }

        public string RoomNameLayer { get; set; }

        public List<string> WallLayers { get; set; } = new List<string>();

        public RoomSemanticConfig Config { get; set; } = new RoomSemanticConfig();
    }

    public static class RoomSemanticStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("F5A66A8D-2F69-4EC9-A3F5-6D5E3A40AA11");
        private const string SchemaName = "CadToRevitRoomSemanticStore";
        private const string FieldName = "JsonPayload";

        public static void Save(Document doc, RoomSemanticRunResult data, RoomSemanticStorageMeta meta)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            RoomSemanticStorePayload payload = new RoomSemanticStorePayload
            {
                Version = "1.0",
                BatchId = DateTime.Now.ToString("yyyyMMdd-HHmmss"),
                DwgImportId = meta != null ? meta.DwgImportId : -1,
                LevelId = meta != null ? meta.LevelId : -1,
                RoomNameLayer = meta != null ? (meta.RoomNameLayer ?? string.Empty) : string.Empty,
                WallLayers = meta != null ? (meta.WallLayers ?? new List<string>()) : new List<string>(),
                Config = ToConfigDto(meta != null ? (meta.Config ?? new RoomSemanticConfig()) : new RoomSemanticConfig()),
                Rooms = ToRoomDtos(data != null ? data.Rooms : null),
                UnmatchedLabels = ToLabelDtos(data != null ? data.UnmatchedLabels : null)
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

        public static string ReadRaw(Document doc)
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

            return entity.Get<string>(field) ?? string.Empty;
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

        private static string Serialize(RoomSemanticStorePayload payload)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(RoomSemanticStorePayload));
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, payload);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static RoomSemanticConfigDto ToConfigDto(RoomSemanticConfig cfg)
        {
            RoomSemanticConfig source = cfg ?? new RoomSemanticConfig();
            return new RoomSemanticConfigDto
            {
                TargetKeywords = source.TargetKeywords ?? new List<string>(),
                CloseTolMm = source.CloseTolMm,
                MaxPatchMm = source.MaxPatchMm,
                MinAreaM2 = source.MinAreaM2,
                DoorGapMaxMm = source.DoorGapMaxMm,
                SmallGapPatchMaxMm = source.SmallGapPatchMaxMm
            };
        }

        private static List<RoomSemanticRecordDto> ToRoomDtos(IEnumerable<RoomSemanticRecord> records)
        {
            List<RoomSemanticRecordDto> result = new List<RoomSemanticRecordDto>();
            foreach (RoomSemanticRecord r in records ?? new List<RoomSemanticRecord>())
            {
                if (r == null)
                {
                    continue;
                }

                result.Add(new RoomSemanticRecordDto
                {
                    Key = r.Key ?? string.Empty,
                    RoomName = r.RoomName ?? string.Empty,
                    RoomNumber = r.RoomNumber ?? string.Empty,
                    TargetRoomType = r.TargetRoomType ?? string.Empty,
                    Status = r.Status ?? string.Empty,
                    AreaM2 = r.AreaM2,
                    CloseGapMm = r.CloseGapMm,
                    BoundaryLayers = r.BoundaryLayers ?? string.Empty,
                    Centroid = ToPointDto(r.Centroid),
                    BBox = ToBoxDto(r.BBox),
                    LoopPoints = (r.LoopPoints ?? new List<XYZ>()).ConvertAll(ToPointDto),
                    BoundaryWalls = (r.BoundaryWalls ?? new List<RoomBoundaryWallReference>()).ConvertAll(x => new RoomBoundaryWallReferenceDto
                    {
                        ElementId = x != null ? x.ElementId : -1,
                        UniqueId = x != null ? (x.UniqueId ?? string.Empty) : string.Empty,
                        DisplayName = x != null ? (x.DisplayName ?? string.Empty) : string.Empty,
                        RevitName = x != null ? (x.RevitName ?? string.Empty) : string.Empty,
                        LengthMm = x != null ? x.LengthMm : 0.0
                    })
                });
            }

            return result;
        }

        private static List<RoomLabelDto> ToLabelDtos(IEnumerable<RoomLabel> labels)
        {
            List<RoomLabelDto> result = new List<RoomLabelDto>();
            foreach (RoomLabel l in labels ?? new List<RoomLabel>())
            {
                if (l == null)
                {
                    continue;
                }

                result.Add(new RoomLabelDto
                {
                    RawText = l.RawText ?? string.Empty,
                    RoomName = l.RoomName ?? string.Empty,
                    RoomNumber = l.RoomNumber ?? string.Empty,
                    TargetRoomType = l.TargetRoomType ?? string.Empty,
                    SourceLayer = l.SourceLayer ?? string.Empty,
                    Position = ToPointDto(l.Position)
                });
            }

            return result;
        }

        private static PointDto ToPointDto(XYZ p)
        {
            if (p == null)
            {
                return new PointDto();
            }

            return new PointDto { X = p.X, Y = p.Y, Z = p.Z };
        }

        private static BoundingBoxDto ToBoxDto(BoundingBoxXYZ box)
        {
            if (box == null)
            {
                return new BoundingBoxDto();
            }

            return new BoundingBoxDto
            {
                Min = ToPointDto(box.Min),
                Max = ToPointDto(box.Max)
            };
        }

        [DataContract]
        private sealed class RoomSemanticStorePayload
        {
            [DataMember(Name = "Version")]
            public string Version { get; set; }

            [DataMember(Name = "BatchId")]
            public string BatchId { get; set; }

            [DataMember(Name = "DwgImportId")]
            public int DwgImportId { get; set; }

            [DataMember(Name = "LevelId")]
            public int LevelId { get; set; }

            [DataMember(Name = "RoomNameLayer")]
            public string RoomNameLayer { get; set; }

            [DataMember(Name = "WallLayers")]
            public List<string> WallLayers { get; set; } = new List<string>();

            [DataMember(Name = "Config")]
            public RoomSemanticConfigDto Config { get; set; } = new RoomSemanticConfigDto();

            [DataMember(Name = "Rooms")]
            public List<RoomSemanticRecordDto> Rooms { get; set; } = new List<RoomSemanticRecordDto>();

            [DataMember(Name = "UnmatchedLabels")]
            public List<RoomLabelDto> UnmatchedLabels { get; set; } = new List<RoomLabelDto>();
        }

        [DataContract]
        private sealed class RoomSemanticConfigDto
        {
            [DataMember(Name = "targetKeywords")]
            public List<string> TargetKeywords { get; set; } = new List<string>();

            [DataMember(Name = "closeTolMm")]
            public double CloseTolMm { get; set; }

            [DataMember(Name = "maxPatchMm")]
            public double MaxPatchMm { get; set; }

            [DataMember(Name = "minAreaM2")]
            public double MinAreaM2 { get; set; }

            [DataMember(Name = "doorGapMaxMm")]
            public double DoorGapMaxMm { get; set; }

            [DataMember(Name = "smallGapPatchMaxMm")]
            public double SmallGapPatchMaxMm { get; set; }
        }

        [DataContract]
        private sealed class RoomSemanticRecordDto
        {
            [DataMember(Name = "Key")]
            public string Key { get; set; }

            [DataMember(Name = "RoomName")]
            public string RoomName { get; set; }

            [DataMember(Name = "RoomNumber")]
            public string RoomNumber { get; set; }

            [DataMember(Name = "TargetRoomType")]
            public string TargetRoomType { get; set; }

            [DataMember(Name = "Status")]
            public string Status { get; set; }

            [DataMember(Name = "AreaM2")]
            public double AreaM2 { get; set; }

            [DataMember(Name = "CloseGapMm")]
            public double CloseGapMm { get; set; }

            [DataMember(Name = "BoundaryLayers")]
            public string BoundaryLayers { get; set; }

            [DataMember(Name = "Centroid")]
            public PointDto Centroid { get; set; } = new PointDto();

            [DataMember(Name = "BBox")]
            public BoundingBoxDto BBox { get; set; } = new BoundingBoxDto();

            [DataMember(Name = "LoopPoints")]
            public List<PointDto> LoopPoints { get; set; } = new List<PointDto>();

            [DataMember(Name = "BoundaryWalls")]
            public List<RoomBoundaryWallReferenceDto> BoundaryWalls { get; set; } = new List<RoomBoundaryWallReferenceDto>();
        }

        [DataContract]
        private sealed class RoomBoundaryWallReferenceDto
        {
            [DataMember(Name = "ElementId")]
            public int ElementId { get; set; }

            [DataMember(Name = "UniqueId")]
            public string UniqueId { get; set; }

            [DataMember(Name = "DisplayName")]
            public string DisplayName { get; set; }

            [DataMember(Name = "RevitName")]
            public string RevitName { get; set; }

            [DataMember(Name = "LengthMm")]
            public double LengthMm { get; set; }
        }

        [DataContract]
        private sealed class RoomLabelDto
        {
            [DataMember(Name = "RawText")]
            public string RawText { get; set; }

            [DataMember(Name = "RoomName")]
            public string RoomName { get; set; }

            [DataMember(Name = "RoomNumber")]
            public string RoomNumber { get; set; }

            [DataMember(Name = "TargetRoomType")]
            public string TargetRoomType { get; set; }

            [DataMember(Name = "SourceLayer")]
            public string SourceLayer { get; set; }

            [DataMember(Name = "Position")]
            public PointDto Position { get; set; } = new PointDto();
        }

        [DataContract]
        private sealed class PointDto
        {
            [DataMember(Name = "X")]
            public double X { get; set; }

            [DataMember(Name = "Y")]
            public double Y { get; set; }

            [DataMember(Name = "Z")]
            public double Z { get; set; }
        }

        [DataContract]
        private sealed class BoundingBoxDto
        {
            [DataMember(Name = "Min")]
            public PointDto Min { get; set; } = new PointDto();

            [DataMember(Name = "Max")]
            public PointDto Max { get; set; } = new PointDto();
        }
    }
}
