using CadToRevit.Models.Rooms.LayoutPlans;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CadToRevit.Services.Rooms.LayoutPlans
{
    public sealed class AhuSubModuleScheduleRow
    {
        public string SubModule { get; set; }

        public string Type { get; set; }

        public string DimensionsMm { get; set; }

        public string Quantity { get; set; }

        public string Remarks { get; set; }

        public string Sequence { get; set; }
    }

    public static class AhuSubModuleScheduleService
    {
        public static List<AhuSubModuleScheduleRow> BuildForPlan(RoomLayoutPlanDto plan)
        {
            return Build(ParseFlowRateNumber(plan != null ? plan.FlowRate : null));
        }

        public static List<AhuSubModuleScheduleRow> Build(int flowRateNumber)
        {
            if (flowRateNumber >= 1 && flowRateNumber <= 5)
            {
                return new List<AhuSubModuleScheduleRow>
                {
                    Row("1st Sub-module", "Airflow Mixing Box + Filter", "1450 x 1600 x 2200", "1", "Module A", "1"),
                    Row("2nd Sub-module", "Coil + Fan Chamber", "1450 x 2200 x 2200", "1", "Module B", "2")
                };
            }

            if (flowRateNumber >= 6 && flowRateNumber <= 7)
            {
                return new List<AhuSubModuleScheduleRow>
                {
                    Row("1st Sub-module", "Airflow Mixing Box + Filter", "1550 x 1800 x 2200", "1", "Module A", "1"),
                    Row("2nd Sub-module", "Coil + Valve Chamber", "1550 x 2400 x 2200", "1", "Module B", "2"),
                    Row("3rd Sub-module", "Fan + EL Chamber", "1800 x 2400 x 2200", "1", "Module C", "3")
                };
            }

            if (flowRateNumber == 8)
            {
                return new List<AhuSubModuleScheduleRow>
                {
                    Row("1st Sub-module", "Airflow Mixing Box + Filter", "1550 x 1800 x 2200", "1", "Module A", "1"),
                    Row("2nd Sub-module", "Cooling Coil + Valve Chamber", "1550 x 2400 x 2200", "1", "Module B", "2"),
                    Row("3rd Sub-module", "Fan Chamber", "1800 x 2400 x 2200", "1", "Module C", "3"),
                    Row("4th Sub-module", "EL Chamber", "1200 x 1800 x 2200", "1", "Module D", "4")
                };
            }

            if (flowRateNumber >= 9 && flowRateNumber <= 10)
            {
                return new List<AhuSubModuleScheduleRow>
                {
                    Row("1st Sub-module", "Airflow Mixing Box + Filter", "1550 x 1800 x 2200", "1", "Module A", "1"),
                    Row("2nd Sub-module", "Cooling Coil + Valve Chamber", "1550 x 2400 x 2200", "1", "Module B", "2"),
                    Row("3rd Sub-module", "Fan Chamber", "1800 x 2400 x 2200", "1", "Module C", "3"),
                    Row("4th Sub-module", "EL Chamber", "1200 x 1800 x 2200", "1", "Module D", "4"),
                    Row("5th Sub-module", "Service Access Chamber", "1200 x 1800 x 2200", "1", "Module E", "5")
                };
            }

            return new List<AhuSubModuleScheduleRow>();
        }

        public static int ParseFlowRateNumber(string flowRate)
        {
            if (string.IsNullOrWhiteSpace(flowRate))
            {
                return 0;
            }

            Match match = Regex.Match(flowRate, "\\d+");
            int value;
            return match.Success && int.TryParse(match.Value, out value) ? value : 0;
        }

        private static AhuSubModuleScheduleRow Row(string subModule, string type, string dimensions, string quantity, string remarks, string sequence)
        {
            return new AhuSubModuleScheduleRow
            {
                SubModule = subModule,
                Type = type,
                DimensionsMm = dimensions,
                Quantity = string.IsNullOrWhiteSpace(sequence) ? quantity : sequence,
                Remarks = remarks,
                Sequence = sequence
            };
        }
    }
}
