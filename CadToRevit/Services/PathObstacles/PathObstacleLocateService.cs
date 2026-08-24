using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.PathObstacles
{
    public static class PathObstacleLocateService
    {
        public static bool Locate(UIApplication uiApp, PathObstacleRecord record)
        {
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null || record == null)
            {
                return false;
            }

            Element element = PathObstacleStoreService.FindElement(doc, record);
            if (element == null)
            {
                return false;
            }

            uiDoc.Selection.SetElementIds(new List<ElementId> { element.Id });
            ZoomToElement(uiDoc, doc.ActiveView, element);
            return true;
        }

        private static void ZoomToElement(UIDocument uiDoc, View activeView, Element element)
        {
            try
            {
                BoundingBoxXYZ box = element.get_BoundingBox(activeView) ?? element.get_BoundingBox(null);
                if (box == null)
                {
                    return;
                }

                XYZ min = box.Min;
                XYZ max = box.Max;
                double span = Math.Max(Math.Max(max.X - min.X, max.Y - min.Y), 1.0);
                double pad = span * 1.25;
                XYZ zoomMin = new XYZ(min.X - pad, min.Y - pad, min.Z - pad);
                XYZ zoomMax = new XYZ(max.X + pad, max.Y + pad, max.Z + pad);

                UIView uiView = uiDoc.GetOpenUIViews()
                    .FirstOrDefault(view => view.ViewId == activeView.Id);
                if (uiView != null)
                {
                    uiView.ZoomAndCenterRectangle(zoomMin, zoomMax);
                }
            }
            catch
            {
            }
        }
    }
}
