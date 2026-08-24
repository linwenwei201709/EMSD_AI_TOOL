using Autodesk.Revit.DB;

namespace CadToRevit.Services.Columns
{
    public interface IWallDirectionProvider
    {
        XYZ TryGetNearestDirection(XYZ point, double radiusMm);
    }
}
