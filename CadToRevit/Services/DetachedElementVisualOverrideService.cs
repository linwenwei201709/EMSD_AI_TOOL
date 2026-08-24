using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CadToRevit.Services
{
    public sealed class ElementOverrideRestoreInfo
    {
        public ElementId ElementId { get; set; }

        public ElementId ViewId { get; set; }

        public OverrideGraphicSettings Override { get; set; }
    }

    public static class DetachedElementVisualOverrideService
    {
        public static void ApplyDetachedOverride(Document doc, IEnumerable<ElementId> elementIds)
        {
            Apply(doc, elementIds, BuildDetachedOverrides(doc));
        }

        public static void ClearDetachedOverride(Document doc, IEnumerable<ElementId> elementIds)
        {
            Apply(doc, elementIds, new OverrideGraphicSettings());
        }

        public static void RestoreOriginalOverrides(Document doc, IEnumerable<ElementOverrideRestoreInfo> restoreInfos)
        {
            List<ElementOverrideRestoreInfo> items = (restoreInfos ?? Enumerable.Empty<ElementOverrideRestoreInfo>())
                .Where(x => x != null && x.ElementId != null && x.ElementId != ElementId.InvalidElementId)
                .ToList();
            if (doc == null || items.Count == 0)
            {
                return;
            }

            Action apply = () =>
            {
                foreach (ElementOverrideRestoreInfo item in items)
                {
                    View view = item.ViewId != null && item.ViewId != ElementId.InvalidElementId
                        ? doc.GetElement(item.ViewId) as View
                        : doc.ActiveView;
                    if (view == null)
                    {
                        continue;
                    }

                    try
                    {
                        view.SetElementOverrides(item.ElementId, item.Override ?? new OverrideGraphicSettings());
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug("[DetachedOverrideRestore] ElementId=" + item.ElementId.IntegerValue + ", Error=" + ex.Message);
                    }
                }
            };

            if (doc.IsModifiable)
            {
                apply();
                return;
            }

            using (Transaction tx = new Transaction(doc, "CadToRevit Restore Detached Element Override"))
            {
                tx.Start();
                apply();
                tx.Commit();
            }
        }

        private static void Apply(Document doc, IEnumerable<ElementId> elementIds, OverrideGraphicSettings settings)
        {
            View view = doc != null ? doc.ActiveView : null;
            List<ElementId> ids = (elementIds ?? Enumerable.Empty<ElementId>())
                .Where(x => x != null && x != ElementId.InvalidElementId)
                .Distinct(new ElementIdComparer())
                .ToList();
            if (view == null || ids.Count == 0)
            {
                return;
            }

            Action apply = () =>
            {
                foreach (ElementId id in ids)
                {
                    try
                    {
                        view.SetElementOverrides(id, settings);
                    }
                    catch (Exception ex)
                    {
                        DiagnosticRecorder.AppendDebug("[DetachedOverride] ElementId=" + id.IntegerValue + ", Error=" + ex.Message);
                    }
                }
            };

            if (doc.IsModifiable)
            {
                apply();
                return;
            }

            using (Transaction tx = new Transaction(doc, "CadToRevit Detached Element Override"))
            {
                tx.Start();
                apply();
                tx.Commit();
            }
        }

        private static OverrideGraphicSettings BuildDetachedOverrides(Document doc)
        {
            Color green = new Color(0, 176, 80);
            ElementId solidFillId = GetSolidFillPatternId(doc);
            OverrideGraphicSettings settings = new OverrideGraphicSettings();
            settings.SetProjectionLineColor(green);
            settings.SetCutLineColor(green);
            if (solidFillId != ElementId.InvalidElementId)
            {
                settings.SetSurfaceForegroundPatternId(solidFillId);
                settings.SetSurfaceForegroundPatternColor(green);
                settings.SetCutForegroundPatternId(solidFillId);
                settings.SetCutForegroundPatternColor(green);
            }

            return settings;
        }

        private static ElementId GetSolidFillPatternId(Document doc)
        {
            if (doc == null)
            {
                return ElementId.InvalidElementId;
            }

            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern() != null && x.GetFillPattern().IsSolidFill);
            return solidFill != null ? solidFill.Id : ElementId.InvalidElementId;
        }

        private sealed class ElementIdComparer : IEqualityComparer<ElementId>
        {
            public bool Equals(ElementId x, ElementId y)
            {
                int left = x != null ? x.IntegerValue : ElementId.InvalidElementId.IntegerValue;
                int right = y != null ? y.IntegerValue : ElementId.InvalidElementId.IntegerValue;
                return left == right;
            }

            public int GetHashCode(ElementId obj)
            {
                return obj != null ? obj.IntegerValue.GetHashCode() : 0;
            }
        }
    }
}
