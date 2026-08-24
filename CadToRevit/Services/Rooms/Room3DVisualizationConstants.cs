using Autodesk.Revit.DB;

namespace CadToRevit.Services.Rooms
{
    internal static class Room3DVisualizationConstants
    {
        internal const string RegionNamePrefix = "EMSD_ROOMVIS_REGION__";
        internal const string MarkerNamePrefix = "EMSD_ROOMVIS_MARKER__";
        internal const string AhuPlacementPointMarkerNamePrefix = "EMSD_AHU_PLACEMENT_POINT__";
        internal const string LegacyTagNamePrefix = "EMSD_ROOMVIS_TAG__";
        internal const string ApplicationId = "CadToRevit.Room3DVisualization";
        internal const string MaterialNormalName = "EMSD_ROOMVIS_MAT_NORMAL";
        internal const string MaterialHighlightName = "EMSD_ROOMVIS_MAT_HIGHLIGHT";
        internal const string RegionDataPrefix = "REGION::";
        internal const string MarkerDataPrefix = "MARKER::";
        internal const string TextNamePrefix = "EMSD_ROOMVIS_TEXT__";
        internal const string TextDataPrefix = "TEXT::";
        internal const string TextFamilyFileName = "EMSD_Room3DText.rfa";
        internal const string TextFamilyName = "EMSD_Room3DText";
        internal const string TextDefaultTypeName = "Default";
        internal const string TextHighlightTypeName = "Highlight";

        internal const double RegionThicknessMm = 5.0;
        internal const double MarkerOuterSizeMm = 900.0;
        internal const double MarkerBarWidthMm = 120.0;
        internal const double MarkerThicknessMm = 60.0;
        internal const double MarkerOffsetMm = 1800.0;
        internal const double TextOffsetMm = 10.0;
        internal const double MinEdgeLengthMm = 1.0;
        internal const double MaxCloseGapMm = 1200.0;
        internal const double MmToFeet = 1.0 / 304.8;

        internal static readonly Color RegionNormalColor = new Color(255, 190, 120);
        internal static readonly Color RegionHighlightColor = new Color(125, 182, 236); 
        internal static readonly Color MarkerNormalColor = new Color(35, 205, 220);
        internal static readonly Color MarkerHighlightColor = new Color(255, 83, 31);
        internal const int RegionNormalTransparency = 18;
        internal const int RegionHighlightTransparency = 3;
        internal const int MarkerNormalTransparency = 0;
        internal const int MarkerHighlightTransparency = 0;
    }
}
