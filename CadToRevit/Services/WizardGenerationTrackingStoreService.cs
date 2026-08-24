using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Mapping;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services
{
    public static class WizardGenerationTrackingStoreService
    {
        private static readonly Guid SchemaGuid = new Guid("0E6D6FB5-7C8C-4B7D-9A9D-7C9D6D0F4D76");
        private const string SchemaName = "CadToRevitWizardGenerationTrackingStore";
        private const string FieldName = "JsonPayload";

        public static List<WizardGenerationRowRecord> Load(Document doc)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return new List<WizardGenerationRowRecord>();
            }

            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null)
                {
                    return new List<WizardGenerationRowRecord>();
                }

                Entity entity = doc.ProjectInformation.GetEntity(schema);
                if (!entity.IsValid())
                {
                    return new List<WizardGenerationRowRecord>();
                }

                Field field = schema.GetField(FieldName);
                if (field == null)
                {
                    return new List<WizardGenerationRowRecord>();
                }

                string payload = entity.Get<string>(field);
                WizardGenerationTrackingDto dto = Deserialize(payload);
                return dto?.Rows ?? new List<WizardGenerationRowRecord>();
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[TrackingStore] Load failed: " + ex.Message);
                return new List<WizardGenerationRowRecord>();
            }
        }

        public static void Save(Document doc, IEnumerable<WizardGenerationRowRecord> rows)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            try
            {
                List<WizardGenerationRowRecord> normalized = (rows ?? new List<WizardGenerationRowRecord>())
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RowKey))
                    .GroupBy(x => x.RowKey, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.Last())
                    .OrderBy(x => x.RowKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                WizardGenerationTrackingDto dto = new WizardGenerationTrackingDto
                {
                    SchemaVersion = 1,
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o"),
                    Rows = normalized
                };
                string payload = Serialize(dto);

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

                using (Transaction tx = new Transaction(doc, "CadToRevit Save Generation Tracking"))
                {
                    tx.Start();
                    write();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[TrackingStore] Save failed: " + ex.Message);
            }
        }

        public static string BuildRowKey(string rawLayer, MapCategory category, ElementId levelId, ElementId dwgId)
        {
            return WizardIdempotencyStoreService.BuildRowKey(rawLayer, category, levelId, dwgId);
        }

        public static string NormalizeRowKey(string rowKey)
        {
            if (string.IsNullOrWhiteSpace(rowKey))
            {
                return string.Empty;
            }

            string[] parts = rowKey.Trim().Split('|');
            // New format: Layer|L{levelId}|D{dwgId}
            if (parts.Length == 3)
            {
                return string.Join("|", parts);
            }

            // Legacy format: Layer|Category|L{levelId}|D{dwgId}
            if (parts.Length == 4 && parts[2].StartsWith("L", StringComparison.OrdinalIgnoreCase) && parts[3].StartsWith("D", StringComparison.OrdinalIgnoreCase))
            {
                return parts[0] + "|" + parts[2] + "|" + parts[3];
            }

            return rowKey.Trim();
        }

        public static string BuildStableRowKeyForRecord(WizardGenerationRowRecord record)
        {
            if (record == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(record.RawLayerName))
            {
                return BuildRowKey(
                    record.RawLayerName,
                    ParseCategoryOrIgnore(record.Category),
                    new ElementId(record.LevelId),
                    new ElementId(record.DwgId));
            }

            return NormalizeRowKey(record.RowKey);
        }

        public static string BuildMappingFingerprint(MapRow mapRow)
        {
            if (mapRow == null)
            {
                return string.Empty;
            }

            AdvancedSettingsRow s = mapRow.Settings ?? new AdvancedSettingsRow();
            JunctureSettings juncture = s.Juncture ?? new JunctureSettings();
            List<string> pairs = new List<string>();

            // Keep field order fixed and normalize all values with InvariantCulture.
            AddPair(pairs, "RawLayerName", NormString(mapRow.RawLayerName));
            AddPair(pairs, "Category", mapRow.Category.ToString());
            AddPair(pairs, "RevitTypeName", NormString(mapRow.RevitTypeName));
            AddPair(pairs, "ExpectedWidthMm", NormDouble(mapRow.ExpectedWidthMm));

            AddPair(pairs, "EnableLayerOverride", NormBool(s.EnableLayerOverride));
            AddPair(pairs, "ApplyAsCategoryDefault", NormBool(s.ApplyAsCategoryDefault));
            AddPair(pairs, "DoorExpectedWidthMm", NormDouble(s.DoorExpectedWidthMm));
            AddPair(pairs, "MinDoorWidthMm", NormDouble(s.MinDoorWidthMm));
            AddPair(pairs, "MaxDoorWidthMm", NormDouble(s.MaxDoorWidthMm));
            AddPair(pairs, "DoorWallMatchTolMm", NormDouble(s.DoorWallMatchTolMm));
            AddPair(pairs, "UseFixedDoorWidth", NormNullableBool(s.UseFixedDoorWidth));
            AddPair(pairs, "PreferGeometryOpeningWidth", NormNullableBool(s.PreferGeometryOpeningWidth));
            AddPair(pairs, "WallMinWallLengthMm", NormDouble(s.WallMinWallLengthMm));
            AddPair(pairs, "WallThicknessTolMm", NormDouble(s.WallThicknessTolMm));
            AddPair(pairs, "WallMaxWallThicknessMm", NormDouble(s.WallMaxWallThicknessMm));
            AddPair(pairs, "WallDefaultSingleWallThicknessMm", NormDouble(s.WallDefaultSingleWallThicknessMm));
            AddPair(pairs, "WallParallelAngleTolDeg", NormDouble(s.WallParallelAngleTolDeg));
            AddPair(pairs, "WallEndpointMergeTolMm", NormDouble(s.WallEndpointMergeTolMm));
            AddPair(pairs, "WallArcThicknessTolMm", NormDouble(s.WallArcThicknessTolMm));
            AddPair(pairs, "WallHeightMm", NormDouble(s.WallHeightMm));
            AddPair(pairs, "WallBaseOffsetMm", NormDouble(s.WallBaseOffsetMm));
            AddPair(pairs, "WallEnableExtendCollinear", NormNullableBool(s.WallEnableExtendCollinear));
            AddPair(pairs, "WallEnableMergeCollinear", NormNullableBool(s.WallEnableMergeCollinear));
            AddPair(pairs, "WallExtendCollinearTolMm", NormDouble(s.WallExtendCollinearTolMm));
            AddPair(pairs, "WallEndpointClusterTolMm", NormDouble(s.WallEndpointClusterTolMm));
            AddPair(pairs, "WallExtendSearchTolMm", NormDouble(s.WallExtendSearchTolMm));
            AddPair(pairs, "WallDuplicateTolMm", NormDouble(s.WallDuplicateTolMm));
            AddPair(pairs, "WallAngleSnapDeg", NormDouble(s.WallAngleSnapDeg));
            AddPair(pairs, "WallEnableOrthogonalSnap", NormNullableBool(s.WallEnableOrthogonalSnap));
            AddPair(pairs, "WallEnableExtendToIntersection", NormNullableBool(s.WallEnableExtendToIntersection));
            AddPair(pairs, "WallEnableEndpointClustering", NormNullableBool(s.WallEnableEndpointClustering));
            AddPair(pairs, "WallEnableDuplicateRemoval", NormNullableBool(s.WallEnableDuplicateRemoval));
            AddPair(pairs, "WallCollinearOffsetTolMm", NormDouble(s.WallCollinearOffsetTolMm));
            AddPair(pairs, "WallExtendProjectionTolMm", NormDouble(s.WallExtendProjectionTolMm));
            AddPair(pairs, "WallUseDirectionalClustering", NormNullableBool(s.WallUseDirectionalClustering));
            AddPair(pairs, "WallEnableAutoDoubleLineThickness", NormNullableBool(s.WallEnableAutoDoubleLineThickness));
            AddPair(pairs, "WallAutoThicknessTopK", NormNullableInt(s.WallAutoThicknessTopK));
            AddPair(pairs, "WallAutoThicknessBinMm", NormDouble(s.WallAutoThicknessBinMm));
            AddPair(pairs, "WallMinDoubleLineThicknessMm", NormDouble(s.WallMinDoubleLineThicknessMm));
            AddPair(pairs, "WallMinDoubleLineOverlapLenMm", NormDouble(s.WallMinDoubleLineOverlapLenMm));
            AddPair(pairs, "WallForceSingleLineMode", NormNullableBool(s.WallForceSingleLineMode));
            AddPair(pairs, "WallDoubleLineSingleWallPlaceMode", NormString(s.WallDoubleLineSingleWallPlaceMode));
            AddPair(pairs, "WallDoubleLineLengthPolicy", NormString(s.WallDoubleLineLengthPolicy));
            AddPair(pairs, "WallDoubleLineAdaptiveContainTolMm", NormDouble(s.WallDoubleLineAdaptiveContainTolMm));
            AddPair(pairs, "WallDoubleLineAdaptiveExtendMaxMm", NormDouble(s.WallDoubleLineAdaptiveExtendMaxMm));
            AddPair(pairs, "DoorHeightMm", NormDouble(s.DoorHeightMm));
            AddPair(pairs, "DoorSillHeightMm", NormDouble(s.DoorSillHeightMm));
            AddPair(pairs, "DoorPreferHeadHeight", NormNullableBool(s.DoorPreferHeadHeight));
            AddPair(pairs, "BeamMinLengthMm", NormDouble(s.BeamMinLengthMm));
            AddPair(pairs, "BeamElevationOffsetMm", NormDouble(s.BeamElevationOffsetMm));
            AddPair(pairs, "BeamEnableMergeCollinear", NormNullableBool(s.BeamEnableMergeCollinear));
            AddPair(pairs, "BeamEndpointMergeTolMm", NormDouble(s.BeamEndpointMergeTolMm));
            AddPair(pairs, "BeamParallelAngleTolDeg", NormDouble(s.BeamParallelAngleTolDeg));
            AddPair(pairs, "BeamAllowArc", NormNullableBool(s.BeamAllowArc));
            AddPair(pairs, "WindowHeightMm", NormDouble(s.WindowHeightMm));
            AddPair(pairs, "WindowSillHeightMm", NormDouble(s.WindowSillHeightMm));
            AddPair(pairs, "WindowUseSillPlusHeight", NormNullableBool(s.WindowUseSillPlusHeight));
            AddPair(pairs, "ColumnHeightMm", NormDouble(s.ColumnHeightMm));
            AddPair(pairs, "ColumnClusterAlgorithm", NormString(s.ColumnClusterAlgorithm));
            AddPair(pairs, "ColumnClusterTolMm", NormDouble(s.ColumnClusterTolMm));
            AddPair(pairs, "ColumnEndpointTolMm", NormDouble(s.ColumnEndpointTolMm));
            AddPair(pairs, "ColumnGapTolMm", NormDouble(s.ColumnGapTolMm));
            AddPair(pairs, "ColumnMinGroupSegments", NormNullableInt(s.ColumnMinGroupSegments));
            AddPair(pairs, "ColumnMinSizeMm", NormDouble(s.ColumnMinSizeMm));
            AddPair(pairs, "ColumnMaxSizeMm", NormDouble(s.ColumnMaxSizeMm));
            AddPair(pairs, "ColumnMinAreaM2", NormDouble(s.ColumnMinAreaM2));
            AddPair(pairs, "ColumnMaxAspectRatio", NormDouble(s.ColumnMaxAspectRatio));
            AddPair(pairs, "ColumnMinFillRatio", NormDouble(s.ColumnMinFillRatio));
            AddPair(pairs, "ColumnEnableLongLineFilter", NormNullableBool(s.ColumnEnableLongLineFilter));
            AddPair(pairs, "ColumnMaxSegmentLengthMm", NormDouble(s.ColumnMaxSegmentLengthMm));
            AddPair(pairs, "ColumnEnableMerge", NormNullableBool(s.ColumnEnableMerge));
            AddPair(pairs, "ColumnMergeTolMm", NormDouble(s.ColumnMergeTolMm));
            AddPair(pairs, "ColumnMergeStrategy", NormString(s.ColumnMergeStrategy));
            AddPair(pairs, "ColumnDedupePlacedTolMm", NormDouble(s.ColumnDedupePlacedTolMm));
            AddPair(pairs, "ColumnAreaWeight", NormDouble(s.ColumnAreaWeight));
            AddPair(pairs, "ColumnSegmentCountWeight", NormDouble(s.ColumnSegmentCountWeight));
            AddPair(pairs, "ColumnRectnessWeight", NormDouble(s.ColumnRectnessWeight));
            AddPair(pairs, "ColumnLongLinePenalty", NormDouble(s.ColumnLongLinePenalty));
            AddPair(pairs, "ColumnAttachToWallEnable", NormNullableBool(s.ColumnAttachToWallEnable));
            AddPair(pairs, "ColumnAttachToWallSnapTolMm", NormDouble(s.ColumnAttachToWallSnapTolMm));
            AddPair(pairs, "ColumnAttachToWallTarget", NormString(s.ColumnAttachToWallTarget));
            AddPair(pairs, "ColumnAttachToWallAllowOverlap", NormNullableBool(s.ColumnAttachToWallAllowOverlap));
            AddPair(pairs, "ColumnDebugDrawCandidates", NormNullableBool(s.ColumnDebugDrawCandidates));
            AddPair(pairs, "ColumnDebugDrawClusterId", NormNullableBool(s.ColumnDebugDrawClusterId));
            AddPair(pairs, "ColumnDebugDrawRejectReason", NormNullableBool(s.ColumnDebugDrawRejectReason));
            AddPair(pairs, "ColumnDebugExportReport", NormNullableBool(s.ColumnDebugExportReport));
            AddPair(pairs, "ColumnIrregularEnable", NormNullableBool(s.ColumnIrregularEnable));
            AddPair(pairs, "ColumnIrregularMaxSizeMm", NormDouble(s.ColumnIrregularMaxSizeMm));
            AddPair(pairs, "ColumnIrregularGapTolMm", NormDouble(s.ColumnIrregularGapTolMm));
            AddPair(pairs, "ColumnIrregularMinAreaM2", NormDouble(s.ColumnIrregularMinAreaM2));

            AddPair(pairs, "Juncture.IgnoreSmallerThanMm", NormDouble(juncture.IgnoreSmallerThanMm));
            AddPair(pairs, "Juncture.MinJunctureWidthMm", NormDouble(juncture.MinJunctureWidthMm));
            AddPair(pairs, "Juncture.IgnoreLargerThanMm", NormDouble(juncture.IgnoreLargerThanMm));
            AddPair(pairs, "Juncture.MaxJunctureWidthMm", NormDouble(juncture.MaxJunctureWidthMm));

            IEnumerable<ParameterMapping> mappings = (s.ParameterMappings ?? new List<ParameterMapping>())
                .Where(x => x != null)
                .OrderBy(x => NormString(x.ParameterName), StringComparer.Ordinal)
                .ThenBy(x => NormString(x.StorageType), StringComparer.Ordinal)
                .ThenBy(x => x.Value == null ? string.Empty : Convert.ToString(x.Value, CultureInfo.InvariantCulture), StringComparer.Ordinal);
            int mappingIndex = 0;
            foreach (ParameterMapping mapping in mappings)
            {
                AddPair(pairs, "ParameterMappings[" + mappingIndex + "].ParameterName", NormString(mapping.ParameterName));
                AddPair(pairs, "ParameterMappings[" + mappingIndex + "].StorageType", NormString(mapping.StorageType));
                AddPair(pairs, "ParameterMappings[" + mappingIndex + "].Value", mapping.Value == null ? "~null" : Convert.ToString(mapping.Value, CultureInfo.InvariantCulture));
                mappingIndex++;
            }

            string raw = string.Join("|", pairs);

            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(raw);
                byte[] hash = sha.ComputeHash(bytes);
                return string.Concat(hash.Select(x => x.ToString("x2")));
            }
        }

        private static MapCategory ParseCategoryOrIgnore(string category)
        {
            MapCategory parsed;
            if (Enum.TryParse(category ?? string.Empty, true, out parsed))
            {
                return parsed;
            }

            return MapCategory.Ignore;
        }

        private static void AddPair(List<string> pairs, string key, string value)
        {
            pairs.Add((key ?? string.Empty) + "=" + (value ?? "~null"));
        }

        private static string NormString(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "~null" : value.Trim();
        }

        private static string NormBool(bool value)
        {
            return value ? "1" : "0";
        }

        private static string NormNullableBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "1" : "0") : "~null";
        }

        private static string NormDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("G17", CultureInfo.InvariantCulture) : "~null";
        }

        private static string NormDouble(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string NormNullableInt(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "~null";
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

        private static string Serialize(WizardGenerationTrackingDto dto)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WizardGenerationTrackingDto));
                serializer.WriteObject(ms, dto ?? new WizardGenerationTrackingDto());
                ms.Position = 0;
                using (StreamReader reader = new StreamReader(ms))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static WizardGenerationTrackingDto Deserialize(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new WizardGenerationTrackingDto();
            }

            try
            {
                using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WizardGenerationTrackingDto));
                    return serializer.ReadObject(ms) as WizardGenerationTrackingDto;
                }
            }
            catch
            {
                return new WizardGenerationTrackingDto();
            }
        }
    }
}
