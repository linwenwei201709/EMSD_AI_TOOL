using Autodesk.Revit.DB;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewConstants
    {
        internal const string PreviewViewName = "AI_PATH_PREVIEW_3D";
        internal const string ApplicationId = "CadToRevit.PathPreview";
        internal const string PreviewProjectTitlePrefix = "AI_PATH_PREVIEW_HOST_";
        internal const string TempRootFolderName = "CadToRevit";
        internal const string TempFeatureFolderName = "PathPreview";
        internal const string TempIfcFilePrefix = "AI_PATH_PREVIEW_MODEL_";
        internal const string TempPreviewProjectPrefix = "AI_PATH_PREVIEW_HOST_";
        internal const string TempLinkedModelRvtPrefix = "AI_PATH_PREVIEW_LINKMODEL_";
        internal const string PreviewLogFileName = "PathPreview.log";
        internal const string GrayMaterialName = "AI_PATH_PREVIEW_GRAY";
        internal const string LabelMaterialName = "EMSD_PATHVIS_MAT_LABEL";
        internal const double SectionBoxPaddingMm = 2000.0;

        internal const string SegmentNamePrefix = "EMSD_PATHVIS_SEG__";
        internal const string ArrowNamePrefix = "EMSD_PATHVIS_ARROW__";
        internal const string NodeNamePrefix = "EMSD_PATHVIS_NODE__";

        internal const string SegmentDataPrefix = "SEG::";
        internal const string ArrowDataPrefix = "ARROW::";
        internal const string NodeDataPrefix = "NODE::";

        internal const string PathMaterialName = "EMSD_PATHVIS_MAT_PATH";
        internal const string ArrowMaterialName = "EMSD_PATHVIS_MAT_ARROW";
        internal const string StartMaterialName = "EMSD_PATHVIS_MAT_START";
        internal const string EndMaterialName = "EMSD_PATHVIS_MAT_END";

        internal const double MmToFeet = 1.0 / 304.8;
        internal const double PathCenterlineDisplayOffsetMm = PathBoxHeightMm * 0.5;

        internal const double PathBoxLengthMm = 1050.0;
        internal const double PathBoxWidthMm = 1100.0;
        internal const double PathBoxHeightMm = 1700.0;
        internal const double PathBoxSpacingMm = 1050.0;

        internal const double ArrowLengthMm = 500.0;
        internal const double ArrowWidthMm = 240.0;
        internal const double ArrowThicknessMm = 40.0;
        internal const double ArrowSpacingMm = 3000.0;

        internal const double NodeLengthMm = PathBoxLengthMm;
        internal const double NodeWidthMm = PathBoxWidthMm;
        internal const double NodeHeightMm = PathBoxHeightMm;

        internal const double LabelHeightMm = 80.0;
        internal const double LabelStrokeWidthMm = 70.0;
        internal const double LabelLetterHeightMm = 260.0;
        internal const double LabelLetterSpacingMm = 40.0;
        internal const double LabelWordGapMm = 100.0;
        internal const double LabelTopOffsetMm = 20.0;

        internal static readonly Color PathColor = new Color(255, 180, 80);

        // Compare Mode path colors. Start/end nodes keep StartColor/EndColor;
        // only middle path boxes cycle these colors.
        // Order: light orange (existing), light green, light blue, light purple, light red.
        internal static readonly Color[] ComparisonPathColors =
        {
            PathColor,
            new Color(150, 215, 150),
            new Color(120, 185, 255),
            new Color(190, 160, 235),
            new Color(255, 135, 135)
        };

        internal static Color GetComparisonPathColor(int pathIndex)
        {
            if (ComparisonPathColors == null || ComparisonPathColors.Length == 0)
            {
                return PathColor;
            }

            int safeIndex = pathIndex % ComparisonPathColors.Length;
            if (safeIndex < 0)
            {
                safeIndex += ComparisonPathColors.Length;
            }

            return ComparisonPathColors[safeIndex];
        }

        internal static readonly Color ArrowColor = new Color(255, 140, 0);
        //internal static readonly Color StartColor = new Color(0, 190, 90);
        //internal static readonly Color EndColor = new Color(255, 70, 70);
        internal static readonly Color LabelColor = new Color(20, 20, 20);

        //internal const int PathTransparency = 0;
        //internal const int ArrowTransparency = 0;
        //internal const int NodeTransparency = 0;
        //internal const int LabelTransparency = 0;

        // 起点改蓝色
        internal static readonly Color StartColor = new Color(0, 120, 255);

        // 终点改绿色
        internal static readonly Color EndColor = new Color(0, 190, 90);
        internal static readonly Color RedZoneColor = new Color(255, 0, 0);

        internal const int PathTransparency = 10;
        internal const int ArrowTransparency = 10;
        internal const int NodeTransparency = 10;
        internal const int LabelTransparency = 0;
        internal const int RedZoneTransparency = 0;
    }
}
