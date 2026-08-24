using Autodesk.Revit.DB;
using Autodesk.Revit.Exceptions;
using Autodesk.Revit.UI;
using CadToRevit.Models.Path;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewCoordinateCaptureService
    {
        internal static void Run(UIApplication uiApp)
        {
            UIDocument uiDoc = uiApp == null ? null : uiApp.ActiveUIDocument;
            if (uiDoc == null || uiDoc.Document == null)
            {
                return;
            }

            Document doc = uiDoc.Document;
            View activeView = doc.ActiveView;
            RevitLinkInstance linkInstance = ValidateAndGetLinkInstance(doc, activeView);
            if (linkInstance == null)
            {
                return;
            }

            EnsureHorizontalSketchPlane(doc, activeView, linkInstance);

            List<XYZ> hostPoints = new List<XYZ>();
            while (true)
            {
                try
                {
                    XYZ point = uiDoc.Selection.PickPoint("\u4f9d\u6b21\u62fe\u53d6\u8def\u5f84\u70b9\uff0c\u6309 ESC \u7ed3\u675f\u3002");
                    if (point != null)
                    {
                        hostPoints.Add(point);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }
            }

            if (hostPoints.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[PathCapture] begin");
                DiagnosticRecorder.AppendDebug("[PathCapture] docTitle=" + (doc.Title ?? string.Empty));
                DiagnosticRecorder.AppendDebug("[PathCapture] viewName=" + (activeView == null ? string.Empty : activeView.Name));
                DiagnosticRecorder.AppendDebug("[PathCapture] noPointsPicked=True");
                DiagnosticRecorder.AppendDebug("[PathCapture] end");
                TaskDialog.Show("\u62fe\u53d6\u8def\u5f84\u5750\u6807", "\u672a\u62fe\u53d6\u5230\u4efb\u4f55\u70b9\uff0c\u672a\u751f\u6210\u8def\u5f84\u5750\u6807\u3002");
                return;
            }

            Transform totalTransform = linkInstance.GetTotalTransform();
            Transform inverse = totalTransform.Inverse;
            List<PathPoint3D> ifcLocalPoints = hostPoints
                .Select(x => ToIfcLocalPoint(inverse, x))
                .ToList();

            WriteLogs(doc, activeView, linkInstance, totalTransform, hostPoints, ifcLocalPoints);
            ShowCompletionDialog(hostPoints.Count);
        }

        private static RevitLinkInstance ValidateAndGetLinkInstance(Document doc, View activeView)
        {
            if (doc == null)
            {
                return null;
            }

            bool titleMatches = (doc.Title ?? string.Empty).StartsWith(PathPreviewConstants.PreviewProjectTitlePrefix, StringComparison.OrdinalIgnoreCase);
            bool hasPreviewView = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Any(x => !x.IsTemplate && string.Equals(x.Name, PathPreviewConstants.PreviewViewName, StringComparison.OrdinalIgnoreCase));
            RevitLinkInstance linkInstance = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>()
                .FirstOrDefault();

            if (!titleMatches && !(hasPreviewView && linkInstance != null))
            {
                DiagnosticRecorder.AppendDebug("[PathCapture] invalidContext=NotPreviewHost");
                TaskDialog.Show("\u62fe\u53d6\u8def\u5f84\u5750\u6807", "\u8bf7\u5148\u6253\u5f00\u8def\u5f84\u9884\u89c8\uff0c\u518d\u6267\u884c\u62fe\u53d6\u8def\u5f84\u5750\u6807\u3002");
                return null;
            }

            if (activeView == null || !string.Equals(activeView.Name, PathPreviewConstants.PreviewViewName, StringComparison.OrdinalIgnoreCase))
            {
                DiagnosticRecorder.AppendDebug("[PathCapture] invalidContext=NotPreview3DView");
                TaskDialog.Show("\u62fe\u53d6\u8def\u5f84\u5750\u6807", "\u8bf7\u5148\u5207\u6362\u5230 AI_PATH_PREVIEW_3D \u89c6\u56fe\u3002");
                return null;
            }

            if (linkInstance == null)
            {
                DiagnosticRecorder.AppendDebug("[PathCapture] invalidContext=MissingRevitLinkInstance");
                TaskDialog.Show("\u62fe\u53d6\u8def\u5f84\u5750\u6807", "\u5f53\u524d\u9884\u89c8\u6587\u6863\u4e2d\u672a\u627e\u5230 RevitLinkInstance\u3002");
                return null;
            }

            return linkInstance;
        }

        private static void EnsureHorizontalSketchPlane(Document doc, View activeView, RevitLinkInstance linkInstance)
        {
            if (doc == null || activeView == null)
            {
                throw new System.InvalidOperationException("\u65e0\u6cd5\u521b\u5efa\u5de5\u4f5c\u5e73\u9762\u3002");
            }

            double planeZ = 0.0;
            BoundingBoxXYZ box = linkInstance == null ? null : (linkInstance.get_BoundingBox(activeView) ?? linkInstance.get_BoundingBox(null));
            if (box != null && box.Min != null)
            {
                planeZ = box.Min.Z;
            }

            using (Transaction tx = new Transaction(doc, "Prepare Path Capture Work Plane"))
            {
                tx.Start();
                Plane plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0.0, 0.0, planeZ));
                SketchPlane sketchPlane = SketchPlane.Create(doc, plane);
                activeView.SketchPlane = sketchPlane;
                tx.Commit();
            }
        }

        private static PathPoint3D ToIfcLocalPoint(Transform inverse, XYZ hostPoint)
        {
            XYZ ifcLocalFeet = inverse.OfPoint(hostPoint);
            return new PathPoint3D(
                ifcLocalFeet.X * 304.8,
                ifcLocalFeet.Y * 304.8,
                ifcLocalFeet.Z * 304.8);
        }

        private static void WriteLogs(
            Document doc,
            View activeView,
            RevitLinkInstance linkInstance,
            Transform totalTransform,
            List<XYZ> hostPoints,
            List<PathPoint3D> ifcLocalPoints)
        {
            DiagnosticRecorder.AppendDebug("[PathCapture] begin");
            DiagnosticRecorder.AppendDebug("[PathCapture] docTitle=" + (doc == null ? string.Empty : (doc.Title ?? string.Empty)));
            DiagnosticRecorder.AppendDebug("[PathCapture] viewName=" + (activeView == null ? string.Empty : (activeView.Name ?? string.Empty)));
            DiagnosticRecorder.AppendDebug("[PathCapture] linkInstanceId=" + (linkInstance == null ? string.Empty : linkInstance.Id.IntegerValue.ToString(CultureInfo.InvariantCulture)));
            DiagnosticRecorder.AppendDebug("[PathCapture] totalTransform=" + FormatTransform(totalTransform));

            for (int i = 0; i < hostPoints.Count; i++)
            {
                XYZ hostPoint = hostPoints[i];
                PathPoint3D ifcLocalPoint = i < ifcLocalPoints.Count ? ifcLocalPoints[i] : null;
                DiagnosticRecorder.AppendDebug("[PathCapture] point[" + i.ToString(CultureInfo.InvariantCulture) + "].hostFeet=" + FormatPointFeet(hostPoint));
                DiagnosticRecorder.AppendDebug("[PathCapture] point[" + i.ToString(CultureInfo.InvariantCulture) + "].ifcLocalMm=" + FormatPointMm(ifcLocalPoint));
            }

            string json = BuildJson(ifcLocalPoints);
            DiagnosticRecorder.AppendDebug("[PathCapture] json=" + json);
            DiagnosticRecorder.AppendDebug("[PathCapture] end");
        }

        private static string BuildJson(List<PathPoint3D> points)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"pathId\":\"PICKED_DEMO_001\",");
            sb.Append("\"coordinateBase\":\"InternalOrigin\",");
            sb.Append("\"frame\":\"IfcLocal\",");
            sb.Append("\"unit\":\"mm\",");
            sb.Append("\"points\":[");

            for (int i = 0; i < (points == null ? 0 : points.Count); i++)
            {
                PathPoint3D point = points[i] ?? new PathPoint3D();
                if (i > 0)
                {
                    sb.Append(",");
                }

                sb.Append("{");
                sb.Append("\"x\":").Append(point.X.ToString("F1", CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"y\":").Append(point.Y.ToString("F1", CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"z\":").Append(point.Z.ToString("F1", CultureInfo.InvariantCulture));
                sb.Append("}");
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static string FormatTransform(Transform transform)
        {
            if (transform == null)
            {
                return "(null)";
            }

            return "Origin=" + FormatPointFeet(transform.Origin) +
                   ", BasisX=" + FormatPointFeet(transform.BasisX) +
                   ", BasisY=" + FormatPointFeet(transform.BasisY) +
                   ", BasisZ=" + FormatPointFeet(transform.BasisZ);
        }

        private static string FormatPointFeet(XYZ point)
        {
            if (point == null)
            {
                return "(null)";
            }

            return "(" +
                   point.X.ToString("F6", CultureInfo.InvariantCulture) + "," +
                   point.Y.ToString("F6", CultureInfo.InvariantCulture) + "," +
                   point.Z.ToString("F6", CultureInfo.InvariantCulture) + ")";
        }

        private static string FormatPointMm(PathPoint3D point)
        {
            if (point == null)
            {
                return "(null)";
            }

            return "(" +
                   point.X.ToString("F1", CultureInfo.InvariantCulture) + "," +
                   point.Y.ToString("F1", CultureInfo.InvariantCulture) + "," +
                   point.Z.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        private static void ShowCompletionDialog(int pointCount)
        {
            if (pointCount < 2)
            {
                TaskDialog.Show("\u62fe\u53d6\u8def\u5f84\u5750\u6807", "\u5df2\u8bb0\u5f55\u70b9\u6570\uff1a" + pointCount.ToString(CultureInfo.InvariantCulture) + "\n\u65e5\u5fd7\u5df2\u5199\u5165\u3002\n\u70b9\u6570\u8fc7\u5c11\uff0c\u4ec5\u4f9b\u8c03\u8bd5\uff0c\u4e0d\u5efa\u8bae\u4f5c\u4e3a\u8def\u5f84\u3002\n\u8bf7\u4ece\u65e5\u5fd7\u590d\u5236 JSON \u56de\u586b\u5230 DemoPathDataService\u3002");
                return;
            }

            TaskDialog.Show("\u62fe\u53d6\u8def\u5f84\u5750\u6807", "\u5df2\u8bb0\u5f55\u70b9\u6570\uff1a" + pointCount.ToString(CultureInfo.InvariantCulture) + "\n\u65e5\u5fd7\u5df2\u5199\u5165\u3002\n\u8bf7\u4ece\u65e5\u5fd7\u590d\u5236 JSON \u56de\u586b\u5230 DemoPathDataService\u3002");
        }
    }
}
