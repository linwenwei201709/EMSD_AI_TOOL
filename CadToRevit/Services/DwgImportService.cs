using Autodesk.Revit.DB;
using CadToRevit.Models.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CadToRevit.Services
{
    public sealed class DwgImportResult
    {
        public bool Success { get; set; }

        public string ErrorMessage { get; set; }

        public ElementId LinkInstanceId { get; set; } = ElementId.InvalidElementId;

        public string FilePath { get; set; }

        public bool UnitSuspicious { get; set; }

        public SourceUnit SourceUnit { get; set; } = SourceUnit.Millimeter;

        public string SourceUnitEvidence { get; set; } = string.Empty;

        public List<string> Layers { get; set; } = new List<string>();
    }

    public static class DwgImportService
    {
        public static DwgImportResult ImportLink(
            Document doc,
            string filePath,
            bool replaceExisting,
            SourceUnit resolvedSourceUnit,
            string sourceUnitEvidence)
        {
            DwgImportResult result = new DwgImportResult
            {
                FilePath = filePath,
                SourceUnit = resolvedSourceUnit,
                SourceUnitEvidence = sourceUnitEvidence ?? string.Empty
            };
            if (doc == null)
            {
                result.ErrorMessage = "Document 无效。";
                return result;
            }

            if (!IsSupportedFinalSourceUnit(resolvedSourceUnit))
            {
                result.ErrorMessage = "Please select a concrete DWG source unit before importing.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                result.ErrorMessage = "DWG 文件不存在。";
                return result;
            }

            try
            {
                List<ImportInstance> existed = GetLinkedImportInstances(doc);
                using (Transaction tx = new Transaction(doc, "CadToRevit Import DWG Link"))
                {
                    tx.Start();
                    if (replaceExisting)
                    {
                        foreach (ImportInstance old in existed)
                        {
                            if (old != null)
                            {
                                doc.Delete(old.Id);
                            }
                        }
                    }

                    DWGImportOptions options = new DWGImportOptions();
                    options.Placement = ImportPlacement.Origin;
                    options.OrientToView = false;
                    options.ThisViewOnly = false;
                    // 中文注释：统一单位为毫米，避免比例错误影响后续识别。
                    options.Unit = MapToImportUnit(resolvedSourceUnit);

                    ElementId linkedId = ExecuteLink(doc, filePath, options);
                    if (linkedId == null || linkedId == ElementId.InvalidElementId)
                    {
                        throw new InvalidOperationException("无法获取 LinkInstanceId。");
                    }

                    tx.Commit();
                    result.LinkInstanceId = linkedId;
                }

                ImportInstance linked = doc.GetElement(result.LinkInstanceId) as ImportInstance;
                if (linked != null)
                {
                    result.UnitSuspicious = IsBoundingBoxSuspicious(linked);
                    result.Layers = ReadLayers(doc, linked);
                }

                DwgSessionManager.Set(doc, new DwgSessionInfo
                {
                    LinkInstanceId = result.LinkInstanceId,
                    FilePath = filePath,
                    ImportTime = DateTime.Now,
                    DwgLayers = new List<string>(result.Layers),
                    SourceUnit = resolvedSourceUnit,
                    SourceUnitEvidence = sourceUnitEvidence ?? string.Empty
                });
                result.Success = true;
                return result;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public static List<ImportInstance> GetLinkedImportInstances(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .Where(IsLinkedInstance)
                .ToList();
        }

        public static DwgImportResult ImportLink(Document doc, string filePath, bool replaceExisting)
        {
            return ImportLink(doc, filePath, replaceExisting, SourceUnit.Millimeter, "LegacyMillimeterFallback");
        }

        private static bool IsLinkedInstance(ImportInstance instance)
        {
            if (instance == null)
            {
                return false;
            }

            PropertyInfo prop = instance.GetType().GetProperty("IsLinked");
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                object value = prop.GetValue(instance, null);
                if (value is bool)
                {
                    return (bool)value;
                }
            }

            return true;
        }

        private static ElementId ExecuteLink(Document doc, string filePath, DWGImportOptions options)
        {
            MethodInfo[] methods = doc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => string.Equals(x.Name, "Link", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] ps = method.GetParameters();
                if (ps.Length == 4 &&
                    ps[0].ParameterType == typeof(string) &&
                    typeof(DWGImportOptions).IsAssignableFrom(ps[1].ParameterType) &&
                    typeof(View).IsAssignableFrom(ps[2].ParameterType) &&
                    ps[3].ParameterType.IsByRef &&
                    ps[3].ParameterType.GetElementType() == typeof(ElementId))
                {
                    object[] args = { filePath, options, null, ElementId.InvalidElementId };
                    object ret = method.Invoke(doc, args);
                    bool ok = ret is bool && (bool)ret;
                    ElementId id = args[3] as ElementId;
                    if (ok && id != null && id != ElementId.InvalidElementId)
                    {
                        return id;
                    }
                }

                if (ps.Length == 3 &&
                    ps[0].ParameterType == typeof(string) &&
                    typeof(DWGImportOptions).IsAssignableFrom(ps[1].ParameterType) &&
                    ps[2].ParameterType.IsByRef &&
                    ps[2].ParameterType.GetElementType() == typeof(ElementId))
                {
                    object[] args = { filePath, options, ElementId.InvalidElementId };
                    object ret = method.Invoke(doc, args);
                    bool ok = ret is bool && (bool)ret;
                    ElementId id = args[2] as ElementId;
                    if (ok && id != null && id != ElementId.InvalidElementId)
                    {
                        return id;
                    }
                }
            }

            throw new InvalidOperationException("当前 Revit API 未找到可用的 Document.Link(DWG) 方法。");
        }

        private static ImportUnit MapToImportUnit(SourceUnit sourceUnit)
        {
            switch (sourceUnit)
            {
                case SourceUnit.Inch:
                    return ImportUnit.Inch;
                case SourceUnit.Millimeter:
                default:
                    return ImportUnit.Millimeter;
            }
        }

        private static bool IsSupportedFinalSourceUnit(SourceUnit sourceUnit)
        {
            return sourceUnit == SourceUnit.Millimeter || sourceUnit == SourceUnit.Inch;
        }

        private static bool IsBoundingBoxSuspicious(ImportInstance instance)
        {
            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            if (box == null || box.Min == null || box.Max == null)
            {
                return false;
            }

            double dx = Math.Abs(box.Max.X - box.Min.X);
            double dy = Math.Abs(box.Max.Y - box.Min.Y);
            double dz = Math.Abs(box.Max.Z - box.Min.Z);
            // 中文注释：阈值按毫米图纸常规范围设置，明显超大时提示单位风险。
            double thresholdFt = 50000.0 / 304.8;
            return dx > thresholdFt || dy > thresholdFt || dz > thresholdFt;
        }

        private static List<string> ReadLayers(Document doc, ImportInstance instance)
        {
            return CadGeometryReader.ReadGeometryItems(doc, instance)
                .Select(x => string.IsNullOrWhiteSpace(x.RawLayerName) ? x.LayerName : x.RawLayerName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
