using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public enum CadCurveSourceType
    {
        NativeLine,
        PolyLineSegment,
        Other
    }

    public class CadSegment
    {
        public int SegmentId { get; set; }

        public string NormalizedLayer { get; set; }

        public string SemanticLayer { get; set; }

        public string LayerName { get; set; }

        public string RawLayerName { get; set; }

        public CadCurveSourceType SourceType { get; set; }

        public XYZ P0 { get; set; }

        public XYZ P1 { get; set; }

        public bool IsArc { get; set; }

        public XYZ Center { get; set; }

        public double RadiusFeet { get; set; }

        public double SweepAngleRad { get; set; }

        public XYZ MidPoint { get; set; }

        public Line AsRevitLine()
        {
            return Line.CreateBound(P0, P1);
        }

        public Curve AsRevitCurve()
        {
            return AsRevitLine();
        }
    }

    public class CadSegmentBuildDiagnostics
    {
        public CadSegmentBuildDiagnostics()
        {
            SegmentCountByLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            SegmentCountBySourceType = new Dictionary<CadCurveSourceType, int>();
            RawLayerSamples = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public Dictionary<string, int> SegmentCountByLayer { get; private set; }

        public Dictionary<CadCurveSourceType, int> SegmentCountBySourceType { get; private set; }

        public HashSet<string> RawLayerSamples { get; private set; }

        public int IgnoredGeometryCount { get; set; }

        public int TinySegmentSkippedCount { get; set; }
    }

    public class CadSegmentBuildResult
    {
        public CadSegmentBuildResult()
        {
            Segments = new List<CadSegment>();
            Diagnostics = new CadSegmentBuildDiagnostics();
        }

        public List<CadSegment> Segments { get; private set; }

        public CadSegmentBuildDiagnostics Diagnostics { get; private set; }

        public int NextSegmentId { get; set; } = 1;
    }

    public static class CadSegmentBuilder
    {
        private const double TinyEpsFeet = 1e-6;

        public static CadSegmentBuildResult BuildSegments(
            Document doc,
            ImportInstance importInstance,
            ISet<string> layerFilter)
        {
            CadSegmentBuildResult result = new CadSegmentBuildResult();
            if (doc == null || importInstance == null)
            {
                return result;
            }

            HashSet<string> normalizedFilter = null;
            if (layerFilter != null && layerFilter.Count > 0)
            {
                normalizedFilter = new HashSet<string>(
                    layerFilter.Select(x => (x ?? string.Empty).Trim().ToUpperInvariant()),
                    StringComparer.OrdinalIgnoreCase);
            }

            Options options = new Options
            {
                IncludeNonVisibleObjects = true,
                ComputeReferences = false
            };

            GeometryElement geometryElement = importInstance.get_Geometry(options);
            if (geometryElement == null)
            {
                return result;
            }

            foreach (GeometryObject geometryObject in geometryElement)
            {
                TraverseGeometryObject(doc, geometryObject, normalizedFilter, result);
            }

            return result;
        }

        private static void TraverseGeometryObject(
            Document doc,
            GeometryObject geometryObject,
            HashSet<string> normalizedFilter,
            CadSegmentBuildResult result)
        {
            if (geometryObject == null)
            {
                return;
            }

            GeometryInstance geometryInstance = geometryObject as GeometryInstance;
            if (geometryInstance != null)
            {
                GeometryElement nestedGeometry = geometryInstance.GetInstanceGeometry();
                if (nestedGeometry == null)
                {
                    return;
                }

                foreach (GeometryObject nestedObject in nestedGeometry)
                {
                    TraverseGeometryObject(doc, nestedObject, normalizedFilter, result);
                }

                return;
            }

            string layerName = LayerNameResolver.ResolveLayerName(doc, geometryObject);
            string semanticLayer = LayerNameMapper.Map(layerName);
            string rawLayerName = LayerNameResolver.ResolveRawLayerName(doc, geometryObject);
            result.Diagnostics.RawLayerSamples.Add(rawLayerName);

            if (normalizedFilter != null &&
                !normalizedFilter.Contains(layerName) &&
                !normalizedFilter.Contains(semanticLayer))
            {
                return;
            }

            Line line = geometryObject as Line;
            if (line != null)
            {
                AddSegment(result, layerName, semanticLayer, rawLayerName, CadCurveSourceType.NativeLine, line.GetEndPoint(0), line.GetEndPoint(1));
                return;
            }

            Arc arc = geometryObject as Arc;
            if (arc != null)
            {
                AddArcSegment(result, layerName, semanticLayer, rawLayerName, CadCurveSourceType.Other, arc);
                return;
            }

            PolyLine polyLine = geometryObject as PolyLine;
            if (polyLine != null)
            {
                IList<XYZ> points = polyLine.GetCoordinates();
                if (points == null || points.Count < 2)
                {
                    return;
                }

                for (int i = 0; i < points.Count - 1; i++)
                {
                    XYZ p0 = points[i];
                    XYZ p1 = points[i + 1];
                    if (p0 == null || p1 == null)
                    {
                        continue;
                    }

                    if (p0.DistanceTo(p1) < TinyEpsFeet)
                    {
                        result.Diagnostics.TinySegmentSkippedCount++;
                        continue;
                    }

                    AddSegment(result, layerName, semanticLayer, rawLayerName, CadCurveSourceType.PolyLineSegment, p0, p1);
                }

                return;
            }

            Curve curve = geometryObject as Curve;
            if (curve != null)
            {
                result.Diagnostics.IgnoredGeometryCount++;
                return;
            }
        }

        private static void AddArcSegment(
            CadSegmentBuildResult result,
            string normalizedLayer,
            string semanticLayer,
            string rawLayerName,
            CadCurveSourceType sourceType,
            Arc arc)
        {
            if (arc == null)
            {
                return;
            }

            // Some imported CAD arcs are unbound in Revit geometry; skip them safely.
            if (!arc.IsBound)
            {
                result.Diagnostics.IgnoredGeometryCount++;
                return;
            }

            try
            {
                XYZ p0 = arc.GetEndPoint(0);
                XYZ p1 = arc.GetEndPoint(1);
                if (p0 == null || p1 == null || p0.DistanceTo(p1) < TinyEpsFeet)
                {
                    result.Diagnostics.TinySegmentSkippedCount++;
                    return;
                }

                double radius = arc.Radius;
                double theta = radius > 1e-9 ? arc.Length / radius : 0.0;
                CadSegment segment = new CadSegment
                {
                    SegmentId = result.NextSegmentId++,
                    NormalizedLayer = normalizedLayer,
                    SemanticLayer = semanticLayer,
                    LayerName = normalizedLayer,
                    RawLayerName = rawLayerName,
                    SourceType = sourceType,
                    P0 = p0,
                    P1 = p1,
                    IsArc = true,
                    Center = arc.Center,
                    RadiusFeet = radius,
                    SweepAngleRad = theta,
                    MidPoint = arc.Evaluate(0.5, true)
                };

                result.Segments.Add(segment);
                Increment(result.Diagnostics.SegmentCountByLayer, semanticLayer);
                Increment(result.Diagnostics.SegmentCountBySourceType, sourceType);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                result.Diagnostics.IgnoredGeometryCount++;
            }
        }

        private static void AddSegment(
            CadSegmentBuildResult result,
            string normalizedLayer,
            string semanticLayer,
            string rawLayerName,
            CadCurveSourceType sourceType,
            XYZ p0,
            XYZ p1)
        {
            if (p0 == null || p1 == null)
            {
                return;
            }

            if (p0.DistanceTo(p1) < TinyEpsFeet)
            {
                result.Diagnostics.TinySegmentSkippedCount++;
                return;
            }

            CadSegment segment = new CadSegment
            {
                SegmentId = result.NextSegmentId++,
                NormalizedLayer = normalizedLayer,
                SemanticLayer = semanticLayer,
                LayerName = normalizedLayer,
                RawLayerName = rawLayerName,
                SourceType = sourceType,
                P0 = p0,
                P1 = p1
            };

            result.Segments.Add(segment);
            Increment(result.Diagnostics.SegmentCountByLayer, semanticLayer);
            Increment(result.Diagnostics.SegmentCountBySourceType, sourceType);
        }

        private static void Increment<TKey>(IDictionary<TKey, int> map, TKey key)
        {
            if (!map.ContainsKey(key))
            {
                map[key] = 0;
            }

            map[key]++;
        }
    }
}
