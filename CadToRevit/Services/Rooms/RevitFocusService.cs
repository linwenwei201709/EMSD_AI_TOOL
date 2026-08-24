using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Rooms;
using System.Linq;

namespace CadToRevit.Services.Rooms
{
    public static class RevitFocusService
    {
        public static void Focus(UIDocument uiDoc, RoomCandidate candidate)
        {
            if (uiDoc == null || candidate == null)
            {
                return;
            }

            if (candidate.RevitRoomId != null && candidate.RevitRoomId != ElementId.InvalidElementId)
            {
                uiDoc.ShowElements(candidate.RevitRoomId);
                return;
            }

            BoundingBoxXYZ box = candidate.BBox;
            if (box == null)
            {
                return;
            }

            UIView uiView = uiDoc.GetOpenUIViews()
                .FirstOrDefault(x => x != null && x.ViewId == uiDoc.ActiveView.Id);
            if (uiView == null)
            {
                return;
            }

            XYZ min = new XYZ(box.Min.X, box.Min.Y, uiDoc.ActiveView.Origin.Z);
            XYZ max = new XYZ(box.Max.X, box.Max.Y, uiDoc.ActiveView.Origin.Z);
            uiView.ZoomAndCenterRectangle(min, max);
        }
    }
}
