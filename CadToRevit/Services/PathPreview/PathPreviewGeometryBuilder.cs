using Autodesk.Revit.DB;
using CadToRevit.Models.Path;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewGeometryBuilder
    {
        internal static List<Solid> BuildSegmentBoxSolids(
            PathPoint3D start,
            PathPoint3D end,
            double boxLengthMm,
            double boxWidthMm,
            double boxHeightMm,
            ElementId materialId)
        {
            List<Solid> solids = new List<Solid>();
            XYZ startPoint = ToModelPoint(start, boxHeightMm * 0.5);
            XYZ endPoint = ToModelPoint(end, boxHeightMm * 0.5);
            if (startPoint == null || endPoint == null)
            {
                return solids;
            }

            XYZ dir = endPoint - startPoint;
            double length = dir.GetLength();
            if (length <= 1e-9)
            {
                return solids;
            }

            XYZ unitDir = dir.Normalize();
            XYZ center = startPoint + (unitDir * (length * 0.5));
            Solid solid = BuildOrientedBoxSolid(
                center,
                unitDir,
                boxLengthMm,
                boxWidthMm,
                boxHeightMm,
                materialId);
            if (solid != null && solid.Faces != null && solid.Faces.Size > 0)
            {
                solids.Add(solid);
            }

            return solids;
        }

        internal static Solid BuildNodeSolid(
            PathPoint3D point,
            double boxLengthMm,
            double boxWidthMm,
            double boxHeightMm,
            ElementId materialId)
        {
            XYZ center = ToModelPoint(point, boxHeightMm * 0.5);
            if (center == null)
            {
                return null;
            }

            return BuildOrientedBoxSolid(
                center,
                XYZ.BasisX,
                boxLengthMm,
                boxWidthMm,
                boxHeightMm,
                materialId);
        }

        internal static Solid BuildPointOrientedBoxSolid(
            PathPoint3D point,
            double orientationRadians,
            double boxLengthMm,
            double boxWidthMm,
            double boxHeightMm,
            ElementId materialId)
        {
            if (point == null)
            {
                return null;
            }

            XYZ center = ToModelPoint(point, boxHeightMm * 0.5);
            if (center == null)
            {
                return null;
            }

            XYZ axisX = new XYZ(
                Math.Cos(orientationRadians),
                Math.Sin(orientationRadians),
                0.0);
            if (axisX.GetLength() <= 1e-9)
            {
                axisX = XYZ.BasisX;
            }

            return BuildOrientedBoxSolid(
                center,
                axisX,
                boxLengthMm,
                boxWidthMm,
                boxHeightMm,
                materialId);
        }

        internal static List<Solid> BuildNodeLabelSolids(
            PathPoint3D point,
            XYZ dirHint,
            string text,
            double boxHeightMm,
            ElementId materialId)
        {
            List<Solid> solids = new List<Solid>();
            XYZ center = ToModelPoint(point, boxHeightMm * 0.5);
            if (center == null || string.IsNullOrWhiteSpace(text))
            {
                return solids;
            }

            XYZ axisX = dirHint;
            if (axisX == null || axisX.GetLength() <= 1e-9)
            {
                axisX = XYZ.BasisX;
            }
            axisX = axisX.Normalize();
            XYZ axisY = XYZ.BasisZ.CrossProduct(axisX);
            if (axisY.GetLength() <= 1e-9)
            {
                axisY = XYZ.BasisY;
            }
            axisY = axisY.Normalize();

            double topZ = center.Z + (boxHeightMm * PathPreviewConstants.MmToFeet * 0.5) +
                          (PathPreviewConstants.LabelTopOffsetMm * PathPreviewConstants.MmToFeet);
            XYZ labelOrigin = new XYZ(center.X, center.Y, topZ);

            List<LetterSpec> letters = BuildLetterSpecs(text.ToUpperInvariant());
            if (letters.Count == 0)
            {
                return solids;
            }

            double totalWidthMm = letters.Sum(x => x.WidthMm) +
                                  Math.Max(0, letters.Count - 1) * PathPreviewConstants.LabelLetterSpacingMm;
            double cursorMm = -totalWidthMm * 0.5;

            foreach (LetterSpec letter in letters)
            {
                foreach (RectMm rect in letter.Rectangles)
                {
                    double xMm = cursorMm + rect.XMm + rect.WidthMm * 0.5;
                    double yMm = rect.YMm + rect.HeightMm * 0.5;
                    XYZ rectCenter = labelOrigin +
                                     axisX.Multiply(xMm * PathPreviewConstants.MmToFeet) +
                                     axisY.Multiply(yMm * PathPreviewConstants.MmToFeet);
                    Solid solid = BuildOrientedBoxSolid(
                        rectCenter,
                        axisX,
                        rect.WidthMm,
                        rect.HeightMm,
                        PathPreviewConstants.LabelHeightMm,
                        materialId);
                    if (solid != null && solid.Faces != null && solid.Faces.Size > 0)
                    {
                        solids.Add(solid);
                    }
                }

                cursorMm += letter.WidthMm + PathPreviewConstants.LabelLetterSpacingMm;
            }

            return solids;
        }

        private static List<LetterSpec> BuildLetterSpecs(string text)
        {
            List<LetterSpec> letters = new List<LetterSpec>();
            foreach (char c in text ?? string.Empty)
            {
                LetterSpec spec = BuildLetterSpec(c);
                if (spec != null)
                {
                    letters.Add(spec);
                }
            }
            return letters;
        }

        private static LetterSpec BuildLetterSpec(char c)
        {
            double h = PathPreviewConstants.LabelLetterHeightMm;
            double s = PathPreviewConstants.LabelStrokeWidthMm;
            double w = h * 0.68;
            double midY = h * 0.5 - s * 0.5;

            switch (c)
            {
                case 'S':
                    return new LetterSpec(w,
                        Rect(0, h - s, w, s),
                        Rect(0, midY, w, s),
                        Rect(0, 0, w, s),
                        Rect(0, midY, s, h * 0.5),
                        Rect(w - s, 0, s, h * 0.5));
                case 'T':
                    return new LetterSpec(w,
                        Rect(0, h - s, w, s),
                        Rect(w * 0.5 - s * 0.5, 0, s, h));
                case 'A':
                    return new LetterSpec(w,
                        Rect(0, 0, s, h),
                        Rect(w - s, 0, s, h),
                        Rect(0, h - s, w, s),
                        Rect(0, midY, w, s));
                case 'R':
                    return new LetterSpec(w,
                        Rect(0, 0, s, h),
                        Rect(0, h - s, w - s * 0.2, s),
                        Rect(0, midY, w - s * 0.2, s),
                        Rect(w - s, midY, s, h * 0.5),
                        Rect(w * 0.52, 0, s, h * 0.5));
                case 'E':
                    return new LetterSpec(w,
                        Rect(0, 0, s, h),
                        Rect(0, h - s, w, s),
                        Rect(0, midY, w * 0.9, s),
                        Rect(0, 0, w, s));
                case 'N':
                    return new LetterSpec(w,
                        Rect(0, 0, s, h),
                        Rect(w - s, 0, s, h),
                        Rect(w * 0.5 - s * 0.5, 0, s, h));
                case 'D':
                    return new LetterSpec(w,
                        Rect(0, 0, s, h),
                        Rect(s, h - s, w - s, s),
                        Rect(s, 0, w - s, s),
                        Rect(w - s, s, s, h - 2 * s));
                default:
                    return null;
            }
        }

        private static RectMm Rect(double xMm, double yMm, double widthMm, double heightMm)
        {
            return new RectMm(xMm, yMm, Math.Max(1.0, widthMm), Math.Max(1.0, heightMm));
        }

        private static XYZ ToModelPoint(PathPoint3D point, double zOffsetMm)
        {
            if (point == null)
            {
                return null;
            }

            return new XYZ(
                point.X * PathPreviewConstants.MmToFeet,
                point.Y * PathPreviewConstants.MmToFeet,
                (point.Z + zOffsetMm) * PathPreviewConstants.MmToFeet);
        }

        private static Solid BuildOrientedBoxSolid(XYZ center, XYZ dir, double lengthMm, double widthMm, double heightMm, ElementId materialId)
        {
            if (center == null)
            {
                return null;
            }

            XYZ axisX = dir;
            if (axisX == null || axisX.GetLength() <= 1e-9)
            {
                axisX = XYZ.BasisX;
            }
            axisX = axisX.Normalize();

            XYZ axisY = XYZ.BasisZ.CrossProduct(axisX);
            if (axisY.GetLength() <= 1e-9)
            {
                axisY = XYZ.BasisY;
            }
            axisY = axisY.Normalize();

            double halfLength = (lengthMm * PathPreviewConstants.MmToFeet) * 0.5;
            double halfWidth = (widthMm * PathPreviewConstants.MmToFeet) * 0.5;

            XYZ p0 = center - (axisX * halfLength) - (axisY * halfWidth);
            XYZ p1 = center + (axisX * halfLength) - (axisY * halfWidth);
            XYZ p2 = center + (axisX * halfLength) + (axisY * halfWidth);
            XYZ p3 = center - (axisX * halfLength) + (axisY * halfWidth);

            return BuildCenteredExtrusionSolid(new List<XYZ> { p0, p1, p2, p3, p0 }, heightMm, materialId);
        }

        private static Solid BuildCenteredExtrusionSolid(List<XYZ> boundary, double thicknessMm, ElementId materialId)
        {
            if (boundary == null || boundary.Count < 4)
            {
                return null;
            }

            List<Curve> curves = new List<Curve>();
            for (int i = 0; i < boundary.Count - 1; i++)
            {
                XYZ a = boundary[i];
                XYZ b = boundary[i + 1];
                if (a == null || b == null || a.DistanceTo(b) <= 1e-9)
                {
                    continue;
                }

                curves.Add(Line.CreateBound(a, b));
            }

            if (curves.Count < 3)
            {
                return null;
            }

            double halfThicknessFeet = (thicknessMm * PathPreviewConstants.MmToFeet) * 0.5;
            Transform baseShift = Transform.CreateTranslation(-XYZ.BasisZ * halfThicknessFeet);
            CurveLoop loop = CurveLoop.CreateViaTransform(CurveLoop.Create(curves), baseShift);
            SolidOptions options = new SolidOptions(materialId, ElementId.InvalidElementId);
            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                XYZ.BasisZ,
                thicknessMm * PathPreviewConstants.MmToFeet,
                options);
        }

        private sealed class RectMm
        {
            internal RectMm(double xMm, double yMm, double widthMm, double heightMm)
            {
                XMm = xMm;
                YMm = yMm;
                WidthMm = widthMm;
                HeightMm = heightMm;
            }

            internal double XMm { get; private set; }
            internal double YMm { get; private set; }
            internal double WidthMm { get; private set; }
            internal double HeightMm { get; private set; }
        }

        private sealed class LetterSpec
        {
            internal LetterSpec(double widthMm, params RectMm[] rectangles)
            {
                WidthMm = widthMm;
                Rectangles = rectangles == null ? new List<RectMm>() : rectangles.ToList();
            }

            internal double WidthMm { get; private set; }
            internal List<RectMm> Rectangles { get; private set; }
        }
    }
}
