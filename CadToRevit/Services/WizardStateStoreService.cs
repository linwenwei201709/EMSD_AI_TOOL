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
    public static class WizardStateStoreService
    {
        private static readonly Guid SchemaGuid = new Guid("1A3CB331-6374-4BEA-8CA6-2D7B95D8A5F1");
        private const string SchemaName = "CadToRevitWizardStateStore";
        private const string FieldName = "JsonPayload";

        public static bool TryLoad(Document doc, string contextSignature, out List<MapRow> mapRows)
        {
            mapRows = new List<MapRow>();
            if (doc == null)
            {
                return false;
            }

            try
            {
                if (HasPersistentDocIdentity(doc))
                {
                    WizardStateDto fileDto = TryLoadFromFile(doc);
                    if (TryResolveDto(fileDto, contextSignature, out mapRows))
                    {
                        return true;
                    }
                }

                if (doc.ProjectInformation == null)
                {
                    return false;
                }

                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null)
                {
                    return false;
                }

                Entity entity = doc.ProjectInformation.GetEntity(schema);
                if (!entity.IsValid())
                {
                    return false;
                }

                Field field = schema.GetField(FieldName);
                if (field == null)
                {
                    return false;
                }

                string payload = entity.Get<string>(field);
                WizardStateDto dto = Deserialize(payload);
                if (!TryResolveDto(dto, contextSignature, out mapRows))
                {
                    return false;
                }

                TryWriteToFile(doc, dto);
                return true;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[WizardStateStore] Load failed: " + ex.Message);
                return false;
            }
        }

        public static void Save(Document doc, string contextSignature, IEnumerable<MapRow> mapRows)
        {
            if (doc == null)
            {
                return;
            }

            try
            {
                WizardStateDto dto = ToDto(contextSignature, mapRows);
                string payload = Serialize(dto);
                if (HasPersistentDocIdentity(doc))
                {
                    TryWriteToFile(doc, dto);
                }

                if (doc.ProjectInformation == null)
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

                    Entity entity = new Entity(schema);
                    entity.Set(field, payload ?? string.Empty);
                    doc.ProjectInformation.SetEntity(entity);
                };

                if (doc.IsModifiable)
                {
                    write();
                    return;
                }

                using (Transaction tx = new Transaction(doc, "CadToRevit Save Wizard State"))
                {
                    tx.Start();
                    write();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[WizardStateStore] Save failed: " + ex.Message);
            }
        }

        public static void Clear(Document doc)
        {
            Save(doc, string.Empty, new List<MapRow>());
        }

        public static void Clear(Document doc, string contextSignature)
        {
            Save(doc, contextSignature, new List<MapRow>());
        }

        private static bool TryResolveDto(WizardStateDto dto, string contextSignature, out List<MapRow> mapRows)
        {
            mapRows = new List<MapRow>();
            if (dto == null)
            {
                return false;
            }

            string savedContext = dto.ContextSignature ?? string.Empty;
            string currentContext = contextSignature ?? string.Empty;
            if (!string.Equals(savedContext, currentContext, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            mapRows = FromDto(dto);
            return mapRows.Count > 0;
        }

        private static WizardStateDto TryLoadFromFile(Document doc)
        {
            try
            {
                string path = GetStorePath(doc);
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                if (!File.Exists(path))
                {
                    return null;
                }

                string payload = File.ReadAllText(path);
                return Deserialize(payload);
            }
            catch
            {
                return null;
            }
        }

        private static void TryWriteToFile(Document doc, WizardStateDto dto)
        {
            try
            {
                string path = GetStorePath(doc);
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, Serialize(dto));
            }
            catch
            {
            }
        }

        private static string GetStorePath(Document doc)
        {
            if (!HasPersistentDocIdentity(doc))
            {
                return null;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string root = Path.Combine(appData, "CadToRevit", "HelixWizard", "wizard_state");
            string key = doc != null ? (doc.PathName ?? string.Empty) : string.Empty;

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(c, '_');
            }

            return Path.Combine(root, key + ".json");
        }

        private static bool HasPersistentDocIdentity(Document doc)
        {
            return doc != null && !string.IsNullOrWhiteSpace(doc.PathName);
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

        private static string Serialize(WizardStateDto dto)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WizardStateDto));
                serializer.WriteObject(ms, dto ?? new WizardStateDto());
                ms.Position = 0;
                using (StreamReader reader = new StreamReader(ms))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static WizardStateDto Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WizardStateDto));
                    return serializer.ReadObject(ms) as WizardStateDto;
                }
            }
            catch
            {
                return null;
            }
        }

        private static WizardStateDto ToDto(string contextSignature, IEnumerable<MapRow> mapRows)
        {
            WizardStateDto dto = new WizardStateDto
            {
                SchemaVersion = 1,
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
                ContextSignature = contextSignature ?? string.Empty
            };

            foreach (MapRow row in mapRows ?? Enumerable.Empty<MapRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.RawLayerName))
                {
                    continue;
                }

                dto.MapRows.Add(new MapRowDto
                {
                    RawLayerName = row.RawLayerName,
                    Category = row.Category.ToString(),
                    RevitTypeName = row.RevitTypeName,
                    ExpectedWidthMm = row.ExpectedWidthMm,
                    Settings = ToSettingsDto(row.Settings)
                });
            }

            return dto;
        }

        private static List<MapRow> FromDto(WizardStateDto dto)
        {
            List<MapRow> rows = new List<MapRow>();
            foreach (MapRowDto item in dto?.MapRows ?? new List<MapRowDto>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName))
                {
                    continue;
                }

                MapCategory category;
                if (!Enum.TryParse(item.Category ?? string.Empty, true, out category))
                {
                    category = MapCategory.Walls;
                }

                rows.Add(new MapRow
                {
                    RawLayerName = item.RawLayerName,
                    Category = category,
                    RevitTypeName = item.RevitTypeName,
                    ExpectedWidthMm = item.ExpectedWidthMm,
                    Settings = FromSettingsDto(item.Settings)
                });
            }

            return rows;
        }

        private static AdvancedSettingsDto ToSettingsDto(AdvancedSettingsRow settings)
        {
            AdvancedSettingsRow s = settings ?? new AdvancedSettingsRow();
            AdvancedSettingsDto dto = new AdvancedSettingsDto
            {
                EnableLayerOverride = s.EnableLayerOverride,
                ApplyAsCategoryDefault = s.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = s.DoorExpectedWidthMm,
                MinDoorWidthMm = s.MinDoorWidthMm,
                MaxDoorWidthMm = s.MaxDoorWidthMm,
                DoorWallMatchTolMm = s.DoorWallMatchTolMm,
                WallMinWallLengthMm = s.WallMinWallLengthMm,
                WallThicknessTolMm = s.WallThicknessTolMm,
                WallMaxWallThicknessMm = s.WallMaxWallThicknessMm,
                WallDefaultSingleWallThicknessMm = s.WallDefaultSingleWallThicknessMm,
                WallParallelAngleTolDeg = s.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = s.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = s.WallArcThicknessTolMm,
                WallHeightMm = s.WallHeightMm,
                WallBaseOffsetMm = s.WallBaseOffsetMm,
                WallEnableExtendCollinear = s.WallEnableExtendCollinear,
                WallEnableMergeCollinear = s.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = s.WallExtendCollinearTolMm,
                WallEndpointClusterTolMm = s.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = s.WallExtendSearchTolMm,
                WallDuplicateTolMm = s.WallDuplicateTolMm,
                WallAngleSnapDeg = s.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = s.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = s.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = s.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = s.WallEnableDuplicateRemoval,
                WallCollinearOffsetTolMm = s.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = s.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = s.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = s.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = s.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = s.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = s.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = s.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = s.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = s.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = s.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = s.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = s.WallDoubleLineAdaptiveExtendMaxMm,
                DoorHeightMm = s.DoorHeightMm,
                DoorSillHeightMm = s.DoorSillHeightMm,
                UseFixedDoorWidth = s.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = s.PreferGeometryOpeningWidth,
                BeamMinLengthMm = s.BeamMinLengthMm,
                BeamElevationOffsetMm = s.BeamElevationOffsetMm,
                BeamEnableMergeCollinear = s.BeamEnableMergeCollinear,
                BeamEndpointMergeTolMm = s.BeamEndpointMergeTolMm,
                BeamParallelAngleTolDeg = s.BeamParallelAngleTolDeg,
                BeamAllowArc = s.BeamAllowArc,
                WindowHeightMm = s.WindowHeightMm,
                WindowSillHeightMm = s.WindowSillHeightMm,
                WindowUseSillPlusHeight = s.WindowUseSillPlusHeight,
                ColumnHeightMm = s.ColumnHeightMm,
                ColumnClusterAlgorithm = s.ColumnClusterAlgorithm,
                ColumnClusterTolMm = s.ColumnClusterTolMm,
                ColumnEndpointTolMm = s.ColumnEndpointTolMm,
                ColumnGapTolMm = s.ColumnGapTolMm,
                ColumnMinGroupSegments = s.ColumnMinGroupSegments,
                ColumnMinSizeMm = s.ColumnMinSizeMm,
                ColumnMaxSizeMm = s.ColumnMaxSizeMm,
                ColumnMinAreaM2 = s.ColumnMinAreaM2,
                ColumnMaxAspectRatio = s.ColumnMaxAspectRatio,
                ColumnMinFillRatio = s.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = s.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = s.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = s.ColumnEnableMerge,
                ColumnMergeTolMm = s.ColumnMergeTolMm,
                ColumnMergeStrategy = s.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = s.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = s.ColumnAreaWeight,
                ColumnSegmentCountWeight = s.ColumnSegmentCountWeight,
                ColumnRectnessWeight = s.ColumnRectnessWeight,
                ColumnLongLinePenalty = s.ColumnLongLinePenalty,
                ColumnIrregularEnable = s.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = s.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = s.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = s.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = s.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = s.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = s.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = s.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = s.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = s.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = s.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = s.ColumnDebugExportReport,
                Juncture = new JunctureDto
                {
                    IgnoreSmallerThanMm = s.Juncture != null ? s.Juncture.IgnoreSmallerThanMm : 0.0,
                    MinJunctureWidthMm = s.Juncture != null ? s.Juncture.MinJunctureWidthMm : 0.0,
                    IgnoreLargerThanMm = s.Juncture != null ? s.Juncture.IgnoreLargerThanMm : 0.0,
                    MaxJunctureWidthMm = s.Juncture != null ? s.Juncture.MaxJunctureWidthMm : 0.0
                }
            };

            foreach (ParameterMapping mapping in s.ParameterMappings ?? new List<ParameterMapping>())
            {
                if (mapping == null)
                {
                    continue;
                }

                dto.ParameterMappings.Add(new ParameterMappingDto
                {
                    ParameterName = mapping.ParameterName,
                    StorageType = mapping.StorageType,
                    Value = mapping.Value == null ? string.Empty : mapping.Value.ToString()
                });
            }

            return dto;
        }

        private static AdvancedSettingsRow FromSettingsDto(AdvancedSettingsDto dto)
        {
            if (dto == null)
            {
                return new AdvancedSettingsRow();
            }

            AdvancedSettingsRow s = new AdvancedSettingsRow
            {
                EnableLayerOverride = dto.EnableLayerOverride,
                ApplyAsCategoryDefault = dto.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = dto.DoorExpectedWidthMm,
                MinDoorWidthMm = dto.MinDoorWidthMm,
                MaxDoorWidthMm = dto.MaxDoorWidthMm,
                DoorWallMatchTolMm = dto.DoorWallMatchTolMm,
                WallMinWallLengthMm = dto.WallMinWallLengthMm,
                WallThicknessTolMm = dto.WallThicknessTolMm,
                WallMaxWallThicknessMm = dto.WallMaxWallThicknessMm,
                WallDefaultSingleWallThicknessMm = dto.WallDefaultSingleWallThicknessMm,
                WallParallelAngleTolDeg = dto.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = dto.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = dto.WallArcThicknessTolMm,
                WallHeightMm = dto.WallHeightMm,
                WallBaseOffsetMm = dto.WallBaseOffsetMm,
                WallEnableExtendCollinear = dto.WallEnableExtendCollinear,
                WallEnableMergeCollinear = dto.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = dto.WallExtendCollinearTolMm,
                WallEndpointClusterTolMm = dto.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = dto.WallExtendSearchTolMm,
                WallDuplicateTolMm = dto.WallDuplicateTolMm,
                WallAngleSnapDeg = dto.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = dto.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = dto.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = dto.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = dto.WallEnableDuplicateRemoval,
                WallCollinearOffsetTolMm = dto.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = dto.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = dto.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = dto.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = dto.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = dto.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = dto.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = dto.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = dto.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = dto.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = dto.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = dto.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = dto.WallDoubleLineAdaptiveExtendMaxMm,
                DoorHeightMm = dto.DoorHeightMm,
                DoorSillHeightMm = dto.DoorSillHeightMm,
                UseFixedDoorWidth = dto.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = dto.PreferGeometryOpeningWidth,
                BeamMinLengthMm = dto.BeamMinLengthMm,
                BeamElevationOffsetMm = dto.BeamElevationOffsetMm,
                BeamEnableMergeCollinear = dto.BeamEnableMergeCollinear,
                BeamEndpointMergeTolMm = dto.BeamEndpointMergeTolMm,
                BeamParallelAngleTolDeg = dto.BeamParallelAngleTolDeg,
                BeamAllowArc = dto.BeamAllowArc,
                WindowHeightMm = dto.WindowHeightMm,
                WindowSillHeightMm = dto.WindowSillHeightMm,
                WindowUseSillPlusHeight = dto.WindowUseSillPlusHeight,
                ColumnHeightMm = dto.ColumnHeightMm,
                ColumnClusterAlgorithm = dto.ColumnClusterAlgorithm,
                ColumnClusterTolMm = dto.ColumnClusterTolMm,
                ColumnEndpointTolMm = dto.ColumnEndpointTolMm,
                ColumnGapTolMm = dto.ColumnGapTolMm,
                ColumnMinGroupSegments = dto.ColumnMinGroupSegments,
                ColumnMinSizeMm = dto.ColumnMinSizeMm,
                ColumnMaxSizeMm = dto.ColumnMaxSizeMm,
                ColumnMinAreaM2 = dto.ColumnMinAreaM2,
                ColumnMaxAspectRatio = dto.ColumnMaxAspectRatio,
                ColumnMinFillRatio = dto.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = dto.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = dto.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = dto.ColumnEnableMerge,
                ColumnMergeTolMm = dto.ColumnMergeTolMm,
                ColumnMergeStrategy = dto.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = dto.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = dto.ColumnAreaWeight,
                ColumnSegmentCountWeight = dto.ColumnSegmentCountWeight,
                ColumnRectnessWeight = dto.ColumnRectnessWeight,
                ColumnLongLinePenalty = dto.ColumnLongLinePenalty,
                ColumnIrregularEnable = dto.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = dto.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = dto.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = dto.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = dto.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = dto.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = dto.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = dto.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = dto.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = dto.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = dto.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = dto.ColumnDebugExportReport,
                Juncture = new JunctureSettings
                {
                    IgnoreSmallerThanMm = dto.Juncture != null ? dto.Juncture.IgnoreSmallerThanMm : 0.0,
                    MinJunctureWidthMm = dto.Juncture != null ? dto.Juncture.MinJunctureWidthMm : 0.0,
                    IgnoreLargerThanMm = dto.Juncture != null ? dto.Juncture.IgnoreLargerThanMm : 0.0,
                    MaxJunctureWidthMm = dto.Juncture != null ? dto.Juncture.MaxJunctureWidthMm : 0.0
                }
            };

            foreach (ParameterMappingDto p in dto.ParameterMappings ?? new List<ParameterMappingDto>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.ParameterName))
                {
                    continue;
                }

                s.ParameterMappings.Add(new ParameterMapping
                {
                    ParameterName = p.ParameterName,
                    StorageType = p.StorageType,
                    Value = p.Value
                });
            }

            return s;
        }

        [DataContract]
        private sealed class WizardStateDto
        {
            [DataMember(Name = "SchemaVersion")]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "SavedAtUtc")]
            public string SavedAtUtc { get; set; }

            [DataMember(Name = "ContextSignature")]
            public string ContextSignature { get; set; }

            [DataMember(Name = "MapRows")]
            public List<MapRowDto> MapRows { get; set; } = new List<MapRowDto>();
        }

        [DataContract]
        private sealed class MapRowDto
        {
            [DataMember(Name = "RawLayerName")]
            public string RawLayerName { get; set; }

            [DataMember(Name = "Category")]
            public string Category { get; set; }

            [DataMember(Name = "RevitTypeName")]
            public string RevitTypeName { get; set; }

            [DataMember(Name = "ExpectedWidthMm")]
            public double? ExpectedWidthMm { get; set; }

            [DataMember(Name = "Settings")]
            public AdvancedSettingsDto Settings { get; set; } = new AdvancedSettingsDto();
        }

        [DataContract]
        private sealed class AdvancedSettingsDto
        {
            [DataMember(Name = "EnableLayerOverride")]
            public bool EnableLayerOverride { get; set; }
            [DataMember(Name = "ApplyAsCategoryDefault")]
            public bool ApplyAsCategoryDefault { get; set; }
            [DataMember(Name = "DoorExpectedWidthMm")]
            public double? DoorExpectedWidthMm { get; set; }
            [DataMember(Name = "MinDoorWidthMm")]
            public double? MinDoorWidthMm { get; set; }
            [DataMember(Name = "MaxDoorWidthMm")]
            public double? MaxDoorWidthMm { get; set; }
            [DataMember(Name = "DoorWallMatchTolMm")]
            public double? DoorWallMatchTolMm { get; set; }
            [DataMember(Name = "WallMinWallLengthMm")]
            public double? WallMinWallLengthMm { get; set; }
            [DataMember(Name = "WallThicknessTolMm")]
            public double? WallThicknessTolMm { get; set; }
            [DataMember(Name = "WallMaxWallThicknessMm")]
            public double? WallMaxWallThicknessMm { get; set; }
            [DataMember(Name = "WallDefaultSingleWallThicknessMm")]
            public double? WallDefaultSingleWallThicknessMm { get; set; }
            [DataMember(Name = "WallParallelAngleTolDeg")]
            public double? WallParallelAngleTolDeg { get; set; }
            [DataMember(Name = "WallEndpointMergeTolMm")]
            public double? WallEndpointMergeTolMm { get; set; }
            [DataMember(Name = "WallArcThicknessTolMm")]
            public double? WallArcThicknessTolMm { get; set; }
            [DataMember(Name = "WallHeightMm")]
            public double? WallHeightMm { get; set; }
            [DataMember(Name = "WallBaseOffsetMm")]
            public double? WallBaseOffsetMm { get; set; }
            [DataMember(Name = "WallEnableExtendCollinear")]
            public bool? WallEnableExtendCollinear { get; set; }
            [DataMember(Name = "WallEnableMergeCollinear")]
            public bool? WallEnableMergeCollinear { get; set; }
            [DataMember(Name = "WallExtendCollinearTolMm")]
            public double? WallExtendCollinearTolMm { get; set; }
            [DataMember(Name = "WallEndpointClusterTolMm")]
            public double? WallEndpointClusterTolMm { get; set; }
            [DataMember(Name = "WallExtendSearchTolMm")]
            public double? WallExtendSearchTolMm { get; set; }
            [DataMember(Name = "WallDuplicateTolMm")]
            public double? WallDuplicateTolMm { get; set; }
            [DataMember(Name = "WallAngleSnapDeg")]
            public double? WallAngleSnapDeg { get; set; }
            [DataMember(Name = "WallEnableOrthogonalSnap")]
            public bool? WallEnableOrthogonalSnap { get; set; }
            [DataMember(Name = "WallEnableExtendToIntersection")]
            public bool? WallEnableExtendToIntersection { get; set; }
            [DataMember(Name = "WallEnableEndpointClustering")]
            public bool? WallEnableEndpointClustering { get; set; }
            [DataMember(Name = "WallEnableDuplicateRemoval")]
            public bool? WallEnableDuplicateRemoval { get; set; }
            [DataMember(Name = "WallCollinearOffsetTolMm")]
            public double? WallCollinearOffsetTolMm { get; set; }
            [DataMember(Name = "WallExtendProjectionTolMm")]
            public double? WallExtendProjectionTolMm { get; set; }
            [DataMember(Name = "WallUseDirectionalClustering")]
            public bool? WallUseDirectionalClustering { get; set; }
            [DataMember(Name = "WallEnableAutoDoubleLineThickness")]
            public bool? WallEnableAutoDoubleLineThickness { get; set; }
            [DataMember(Name = "WallAutoThicknessTopK")]
            public int? WallAutoThicknessTopK { get; set; }
            [DataMember(Name = "WallAutoThicknessBinMm")]
            public double? WallAutoThicknessBinMm { get; set; }
            [DataMember(Name = "WallMinDoubleLineThicknessMm")]
            public double? WallMinDoubleLineThicknessMm { get; set; }
            [DataMember(Name = "WallMinDoubleLineOverlapLenMm")]
            public double? WallMinDoubleLineOverlapLenMm { get; set; }
            [DataMember(Name = "WallForceSingleLineMode")]
            public bool? WallForceSingleLineMode { get; set; }
            [DataMember(Name = "WallDoubleLineSingleWallPlaceMode")]
            public string WallDoubleLineSingleWallPlaceMode { get; set; }
            [DataMember(Name = "WallDoubleLineLengthPolicy")]
            public string WallDoubleLineLengthPolicy { get; set; }
            [DataMember(Name = "WallDoubleLineAdaptiveContainTolMm")]
            public double? WallDoubleLineAdaptiveContainTolMm { get; set; }
            [DataMember(Name = "WallDoubleLineAdaptiveExtendMaxMm")]
            public double? WallDoubleLineAdaptiveExtendMaxMm { get; set; }
            [DataMember(Name = "DoorHeightMm")]
            public double? DoorHeightMm { get; set; }
            [DataMember(Name = "DoorSillHeightMm")]
            public double? DoorSillHeightMm { get; set; }
            [DataMember(Name = "UseFixedDoorWidth")]
            public bool? UseFixedDoorWidth { get; set; }
            [DataMember(Name = "PreferGeometryOpeningWidth")]
            public bool? PreferGeometryOpeningWidth { get; set; }
            [DataMember(Name = "DoorPreferHeadHeight")]
            public bool? DoorPreferHeadHeight { get; set; }
            [DataMember(Name = "BeamMinLengthMm")]
            public double? BeamMinLengthMm { get; set; }
            [DataMember(Name = "BeamElevationOffsetMm")]
            public double? BeamElevationOffsetMm { get; set; }
            [DataMember(Name = "BeamEnableMergeCollinear")]
            public bool? BeamEnableMergeCollinear { get; set; }
            [DataMember(Name = "BeamEndpointMergeTolMm")]
            public double? BeamEndpointMergeTolMm { get; set; }
            [DataMember(Name = "BeamParallelAngleTolDeg")]
            public double? BeamParallelAngleTolDeg { get; set; }
            [DataMember(Name = "BeamAllowArc")]
            public bool? BeamAllowArc { get; set; }
            [DataMember(Name = "WindowHeightMm")]
            public double? WindowHeightMm { get; set; }
            [DataMember(Name = "WindowSillHeightMm")]
            public double? WindowSillHeightMm { get; set; }
            [DataMember(Name = "WindowUseSillPlusHeight")]
            public bool? WindowUseSillPlusHeight { get; set; }
            [DataMember(Name = "ColumnHeightMm")]
            public double? ColumnHeightMm { get; set; }

            [DataMember(Name = "ColumnClusterAlgorithm")]
            public string ColumnClusterAlgorithm { get; set; }
            [DataMember(Name = "ColumnClusterTolMm")]
            public double? ColumnClusterTolMm { get; set; }
            [DataMember(Name = "ColumnEndpointTolMm")]
            public double? ColumnEndpointTolMm { get; set; }
            [DataMember(Name = "ColumnGapTolMm")]
            public double? ColumnGapTolMm { get; set; }
            [DataMember(Name = "ColumnMinGroupSegments")]
            public int? ColumnMinGroupSegments { get; set; }
            [DataMember(Name = "ColumnMinSizeMm")]
            public double? ColumnMinSizeMm { get; set; }
            [DataMember(Name = "ColumnMaxSizeMm")]
            public double? ColumnMaxSizeMm { get; set; }
            [DataMember(Name = "ColumnMinAreaM2")]
            public double? ColumnMinAreaM2 { get; set; }
            [DataMember(Name = "ColumnMaxAspectRatio")]
            public double? ColumnMaxAspectRatio { get; set; }
            [DataMember(Name = "ColumnMinFillRatio")]
            public double? ColumnMinFillRatio { get; set; }
            [DataMember(Name = "ColumnEnableLongLineFilter")]
            public bool? ColumnEnableLongLineFilter { get; set; }
            [DataMember(Name = "ColumnMaxSegmentLengthMm")]
            public double? ColumnMaxSegmentLengthMm { get; set; }
            [DataMember(Name = "ColumnEnableMerge")]
            public bool? ColumnEnableMerge { get; set; }
            [DataMember(Name = "ColumnMergeTolMm")]
            public double? ColumnMergeTolMm { get; set; }
            [DataMember(Name = "ColumnMergeStrategy")]
            public string ColumnMergeStrategy { get; set; }
            [DataMember(Name = "ColumnDedupePlacedTolMm")]
            public double? ColumnDedupePlacedTolMm { get; set; }
            [DataMember(Name = "ColumnAreaWeight")]
            public double? ColumnAreaWeight { get; set; }
            [DataMember(Name = "ColumnSegmentCountWeight")]
            public double? ColumnSegmentCountWeight { get; set; }
            [DataMember(Name = "ColumnRectnessWeight")]
            public double? ColumnRectnessWeight { get; set; }
            [DataMember(Name = "ColumnLongLinePenalty")]
            public double? ColumnLongLinePenalty { get; set; }
            [DataMember(Name = "ColumnIrregularEnable")]
            public bool? ColumnIrregularEnable { get; set; }
            [DataMember(Name = "ColumnIrregularMaxSizeMm")]
            public double? ColumnIrregularMaxSizeMm { get; set; }
            [DataMember(Name = "ColumnIrregularGapTolMm")]
            public double? ColumnIrregularGapTolMm { get; set; }
            [DataMember(Name = "ColumnIrregularMinAreaM2")]
            public double? ColumnIrregularMinAreaM2 { get; set; }
            [DataMember(Name = "ColumnAttachToWallEnable")]
            public bool? ColumnAttachToWallEnable { get; set; }
            [DataMember(Name = "ColumnAttachToWallSnapTolMm")]
            public double? ColumnAttachToWallSnapTolMm { get; set; }
            [DataMember(Name = "ColumnAttachToWallTarget")]
            public string ColumnAttachToWallTarget { get; set; }
            [DataMember(Name = "ColumnAttachToWallAllowOverlap")]
            public bool? ColumnAttachToWallAllowOverlap { get; set; }
            [DataMember(Name = "ColumnDebugDrawCandidates")]
            public bool? ColumnDebugDrawCandidates { get; set; }
            [DataMember(Name = "ColumnDebugDrawClusterId")]
            public bool? ColumnDebugDrawClusterId { get; set; }
            [DataMember(Name = "ColumnDebugDrawRejectReason")]
            public bool? ColumnDebugDrawRejectReason { get; set; }
            [DataMember(Name = "ColumnDebugExportReport")]
            public bool? ColumnDebugExportReport { get; set; }
            [DataMember(Name = "Juncture")]
            public JunctureDto Juncture { get; set; } = new JunctureDto();
            [DataMember(Name = "ParameterMappings")]
            public List<ParameterMappingDto> ParameterMappings { get; set; } = new List<ParameterMappingDto>();
        }

        [DataContract]
        private sealed class JunctureDto
        {
            [DataMember(Name = "IgnoreSmallerThanMm")]
            public double IgnoreSmallerThanMm { get; set; }
            [DataMember(Name = "MinJunctureWidthMm")]
            public double MinJunctureWidthMm { get; set; }
            [DataMember(Name = "IgnoreLargerThanMm")]
            public double IgnoreLargerThanMm { get; set; }
            [DataMember(Name = "MaxJunctureWidthMm")]
            public double MaxJunctureWidthMm { get; set; }
        }

        [DataContract]
        private sealed class ParameterMappingDto
        {
            [DataMember(Name = "ParameterName")]
            public string ParameterName { get; set; }
            [DataMember(Name = "StorageType")]
            public string StorageType { get; set; }
            [DataMember(Name = "Value")]
            public string Value { get; set; }
        }
    }
}



