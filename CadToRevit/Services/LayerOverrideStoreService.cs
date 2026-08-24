using Autodesk.Revit.DB;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Settings;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services
{
    /// <summary>
    /// 閸ユ儳鐪扮憰鍡欐磰鐎涙ê鍋嶉弫鐗堝祦濡€崇€烽敍姘瘶閸氼偅瀵滈崶鎯х湴鐟曞棛娲婃稉搴㈠瘻缁鍩嗘妯款吇娑撱倝鍎撮崚鍡愨�?    /// </summary>
    public sealed class LayerOverrideStoreData
    {
        public Dictionary<string, AdvancedSettingsRow> LayerOverrides { get; set; } =
            new Dictionary<string, AdvancedSettingsRow>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<MapCategory, AdvancedSettingsRow> CategoryDefaults { get; set; } =
            new Dictionary<MapCategory, AdvancedSettingsRow>();

        public RoomRecognitionSettings RoomRecognitionSettings { get; set; } =
            RoomRecognitionSettings.CreateDefault();

        public GlobalGenerationSettings GlobalGenerationSettings { get; set; } =
            GlobalGenerationSettings.CreateDefault();

        public string LoadSource { get; set; }
    }

    /// <summary>
    /// 閸ユ儳鐪扮憰鍡欐磰閹镐椒绠欓崠鏍ㄦ箛閸斺槄绱扮拹鐔荤�?RVT閵嗕竸ppData閵嗕礁顕遍崗銉ヮ嚤閸戣櫣娈戠拠璇插晸娑撳氦绺肩粔姹団�?    /// </summary>
    public static class LayerOverrideStoreService
    {
        /// <summary>
        /// 閸旂姾娴囩憰鍡欐磰闁板秶鐤嗛敍鍫滅喘�?RVT閿涘苯鍙惧▎?AppData閿涘苯鍟€鐏忔繆鐦弮褏澧楁潻浣盒╅敍澶堚偓?        /// </summary>
        public static LayerOverrideStoreData Load(Document doc = null)
        {
            LayerOverrideStoreData data = TryLoadFromRvt(doc);
            if (HasAnyData(data))
            {
                data.LoadSource = "RVT";
                return data;
            }

            data = TryLoadFromPath(GetStorePath());
            if (HasAnyData(data))
            {
                data.LoadSource = "AppData";
                return data;
            }

            LayerOverrideStoreData migrated = TryMigrateLegacyOverrides();
            if (HasAnyData(migrated))
            {
                SaveData(doc, migrated);
                migrated.LoadSource = "LegacyMigrated";
                return migrated;
            }

            return CreateEmpty("None");
        }

        /// <summary>
        /// 鐏忓棗缍嬮崜宥嗘Ё鐏忓嫯顢戞穱婵嗙摠娑撻缚顩惄鏍帳缂冾喓鈧?        /// </summary>
        public static void Save(Document doc, IEnumerable<MapRow> rows)
        {
            RoomRecognitionSettings roomRecognitionSettings = null;
            GlobalGenerationSettings globalGenerationSettings = null;
            try
            {
                LayerOverrideStoreData existing = Load(doc);
                roomRecognitionSettings = existing.RoomRecognitionSettings;
                globalGenerationSettings = existing.GlobalGenerationSettings;
            }
            catch
            {
                roomRecognitionSettings = RoomRecognitionSettings.CreateDefault();
                globalGenerationSettings = GlobalGenerationSettings.CreateDefault();
            }

            LayerOverrideStoreData data = BuildFromRows(rows, roomRecognitionSettings, globalGenerationSettings);
            SaveData(doc, data);
        }

        /// <summary>
        /// Saves layer overrides together with global room-recognition settings.
        /// </summary>
        public static void Save(Document doc, IEnumerable<MapRow> rows, RoomRecognitionSettings roomRecognitionSettings)
        {
            GlobalGenerationSettings globalGenerationSettings = null;
            try
            {
                globalGenerationSettings = Load(doc).GlobalGenerationSettings;
            }
            catch
            {
                globalGenerationSettings = GlobalGenerationSettings.CreateDefault();
            }

            LayerOverrideStoreData data = BuildFromRows(rows, roomRecognitionSettings, globalGenerationSettings);
            SaveData(doc, data);
        }

        /// <summary>
        /// Saves layer overrides together with shared project-level settings.
        /// </summary>
        public static void Save(Document doc, IEnumerable<MapRow> rows, RoomRecognitionSettings roomRecognitionSettings, GlobalGenerationSettings globalGenerationSettings)
        {
            // This overload is used by CAD Import Wizard when saving layer mappings before
            // Gen Elements / Regenerate. The wizard does not own the door generation mode,
            // so preserve the value saved by Global Settings instead of overwriting it with
            // GlobalGenerationSettings' default value.
            GlobalGenerationSettings preservedGlobalSettings = PreserveGlobalSettingsForLayerMappingSave(doc, globalGenerationSettings);
            LayerOverrideStoreData data = BuildFromRows(rows, roomRecognitionSettings, preservedGlobalSettings);
            SaveData(doc, data);
        }

        /// <summary>
        /// Saves shared project-level settings without modifying layer mappings.
        /// </summary>
        public static void SaveGlobalSettings(Document doc, RoomRecognitionSettings roomRecognitionSettings, GlobalGenerationSettings globalGenerationSettings)
        {
            LayerOverrideStoreData existing = Load(doc) ?? CreateEmpty("SaveGlobal");
            LayerOverrideStoreData data = CreateEmpty("SaveGlobal");
            data.LayerOverrides = existing.LayerOverrides ?? new Dictionary<string, AdvancedSettingsRow>(StringComparer.OrdinalIgnoreCase);
            data.CategoryDefaults = existing.CategoryDefaults ?? new Dictionary<MapCategory, AdvancedSettingsRow>();
            data.RoomRecognitionSettings = NormalizeRoomRecognitionSettings(roomRecognitionSettings, globalGenerationSettings);
            data.GlobalGenerationSettings = NormalizeGlobalGenerationSettings(globalGenerationSettings, roomRecognitionSettings);
            SaveData(doc, data);
        }

        /// <summary>
        /// 鐎电厧鍤憰鍡欐磰闁板秶鐤嗘�?JSON 閺傚洣娆㈤妴?        /// </summary>
        public static bool ExportProfile(string filePath, IEnumerable<MapRow> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            try
            {
                LayerOverrideStoreData data = BuildFromRows(rows, null, null);
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                WriteToPath(filePath, data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// �?JSON 閺傚洣娆㈢€电厧鍙嗙€瑰本鏆ｇ憰鍡欐磰闁板秶鐤嗛敍鍫濇禈鐏炲倽顩惄?+ 缁鍩嗘妯款吇閿涘�?        /// </summary>
        public static LayerOverrideStoreData ImportProfileFull(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return CreateEmpty("ImportMissing");
            }

            LayerOverrideStoreData data = TryLoadFromPath(filePath);
            data.LoadSource = "Import";
            return data;
        }

        /// <summary>
        /// �?JSON 閺傚洣娆㈢€电厧鍙嗛崶鎯х湴鐟曞棛娲婄€涙鍚€閵?        /// </summary>
        public static Dictionary<string, AdvancedSettingsRow> ImportProfile(string filePath)
        {
            return ImportProfileFull(filePath).LayerOverrides;
        }

        /// <summary>
        /// 娴犲孩妲х亸鍕攽閺嬪嫬缂撻崣顖涘瘮娑斿懎瀵茬憰鍡欐磰閺佺増宓侀�?        /// </summary>
        private static GlobalGenerationSettings PreserveGlobalSettingsForLayerMappingSave(Document doc, GlobalGenerationSettings incomingSettings)
        {
            GlobalGenerationSettings merged = GlobalGenerationSettings.Clone(incomingSettings);
            try
            {
                LayerOverrideStoreData existing = Load(doc);
                if (existing != null && existing.GlobalGenerationSettings != null)
                {
                    // CreateDoorOpeningOnly is controlled by Global Settings only.
                    // Keep both true and false exactly as saved; do not let generation buttons reset it.
                    merged.CreateDoorOpeningOnly = existing.GlobalGenerationSettings.CreateDoorOpeningOnly;
                }
            }
            catch
            {
                // Keep the incoming settings if the existing store cannot be loaded.
            }

            return merged;
        }

        private static LayerOverrideStoreData BuildFromRows(IEnumerable<MapRow> rows, RoomRecognitionSettings roomRecognitionSettings, GlobalGenerationSettings globalGenerationSettings)
        {
            LayerOverrideStoreData data = CreateEmpty("Build");
            data.GlobalGenerationSettings = NormalizeGlobalGenerationSettings(globalGenerationSettings, roomRecognitionSettings);
            data.RoomRecognitionSettings = NormalizeRoomRecognitionSettings(roomRecognitionSettings, data.GlobalGenerationSettings);
            foreach (MapRow row in rows ?? Enumerable.Empty<MapRow>())
            {
                if (row == null)
                {
                    continue;
                }

                AdvancedSettingsRow settings = row.Settings;
                if (settings == null)
                {
                    continue;
                }

                if (settings.EnableLayerOverride && !string.IsNullOrWhiteSpace(row.RawLayerName))
                {
                    data.LayerOverrides[row.RawLayerName] = CloneSettings(settings);
                }

                if (settings.ApplyAsCategoryDefault)
                {
                    data.CategoryDefaults[row.Category] = CloneSettings(settings);
                }
            }

            return data;
        }

        /// <summary>
        /// 娣囨繂鐡ㄩ弫鐗堝祦閸?RVT �?AppData�?        /// </summary>
        private static void SaveData(Document doc, LayerOverrideStoreData data)
        {
            string payload = Serialize(data);
            TryWriteToRvt(doc, payload);
            WriteToPath(GetStorePath(), data);
        }

        /// <summary>
        /// 鐏忔繆鐦禒?RVT 妞ゅ湱娲扮€涙ê鍋嶇拠璇插絿鐟曞棛娲婇柊宥囩枂�?        /// </summary>
        private static LayerOverrideStoreData TryLoadFromRvt(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            try
            {
                string payload = ProjectSettingsStorageService.Read(doc);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    return null;
                }

                return Deserialize(payload);
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Store] RVT load failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 鐏忔繆鐦禒搴㈠瘹鐎规俺鐭惧鍕嚢閸欐牞顩惄鏍帳缂冾喓�?        /// </summary>
        private static LayerOverrideStoreData TryLoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return CreateEmpty("PathMissing");
            }

            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerOverrideStoreDto));
                    LayerOverrideStoreDto dto = serializer.ReadObject(fs) as LayerOverrideStoreDto;
                    return FromDto(dto);
                }
            }
            catch
            {
                return CreateEmpty("PathInvalid");
            }
        }

        /// <summary>
        /// 鐏忓棜顩惄鏍帳缂冾喖鍟撻崗銉﹀瘹鐎规俺鐭惧鍕瀮娴犺翰�?        /// </summary>
        private static void WriteToPath(string path, LayerOverrideStoreData data)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using (FileStream fs = File.Create(path))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerOverrideStoreDto));
                serializer.WriteObject(fs, ToDto(data));
            }
        }

        /// <summary>
        /// 鐏忓棗绨崚妤€瀵查崘鍛啇閸愭瑥鍙?RVT閿涘牆绻€鐟曚焦妞傚鈧崥顖欑皑閸斺槄绱氶�?        /// </summary>
        private static void TryWriteToRvt(Document doc, string payload)
        {
            if (doc == null)
            {
                return;
            }

            Action writeAction = () => ProjectSettingsStorageService.Write(doc, payload);
            try
            {
                if (doc.IsModifiable)
                {
                    writeAction();
                    return;
                }

                using (Transaction tx = new Transaction(doc, "CadToRevit Save Layer Overrides"))
                {
                    tx.Start();
                    writeAction();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[Store] RVT write failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 鐏忓棜顩惄鏍ㄦ殶閹诡喖绨崚妤€瀵叉�?JSON 鐎涙顑佹稉灞傗�?        /// </summary>
        private static string Serialize(LayerOverrideStoreData data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerOverrideStoreDto));
                serializer.WriteObject(ms, ToDto(data));
                ms.Position = 0;
                using (StreamReader reader = new StreamReader(ms))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// �?JSON 鐎涙顑佹稉鎻掑冀鎼村繐鍨崠鏍﹁礋鐟曞棛娲婇弫鐗堝祦�?        /// </summary>
        private static LayerOverrideStoreData Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateEmpty("DeserializeEmpty");
            }

            using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LayerOverrideStoreDto));
                LayerOverrideStoreDto dto = serializer.ReadObject(ms) as LayerOverrideStoreDto;
                return FromDto(dto);
            }
        }

        /// <summary>
        /// 鏉╂劘顢戦弮鑸垫殶閹诡喛娴嗛幑顫礋 DTO�?        /// </summary>
        private static LayerOverrideStoreDto ToDto(LayerOverrideStoreData data)
        {
            LayerOverrideStoreDto dto = new LayerOverrideStoreDto
            {
                Version = 2,
                UpdatedAt = DateTime.Now.ToString("o")
            };

            foreach (KeyValuePair<string, AdvancedSettingsRow> kv in data?.LayerOverrides ?? new Dictionary<string, AdvancedSettingsRow>())
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null)
                {
                    continue;
                }

                LayerOverrideEntryDto entry = ToEntry(kv.Key, kv.Value);
                dto.Overrides.Add(entry);
            }

            foreach (KeyValuePair<MapCategory, AdvancedSettingsRow> kv in data?.CategoryDefaults ?? new Dictionary<MapCategory, AdvancedSettingsRow>())
            {
                if (kv.Value == null)
                {
                    continue;
                }

                CategoryDefaultEntryDto entry = ToCategoryDefault(kv.Key, kv.Value);
                dto.CategoryDefaults.Add(entry);
            }

            dto.RoomRecognition = ToRoomRecognitionDto(data != null ? data.RoomRecognitionSettings : null);
            dto.GlobalGeneration = ToGlobalGenerationDto(data != null ? data.GlobalGenerationSettings : null);

            return dto;
        }

        /// <summary>
        /// DTO 鏉烆剙娲栨潻鎰攽閺冭埖鏆熼幑顔衡偓?        /// </summary>
        private static LayerOverrideStoreData FromDto(LayerOverrideStoreDto dto)
        {
            LayerOverrideStoreData data = CreateEmpty("Dto");

            foreach (LayerOverrideEntryDto item in dto?.Overrides ?? new List<LayerOverrideEntryDto>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.RawLayerName))
                {
                    continue;
                }

                data.LayerOverrides[item.RawLayerName] = ToAdvancedSettings(item);
            }

            foreach (CategoryDefaultEntryDto item in dto?.CategoryDefaults ?? new List<CategoryDefaultEntryDto>())
            {
                if (item == null)
                {
                    continue;
                }

                MapCategory category;
                if (!Enum.TryParse(item.Category, true, out category))
                {
                    continue;
                }

                data.CategoryDefaults[category] = ToAdvancedSettings(item);
            }

            data.RoomRecognitionSettings = FromRoomRecognitionDto(dto != null ? dto.RoomRecognition : null);
            data.GlobalGenerationSettings = FromGlobalGenerationDto(dto != null ? dto.GlobalGeneration : null, data.RoomRecognitionSettings);

            return data;
        }

        private static RoomRecognitionSettingsDto ToRoomRecognitionDto(RoomRecognitionSettings settings)
        {
            RoomRecognitionSettings source = RoomRecognitionSettings.Clone(settings);
            return new RoomRecognitionSettingsDto
            {
                RoomTextLayerNames = source.RoomTextLayerNames,
                DoorGapMaxMm = source.DoorGapMaxMm,
                SmallGapPatchMaxMm = source.SmallGapPatchMaxMm,
                TargetKeywordsText = source.TargetKeywordsText,
                LiftGeometryLayerNames = source.LiftGeometryLayerNames,
                ModelRecognitionWindowSizeM = source.ModelRecognitionWindowSizeM,
                HeadRoomMm = source.HeadRoomMm
            };
        }

        private static RoomRecognitionSettings FromRoomRecognitionDto(RoomRecognitionSettingsDto dto)
        {
            if (dto == null)
            {
                return RoomRecognitionSettings.CreateDefault();
            }

            return RoomRecognitionSettings.Clone(new RoomRecognitionSettings
            {
                RoomTextLayerNames = dto.RoomTextLayerNames,
                DoorGapMaxMm = dto.DoorGapMaxMm,
                SmallGapPatchMaxMm = dto.SmallGapPatchMaxMm,
                TargetKeywordsText = dto.TargetKeywordsText,
                LiftGeometryLayerNames = dto.LiftGeometryLayerNames,
                ModelRecognitionWindowSizeM = dto.ModelRecognitionWindowSizeM,
                HeadRoomMm = dto.HeadRoomMm
            });
        }

        private static GlobalGenerationSettingsDto ToGlobalGenerationDto(GlobalGenerationSettings settings)
        {
            GlobalGenerationSettings source = GlobalGenerationSettings.Clone(settings);
            return new GlobalGenerationSettingsDto
            {
                SafeModeEnabled = source.SafeModeEnabled,
                AutoJoinWallsAfterCreate = source.AutoJoinWallsAfterCreate,
                HeadRoomMm = source.HeadRoomMm,
                UseGlobalWallHeightOverride = source.UseGlobalWallHeightOverride,
                GlobalWallHeightMm = source.GlobalWallHeightMm,
                UseGlobalDoorHeightOverride = source.UseGlobalDoorHeightOverride,
                GlobalDoorHeightMm = source.GlobalDoorHeightMm,
                UseGlobalDoorSillHeightOverride = source.UseGlobalDoorSillHeightOverride,
                GlobalDoorSillHeightMm = source.GlobalDoorSillHeightMm,
                CreateDoorOpeningOnly = source.CreateDoorOpeningOnly
            };
        }

        private static GlobalGenerationSettings FromGlobalGenerationDto(GlobalGenerationSettingsDto dto, RoomRecognitionSettings roomRecognitionSettings)
        {
            if (dto == null)
            {
                return NormalizeGlobalGenerationSettings(null, roomRecognitionSettings);
            }

            return NormalizeGlobalGenerationSettings(new GlobalGenerationSettings
            {
                SafeModeEnabled = dto.SafeModeEnabled,
                AutoJoinWallsAfterCreate = dto.AutoJoinWallsAfterCreate,
                HeadRoomMm = dto.HeadRoomMm,
                UseGlobalWallHeightOverride = dto.UseGlobalWallHeightOverride,
                GlobalWallHeightMm = dto.GlobalWallHeightMm,
                UseGlobalDoorHeightOverride = dto.UseGlobalDoorHeightOverride,
                GlobalDoorHeightMm = dto.GlobalDoorHeightMm,
                UseGlobalDoorSillHeightOverride = dto.UseGlobalDoorSillHeightOverride,
                GlobalDoorSillHeightMm = dto.GlobalDoorSillHeightMm,
                CreateDoorOpeningOnly = dto.CreateDoorOpeningOnly.HasValue
                    ? dto.CreateDoorOpeningOnly.Value
                    : GlobalGenerationSettings.CreateDefault().CreateDoorOpeningOnly
            }, roomRecognitionSettings);
        }

        /// <summary>
        /// 閺嬪嫬缂撶猾璇插焼姒涙�?DTO 閺夛紕娲伴妴?        /// </summary>
        private static CategoryDefaultEntryDto ToCategoryDefault(MapCategory category, AdvancedSettingsRow settings)
        {
            return new CategoryDefaultEntryDto
            {
                Category = category.ToString(),
                EnableLayerOverride = settings.EnableLayerOverride,
                ApplyAsCategoryDefault = settings.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = settings.DoorExpectedWidthMm,
                MinDoorWidthMm = settings.MinDoorWidthMm,
                MaxDoorWidthMm = settings.MaxDoorWidthMm,
                DoorWallMatchTolMm = settings.DoorWallMatchTolMm,
                WallMinWallLengthMm = settings.WallMinWallLengthMm,
                WallThicknessTolMm = settings.WallThicknessTolMm,
                WallMaxWallThicknessMm = settings.WallMaxWallThicknessMm,
                WallDefaultSingleWallThicknessMm = settings.WallDefaultSingleWallThicknessMm,
                WallParallelAngleTolDeg = settings.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = settings.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = settings.WallArcThicknessTolMm,
                WallHeightMm = settings.WallHeightMm,
                WallBaseOffsetMm = settings.WallBaseOffsetMm,
                WallEndpointClusterTolMm = settings.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = settings.WallExtendSearchTolMm,
                WallDuplicateTolMm = settings.WallDuplicateTolMm,
                WallAngleSnapDeg = settings.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = settings.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = settings.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = settings.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = settings.WallEnableDuplicateRemoval,
                WallEnableExtendCollinear = settings.WallEnableExtendCollinear,
                WallEnableMergeCollinear = settings.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = settings.WallExtendCollinearTolMm,
                WallCollinearOffsetTolMm = settings.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = settings.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = settings.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = settings.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = settings.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = settings.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = settings.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = settings.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = settings.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = settings.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = settings.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = settings.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = settings.WallDoubleLineAdaptiveExtendMaxMm,
                DoorHeightMm = settings.DoorHeightMm,
                DoorSillHeightMm = settings.DoorSillHeightMm,
                UseFixedDoorWidth = settings.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = settings.PreferGeometryOpeningWidth,
                BeamMinLengthMm = settings.BeamMinLengthMm,
                BeamElevationOffsetMm = settings.BeamElevationOffsetMm,
                BeamEnableMergeCollinear = settings.BeamEnableMergeCollinear,
                BeamEndpointMergeTolMm = settings.BeamEndpointMergeTolMm,
                BeamParallelAngleTolDeg = settings.BeamParallelAngleTolDeg,
                BeamAllowArc = settings.BeamAllowArc,
                WindowHeightMm = settings.WindowHeightMm,
                WindowSillHeightMm = settings.WindowSillHeightMm,
                WindowUseSillPlusHeight = settings.WindowUseSillPlusHeight,
                ColumnHeightMm = settings.ColumnHeightMm,
                ColumnClusterAlgorithm = settings.ColumnClusterAlgorithm,
                ColumnClusterTolMm = settings.ColumnClusterTolMm,
                ColumnEndpointTolMm = settings.ColumnEndpointTolMm,
                ColumnGapTolMm = settings.ColumnGapTolMm,
                ColumnMinGroupSegments = settings.ColumnMinGroupSegments,
                ColumnMinSizeMm = settings.ColumnMinSizeMm,
                ColumnMaxSizeMm = settings.ColumnMaxSizeMm,
                ColumnMinAreaM2 = settings.ColumnMinAreaM2,
                ColumnMaxAspectRatio = settings.ColumnMaxAspectRatio,
                ColumnMinFillRatio = settings.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = settings.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = settings.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = settings.ColumnEnableMerge,
                ColumnMergeTolMm = settings.ColumnMergeTolMm,
                ColumnMergeStrategy = settings.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = settings.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = settings.ColumnAreaWeight,
                ColumnSegmentCountWeight = settings.ColumnSegmentCountWeight,
                ColumnRectnessWeight = settings.ColumnRectnessWeight,
                ColumnLongLinePenalty = settings.ColumnLongLinePenalty,
                ColumnIrregularEnable = settings.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = settings.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = settings.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = settings.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = settings.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = settings.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = settings.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = settings.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = settings.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = settings.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = settings.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = settings.ColumnDebugExportReport,
                IgnoreSmallerThanMm = settings.Juncture?.IgnoreSmallerThanMm ?? 0.0,
                MinJunctureWidthMm = settings.Juncture?.MinJunctureWidthMm ?? 0.0,
                IgnoreLargerThanMm = settings.Juncture?.IgnoreLargerThanMm ?? 0.0,
                MaxJunctureWidthMm = settings.Juncture?.MaxJunctureWidthMm ?? 0.0,
                ParameterMappings = ToMappings(settings.ParameterMappings)
            };
        }

        /// <summary>
        /// 閺嬪嫬缂撻崶鎯х湴鐟曞棛娲?DTO 閺夛紕娲伴妴?        /// </summary>
        private static LayerOverrideEntryDto ToEntry(string layer, AdvancedSettingsRow settings)
        {
            return new LayerOverrideEntryDto
            {
                RawLayerName = layer,
                EnableLayerOverride = settings.EnableLayerOverride,
                ApplyAsCategoryDefault = settings.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = settings.DoorExpectedWidthMm,
                MinDoorWidthMm = settings.MinDoorWidthMm,
                MaxDoorWidthMm = settings.MaxDoorWidthMm,
                DoorWallMatchTolMm = settings.DoorWallMatchTolMm,
                WallMinWallLengthMm = settings.WallMinWallLengthMm,
                WallThicknessTolMm = settings.WallThicknessTolMm,
                WallMaxWallThicknessMm = settings.WallMaxWallThicknessMm,
                WallDefaultSingleWallThicknessMm = settings.WallDefaultSingleWallThicknessMm,
                WallParallelAngleTolDeg = settings.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = settings.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = settings.WallArcThicknessTolMm,
                WallHeightMm = settings.WallHeightMm,
                WallBaseOffsetMm = settings.WallBaseOffsetMm,
                WallEndpointClusterTolMm = settings.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = settings.WallExtendSearchTolMm,
                WallDuplicateTolMm = settings.WallDuplicateTolMm,
                WallAngleSnapDeg = settings.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = settings.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = settings.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = settings.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = settings.WallEnableDuplicateRemoval,
                WallEnableExtendCollinear = settings.WallEnableExtendCollinear,
                WallEnableMergeCollinear = settings.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = settings.WallExtendCollinearTolMm,
                WallCollinearOffsetTolMm = settings.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = settings.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = settings.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = settings.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = settings.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = settings.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = settings.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = settings.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = settings.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = settings.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = settings.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = settings.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = settings.WallDoubleLineAdaptiveExtendMaxMm,
                DoorHeightMm = settings.DoorHeightMm,
                DoorSillHeightMm = settings.DoorSillHeightMm,
                UseFixedDoorWidth = settings.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = settings.PreferGeometryOpeningWidth,
                BeamMinLengthMm = settings.BeamMinLengthMm,
                BeamElevationOffsetMm = settings.BeamElevationOffsetMm,
                BeamEnableMergeCollinear = settings.BeamEnableMergeCollinear,
                BeamEndpointMergeTolMm = settings.BeamEndpointMergeTolMm,
                BeamParallelAngleTolDeg = settings.BeamParallelAngleTolDeg,
                BeamAllowArc = settings.BeamAllowArc,
                WindowHeightMm = settings.WindowHeightMm,
                WindowSillHeightMm = settings.WindowSillHeightMm,
                WindowUseSillPlusHeight = settings.WindowUseSillPlusHeight,
                ColumnHeightMm = settings.ColumnHeightMm,
                ColumnClusterAlgorithm = settings.ColumnClusterAlgorithm,
                ColumnClusterTolMm = settings.ColumnClusterTolMm,
                ColumnEndpointTolMm = settings.ColumnEndpointTolMm,
                ColumnGapTolMm = settings.ColumnGapTolMm,
                ColumnMinGroupSegments = settings.ColumnMinGroupSegments,
                ColumnMinSizeMm = settings.ColumnMinSizeMm,
                ColumnMaxSizeMm = settings.ColumnMaxSizeMm,
                ColumnMinAreaM2 = settings.ColumnMinAreaM2,
                ColumnMaxAspectRatio = settings.ColumnMaxAspectRatio,
                ColumnMinFillRatio = settings.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = settings.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = settings.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = settings.ColumnEnableMerge,
                ColumnMergeTolMm = settings.ColumnMergeTolMm,
                ColumnMergeStrategy = settings.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = settings.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = settings.ColumnAreaWeight,
                ColumnSegmentCountWeight = settings.ColumnSegmentCountWeight,
                ColumnRectnessWeight = settings.ColumnRectnessWeight,
                ColumnLongLinePenalty = settings.ColumnLongLinePenalty,
                ColumnIrregularEnable = settings.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = settings.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = settings.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = settings.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = settings.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = settings.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = settings.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = settings.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = settings.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = settings.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = settings.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = settings.ColumnDebugExportReport,
                IgnoreSmallerThanMm = settings.Juncture?.IgnoreSmallerThanMm ?? 0.0,
                MinJunctureWidthMm = settings.Juncture?.MinJunctureWidthMm ?? 0.0,
                IgnoreLargerThanMm = settings.Juncture?.IgnoreLargerThanMm ?? 0.0,
                MaxJunctureWidthMm = settings.Juncture?.MaxJunctureWidthMm ?? 0.0,
                ParameterMappings = ToMappings(settings.ParameterMappings)
            };
        }

        /// <summary>
        /// 閸欏倹鏆熼弰鐘茬殸閸掓銆冩潪顒佸床�?DTO 閸掓銆冮妴?        /// </summary>
        private static List<ParameterMappingDto> ToMappings(List<ParameterMapping> source)
        {
            return (source ?? new List<ParameterMapping>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ParameterName))
                .Select(x => new ParameterMappingDto
                {
                    ParameterName = x.ParameterName,
                    StorageType = x.StorageType,
                    Value = x.Value == null ? null : x.Value.ToString()
                })
                .ToList();
        }

        /// <summary>
        /// 閸ユ儳鐪扮憰鍡欐�?DTO 鏉烆剟鐝痪褑顔曠純顔碱嚠鐠灺扳�?        /// </summary>
        private static AdvancedSettingsRow ToAdvancedSettings(LayerOverrideEntryDto item)
        {
            AdvancedSettingsRow settings = new AdvancedSettingsRow
            {
                EnableLayerOverride = item.EnableLayerOverride,
                ApplyAsCategoryDefault = item.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = item.DoorExpectedWidthMm,
                MinDoorWidthMm = item.MinDoorWidthMm,
                MaxDoorWidthMm = item.MaxDoorWidthMm,
                DoorWallMatchTolMm = item.DoorWallMatchTolMm,
                WallMinWallLengthMm = item.WallMinWallLengthMm,
                WallThicknessTolMm = item.WallThicknessTolMm,
                WallMaxWallThicknessMm = item.WallMaxWallThicknessMm,
                WallDefaultSingleWallThicknessMm = item.WallDefaultSingleWallThicknessMm,
                WallParallelAngleTolDeg = item.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = item.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = item.WallArcThicknessTolMm,
                WallHeightMm = item.WallHeightMm,
                WallBaseOffsetMm = item.WallBaseOffsetMm,
                WallEndpointClusterTolMm = item.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = item.WallExtendSearchTolMm,
                WallDuplicateTolMm = item.WallDuplicateTolMm,
                WallAngleSnapDeg = item.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = item.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = item.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = item.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = item.WallEnableDuplicateRemoval,
                WallEnableExtendCollinear = item.WallEnableExtendCollinear,
                WallEnableMergeCollinear = item.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = item.WallExtendCollinearTolMm,
                WallCollinearOffsetTolMm = item.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = item.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = item.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = item.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = item.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = item.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = item.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = item.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = item.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = item.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = item.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = item.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = item.WallDoubleLineAdaptiveExtendMaxMm,
                DoorHeightMm = item.DoorHeightMm,
                DoorSillHeightMm = item.DoorSillHeightMm,
                UseFixedDoorWidth = item.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = item.PreferGeometryOpeningWidth,
                BeamMinLengthMm = item.BeamMinLengthMm,
                BeamElevationOffsetMm = item.BeamElevationOffsetMm,
                BeamEnableMergeCollinear = item.BeamEnableMergeCollinear,
                BeamEndpointMergeTolMm = item.BeamEndpointMergeTolMm,
                BeamParallelAngleTolDeg = item.BeamParallelAngleTolDeg,
                BeamAllowArc = item.BeamAllowArc,
                WindowHeightMm = item.WindowHeightMm,
                WindowSillHeightMm = item.WindowSillHeightMm,
                WindowUseSillPlusHeight = item.WindowUseSillPlusHeight,
                ColumnHeightMm = item.ColumnHeightMm,
                ColumnClusterAlgorithm = item.ColumnClusterAlgorithm,
                ColumnClusterTolMm = item.ColumnClusterTolMm,
                ColumnEndpointTolMm = item.ColumnEndpointTolMm,
                ColumnGapTolMm = item.ColumnGapTolMm,
                ColumnMinGroupSegments = item.ColumnMinGroupSegments,
                ColumnMinSizeMm = item.ColumnMinSizeMm,
                ColumnMaxSizeMm = item.ColumnMaxSizeMm,
                ColumnMinAreaM2 = item.ColumnMinAreaM2,
                ColumnMaxAspectRatio = item.ColumnMaxAspectRatio,
                ColumnMinFillRatio = item.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = item.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = item.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = item.ColumnEnableMerge,
                ColumnMergeTolMm = item.ColumnMergeTolMm,
                ColumnMergeStrategy = item.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = item.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = item.ColumnAreaWeight,
                ColumnSegmentCountWeight = item.ColumnSegmentCountWeight,
                ColumnRectnessWeight = item.ColumnRectnessWeight,
                ColumnLongLinePenalty = item.ColumnLongLinePenalty,
                ColumnIrregularEnable = item.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = item.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = item.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = item.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = item.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = item.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = item.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = item.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = item.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = item.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = item.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = item.ColumnDebugExportReport,
                Juncture = new JunctureSettings
                {
                    IgnoreSmallerThanMm = item.IgnoreSmallerThanMm,
                    MinJunctureWidthMm = item.MinJunctureWidthMm,
                    IgnoreLargerThanMm = item.IgnoreLargerThanMm,
                    MaxJunctureWidthMm = item.MaxJunctureWidthMm
                }
            };

            foreach (ParameterMappingDto m in item.ParameterMappings ?? new List<ParameterMappingDto>())
            {
                if (m == null || string.IsNullOrWhiteSpace(m.ParameterName))
                {
                    continue;
                }

                settings.ParameterMappings.Add(new ParameterMapping
                {
                    ParameterName = m.ParameterName,
                    StorageType = m.StorageType,
                    Value = m.Value
                });
            }

            return settings;
        }

        /// <summary>
        /// 缁鍩嗘妯款�?DTO 鏉烆剟鐝痪褑顔曠純顔碱嚠鐠灺扳�?        /// </summary>
        private static AdvancedSettingsRow ToAdvancedSettings(CategoryDefaultEntryDto item)
        {
            return ToAdvancedSettings(new LayerOverrideEntryDto
            {
                EnableLayerOverride = item.EnableLayerOverride,
                ApplyAsCategoryDefault = item.ApplyAsCategoryDefault,
                DoorExpectedWidthMm = item.DoorExpectedWidthMm,
                MinDoorWidthMm = item.MinDoorWidthMm,
                MaxDoorWidthMm = item.MaxDoorWidthMm,
                DoorWallMatchTolMm = item.DoorWallMatchTolMm,
                WallMinWallLengthMm = item.WallMinWallLengthMm,
                WallThicknessTolMm = item.WallThicknessTolMm,
                WallMaxWallThicknessMm = item.WallMaxWallThicknessMm,
                WallDefaultSingleWallThicknessMm = item.WallDefaultSingleWallThicknessMm,
                WallParallelAngleTolDeg = item.WallParallelAngleTolDeg,
                WallEndpointMergeTolMm = item.WallEndpointMergeTolMm,
                WallArcThicknessTolMm = item.WallArcThicknessTolMm,
                WallHeightMm = item.WallHeightMm,
                WallBaseOffsetMm = item.WallBaseOffsetMm,
                WallEndpointClusterTolMm = item.WallEndpointClusterTolMm,
                WallExtendSearchTolMm = item.WallExtendSearchTolMm,
                WallDuplicateTolMm = item.WallDuplicateTolMm,
                WallAngleSnapDeg = item.WallAngleSnapDeg,
                WallEnableOrthogonalSnap = item.WallEnableOrthogonalSnap,
                WallEnableExtendToIntersection = item.WallEnableExtendToIntersection,
                WallEnableEndpointClustering = item.WallEnableEndpointClustering,
                WallEnableDuplicateRemoval = item.WallEnableDuplicateRemoval,
                WallEnableExtendCollinear = item.WallEnableExtendCollinear,
                WallEnableMergeCollinear = item.WallEnableMergeCollinear,
                WallExtendCollinearTolMm = item.WallExtendCollinearTolMm,
                WallCollinearOffsetTolMm = item.WallCollinearOffsetTolMm,
                WallExtendProjectionTolMm = item.WallExtendProjectionTolMm,
                WallUseDirectionalClustering = item.WallUseDirectionalClustering,
                WallEnableAutoDoubleLineThickness = item.WallEnableAutoDoubleLineThickness,
                WallAutoThicknessTopK = item.WallAutoThicknessTopK,
                WallAutoThicknessBinMm = item.WallAutoThicknessBinMm,
                WallMinDoubleLineThicknessMm = item.WallMinDoubleLineThicknessMm,
                WallMinDoubleLineOverlapLenMm = item.WallMinDoubleLineOverlapLenMm,
                WallForceSingleLineMode = item.WallForceSingleLineMode,
                WallDoubleLineSingleWallPlaceMode = item.WallDoubleLineSingleWallPlaceMode,
                WallDoubleLineLengthPolicy = item.WallDoubleLineLengthPolicy,
                WallDoubleLineAdaptiveContainTolMm = item.WallDoubleLineAdaptiveContainTolMm,
                WallDoubleLineAdaptiveExtendMaxMm = item.WallDoubleLineAdaptiveExtendMaxMm,
                DoorHeightMm = item.DoorHeightMm,
                DoorSillHeightMm = item.DoorSillHeightMm,
                UseFixedDoorWidth = item.UseFixedDoorWidth,
                PreferGeometryOpeningWidth = item.PreferGeometryOpeningWidth,
                BeamMinLengthMm = item.BeamMinLengthMm,
                BeamElevationOffsetMm = item.BeamElevationOffsetMm,
                BeamEnableMergeCollinear = item.BeamEnableMergeCollinear,
                BeamEndpointMergeTolMm = item.BeamEndpointMergeTolMm,
                BeamParallelAngleTolDeg = item.BeamParallelAngleTolDeg,
                BeamAllowArc = item.BeamAllowArc,
                WindowHeightMm = item.WindowHeightMm,
                WindowSillHeightMm = item.WindowSillHeightMm,
                WindowUseSillPlusHeight = item.WindowUseSillPlusHeight,
                ColumnHeightMm = item.ColumnHeightMm,
                ColumnClusterAlgorithm = item.ColumnClusterAlgorithm,
                ColumnClusterTolMm = item.ColumnClusterTolMm,
                ColumnEndpointTolMm = item.ColumnEndpointTolMm,
                ColumnGapTolMm = item.ColumnGapTolMm,
                ColumnMinGroupSegments = item.ColumnMinGroupSegments,
                ColumnMinSizeMm = item.ColumnMinSizeMm,
                ColumnMaxSizeMm = item.ColumnMaxSizeMm,
                ColumnMinAreaM2 = item.ColumnMinAreaM2,
                ColumnMaxAspectRatio = item.ColumnMaxAspectRatio,
                ColumnMinFillRatio = item.ColumnMinFillRatio,
                ColumnEnableLongLineFilter = item.ColumnEnableLongLineFilter,
                ColumnMaxSegmentLengthMm = item.ColumnMaxSegmentLengthMm,
                ColumnEnableMerge = item.ColumnEnableMerge,
                ColumnMergeTolMm = item.ColumnMergeTolMm,
                ColumnMergeStrategy = item.ColumnMergeStrategy,
                ColumnDedupePlacedTolMm = item.ColumnDedupePlacedTolMm,
                ColumnAreaWeight = item.ColumnAreaWeight,
                ColumnSegmentCountWeight = item.ColumnSegmentCountWeight,
                ColumnRectnessWeight = item.ColumnRectnessWeight,
                ColumnLongLinePenalty = item.ColumnLongLinePenalty,
                ColumnIrregularEnable = item.ColumnIrregularEnable,
                ColumnIrregularMaxSizeMm = item.ColumnIrregularMaxSizeMm,
                ColumnIrregularGapTolMm = item.ColumnIrregularGapTolMm,
                ColumnIrregularMinAreaM2 = item.ColumnIrregularMinAreaM2,
                ColumnAttachToWallEnable = item.ColumnAttachToWallEnable,
                ColumnAttachToWallSnapTolMm = item.ColumnAttachToWallSnapTolMm,
                ColumnAttachToWallTarget = item.ColumnAttachToWallTarget,
                ColumnAttachToWallAllowOverlap = item.ColumnAttachToWallAllowOverlap,
                ColumnDebugDrawCandidates = item.ColumnDebugDrawCandidates,
                ColumnDebugDrawClusterId = item.ColumnDebugDrawClusterId,
                ColumnDebugDrawRejectReason = item.ColumnDebugDrawRejectReason,
                ColumnDebugExportReport = item.ColumnDebugExportReport,
                IgnoreSmallerThanMm = item.IgnoreSmallerThanMm,
                MinJunctureWidthMm = item.MinJunctureWidthMm,
                IgnoreLargerThanMm = item.IgnoreLargerThanMm,
                MaxJunctureWidthMm = item.MaxJunctureWidthMm,
                ParameterMappings = item.ParameterMappings
            });
        }

        /// <summary>
        /// 閸掋倖鏌囩憰鍡欐磰閺佺増宓侀弰顖氭儊閸栧懎鎯堥張澶嬫櫏閸愬懎顔愰�?        /// </summary>
        private static bool HasAnyData(LayerOverrideStoreData data)
        {
            return data != null &&
                   ((data.LayerOverrides != null && data.LayerOverrides.Count > 0) ||
                    (data.CategoryDefaults != null && data.CategoryDefaults.Count > 0) ||
                    HasRoomRecognitionData(data.RoomRecognitionSettings) ||
                    HasGlobalGenerationData(data.GlobalGenerationSettings));
        }

        /// <summary>
        /// 閸掓稑缂撶粚楦款洬閻╂牗鏆熼幑顔碱嚠鐠灺扳偓?        /// </summary>
        private static LayerOverrideStoreData CreateEmpty(string source)
        {
            return new LayerOverrideStoreData
            {
                LoadSource = source,
                LayerOverrides = new Dictionary<string, AdvancedSettingsRow>(StringComparer.OrdinalIgnoreCase),
                CategoryDefaults = new Dictionary<MapCategory, AdvancedSettingsRow>(),
                RoomRecognitionSettings = RoomRecognitionSettings.CreateDefault(),
                GlobalGenerationSettings = GlobalGenerationSettings.CreateDefault()
            };
        }

        private static GlobalGenerationSettings NormalizeGlobalGenerationSettings(GlobalGenerationSettings settings, RoomRecognitionSettings roomRecognitionSettings)
        {
            GlobalGenerationSettings normalized = GlobalGenerationSettings.Clone(settings);
            RoomRecognitionSettings room = RoomRecognitionSettings.Clone(roomRecognitionSettings);
            if (normalized.HeadRoomMm <= 0 && room.HeadRoomMm > 0)
            {
                normalized.HeadRoomMm = room.HeadRoomMm;
            }

            return normalized;
        }

        private static RoomRecognitionSettings NormalizeRoomRecognitionSettings(RoomRecognitionSettings settings, GlobalGenerationSettings globalGenerationSettings)
        {
            RoomRecognitionSettings normalized = RoomRecognitionSettings.Clone(settings);
            GlobalGenerationSettings global = GlobalGenerationSettings.Clone(globalGenerationSettings);
            normalized.HeadRoomMm = global.HeadRoomMm >= 0 ? global.HeadRoomMm : RoomRecognitionSettings.DefaultHeadRoomMm;
            return normalized;
        }

        private static bool HasRoomRecognitionData(RoomRecognitionSettings settings)
        {
            RoomRecognitionSettings source = RoomRecognitionSettings.Clone(settings);
            return !string.Equals(source.RoomTextLayerNames, RoomRecognitionSettings.DefaultRoomTextLayerNames, StringComparison.OrdinalIgnoreCase) ||
                   Math.Abs(source.DoorGapMaxMm - RoomRecognitionSettings.DefaultDoorGapMaxMm) > 0.001 ||
                   Math.Abs(source.SmallGapPatchMaxMm - RoomRecognitionSettings.DefaultSmallGapPatchMaxMm) > 0.001 ||
                   !string.Equals(source.TargetKeywordsText, RoomRecognitionSettings.DefaultTargetKeywordsText, StringComparison.OrdinalIgnoreCase) ||
                   !string.Equals(source.LiftGeometryLayerNames, RoomRecognitionSettings.DefaultLiftGeometryLayerNames, StringComparison.OrdinalIgnoreCase) ||
                   Math.Abs(
                       RoomRecognitionSettings.NormalizeModelRecognitionWindowSizeM(source.ModelRecognitionWindowSizeM) -
                       RoomRecognitionSettings.DefaultModelRecognitionWindowSizeM) > 0.001;
        }

        private static bool HasGlobalGenerationData(GlobalGenerationSettings settings)
        {
            GlobalGenerationSettings source = GlobalGenerationSettings.Clone(settings);
            return source.SafeModeEnabled != true ||
                   source.AutoJoinWallsAfterCreate != true ||
                   Math.Abs(source.HeadRoomMm - GlobalGenerationSettings.DefaultHeadRoomMm) > 0.001 ||
                   source.UseGlobalWallHeightOverride ||
                   Math.Abs(source.GlobalWallHeightMm - GlobalGenerationSettings.DefaultWallHeightMm) > 0.001 ||
                   source.UseGlobalDoorHeightOverride ||
                   Math.Abs(source.GlobalDoorHeightMm - GlobalGenerationSettings.DefaultDoorHeightMm) > 0.001 ||
                   source.UseGlobalDoorSillHeightOverride ||
                   Math.Abs(source.GlobalDoorSillHeightMm - GlobalGenerationSettings.DefaultDoorSillHeightMm) > 0.001 ||
                   source.CreateDoorOpeningOnly != GlobalGenerationSettings.CreateDefault().CreateDoorOpeningOnly;
        }

        /// <summary>
        /// 閼惧嘲褰?AppData 閹镐椒绠欓崠鏍ㄦ瀮娴犳儼鐭惧鍕┾偓?        /// </summary>
        private static string GetStorePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "CadToRevit", "HelixWizard", "Overrides", "layer_overrides.json");
        }

        /// <summary>
        /// 鐏忔繆鐦潻浣盒╅弮褏澧楃憰鍡欐磰閺傚洣娆㈤崚鐗堟煀缂佹挻鐎妴?        /// </summary>
        private static LayerOverrideStoreData TryMigrateLegacyOverrides()
        {
            string[] legacyCandidates = ResolveLegacyCandidates();
            foreach (string path in legacyCandidates)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    using (FileStream fs = File.OpenRead(path))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(LegacyLayerOverrideConfig));
                        LegacyLayerOverrideConfig legacy = serializer.ReadObject(fs) as LegacyLayerOverrideConfig;
                        LayerOverrideStoreData converted = ConvertLegacy(legacy);
                        if (!HasAnyData(converted))
                        {
                            continue;
                        }

                        DiagnosticRecorder.AppendDebug("[OverrideMigration] Migrated legacy overrides from " + path + ", count=" + converted.LayerOverrides.Count);
                        return converted;
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[OverrideMigration] Failed migrate from " + path + ", ex=" + ex.Message);
                }
            }

            return CreateEmpty("NoLegacy");
        }

        /// <summary>
        /// 閺冄呭鐟曞棛娲婄紒鎾寸€潪顒佸床娑撶儤鏌婃潻鎰攽閺冨墎绮ㄩ弸鍕┾偓?        /// </summary>
        private static LayerOverrideStoreData ConvertLegacy(LegacyLayerOverrideConfig legacy)
        {
            LayerOverrideStoreData data = CreateEmpty("Legacy");
            foreach (LegacyLayerOverrideEntry layer in legacy?.Layers ?? new List<LegacyLayerOverrideEntry>())
            {
                if (layer == null || string.IsNullOrWhiteSpace(layer.RawLayerName))
                {
                    continue;
                }

                AdvancedSettingsRow settings = new AdvancedSettingsRow
                {
                    EnableLayerOverride = true,
                    WallMinWallLengthMm = layer.MinWallLengthMm,
                    WallEnableExtendCollinear = layer.Topology?.EnableExtendCollinear,
                    WallEnableMergeCollinear = layer.Topology?.EnableMergeCollinear,
                    WallExtendCollinearTolMm = layer.Topology?.ExtendCollinearTolMm
                };
                data.LayerOverrides[layer.RawLayerName] = settings;
            }

            return data;
        }

        /// <summary>
        /// 鐟欙絾鐎介弮褏澧楃憰鍡欐磰閺傚洣娆㈤崐娆撯偓澶庣熅瀵板嫬鍨悰銊ｂ�?        /// </summary>
        private static string[] ResolveLegacyCandidates()
        {
            string dllDir = null;
            try
            {
                dllDir = Path.GetDirectoryName(typeof(LayerOverrideStoreService).Assembly.Location);
            }
            catch
            {
                dllDir = null;
            }

            return new[]
            {
                string.IsNullOrWhiteSpace(dllDir) ? null : Path.Combine(dllDir, "WallRecognitionLayerOverrides.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WallRecognitionLayerOverrides.json"),
                Path.Combine(Environment.CurrentDirectory, "WallRecognitionLayerOverrides.json")
            };
        }

        /// <summary>
        /// 濞ｈ鲸瀚圭拹婵嬬彯缁狙嗩啎缂冾喖顕挒鈽呯礉闁灝鍘ゅ鏇犳暏閸忓彉闊╅�?        /// </summary>
        private static AdvancedSettingsRow CloneSettings(AdvancedSettingsRow source)
        {
            AdvancedSettingsRow target = new AdvancedSettingsRow();
            if (source == null)
            {
                return target;
            }

            target.EnableLayerOverride = source.EnableLayerOverride;
            target.ApplyAsCategoryDefault = source.ApplyAsCategoryDefault;
            target.DoorExpectedWidthMm = source.DoorExpectedWidthMm;
            target.MinDoorWidthMm = source.MinDoorWidthMm;
            target.MaxDoorWidthMm = source.MaxDoorWidthMm;
            target.DoorWallMatchTolMm = source.DoorWallMatchTolMm;
            target.WallMinWallLengthMm = source.WallMinWallLengthMm;
            target.WallThicknessTolMm = source.WallThicknessTolMm;
            target.WallMaxWallThicknessMm = source.WallMaxWallThicknessMm;
            target.WallDefaultSingleWallThicknessMm = source.WallDefaultSingleWallThicknessMm;
            target.WallParallelAngleTolDeg = source.WallParallelAngleTolDeg;
            target.WallEndpointMergeTolMm = source.WallEndpointMergeTolMm;
            target.WallArcThicknessTolMm = source.WallArcThicknessTolMm;
            target.WallHeightMm = source.WallHeightMm;
            target.WallBaseOffsetMm = source.WallBaseOffsetMm;
            target.WallEndpointClusterTolMm = source.WallEndpointClusterTolMm;
            target.WallExtendSearchTolMm = source.WallExtendSearchTolMm;
            target.WallDuplicateTolMm = source.WallDuplicateTolMm;
            target.WallAngleSnapDeg = source.WallAngleSnapDeg;
            target.WallEnableOrthogonalSnap = source.WallEnableOrthogonalSnap;
            target.WallEnableExtendToIntersection = source.WallEnableExtendToIntersection;
            target.WallEnableEndpointClustering = source.WallEnableEndpointClustering;
            target.WallEnableDuplicateRemoval = source.WallEnableDuplicateRemoval;
            target.WallEnableExtendCollinear = source.WallEnableExtendCollinear;
            target.WallEnableMergeCollinear = source.WallEnableMergeCollinear;
            target.WallExtendCollinearTolMm = source.WallExtendCollinearTolMm;
            target.WallCollinearOffsetTolMm = source.WallCollinearOffsetTolMm;
            target.WallExtendProjectionTolMm = source.WallExtendProjectionTolMm;
            target.WallUseDirectionalClustering = source.WallUseDirectionalClustering;
            target.WallEnableAutoDoubleLineThickness = source.WallEnableAutoDoubleLineThickness;
            target.WallAutoThicknessTopK = source.WallAutoThicknessTopK;
            target.WallAutoThicknessBinMm = source.WallAutoThicknessBinMm;
            target.WallMinDoubleLineThicknessMm = source.WallMinDoubleLineThicknessMm;
            target.WallMinDoubleLineOverlapLenMm = source.WallMinDoubleLineOverlapLenMm;
            target.WallForceSingleLineMode = source.WallForceSingleLineMode;
            target.WallDoubleLineSingleWallPlaceMode = source.WallDoubleLineSingleWallPlaceMode;
            target.WallDoubleLineLengthPolicy = source.WallDoubleLineLengthPolicy;
            target.WallDoubleLineAdaptiveContainTolMm = source.WallDoubleLineAdaptiveContainTolMm;
            target.WallDoubleLineAdaptiveExtendMaxMm = source.WallDoubleLineAdaptiveExtendMaxMm;
            target.DoorHeightMm = source.DoorHeightMm;
            target.DoorSillHeightMm = source.DoorSillHeightMm;
            target.UseFixedDoorWidth = source.UseFixedDoorWidth;
            target.PreferGeometryOpeningWidth = source.PreferGeometryOpeningWidth;
            target.BeamMinLengthMm = source.BeamMinLengthMm;
            target.BeamElevationOffsetMm = source.BeamElevationOffsetMm;
            target.BeamEnableMergeCollinear = source.BeamEnableMergeCollinear;
            target.BeamEndpointMergeTolMm = source.BeamEndpointMergeTolMm;
            target.BeamParallelAngleTolDeg = source.BeamParallelAngleTolDeg;
            target.BeamAllowArc = source.BeamAllowArc;
            target.WindowHeightMm = source.WindowHeightMm;
            target.WindowSillHeightMm = source.WindowSillHeightMm;
            target.WindowUseSillPlusHeight = source.WindowUseSillPlusHeight;
            target.ColumnHeightMm = source.ColumnHeightMm;
            target.ColumnClusterAlgorithm = source.ColumnClusterAlgorithm;
            target.ColumnClusterTolMm = source.ColumnClusterTolMm;
            target.ColumnEndpointTolMm = source.ColumnEndpointTolMm;
            target.ColumnGapTolMm = source.ColumnGapTolMm;
            target.ColumnMinGroupSegments = source.ColumnMinGroupSegments;
            target.ColumnMinSizeMm = source.ColumnMinSizeMm;
            target.ColumnMaxSizeMm = source.ColumnMaxSizeMm;
            target.ColumnMinAreaM2 = source.ColumnMinAreaM2;
            target.ColumnMaxAspectRatio = source.ColumnMaxAspectRatio;
            target.ColumnMinFillRatio = source.ColumnMinFillRatio;
            target.ColumnEnableLongLineFilter = source.ColumnEnableLongLineFilter;
            target.ColumnMaxSegmentLengthMm = source.ColumnMaxSegmentLengthMm;
            target.ColumnEnableMerge = source.ColumnEnableMerge;
            target.ColumnMergeTolMm = source.ColumnMergeTolMm;
            target.ColumnMergeStrategy = source.ColumnMergeStrategy;
            target.ColumnDedupePlacedTolMm = source.ColumnDedupePlacedTolMm;
            target.ColumnAreaWeight = source.ColumnAreaWeight;
            target.ColumnSegmentCountWeight = source.ColumnSegmentCountWeight;
            target.ColumnRectnessWeight = source.ColumnRectnessWeight;
            target.ColumnLongLinePenalty = source.ColumnLongLinePenalty;
            target.ColumnIrregularEnable = source.ColumnIrregularEnable;
            target.ColumnIrregularMaxSizeMm = source.ColumnIrregularMaxSizeMm;
            target.ColumnIrregularGapTolMm = source.ColumnIrregularGapTolMm;
            target.ColumnIrregularMinAreaM2 = source.ColumnIrregularMinAreaM2;
            target.ColumnAttachToWallEnable = source.ColumnAttachToWallEnable;
            target.ColumnAttachToWallSnapTolMm = source.ColumnAttachToWallSnapTolMm;
            target.ColumnAttachToWallTarget = source.ColumnAttachToWallTarget;
            target.ColumnAttachToWallAllowOverlap = source.ColumnAttachToWallAllowOverlap;
            target.ColumnDebugDrawCandidates = source.ColumnDebugDrawCandidates;
            target.ColumnDebugDrawClusterId = source.ColumnDebugDrawClusterId;
            target.ColumnDebugDrawRejectReason = source.ColumnDebugDrawRejectReason;
            target.ColumnDebugExportReport = source.ColumnDebugExportReport;
            target.Juncture = new JunctureSettings
            {
                IgnoreSmallerThanMm = source.Juncture?.IgnoreSmallerThanMm ?? 0.0,
                MinJunctureWidthMm = source.Juncture?.MinJunctureWidthMm ?? 0.0,
                IgnoreLargerThanMm = source.Juncture?.IgnoreLargerThanMm ?? 0.0,
                MaxJunctureWidthMm = source.Juncture?.MaxJunctureWidthMm ?? 0.0
            };

            foreach (ParameterMapping mapping in source.ParameterMappings ?? new List<ParameterMapping>())
            {
                if (mapping == null)
                {
                    continue;
                }

                target.ParameterMappings.Add(new ParameterMapping
                {
                    ParameterName = mapping.ParameterName,
                    StorageType = mapping.StorageType,
                    Value = mapping.Value
                });
            }

            return target;
        }
    }

    [DataContract]
    internal sealed class LayerOverrideStoreDto
    {
        [DataMember(Name = "Version")]
        public int Version { get; set; } = 3;

        [DataMember(Name = "UpdatedAt")]
        public string UpdatedAt { get; set; }

        [DataMember(Name = "Overrides")]
        public List<LayerOverrideEntryDto> Overrides { get; set; } = new List<LayerOverrideEntryDto>();

        [DataMember(Name = "CategoryDefaults")]
        public List<CategoryDefaultEntryDto> CategoryDefaults { get; set; } = new List<CategoryDefaultEntryDto>();

        [DataMember(Name = "RoomRecognition")]
        public RoomRecognitionSettingsDto RoomRecognition { get; set; } = new RoomRecognitionSettingsDto();

        [DataMember(Name = "GlobalGeneration")]
        public GlobalGenerationSettingsDto GlobalGeneration { get; set; } = new GlobalGenerationSettingsDto();
    }

    [DataContract]
    internal sealed class RoomRecognitionSettingsDto
    {
        [DataMember(Name = "RoomTextLayerNames")]
        public string RoomTextLayerNames { get; set; }

        [DataMember(Name = "DoorGapMaxMm")]
        public double DoorGapMaxMm { get; set; } = RoomRecognitionSettings.DefaultDoorGapMaxMm;

        [DataMember(Name = "SmallGapPatchMaxMm")]
        public double SmallGapPatchMaxMm { get; set; } = RoomRecognitionSettings.DefaultSmallGapPatchMaxMm;

        [DataMember(Name = "TargetKeywordsText")]
        public string TargetKeywordsText { get; set; } = RoomRecognitionSettings.DefaultTargetKeywordsText;

        [DataMember(Name = "LiftGeometryLayerNames")]
        public string LiftGeometryLayerNames { get; set; } = RoomRecognitionSettings.DefaultLiftGeometryLayerNames;

        [DataMember(Name = "ModelRecognitionWindowSizeM")]
        public double ModelRecognitionWindowSizeM { get; set; } = RoomRecognitionSettings.DefaultModelRecognitionWindowSizeM;

        [DataMember(Name = "HeadRoomMm")]
        public double HeadRoomMm { get; set; } = RoomRecognitionSettings.DefaultHeadRoomMm;
    }

    [DataContract]
    internal sealed class GlobalGenerationSettingsDto
    {
        [DataMember(Name = "SafeModeEnabled")]
        public bool SafeModeEnabled { get; set; } = true;

        [DataMember(Name = "AutoJoinWallsAfterCreate")]
        public bool AutoJoinWallsAfterCreate { get; set; } = true;

        [DataMember(Name = "HeadRoomMm")]
        public double HeadRoomMm { get; set; } = GlobalGenerationSettings.DefaultHeadRoomMm;

        [DataMember(Name = "UseGlobalWallHeightOverride")]
        public bool UseGlobalWallHeightOverride { get; set; }

        [DataMember(Name = "GlobalWallHeightMm")]
        public double GlobalWallHeightMm { get; set; } = GlobalGenerationSettings.DefaultWallHeightMm;

        [DataMember(Name = "UseGlobalDoorHeightOverride")]
        public bool UseGlobalDoorHeightOverride { get; set; }

        [DataMember(Name = "GlobalDoorHeightMm")]
        public double GlobalDoorHeightMm { get; set; } = GlobalGenerationSettings.DefaultDoorHeightMm;

        [DataMember(Name = "UseGlobalDoorSillHeightOverride")]
        public bool UseGlobalDoorSillHeightOverride { get; set; }

        [DataMember(Name = "GlobalDoorSillHeightMm")]
        public double GlobalDoorSillHeightMm { get; set; } = GlobalGenerationSettings.DefaultDoorSillHeightMm;

        [DataMember(Name = "CreateDoorOpeningOnly")]
        public bool? CreateDoorOpeningOnly { get; set; }
    }

    [DataContract]
    internal sealed class LayerOverrideEntryDto
    {
        [DataMember(Name = "RawLayerName")]
        public string RawLayerName { get; set; }

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

        [DataMember(Name = "WallEnableExtendCollinear")]
        public bool? WallEnableExtendCollinear { get; set; }

        [DataMember(Name = "WallEnableMergeCollinear")]
        public bool? WallEnableMergeCollinear { get; set; }

        [DataMember(Name = "WallExtendCollinearTolMm")]
        public double? WallExtendCollinearTolMm { get; set; }

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

        [DataMember(Name = "IgnoreSmallerThanMm")]
        public double IgnoreSmallerThanMm { get; set; }

        [DataMember(Name = "MinJunctureWidthMm")]
        public double MinJunctureWidthMm { get; set; }

        [DataMember(Name = "IgnoreLargerThanMm")]
        public double IgnoreLargerThanMm { get; set; }

        [DataMember(Name = "MaxJunctureWidthMm")]
        public double MaxJunctureWidthMm { get; set; }

        [DataMember(Name = "ParameterMappings")]
        public List<ParameterMappingDto> ParameterMappings { get; set; } = new List<ParameterMappingDto>();
    }

    [DataContract]
    internal sealed class CategoryDefaultEntryDto
    {
        [DataMember(Name = "Category")]
        public string Category { get; set; }

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

        [DataMember(Name = "WallEnableExtendCollinear")]
        public bool? WallEnableExtendCollinear { get; set; }

        [DataMember(Name = "WallEnableMergeCollinear")]
        public bool? WallEnableMergeCollinear { get; set; }

        [DataMember(Name = "WallExtendCollinearTolMm")]
        public double? WallExtendCollinearTolMm { get; set; }

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

        [DataMember(Name = "IgnoreSmallerThanMm")]
        public double IgnoreSmallerThanMm { get; set; }

        [DataMember(Name = "MinJunctureWidthMm")]
        public double MinJunctureWidthMm { get; set; }

        [DataMember(Name = "IgnoreLargerThanMm")]
        public double IgnoreLargerThanMm { get; set; }

        [DataMember(Name = "MaxJunctureWidthMm")]
        public double MaxJunctureWidthMm { get; set; }

        [DataMember(Name = "ParameterMappings")]
        public List<ParameterMappingDto> ParameterMappings { get; set; } = new List<ParameterMappingDto>();
    }

    [DataContract]
    internal sealed class ParameterMappingDto
    {
        [DataMember(Name = "ParameterName")]
        public string ParameterName { get; set; }

        [DataMember(Name = "StorageType")]
        public string StorageType { get; set; }

        [DataMember(Name = "Value")]
        public string Value { get; set; }
    }

    [DataContract]
    internal sealed class LegacyLayerOverrideConfig
    {
        [DataMember(Name = "Layers")]
        public List<LegacyLayerOverrideEntry> Layers { get; set; } = new List<LegacyLayerOverrideEntry>();
    }

    [DataContract]
    internal sealed class LegacyLayerOverrideEntry
    {
        [DataMember(Name = "RawLayerName")]
        public string RawLayerName { get; set; }

        [DataMember(Name = "MinWallLengthMm")]
        public double? MinWallLengthMm { get; set; }

        [DataMember(Name = "Topology")]
        public LegacyTopologyOverride Topology { get; set; }
    }

    [DataContract]
    internal sealed class LegacyTopologyOverride
    {
        [DataMember(Name = "EnableExtendCollinear")]
        public bool? EnableExtendCollinear { get; set; }

        [DataMember(Name = "EnableMergeCollinear")]
        public bool? EnableMergeCollinear { get; set; }

        [DataMember(Name = "ExtendCollinearTolMm")]
        public double? ExtendCollinearTolMm { get; set; }
    }
}



