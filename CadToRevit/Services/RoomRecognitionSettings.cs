using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    /// <summary>
    /// Stores global room-recognition settings shared by the dockable pane and generation flow.
    /// </summary>
    public sealed class RoomRecognitionSettings
    {
        public const string DefaultRoomTextLayerNames = "ROOMNAME";
        public const double DefaultDoorGapMaxMm = 1200.0;
        public const double DefaultSmallGapPatchMaxMm = 350.0;
        public const string DefaultTargetKeywordsText = "A/C,AHU,PAU";
        public const string DefaultLiftGeometryLayerNames = "DT001";
        public const double DefaultModelRecognitionWindowSizeM = 18.0;
        public const double DefaultHeadRoomMm = 0.0;

        public string RoomTextLayerNames { get; set; } = DefaultRoomTextLayerNames;

        public double DoorGapMaxMm { get; set; } = DefaultDoorGapMaxMm;

        public double SmallGapPatchMaxMm { get; set; } = DefaultSmallGapPatchMaxMm;

        public string TargetKeywordsText { get; set; } = DefaultTargetKeywordsText;

        public string LiftGeometryLayerNames { get; set; } = DefaultLiftGeometryLayerNames;

        // Configurable local flood-fill window size in meters (allowed values: 12/15/18).
        public double ModelRecognitionWindowSizeM { get; set; } = DefaultModelRecognitionWindowSizeM;

        // Reserved global head-room value for future use.
        public double HeadRoomMm { get; set; } = DefaultHeadRoomMm;

        public static RoomRecognitionSettings CreateDefault()
        {
            return new RoomRecognitionSettings();
        }

        public static RoomRecognitionSettings Clone(RoomRecognitionSettings source)
        {
            if (source == null)
            {
                return CreateDefault();
            }

            return new RoomRecognitionSettings
            {
                RoomTextLayerNames = string.IsNullOrWhiteSpace(source.RoomTextLayerNames)
                    ? DefaultRoomTextLayerNames
                    : source.RoomTextLayerNames.Trim(),
                DoorGapMaxMm = source.DoorGapMaxMm > 0 ? source.DoorGapMaxMm : DefaultDoorGapMaxMm,
                SmallGapPatchMaxMm = source.SmallGapPatchMaxMm > 0 ? source.SmallGapPatchMaxMm : DefaultSmallGapPatchMaxMm,
                TargetKeywordsText = string.IsNullOrWhiteSpace(source.TargetKeywordsText)
                    ? DefaultTargetKeywordsText
                    : source.TargetKeywordsText.Trim(),
                LiftGeometryLayerNames = string.IsNullOrWhiteSpace(source.LiftGeometryLayerNames)
                    ? DefaultLiftGeometryLayerNames
                    : source.LiftGeometryLayerNames.Trim(),
                ModelRecognitionWindowSizeM = NormalizeModelRecognitionWindowSizeM(source.ModelRecognitionWindowSizeM),
                HeadRoomMm = source.HeadRoomMm >= 0 ? source.HeadRoomMm : DefaultHeadRoomMm
            };
        }

        public static double NormalizeModelRecognitionWindowSizeM(double value)
        {
            if (Math.Abs(value - 12.0) < 0.01)
            {
                return 12.0;
            }

            if (Math.Abs(value - 18.0) < 0.01)
            {
                return 18.0;
            }

            // Default and fallback option.
            return DefaultModelRecognitionWindowSizeM;
        }

        public List<string> GetConfiguredRoomTextLayers()
        {
            return (RoomTextLayerNames ?? string.Empty)
                .Split(new[] { ',', ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> GetConfiguredTargetKeywords()
        {
            return (TargetKeywordsText ?? string.Empty)
                .Split(new[] { ',', ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<string> GetConfiguredLiftGeometryLayers()
        {
            List<string> layers = (LiftGeometryLayerNames ?? string.Empty)
                .Split(new[] { ',', ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (layers.Count == 0)
            {
                layers.Add(DefaultLiftGeometryLayerNames);
            }

            return layers;
        }
    }
}
