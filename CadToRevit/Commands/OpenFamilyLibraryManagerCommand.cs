using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Services.Rooms;
using CadToRevit.UI;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class OpenFamilyLibraryManagerCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            FamilyLibraryManagerWindow window = new FamilyLibraryManagerWindow();
            bool? saved = window.ShowDialog();
            if (saved == true)
            {
                RoomCustomFamilyCatalogService.Reload();
            }

            return Result.Succeeded;
        }
    }
}
