using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Rooms;
using System;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class GenerateVentilationDuctCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiApp = commandData != null ? commandData.Application : null;
            UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
            Document doc = uiDoc != null ? uiDoc.Document : null;
            if (uiDoc == null || doc == null)
            {
                return Result.Cancelled;
            }

            try
            {
                Reference equipmentRef = uiDoc.Selection.PickObject(
                    ObjectType.Element,
                    new MechanicalEquipmentSelectionFilter(),
                    "Select an AHU or mechanical equipment instance with an HVAC duct connector.");
                if (equipmentRef == null)
                {
                    return Result.Cancelled;
                }

                FamilyInstance equipment = doc.GetElement(equipmentRef.ElementId) as FamilyInstance;
                if (equipment == null)
                {
                    TaskDialog.Show("Generate Ventilation Duct", "Selected element is not a mechanical equipment family instance.");
                    return Result.Cancelled;
                }

                RoomRigidDuctService.WallPointPickResult wallPick = RoomRigidDuctService.PickWallPoint(uiDoc);
                if (wallPick == null)
                {
                    TaskDialog.Show("Generate Ventilation Duct", "Pick wall point failed.");
                    return Result.Failed;
                }

                if (wallPick.Canceled)
                {
                    return Result.Cancelled;
                }

                if (!wallPick.Succeeded)
                {
                    TaskDialog.Show("Generate Ventilation Duct", wallPick.Message ?? "Pick wall point failed.");
                    return Result.Failed;
                }

                RoomRigidDuctService.CreateRigidDuctResult createResult = RoomRigidDuctService.CreateThreePieceDuctToWall(
                    doc,
                    equipment.Id,
                    wallPick.WallElementId,
                    wallPick.PickPoint,
                    new RoomRigidDuctService.RigidDuctOptions());

                if (createResult == null || !createResult.Succeeded)
                {
                    string error = createResult != null ? createResult.Message : "Create duct failed.";
                    TaskDialog.Show("Generate Ventilation Duct", error ?? "Create duct failed.");
                    return Result.Failed;
                }

                TaskDialog.Show("Generate Ventilation Duct", "Ventilation duct created successfully.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RigidDuct] Command failed=" + ex);
                message = ex.Message;
                TaskDialog.Show("Generate Ventilation Duct", ex.Message);
                return Result.Failed;
            }
        }

        private sealed class MechanicalEquipmentSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                FamilyInstance instance = elem as FamilyInstance;
                if (instance == null)
                {
                    return false;
                }

                Category category = instance.Category;
                return category != null && category.Id.IntegerValue == (int)BuiltInCategory.OST_MechanicalEquipment;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
