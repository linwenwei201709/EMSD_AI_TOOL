using CadToRevit.Models.Units;
using System;
using System.IO;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using AcRt = Autodesk.AutoCAD.Runtime;

namespace CadToRevit.Services.Dwg
{
    public sealed class DwgUnitDetectionResult
    {
        public SourceUnit DetectedUnit { get; set; } = SourceUnit.Auto;

        public SourceUnit SuggestedUnit { get; set; } = SourceUnit.Millimeter;

        public bool IsResolved { get; set; }

        public bool HasConflict { get; set; }

        public string Evidence { get; set; } = "Unknown";

        public string WarningMessage { get; set; } = string.Empty;

        public string LunisText { get; set; } = "Unknown";

        public string InsunitsText { get; set; } = "Unknown";

        public SourceUnit Unit
        {
            get { return DetectedUnit; }
            set { DetectedUnit = value; }
        }
    }

    public static class DwgUnitDetector
    {
        private const int LocaleLcid = 1033;
        private static readonly object RuntimeInitLock = new object();
        private static bool _runtimeInitialized;
        private static AcDb.HostApplicationServices _runtimeHost;

        public static DwgUnitDetectionResult Detect(string dwgPath)
        {
            if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
            {
                return Unresolved("Unknown", "Unknown", "Read failed: DWG file does not exist.");
            }

            try
            {
                EnsureRuntimeInitialized();
                using (AcDb.Database db = new AcDb.Database(false, true))
                {
                    db.ReadDwgFile(dwgPath, AcDb.FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    db.CloseInput(true);

                    int lunits = db.Lunits;
                    int luprec = db.Luprec;
                    string lunitsText = MapLunitsText(lunits);
                    string insunitsText = MapInsunitsText(db.Insunits);
                    SourceUnit insunitsUnit = MapInsunitsUnit(db.Insunits);
                    string precisionText = "LUPREC=" + luprec;

                    if (lunits == 4)
                    {
                        return ImperialByLengthFormat(lunitsText, insunitsText, insunitsUnit, precisionText);
                    }

                    if (lunits == 3)
                    {
                        return ImperialByLengthFormat(lunitsText, insunitsText, insunitsUnit, precisionText);
                    }

                    if (lunits == 2)
                    {
                        return DecimalByInsertionUnit(lunitsText, insunitsText, insunitsUnit, precisionText);
                    }

                    if (insunitsUnit != SourceUnit.Auto)
                    {
                        return Resolved(insunitsUnit, insunitsUnit, lunitsText, insunitsText, precisionText, false, string.Empty);
                    }

                    return Unresolved(lunitsText, insunitsText, "LUNITS=" + lunitsText + "; INSUNITS=" + insunitsText + "; " + precisionText);
                }
            }
            catch (Exception ex)
            {
                return Unresolved("Unknown", "Unknown", "Read failed: " + ex.Message);
            }
        }

        private static DwgUnitDetectionResult ImperialByLengthFormat(
            string lunitsText,
            string insunitsText,
            SourceUnit insunitsUnit,
            string precisionText)
        {
            bool hasConflict = insunitsUnit != SourceUnit.Auto && insunitsUnit != SourceUnit.Inch;
            string warning = hasConflict
                ? "Length format indicates Inch, but insertion scale is " + insunitsText + ". Please confirm the source unit."
                : string.Empty;
            return Resolved(SourceUnit.Inch, SourceUnit.Inch, lunitsText, insunitsText, precisionText, hasConflict, warning);
        }

        private static DwgUnitDetectionResult DecimalByInsertionUnit(
            string lunitsText,
            string insunitsText,
            SourceUnit insunitsUnit,
            string precisionText)
        {
            if (insunitsUnit == SourceUnit.Millimeter || insunitsUnit == SourceUnit.Inch)
            {
                return Resolved(insunitsUnit, insunitsUnit, lunitsText, insunitsText, precisionText, false, string.Empty);
            }

            if (insunitsUnit == SourceUnit.Feet || insunitsUnit == SourceUnit.Meter)
            {
                return Resolved(
                    insunitsUnit,
                    insunitsUnit,
                    lunitsText,
                    insunitsText,
                    precisionText,
                    false,
                    "The DWG unit appears to be " + insunitsText + ", but this version only supports Millimeter and Inch for DWG import. Please confirm the source unit.");
            }

            return Unresolved(lunitsText, insunitsText, "LUNITS=" + lunitsText + "; INSUNITS=" + insunitsText + "; " + precisionText);
        }

        private static DwgUnitDetectionResult Resolved(
            SourceUnit detectedUnit,
            SourceUnit suggestedUnit,
            string lunitsText,
            string insunitsText,
            string precisionText,
            bool hasConflict,
            string warning)
        {
            string evidence = "LUNITS=" + lunitsText + "; INSUNITS=" + insunitsText + "; " + precisionText;
            if (hasConflict && detectedUnit == SourceUnit.Inch)
            {
                evidence += "; default Inch by architectural length format";
            }

            return new DwgUnitDetectionResult
            {
                DetectedUnit = detectedUnit,
                SuggestedUnit = NormalizeSuggestedUnit(suggestedUnit),
                IsResolved = true,
                HasConflict = hasConflict,
                Evidence = evidence,
                WarningMessage = warning ?? string.Empty,
                LunisText = lunitsText,
                InsunitsText = insunitsText
            };
        }

        private static DwgUnitDetectionResult Unresolved(string lunitsText, string insunitsText, string evidence)
        {
            string normalizedEvidence = string.IsNullOrWhiteSpace(evidence)
                ? "LUNITS=" + lunitsText + "; INSUNITS=" + insunitsText
                : evidence;

            return new DwgUnitDetectionResult
            {
                DetectedUnit = SourceUnit.Auto,
                SuggestedUnit = SourceUnit.Millimeter,
                IsResolved = false,
                HasConflict = false,
                Evidence = normalizedEvidence,
                WarningMessage = "Please confirm the source unit manually.",
                LunisText = lunitsText,
                InsunitsText = insunitsText
            };
        }

        private static SourceUnit NormalizeSuggestedUnit(SourceUnit unit)
        {
            return unit == SourceUnit.Auto ? SourceUnit.Millimeter : unit;
        }

        private static SourceUnit MapInsunitsUnit(AcDb.UnitsValue value)
        {
            if (Convert.ToInt32(value) == 0)
            {
                return SourceUnit.Auto;
            }

            switch (value)
            {
                case AcDb.UnitsValue.Inches:
                    return SourceUnit.Inch;
                case AcDb.UnitsValue.Feet:
                    return SourceUnit.Feet;
                case AcDb.UnitsValue.Millimeters:
                    return SourceUnit.Millimeter;
                case AcDb.UnitsValue.Meters:
                    return SourceUnit.Meter;
                default:
                    return SourceUnit.Auto;
            }
        }

        private static string MapInsunitsText(AcDb.UnitsValue value)
        {
            if (Convert.ToInt32(value) == 0)
            {
                return "Unitless";
            }

            switch (value)
            {
                case AcDb.UnitsValue.Inches:
                    return "Inches";
                case AcDb.UnitsValue.Feet:
                    return "Feet";
                case AcDb.UnitsValue.Millimeters:
                    return "Millimeters";
                case AcDb.UnitsValue.Meters:
                    return "Meters";
                default:
                    return value.ToString();
            }
        }

        private static string MapLunitsText(int value)
        {
            switch (value)
            {
                case 1:
                    return "Scientific";
                case 2:
                    return "Decimal";
                case 3:
                    return "Engineering";
                case 4:
                    return "Architectural";
                case 5:
                    return "Fractional";
                default:
                    return "Unknown";
            }
        }

        private static void EnsureRuntimeInitialized()
        {
            if (_runtimeInitialized)
            {
                return;
            }

            lock (RuntimeInitLock)
            {
                if (_runtimeInitialized)
                {
                    return;
                }

                _runtimeHost = new RevitHostServices();
                AcRt.RuntimeSystem.Initialize(_runtimeHost, LocaleLcid);
                _runtimeInitialized = true;
            }
        }

        private sealed class RevitHostServices : AcDb.HostApplicationServices
        {
            public override string FindFile(string fileName, AcDb.Database database, AcDb.FindFileHint hint)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return string.Empty;
                }

                return File.Exists(fileName) ? fileName : fileName;
            }
        }
    }
}
