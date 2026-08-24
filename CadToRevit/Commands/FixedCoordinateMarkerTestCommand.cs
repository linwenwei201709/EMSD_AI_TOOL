using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Globalization;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class FixedCoordinateMarkerTestCommand : IExternalCommand
    {
        private const double StartXmm = 57712.3;
        private const double StartYmm = 43745.0;
        private const double GoalXmm = 21980.0;
        private const double GoalYmm = 45215.0;
        private const double ExtraStartXmm = 40970.0;
        private const double ExtraStartYmm = 24695.0;
        private const double ExtraGoalXmm = 18270.0;
        private const double ExtraGoalYmm = 18495.0;
        private const double GreenStartXmm = 44470.0;
        private const double GreenStartYmm = 39495.0;
        private const double GreenGoalXmm = 20070.0;
        private const double GreenGoalYmm = 42595.0;
        private const double MarkerHalfSizeMm = 800.0;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData == null ? null : commandData.Application.ActiveUIDocument;
            Document doc = uiDoc == null ? null : uiDoc.Document;
            if (doc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                XYZ startPoint = new XYZ(ToFeet(StartXmm), ToFeet(StartYmm), 0.0);
                XYZ goalPoint = new XYZ(ToFeet(GoalXmm), ToFeet(GoalYmm), 0.0);
                XYZ extraStartPoint = new XYZ(ToFeet(ExtraStartXmm), ToFeet(ExtraStartYmm), 0.0);
                XYZ extraGoalPoint = new XYZ(ToFeet(ExtraGoalXmm), ToFeet(ExtraGoalYmm), 0.0);
                XYZ greenStartPoint = new XYZ(ToFeet(GreenStartXmm), ToFeet(GreenStartYmm), 0.0);
                XYZ greenGoalPoint = new XYZ(ToFeet(GreenGoalXmm), ToFeet(GreenGoalYmm), 0.0);
                double halfSizeFeet = ToFeet(MarkerHalfSizeMm);
                View activeView = doc.ActiveView;

                OverrideGraphicSettings redOverrides = new OverrideGraphicSettings();
                redOverrides.SetProjectionLineColor(new Color(255, 0, 0));
                OverrideGraphicSettings greenOverrides = new OverrideGraphicSettings();
                greenOverrides.SetProjectionLineColor(new Color(0, 170, 0));

                using (Transaction tx = new Transaction(doc, "Draw Fixed Coordinate Test Markers"))
                {
                    tx.Start();
                    DrawMarker(doc, activeView, startPoint, halfSizeFeet, null);
                    DrawMarker(doc, activeView, goalPoint, halfSizeFeet, null);
                    DrawMarker(doc, activeView, extraStartPoint, halfSizeFeet, redOverrides);
                    DrawMarker(doc, activeView, extraGoalPoint, halfSizeFeet, redOverrides);
                    DrawMarker(doc, activeView, greenStartPoint, halfSizeFeet, greenOverrides);
                    DrawMarker(doc, activeView, greenGoalPoint, halfSizeFeet, greenOverrides);
                    tx.Commit();
                }

                TaskDialog.Show(
                    "固定坐标测试",
                    "已绘制 6 个固定坐标 X 标记。" + Environment.NewLine +
                    "start_point: [" + StartXmm.ToString("0.###", CultureInfo.InvariantCulture) + ", " + StartYmm.ToString("0.###", CultureInfo.InvariantCulture) + "]" + Environment.NewLine +
                    "goal_point: [" + GoalXmm.ToString("0.###", CultureInfo.InvariantCulture) + ", " + GoalYmm.ToString("0.###", CultureInfo.InvariantCulture) + "]" + Environment.NewLine +
                    "red_start_point: [" + ExtraStartXmm.ToString("0.###", CultureInfo.InvariantCulture) + ", " + ExtraStartYmm.ToString("0.###", CultureInfo.InvariantCulture) + "]" + Environment.NewLine +
                    "red_goal_point: [" + ExtraGoalXmm.ToString("0.###", CultureInfo.InvariantCulture) + ", " + ExtraGoalYmm.ToString("0.###", CultureInfo.InvariantCulture) + "]" + Environment.NewLine +
                    "green_start_point: [" + GreenStartXmm.ToString("0.###", CultureInfo.InvariantCulture) + ", " + GreenStartYmm.ToString("0.###", CultureInfo.InvariantCulture) + "]" + Environment.NewLine +
                    "green_goal_point: [" + GreenGoalXmm.ToString("0.###", CultureInfo.InvariantCulture) + ", " + GreenGoalYmm.ToString("0.###", CultureInfo.InvariantCulture) + "]");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("固定坐标测试", ex.Message);
                return Result.Failed;
            }
        }

        private static void DrawMarker(Document doc, View view, XYZ point, double halfSizeFeet, OverrideGraphicSettings overrides)
        {
            Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, point);
            SketchPlane sketchPlane = SketchPlane.Create(doc, plane);

            Line diagonal1 = Line.CreateBound(
                new XYZ(point.X - halfSizeFeet, point.Y - halfSizeFeet, point.Z),
                new XYZ(point.X + halfSizeFeet, point.Y + halfSizeFeet, point.Z));
            Line diagonal2 = Line.CreateBound(
                new XYZ(point.X - halfSizeFeet, point.Y + halfSizeFeet, point.Z),
                new XYZ(point.X + halfSizeFeet, point.Y - halfSizeFeet, point.Z));

            ModelCurve curve1 = doc.Create.NewModelCurve(diagonal1, sketchPlane);
            ModelCurve curve2 = doc.Create.NewModelCurve(diagonal2, sketchPlane);
            if (view != null && overrides != null)
            {
                view.SetElementOverrides(curve1.Id, overrides);
                view.SetElementOverrides(curve2.Id, overrides);
            }
        }

        private static double ToFeet(double millimeters)
        {
            return millimeters / 304.8;
        }
    }
}
