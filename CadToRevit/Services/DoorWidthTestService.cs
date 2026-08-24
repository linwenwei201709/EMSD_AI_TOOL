using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public static class DoorWidthTestService
    {
        public sealed class DoorWidthTestResult
        {
            public int TotalDoors { get; set; }
            public int SuccessCount { get; set; }
            public int FailedCount { get; set; }
            public int InstanceWriteCount { get; set; }
            public int TypeWriteCount { get; set; }
            public int NoneWriteCount { get; set; }
        }

        public static DoorWidthTestResult SetAllDoorsWidth(Document doc, double targetWidthMm)
        {
            DoorWidthTestResult result = new DoorWidthTestResult();
            if (doc == null || targetWidthMm <= 0.0)
            {
                return result;
            }

            List<FamilyInstance> doors = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Doors)
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>()
                .Where(x => x != null)
                .ToList();
            result.TotalDoors = doors.Count;
            if (doors.Count == 0)
            {
                return result;
            }

            using (Transaction tx = new Transaction(doc, "Set Door Width Test"))
            {
                tx.Start();
                foreach (FamilyInstance door in doors)
                {
                    string writeTarget;
                    bool ok = TryWriteDoorWidth(door, targetWidthMm, out writeTarget);
                    double finalWidthMm = ReadDoorWidthMm(door);
                    FamilySymbol symbol = door.Symbol;
                    string familyName = symbol == null ? string.Empty : symbol.FamilyName;
                    string typeName = symbol == null ? string.Empty : symbol.Name;

                    if (ok)
                    {
                        result.SuccessCount++;
                    }
                    else
                    {
                        result.FailedCount++;
                    }

                    if (string.Equals(writeTarget, "Instance", StringComparison.OrdinalIgnoreCase))
                    {
                        result.InstanceWriteCount++;
                    }
                    else if (string.Equals(writeTarget, "Type", StringComparison.OrdinalIgnoreCase))
                    {
                        result.TypeWriteCount++;
                    }
                    else
                    {
                        result.NoneWriteCount++;
                    }

                    DiagnosticRecorder.AppendDebug(
                        "[DoorWidthTest] DoorId=" + door.Id.IntegerValue +
                        ", FamilyName=" + familyName +
                        ", TypeName=" + typeName +
                        ", TargetWidthMm=" + targetWidthMm.ToString("F1") +
                        ", WriteTarget=" + writeTarget +
                        ", WriteSuccess=" + ok +
                        ", FinalWidthMm=" + finalWidthMm.ToString("F1"));
                }

                tx.Commit();
            }

            return result;
        }

        // Write order must be: instance parameter first, then type parameter.
        private static bool TryWriteDoorWidth(FamilyInstance door, double widthMm, out string writeTarget)
        {
            writeTarget = "None";
            if (door == null || widthMm <= 0.0)
            {
                return false;
            }

            double widthFt = UnitUtils.ConvertToInternalUnits(widthMm, UnitTypeId.Millimeters);
            if (TrySetLengthParameter(door.LookupParameter("Width"), widthFt) ||
                TrySetLengthParameter(door.LookupParameter("宽度"), widthFt) ||
                TrySetLengthParameter(door.LookupParameter("寬度"), widthFt))
            {
                writeTarget = "Instance";
                return true;
            }

            FamilySymbol symbol = door.Symbol;
            if (symbol != null &&
                (TrySetLengthParameter(symbol.LookupParameter("Width"), widthFt) ||
                 TrySetLengthParameter(symbol.LookupParameter("宽度"), widthFt) ||
                 TrySetLengthParameter(symbol.LookupParameter("寬度"), widthFt)))
            {
                writeTarget = "Type";
                return true;
            }

            return false;
        }

        private static bool TrySetLengthParameter(Parameter parameter, double valueFt)
        {
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
            {
                return false;
            }

            return parameter.Set(valueFt);
        }

        private static double ReadDoorWidthMm(FamilyInstance door)
        {
            if (door == null)
            {
                return 0.0;
            }

            Parameter instance = door.LookupParameter("Width") ?? door.LookupParameter("宽度") ?? door.LookupParameter("寬度");
            if (instance != null && instance.StorageType == StorageType.Double)
            {
                return UnitUtils.ConvertFromInternalUnits(instance.AsDouble(), UnitTypeId.Millimeters);
            }

            FamilySymbol symbol = door.Symbol;
            Parameter type = symbol == null ? null : (symbol.LookupParameter("Width") ?? symbol.LookupParameter("宽度") ?? symbol.LookupParameter("寬度"));
            if (type != null && type.StorageType == StorageType.Double)
            {
                return UnitUtils.ConvertFromInternalUnits(type.AsDouble(), UnitTypeId.Millimeters);
            }

            return 0.0;
        }
    }
}
