using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ToggleAhuMaintenanceSpaceCommand : IExternalCommand
    {
        private const string CommandTitle = "AHU Maintenance Space";
        private const string ParameterName = "Show Maintenance Space";
        private const string RoomCustomFamilyPrefix = "ROOM_CUSTOM_FAMILY__";

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
                List<FamilyInstance> targets = FindSelectedAhuInstances(uiDoc, doc);
                if (targets.Count == 0)
                {
                    targets = FindAllAhuInstances(doc);
                }

                targets = targets
                    .Where(x => GetShowMaintenanceSpaceParameter(x) != null)
                    .GroupBy(x => x.Id.IntegerValue)
                    .Select(x => x.First())
                    .ToList();

                if (targets.Count == 0)
                {
                    TaskDialog.Show(CommandTitle, "No AHU equipment with \"Show Maintenance Space\" parameter was found.");
                    return Result.Succeeded;
                }

                bool anyVisible = targets.Any(x => GetShowMaintenanceSpaceParameter(x).AsInteger() == 1);
                int newValue = anyVisible ? 0 : 1;
                int changedCount = 0;

                using (Transaction tx = new Transaction(doc, "Toggle AHU Maintenance Space"))
                {
                    tx.Start();
                    foreach (FamilyInstance target in targets)
                    {
                        Parameter parameter = GetShowMaintenanceSpaceParameter(target);
                        if (parameter == null)
                        {
                            continue;
                        }

                        parameter.Set(newValue);
                        changedCount++;
                    }
                    tx.Commit();
                }

                TryRefreshActiveView(uiDoc);
                string resultText = newValue == 1
                    ? "Maintenance space shown for " + changedCount + " AHU equipment item(s)."
                    : "Maintenance space hidden for " + changedCount + " AHU equipment item(s).";
                // TaskDialog.Show(CommandTitle, resultText);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(CommandTitle, ex.Message);
                return Result.Failed;
            }
        }

        private static List<FamilyInstance> FindSelectedAhuInstances(UIDocument uiDoc, Document doc)
        {
            if (uiDoc == null || doc == null)
            {
                return new List<FamilyInstance>();
            }

            return uiDoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id) as FamilyInstance)
                .Where(IsAhuEquipmentInstance)
                .ToList();
        }

        private static List<FamilyInstance> FindAllAhuInstances(Document doc)
        {
            if (doc == null)
            {
                return new List<FamilyInstance>();
            }

            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
                .Cast<FamilyInstance>()
                .Where(IsAhuEquipmentInstance)
                .ToList();
        }

        private static bool IsAhuEquipmentInstance(FamilyInstance instance)
        {
            if (instance == null || !IsMechanicalEquipment(instance))
            {
                return false;
            }

            if (GetShowMaintenanceSpaceParameter(instance) == null)
            {
                return false;
            }

            return HasRoomCustomFamilyMarker(instance) || HasAhuName(instance);
        }

        private static bool IsMechanicalEquipment(FamilyInstance instance)
        {
            Category category = instance != null ? instance.Category : null;
            return category != null && category.Id.IntegerValue == (int)BuiltInCategory.OST_MechanicalEquipment;
        }

        private static Parameter GetShowMaintenanceSpaceParameter(FamilyInstance instance)
        {
            if (instance == null)
            {
                return null;
            }

            Parameter parameter = instance.LookupParameter(ParameterName);
            if (parameter != null &&
                !parameter.IsReadOnly &&
                parameter.StorageType == StorageType.Integer)
            {
                return parameter;
            }

            return null;
        }

        private static bool HasRoomCustomFamilyMarker(FamilyInstance instance)
        {
            return StartsWithRoomCustomFamilyPrefix(GetParameterText(instance, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)) ||
                   StartsWithRoomCustomFamilyPrefix(GetParameterText(instance, BuiltInParameter.ALL_MODEL_MARK));
        }

        private static string GetParameterText(Element element, BuiltInParameter builtInParameter)
        {
            Parameter parameter = element != null ? element.get_Parameter(builtInParameter) : null;
            return parameter != null ? parameter.AsString() : string.Empty;
        }

        private static bool StartsWithRoomCustomFamilyPrefix(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Trim().StartsWith(RoomCustomFamilyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAhuName(FamilyInstance instance)
        {
            if (ContainsAhu(instance.Name))
            {
                return true;
            }

            FamilySymbol symbol = instance.Symbol;
            if (symbol != null && ContainsAhu(symbol.Name))
            {
                return true;
            }

            Family family = symbol != null ? symbol.Family : null;
            return family != null && ContainsAhu(family.Name);
        }

        private static bool ContainsAhu(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.IndexOf("AHU with Flow rate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("AHU with airflow rate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf("AHU", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryRefreshActiveView(UIDocument uiDoc)
        {
            try
            {
                uiDoc.RefreshActiveView();
            }
            catch
            {
                // The parameter change is already committed; view refresh is best effort.
            }
        }
    }
}
