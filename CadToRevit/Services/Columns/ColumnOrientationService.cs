using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace CadToRevit.Services.Columns
{
    public static class ColumnOrientationService
    {
        public static void Align(
            Document doc,
            FamilyInstance instance,
            ColumnCandidate candidate,
            IWallDirectionProvider wallProvider,
            ColumnOrientationSettings config)
        {
            if (doc == null || instance == null || candidate == null)
            {
                return;
            }

            ColumnOrientationSettings cfg = config ?? new ColumnOrientationSettings();
            if (!cfg.EnableAutoRotate)
            {
                return;
            }

            XYZ wallDir = wallProvider == null ? null : wallProvider.TryGetNearestDirection(candidate.Center, cfg.WallDirSearchRadiusMm);
            if (wallDir != null)
            {
                // 中文注释：优先对齐最近墙方向，贴墙柱稳定性最高。
                RotateToDirection(doc, instance, candidate.Center, wallDir, cfg);
                return;
            }

            DominantDirectionResult dominant = DominantDirectionFromSegments(candidate.SourceSegments);
            if (dominant.Ok && dominant.Confidence >= cfg.MinDominantDirConfidence)
            {
                // 中文注释：无墙方向时降级为图元主方向投票。
                RotateToDirection(doc, instance, candidate.Center, dominant.Direction, cfg);
                return;
            }

            if (cfg.SnapToOrthoEnable)
            {
                // 中文注释：最终降级仅做正交吸附，避免几度偏差影响观感。
                SnapInstanceToOrthoIfNear(doc, instance, candidate.Center, cfg);
            }
        }

        private static void RotateToDirection(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            XYZ targetDir,
            ColumnOrientationSettings config)
        {
            XYZ target2 = Normalize2D(targetDir);
            if (target2 == null)
            {
                return;
            }

            XYZ current2 = Normalize2D(instance.HandOrientation);
            if (current2 == null)
            {
                return;
            }

            double currentAngle = Math.Atan2(current2.Y, current2.X);
            double targetAngle = Math.Atan2(target2.Y, target2.X);
            if (config.SnapToOrthoEnable)
            {
                targetAngle = SnapAngleToOrtho(targetAngle, config.SnapToOrthoThresholdDeg);
            }

            double delta = NormalizeAngle(targetAngle - currentAngle);
            if (Math.Abs(delta) < 1e-4)
            {
                return;
            }

            Line axis = Line.CreateBound(center, center + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, instance.Id, axis, delta);
        }

        private static void SnapInstanceToOrthoIfNear(
            Document doc,
            FamilyInstance instance,
            XYZ center,
            ColumnOrientationSettings config)
        {
            XYZ current2 = Normalize2D(instance.HandOrientation);
            if (current2 == null)
            {
                return;
            }

            double currentAngle = Math.Atan2(current2.Y, current2.X);
            double snapped = SnapAngleToOrtho(currentAngle, config.SnapToOrthoThresholdDeg);
            double delta = NormalizeAngle(snapped - currentAngle);
            if (Math.Abs(delta) < 1e-4)
            {
                return;
            }

            Line axis = Line.CreateBound(center, center + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, instance.Id, axis, delta);
        }

        private static DominantDirectionResult DominantDirectionFromSegments(IEnumerable<CadSegment> segments)
        {
            const int bins = 36;
            double[] histogram = new double[bins];
            double total = 0.0;
            foreach (CadSegment segment in segments ?? new List<CadSegment>())
            {
                if (segment == null || segment.P0 == null || segment.P1 == null)
                {
                    continue;
                }

                XYZ vec2 = Normalize2D(segment.P1 - segment.P0, false);
                if (vec2 == null)
                {
                    continue;
                }

                double len = new XYZ(segment.P1.X - segment.P0.X, segment.P1.Y - segment.P0.Y, 0).GetLength();
                if (len < 1e-6)
                {
                    continue;
                }

                double angle = Math.Atan2(vec2.Y, vec2.X);
                if (angle < 0)
                {
                    angle += Math.PI;
                }

                if (angle >= Math.PI)
                {
                    angle -= Math.PI;
                }

                int idx = (int)Math.Round((angle / Math.PI) * (bins - 1));
                if (idx < 0)
                {
                    idx = 0;
                }
                else if (idx >= bins)
                {
                    idx = bins - 1;
                }

                histogram[idx] += len;
                total += len;
            }

            if (total < 1e-6)
            {
                return DominantDirectionResult.Fail;
            }

            int maxIndex = 0;
            for (int i = 1; i < bins; i++)
            {
                if (histogram[i] > histogram[maxIndex])
                {
                    maxIndex = i;
                }
            }

            double maxVal = histogram[maxIndex];
            double confidence = maxVal / total;
            double ang = (maxIndex / (double)(bins - 1)) * Math.PI;
            XYZ dir = new XYZ(Math.Cos(ang), Math.Sin(ang), 0);
            return new DominantDirectionResult
            {
                Ok = true,
                Direction = dir,
                Confidence = confidence
            };
        }

        private static double SnapAngleToOrtho(double angleRad, double thresholdDeg)
        {
            double[] snaps = { 0.0, Math.PI * 0.5, Math.PI, Math.PI * 1.5 };
            double threshold = thresholdDeg * Math.PI / 180.0;
            double best = angleRad;
            double bestDiff = double.MaxValue;
            foreach (double snap in snaps)
            {
                double diff = Math.Abs(NormalizeAngle(angleRad - snap));
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = snap;
                }
            }

            return bestDiff <= threshold ? best : angleRad;
        }

        private static XYZ Normalize2D(XYZ vector, bool normalize = true)
        {
            if (vector == null)
            {
                return null;
            }

            XYZ v = new XYZ(vector.X, vector.Y, 0);
            if (v.GetLength() <= 1e-9)
            {
                return null;
            }

            return normalize ? v.Normalize() : v;
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI)
            {
                angle -= Math.PI * 2.0;
            }

            while (angle < -Math.PI)
            {
                angle += Math.PI * 2.0;
            }

            return angle;
        }

        private sealed class DominantDirectionResult
        {
            public static DominantDirectionResult Fail => new DominantDirectionResult
            {
                Ok = false,
                Direction = XYZ.BasisX,
                Confidence = 0.0
            };

            public bool Ok { get; set; }

            public XYZ Direction { get; set; }

            public double Confidence { get; set; }
        }
    }
}
