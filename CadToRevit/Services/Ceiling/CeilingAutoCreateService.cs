using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services.Ceiling
{
    public sealed class CeilingCreateResult
    {
        public int TotalCircuitCount { get; set; }
        public int ClosedCircuitCount { get; set; }
        public int SkippedByMinArea { get; set; }
        public int CreatedCount { get; set; }
        public int CleanupDeletedCount { get; set; }
        public List<string> Failures { get; set; } = new List<string>();
        public string LogPath { get; set; }
    }

    public static class CeilingAutoCreateService
    {
        public static CeilingCreateResult Create(
            Document doc,
            ElementId levelId,
            ElementId ceilingTypeId,
            UI.CeilingGenerationMode generationMode,
            double ceilingHeightMm,
            double minAreaM2,
            IList<ElementId> previewTempLineIds,
            bool autoCleanupTempLines)
        {
            CeilingCreateResult result = new CeilingCreateResult();
            Level level = doc.GetElement(levelId) as Level;
            CeilingType type = doc.GetElement(ceilingTypeId) as CeilingType;
            if (level == null || type == null)
            {
                result.Failures.Add("Invalid level/ceiling type.");
                result.LogPath = CeilingDetectionService.WriteLog(
                    "Create", levelId, ceilingTypeId, ceilingHeightMm, minAreaM2,
                    new CeilingDetectionResult(), result.Failures);
                return result;
            }

            double minAreaFt2 = UnitUtils.ConvertToInternalUnits(minAreaM2, UnitTypeId.SquareMeters);
            double heightFt = UnitUtils.ConvertToInternalUnits(ceilingHeightMm, UnitTypeId.Millimeters);
            List<ElementId> tempRoomIds = new List<ElementId>();

            using (TransactionGroup tg = new TransactionGroup(doc, "Auto Create Ceilings"))
            {
                tg.Start();
                using (Transaction tx = new Transaction(doc, "Create Ceilings From Closed Circuits"))
                {
                    tx.Start();
                    PlanTopology topology = doc.get_PlanTopology(level);
                    if (topology == null)
                    {
                        result.Failures.Add("No PlanTopology.");
                        tx.RollBack();
                        tg.RollBack();
                        result.LogPath = CeilingDetectionService.WriteLog(
                            "Create", levelId, ceilingTypeId, ceilingHeightMm, minAreaM2,
                            new CeilingDetectionResult(), result.Failures);
                        return result;
                    }

                    List<Curve> unionBoundaryCurves = new List<Curve>();
                    foreach (PlanCircuit circuit in topology.Circuits)
                    {
                        result.TotalCircuitCount++;
                        if (circuit == null || circuit.IsRoomLocated)
                        {
                            continue;
                        }

                        if (circuit.Area <= 1e-9)
                        {
                            continue;
                        }

                        result.ClosedCircuitCount++;
                        if (circuit.Area < minAreaFt2)
                        {
                            result.SkippedByMinArea++;
                            continue;
                        }

                        if (generationMode == UI.CeilingGenerationMode.OuterBoundary)
                        {
                            CollectCircuitBoundaryCurves(doc, circuit, tempRoomIds, unionBoundaryCurves, result);
                            continue;
                        }

                        TryCreateOneCeiling(doc, circuit, ceilingTypeId, levelId, heightFt, tempRoomIds, result);
                    }

                    if (generationMode == UI.CeilingGenerationMode.OuterBoundary)
                    {
                        CurveLoop outerLoop = BuildOuterLoopFromUnionBoundaries(unionBoundaryCurves, doc);
                        if (outerLoop == null)
                        {
                            result.Failures.Add("No valid outer boundary loop.");
                        }
                        else
                        {
                            double areaM2 = CalculateLoopAreaM2(outerLoop);
                            if (areaM2 < minAreaM2)
                            {
                                result.SkippedByMinArea++;
                                result.Failures.Add("Outer boundary area below minimum.");
                            }
                            else
                            {
                                TryCreateOneCeilingByLoop(doc, outerLoop, ceilingTypeId, levelId, heightFt, result);
                            }
                        }
                    }

                    foreach (ElementId id in tempRoomIds)
                    {
                        try
                        {
                            doc.Delete(id);
                        }
                        catch
                        {
                        }
                    }

                    // 自动清理预览阶段创建的临时补线，防止污染正式模型。
                    if (autoCleanupTempLines && previewTempLineIds != null && previewTempLineIds.Count > 0)
                    {
                        foreach (ElementId id in previewTempLineIds)
                        {
                            if (id == null || id == ElementId.InvalidElementId)
                            {
                                continue;
                            }

                            try
                            {
                                if (doc.GetElement(id) != null)
                                {
                                    doc.Delete(id);
                                    result.CleanupDeletedCount++;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }

                    tx.Commit();
                }
                tg.Assimilate();
            }

            result.LogPath = CeilingDetectionService.WriteLog(
                generationMode == UI.CeilingGenerationMode.OuterBoundary ? "CreateOuterBoundary" : "Create",
                levelId, ceilingTypeId, ceilingHeightMm, minAreaM2,
                new CeilingDetectionResult
                {
                    TotalCircuitCount = result.TotalCircuitCount,
                    ClosedCircuitCount = result.ClosedCircuitCount,
                    SkippedByMinArea = result.SkippedByMinArea
                },
                result.Failures);
            return result;
        }

        private static void TryCreateOneCeiling(
            Document doc,
            PlanCircuit circuit,
            ElementId ceilingTypeId,
            ElementId levelId,
            double heightFt,
            List<ElementId> tempRoomIds,
            CeilingCreateResult result)
        {
            Room tempRoom = null;
            try
            {
                tempRoom = doc.Create.NewRoom(null, circuit);
                if (tempRoom == null)
                {
                    result.Failures.Add("Room create failed for circuit.");
                    return;
                }

                tempRoomIds.Add(tempRoom.Id);
                var boundaries = tempRoom.GetBoundarySegments(new SpatialElementBoundaryOptions());
                List<CurveLoop> loops = BuildCurveLoops(boundaries, doc);
                if (loops.Count == 0)
                {
                    result.Failures.Add("No valid boundary loop.");
                    return;
                }

                Autodesk.Revit.DB.Ceiling ceiling = Autodesk.Revit.DB.Ceiling.Create(doc, loops, ceilingTypeId, levelId);
                if (ceiling == null)
                {
                    result.Failures.Add("Ceiling.Create returned null.");
                    return;
                }

                Parameter p = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(heightFt);
                }
                result.CreatedCount++;
            }
            catch (Exception ex)
            {
                if (result.Failures.Count < 50)
                {
                    result.Failures.Add(ex.Message);
                }
            }
        }

        private static void CollectCircuitBoundaryCurves(
            Document doc,
            PlanCircuit circuit,
            List<ElementId> tempRoomIds,
            List<Curve> targetCurves,
            CeilingCreateResult result)
        {
            Room tempRoom = null;
            try
            {
                tempRoom = doc.Create.NewRoom(null, circuit);
                if (tempRoom == null)
                {
                    return;
                }

                tempRoomIds.Add(tempRoom.Id);
                IList<IList<BoundarySegment>> boundaries = tempRoom.GetBoundarySegments(new SpatialElementBoundaryOptions());
                if (boundaries == null)
                {
                    return;
                }

                foreach (IList<BoundarySegment> boundary in boundaries)
                {
                    if (boundary == null)
                    {
                        continue;
                    }

                    foreach (BoundarySegment seg in boundary)
                    {
                        Curve c = seg == null ? null : seg.GetCurve();
                        if (c == null || c.Length <= 1e-9)
                        {
                            continue;
                        }
                        targetCurves.Add(c);
                    }
                }
            }
            catch (Exception ex)
            {
                if (result.Failures.Count < 50)
                {
                    result.Failures.Add(ex.Message);
                }
            }
        }

        private static void TryCreateOneCeilingByLoop(
            Document doc,
            CurveLoop loop,
            ElementId ceilingTypeId,
            ElementId levelId,
            double heightFt,
            CeilingCreateResult result)
        {
            try
            {
                Autodesk.Revit.DB.Ceiling ceiling = Autodesk.Revit.DB.Ceiling.Create(doc, new List<CurveLoop> { loop }, ceilingTypeId, levelId);
                if (ceiling == null)
                {
                    result.Failures.Add("Ceiling.Create returned null.");
                    return;
                }

                Parameter p = ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM);
                if (p != null && !p.IsReadOnly)
                {
                    p.Set(heightFt);
                }
                result.CreatedCount++;
            }
            catch (Exception ex)
            {
                if (result.Failures.Count < 50)
                {
                    result.Failures.Add(ex.Message);
                }
            }
        }

        private static CurveLoop BuildOuterLoopFromUnionBoundaries(List<Curve> curves, Document doc)
        {
            if (curves == null || curves.Count == 0)
            {
                return null;
            }

            double shortTol = doc == null ? 1e-6 : doc.Application.ShortCurveTolerance;
            double connectTol = Math.Max(shortTol * 5.0, UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters));
            Dictionary<string, EdgeBucket> edgeMap = new Dictionary<string, EdgeBucket>();

            foreach (Curve c in curves)
            {
                Line l = c as Line;
                if (l == null || l.Length < shortTol * 1.05)
                {
                    continue;
                }

                XYZ a = l.GetEndPoint(0);
                XYZ b = l.GetEndPoint(1);
                string key = BuildUndirectedEdgeKey(a, b, connectTol);
                EdgeBucket bucket;
                if (!edgeMap.TryGetValue(key, out bucket))
                {
                    bucket = new EdgeBucket { A = a, B = b, Count = 0 };
                    edgeMap[key] = bucket;
                }
                bucket.Count++;
            }

            List<Curve> outerEdges = new List<Curve>();
            foreach (EdgeBucket bucket in edgeMap.Values)
            {
                if (bucket.Count == 1 && bucket.A.DistanceTo(bucket.B) >= shortTol * 1.05)
                {
                    outerEdges.Add(Line.CreateBound(bucket.A, bucket.B));
                }
            }

            if (outerEdges.Count < 3)
            {
                return null;
            }

            List<Curve> ordered = TryOrderByNearest(outerEdges, connectTol);
            if (ordered == null)
            {
                ordered = TryOrientBySequence(outerEdges, connectTol);
            }

            return BuildContinuousLoopWithBridges(ordered, shortTol, connectTol);
        }

        private static double CalculateLoopAreaM2(CurveLoop loop)
        {
            if (loop == null)
            {
                return 0.0;
            }

            List<XYZ> points = new List<XYZ>();
            foreach (Curve c in loop)
            {
                if (c == null)
                {
                    continue;
                }
                points.Add(c.GetEndPoint(0));
            }

            if (points.Count < 3)
            {
                return 0.0;
            }

            double areaFt2 = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ p1 = points[i];
                XYZ p2 = points[(i + 1) % points.Count];
                areaFt2 += (p1.X * p2.Y - p2.X * p1.Y);
            }
            areaFt2 = Math.Abs(areaFt2) * 0.5;
            return UnitUtils.ConvertFromInternalUnits(areaFt2, UnitTypeId.SquareMeters);
        }

        private static string BuildUndirectedEdgeKey(XYZ a, XYZ b, double tolFt)
        {
            KeyPoint ka = QuantizePoint(a, tolFt);
            KeyPoint kb = QuantizePoint(b, tolFt);
            if (CompareKeyPoint(ka, kb) > 0)
            {
                KeyPoint t = ka;
                ka = kb;
                kb = t;
            }
            return ka.X + "," + ka.Y + "|" + kb.X + "," + kb.Y;
        }

        private static int CompareKeyPoint(KeyPoint a, KeyPoint b)
        {
            if (a.X != b.X)
            {
                return a.X < b.X ? -1 : 1;
            }
            if (a.Y != b.Y)
            {
                return a.Y < b.Y ? -1 : 1;
            }
            return 0;
        }

        private static KeyPoint QuantizePoint(XYZ p, double tolFt)
        {
            double step = Math.Max(tolFt, 1e-6);
            return new KeyPoint
            {
                X = (long)Math.Round(p.X / step),
                Y = (long)Math.Round(p.Y / step)
            };
        }

        private sealed class KeyPoint
        {
            public long X { get; set; }
            public long Y { get; set; }
        }

        private sealed class EdgeBucket
        {
            public XYZ A { get; set; }
            public XYZ B { get; set; }
            public int Count { get; set; }
        }

        private static List<CurveLoop> BuildCurveLoops(IList<IList<BoundarySegment>> boundaries, Document doc)
        {
            List<CurveLoop> loops = new List<CurveLoop>();
            if (boundaries == null)
            {
                return loops;
            }

            double shortTol = doc == null ? 1e-6 : doc.Application.ShortCurveTolerance;
            double connectTol = Math.Max(shortTol * 5.0, UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters));

            foreach (IList<BoundarySegment> boundary in boundaries)
            {
                if (boundary == null || boundary.Count < 3)
                {
                    continue;
                }

                List<Curve> raw = new List<Curve>();
                foreach (BoundarySegment seg in boundary)
                {
                    Curve c = seg.GetCurve();
                    if (c == null || c.Length < shortTol * 1.05)
                    {
                        continue;
                    }
                    raw.Add(c);
                }

                if (raw.Count < 3)
                {
                    continue;
                }

                List<Curve> ordered = TryOrientBySequence(raw, connectTol);
                if (ordered == null)
                {
                    ordered = TryOrderByNearest(raw, connectTol);
                }

                CurveLoop loop = BuildContinuousLoopWithBridges(ordered, shortTol, connectTol);
                if (loop != null)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }

        // 按原顺序统一方向，优先修复“方向反了但顺序正确”的情况。
        private static List<Curve> TryOrientBySequence(List<Curve> raw, double connectTol)
        {
            if (raw == null || raw.Count < 3)
            {
                return null;
            }

            List<Curve> oriented = new List<Curve>();
            Curve first = raw[0];
            oriented.Add(first);
            XYZ prevEnd = first.GetEndPoint(1);
            for (int i = 1; i < raw.Count; i++)
            {
                Curve c = raw[i];
                XYZ s = c.GetEndPoint(0);
                XYZ e = c.GetEndPoint(1);
                double ds = prevEnd.DistanceTo(s);
                double de = prevEnd.DistanceTo(e);
                Curve chosen = de < ds ? c.CreateReversed() : c;
                double gap = Math.Min(ds, de);
                if (gap > connectTol)
                {
                    return null;
                }

                oriented.Add(chosen);
                prevEnd = chosen.GetEndPoint(1);
            }

            if (oriented[oriented.Count - 1].GetEndPoint(1).DistanceTo(oriented[0].GetEndPoint(0)) > connectTol)
            {
                return null;
            }

            return oriented;
        }

        // 备用策略：最近端点重排，处理“边界段顺序错乱”。
        private static List<Curve> TryOrderByNearest(List<Curve> raw, double connectTol)
        {
            if (raw == null || raw.Count < 3)
            {
                return null;
            }

            int seedIndex = 0;
            double maxLen = -1;
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i].Length > maxLen)
                {
                    maxLen = raw[i].Length;
                    seedIndex = i;
                }
            }

            List<Curve> ordered = new List<Curve>();
            bool[] used = new bool[raw.Count];
            Curve current = raw[seedIndex];
            ordered.Add(current);
            used[seedIndex] = true;
            XYZ prevEnd = current.GetEndPoint(1);

            for (int step = 1; step < raw.Count; step++)
            {
                int bestIndex = -1;
                bool bestReverse = false;
                double bestGap = double.MaxValue;
                for (int i = 0; i < raw.Count; i++)
                {
                    if (used[i])
                    {
                        continue;
                    }

                    Curve c = raw[i];
                    double ds = prevEnd.DistanceTo(c.GetEndPoint(0));
                    double de = prevEnd.DistanceTo(c.GetEndPoint(1));
                    if (ds < bestGap)
                    {
                        bestGap = ds;
                        bestIndex = i;
                        bestReverse = false;
                    }
                    if (de < bestGap)
                    {
                        bestGap = de;
                        bestIndex = i;
                        bestReverse = true;
                    }
                }

                if (bestIndex < 0 || bestGap > connectTol)
                {
                    return null;
                }

                Curve next = bestReverse ? raw[bestIndex].CreateReversed() : raw[bestIndex];
                ordered.Add(next);
                used[bestIndex] = true;
                prevEnd = next.GetEndPoint(1);
            }

            if (ordered[ordered.Count - 1].GetEndPoint(1).DistanceTo(ordered[0].GetEndPoint(0)) > connectTol)
            {
                return null;
            }

            return ordered;
        }

        // 用“线段化+桥接”生成严格连续闭环，避免 pCurve discontinuous。
        private static CurveLoop BuildContinuousLoopWithBridges(List<Curve> ordered, double shortTol, double connectTol)
        {
            if (ordered == null || ordered.Count < 3)
            {
                return null;
            }

            List<Curve> loopCurves = new List<Curve>();
            XYZ firstStart = ordered[0].GetEndPoint(0);
            XYZ prevEnd = firstStart;

            for (int i = 0; i < ordered.Count; i++)
            {
                Curve c = ordered[i];
                XYZ s = c.GetEndPoint(0);
                XYZ e = c.GetEndPoint(1);

                double gap = prevEnd.DistanceTo(s);
                if (gap > connectTol)
                {
                    return null;
                }
                if (gap >= shortTol * 1.05)
                {
                    loopCurves.Add(Line.CreateBound(prevEnd, s));
                }

                if (s.DistanceTo(e) >= shortTol * 1.05)
                {
                    // 统一降级为直线，保证环连续可创建。
                    loopCurves.Add(Line.CreateBound(s, e));
                }
                prevEnd = e;
            }

            double closeGap = prevEnd.DistanceTo(firstStart);
            if (closeGap > connectTol)
            {
                return null;
            }
            if (closeGap >= shortTol * 1.05)
            {
                loopCurves.Add(Line.CreateBound(prevEnd, firstStart));
            }

            if (loopCurves.Count < 3)
            {
                return null;
            }

            try
            {
                CurveLoop loop = new CurveLoop();
                foreach (Curve c in loopCurves.Where(x => x != null && x.Length >= shortTol * 1.05))
                {
                    loop.Append(c);
                }
                return loop;
            }
            catch
            {
                return null;
            }
        }
    }
}
