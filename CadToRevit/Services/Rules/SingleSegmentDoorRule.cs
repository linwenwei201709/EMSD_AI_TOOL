using Autodesk.Revit.DB;
using CadToRevit.Models;
using System.Collections.Generic;

namespace CadToRevit.Services.Rules
{
    public class SingleSegmentDoorRule : IDoorCandidateRule
    {
        public string Name => "R2";

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            if (doorSegments == null)
            {
                return result;
            }

            foreach (CadSegment segment in doorSegments)
            {
                if (segment == null || segment.IsArc)
                {
                    continue;
                }

                double lengthMm = UnitUtils.ConvertFromInternalUnits(segment.P0.DistanceTo(segment.P1), UnitTypeId.Millimeters);
                if (lengthMm < settings.DoorWidthMinMm || lengthMm > settings.DoorWidthMaxMm)
                {
                    continue;
                }

                XYZ center = new XYZ(
                    (segment.P0.X + segment.P1.X) * 0.5,
                    (segment.P0.Y + segment.P1.Y) * 0.5,
                    (segment.P0.Z + segment.P1.Z) * 0.5);

                result.Add(new DoorCandidate
                {
                    CenterPoint = center,
                    WidthMm = lengthMm,
                    RuleSource = Name,
                    SegmentIds = new List<int> { segment.SegmentId }
                });
            }

            return result;
        }
    }
}
