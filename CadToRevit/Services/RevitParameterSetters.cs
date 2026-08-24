using Autodesk.Revit.DB;

namespace CadToRevit.Services
{
    public static class RevitParameterSetters
    {
        public static bool TrySetInstanceLength(FamilyInstance inst, BuiltInParameter bip, double mm)
        {
            if (inst == null || mm <= 0)
            {
                return false;
            }

            try
            {
                Parameter p = inst.get_Parameter(bip);
                return TrySetLengthParameter(p, mm);
            }
            catch
            {
                return false;
            }
        }

        public static bool TrySetByNames(Element e, double mm, params string[] names)
        {
            if (e == null || mm <= 0 || names == null || names.Length == 0)
            {
                return false;
            }

            foreach (string name in names)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                Parameter p = e.LookupParameter(name);
                if (TrySetLengthParameter(p, mm))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TrySetTypeByNames(FamilySymbol symbol, double mm, params string[] names)
        {
            return TrySetByNames(symbol, mm, names);
        }

        private static bool TrySetLengthParameter(Parameter p, double mm)
        {
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double || mm <= 0)
            {
                return false;
            }

            try
            {
                double ft = UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
                p.Set(ft);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
