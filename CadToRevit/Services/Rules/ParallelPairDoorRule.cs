using Autodesk.Revit.DB;
using CadToRevit.Models;
using System;
using System.Collections.Generic;

namespace CadToRevit.Services.Rules
{
    public class ParallelPairDoorRule : IDoorCandidateRule
    {
        public string Name => "R1";

        public IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings)
        {
            List<DoorCandidate> result = new List<DoorCandidate>();
            if (doorSegments == null || doorSegments.Count < 2)
            {
                return result;
            }

            double cosTol = Math.Cos(settings.ParallelAngleTolDeg * Math.PI / 180.0);

            for (int i = 0; i < doorSegments.Count; i++)
            {
                CadSegment a = doorSegments[i];
                if (a == null || a.IsArc)
                {
                    continue;
                }
                double lenAmm = ToMm(a.P0.DistanceTo(a.P1));
                if (lenAmm < settings.SegmentLengthMinMm || lenAmm > settings.SegmentLengthMaxMm)
                {
                    continue;
                }

                XYZ dirA = Normalize2D(a.P1 - a.P0);
                for (int j = i + 1; j < doorSegments.Count; j++)
                {
                    CadSegment b = doorSegments[j];
                    if (b == null || b.IsArc)
                    {
                        continue;
                    }
                    double lenBmm = ToMm(b.P0.DistanceTo(b.P1));
                    if (lenBmm < settings.SegmentLengthMinMm || lenBmm > settings.SegmentLengthMaxMm)
                    {
                        continue;
                    }

                    XYZ dirB = Normalize2D(b.P1 - b.P0);
                    double dot = Math.Abs(Dot2D(dirA, dirB));
                    if (dot < cosTol)
                    {
                        continue;
                    }

                    double widthMm = ToMm(Math.Abs(SignedDistanceFeet(a.P0, b.P0, dirA)));
                    if (widthMm < settings.DoorWidthMinMm || widthMm > settings.DoorWidthMaxMm)
                    {
                        continue;
                    }

                    double overlapMm = ToMm(ComputeOverlapFeet(a, b, dirA));
                    if (overlapMm < settings.OverlapMinMm)
                    {
                        continue;
                    }

                    XYZ center = Midpoint(Midpoint(a.P0, a.P1), Midpoint(b.P0, b.P1));
                    result.Add(new DoorCandidate
                    {
                        CenterPoint = center,
                        WidthMm = widthMm,
                        RuleSource = Name,
                        SegmentIds = new List<int> { a.SegmentId, b.SegmentId }
                    });
                }
            }

            return result;
        }

        private static double ComputeOverlapFeet(CadSegment a, CadSegment b, XYZ u)
        {
            double a0 = Dot2D(a.P0, u);
            double a1 = Dot2D(a.P1, u);
            if (a0 > a1)
            {
                Swap(ref a0, ref a1);
            }

            double b0 = Dot2D(b.P0, u);
            double b1 = Dot2D(b.P1, u);
            if (b0 > b1)
            {
                Swap(ref b0, ref b1);
            }

            return Math.Max(0.0, Math.Min(a1, b1) - Math.Max(a0, b0));
        }

        private static double SignedDistanceFeet(XYZ pA, XYZ pB, XYZ dir)
        {
            XYZ n = new XYZ(-dir.Y, dir.X, 0);
            return Dot2D(pB - pA, n);
        }

        private static XYZ Normalize2D(XYZ v)
        {
            double len = Math.Sqrt((v.X * v.X) + (v.Y * v.Y));
            if (len < 1e-9)
            {
                return new XYZ(1, 0, 0);
            }

            return new XYZ(v.X / len, v.Y / len, 0);
        }

        private static double Dot2D(XYZ a, XYZ b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        private static XYZ Midpoint(XYZ a, XYZ b)
        {
            return new XYZ((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static double ToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        private static void Swap(ref double x, ref double y)
        {
            double t = x;
            x = y;
            y = t;
        }
    }
}
