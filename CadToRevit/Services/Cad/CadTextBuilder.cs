using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CadToRevit.Services.Cad
{
    public static class CadTextBuilder
    {
        public static List<CadText> Extract(Document doc, ImportInstance importInstance)
        {
            List<CadText> result = new List<CadText>();
            TextExtractStats stats = new TextExtractStats();
            if (doc == null || importInstance == null)
            {
                DiagnosticRecorder.AppendDebug("[RoomTextDiag] Skip extract: doc/import is null.");
                return result;
            }

            try
            {
                Options options = new Options
                {
                    IncludeNonVisibleObjects = true,
                    ComputeReferences = false
                };
                GeometryElement geometry = importInstance.get_Geometry(options);
                if (geometry == null)
                {
                    DiagnosticRecorder.AppendDebug(
                        "[RoomTextDiag] Geometry is null. Import=" + (importInstance.Name ?? string.Empty) +
                        ", Id=" + importInstance.Id.IntegerValue);
                    return result;
                }

                foreach (GeometryObject obj in geometry)
                {
                    TraverseGeometryObject(doc, obj, result, stats);
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoomTextDiag] Extract exception: " + ex.Message);
            }
            finally
            {
                string topTypes = stats.GetTopTypes();
                DiagnosticRecorder.AppendDebug(
                    "[RoomTextDiag] Summary: Import=" + (importInstance.Name ?? string.Empty) +
                    ", Id=" + importInstance.Id.IntegerValue +
                    ", Objects=" + stats.TotalObjects +
                    ", Instances=" + stats.GeometryInstances +
                    ", Leaves=" + stats.LeafObjects +
                    ", TextCandidates=" + stats.TextCandidates +
                    ", Converted=" + stats.Converted +
                    ", MissingText=" + stats.MissingText +
                    ", MissingPosition=" + stats.MissingPosition +
                    ", MissingLayer=" + stats.MissingLayer +
                    ", ConvertErrors=" + stats.ConvertErrors +
                    ", TopTypes={" + topTypes + "}");
            }

            return result;
        }

        private static void TraverseGeometryObject(Document doc, GeometryObject obj, List<CadText> output, TextExtractStats stats)
        {
            if (obj == null)
            {
                return;
            }

            stats.TotalObjects++;
            GeometryInstance instance = obj as GeometryInstance;
            if (instance != null)
            {
                stats.GeometryInstances++;
                GeometryElement nested = instance.GetInstanceGeometry();
                if (nested != null)
                {
                    foreach (GeometryObject child in nested)
                    {
                        TraverseGeometryObject(doc, child, output, stats);
                    }
                }

                return;
            }

            string typeName = obj.GetType().Name ?? string.Empty;
            stats.LeafObjects++;
            stats.AddType(typeName);
            if (typeName.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            stats.TextCandidates++;
            string reason;
            CadText text = TryConvertTextObject(doc, obj, out reason);
            if (text != null && !string.IsNullOrWhiteSpace(text.Text))
            {
                output.Add(text);
                stats.Converted++;
                if (string.IsNullOrWhiteSpace(text.RawLayerName))
                {
                    stats.MissingLayer++;
                }
            }
            else
            {
                if (string.Equals(reason, "MissingText", StringComparison.OrdinalIgnoreCase))
                {
                    stats.MissingText++;
                }
                else if (string.Equals(reason, "MissingPosition", StringComparison.OrdinalIgnoreCase))
                {
                    stats.MissingPosition++;
                }
                else
                {
                    stats.ConvertErrors++;
                }
            }
        }

        private static CadText TryConvertTextObject(Document doc, object obj, out string reason)
        {
            reason = string.Empty;
            try
            {
                Type type = obj.GetType();
                string text = TryReadString(type, obj, "Text") ?? TryReadString(type, obj, "Value");
                XYZ pos = TryReadPoint(type, obj, "Coord") ?? TryReadPoint(type, obj, "Position");
                if (string.IsNullOrWhiteSpace(text))
                {
                    reason = "MissingText";
                    return null;
                }

                if (pos == null)
                {
                    reason = "MissingPosition";
                    return null;
                }

                GeometryObject gobj = obj as GeometryObject;
                string rawLayer = gobj != null ? LayerNameResolver.ResolveRawLayerName(doc, gobj) : string.Empty;
                return new CadText
                {
                    RawLayerName = rawLayer,
                    Text = text,
                    Position = pos,
                    RotationRad = TryReadDouble(type, obj, "Rotation")
                };
            }
            catch
            {
                reason = "Exception";
                return null;
            }
        }

        private static string TryReadString(Type type, object obj, string name)
        {
            PropertyInfo pi = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = pi != null ? pi.GetValue(obj, null) : null;
            return value == null ? null : value.ToString();
        }

        private static double TryReadDouble(Type type, object obj, string name)
        {
            PropertyInfo pi = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = pi != null ? pi.GetValue(obj, null) : null;
            if (value is double d)
            {
                return d;
            }

            return 0.0;
        }

        private static XYZ TryReadPoint(Type type, object obj, string name)
        {
            PropertyInfo pi = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = pi != null ? pi.GetValue(obj, null) : null;
            return value as XYZ;
        }

        private sealed class TextExtractStats
        {
            public int TotalObjects { get; set; }

            public int GeometryInstances { get; set; }

            public int LeafObjects { get; set; }

            public int TextCandidates { get; set; }

            public int Converted { get; set; }

            public int MissingText { get; set; }

            public int MissingPosition { get; set; }

            public int MissingLayer { get; set; }

            public int ConvertErrors { get; set; }

            private readonly Dictionary<string, int> _typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            public void AddType(string typeName)
            {
                string key = string.IsNullOrWhiteSpace(typeName) ? "(empty)" : typeName;
                if (!_typeCounts.ContainsKey(key))
                {
                    _typeCounts[key] = 0;
                }

                _typeCounts[key]++;
            }

            public string GetTopTypes()
            {
                List<string> top = new List<string>();
                foreach (KeyValuePair<string, int> kv in _typeCounts)
                {
                    top.Add(kv.Key + ":" + kv.Value);
                }

                return string.Join(", ", top
                    .OrderByDescending(x =>
                    {
                        int idx = x.LastIndexOf(':');
                        if (idx < 0) return 0;
                        int v;
                        return int.TryParse(x.Substring(idx + 1), out v) ? v : 0;
                    })
                    .Take(8));
            }
        }
    }
}
