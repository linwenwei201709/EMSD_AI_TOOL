using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadToRevit.Services.Part3
{
    public static class LiftDoorIfcCleanupService
    {
        public sealed class CleanupResult
        {
            public int MatchedLiftDoorCount { get; set; }
            public int DeletedCount { get; set; }
            public int SkippedCount { get; set; }
        }

        public static CleanupResult DeleteLiftDoorElements(Document doc)
        {
            CleanupResult result = new CleanupResult();
            DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] Started");

            if (doc == null)
            {
                DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] MatchedLiftDoorCount=0");
                DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] DeletedCount=0");
                DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] Finished");
                return result;
            }

            List<LiftDoorCandidate> candidates = CollectCandidates(doc);
            result.MatchedLiftDoorCount = candidates.Count;
            DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] MatchedLiftDoorCount=" + candidates.Count.ToString(CultureInfo.InvariantCulture));

            if (candidates.Count == 0)
            {
                DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] DeletedCount=0");
                DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] Finished");
                return result;
            }

            using (Transaction tx = new Transaction(doc, "Delete Lift Doors Before IFC Export"))
            {
                tx.Start();
                foreach (LiftDoorCandidate candidate in candidates)
                {
                    Element element = doc.GetElement(candidate.ElementId);
                    if (element == null || !IsAllowedLiftDoorElement(element))
                    {
                        result.SkippedCount++;
                        DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] SkippedElementId=" +
                            FormatElementId(candidate.ElementId) +
                            ", Reason=InvalidCategory");
                        continue;
                    }

                    try
                    {
                        ICollection<ElementId> deleted = doc.Delete(candidate.ElementId);
                        if (deleted != null && deleted.Count > 0)
                        {
                            result.DeletedCount++;
                            DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] DeletedElementId=" +
                                FormatElementId(candidate.ElementId) +
                                ", Name=" + (candidate.Name ?? string.Empty));
                        }
                        else
                        {
                            result.SkippedCount++;
                            DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] SkippedElementId=" +
                                FormatElementId(candidate.ElementId) +
                                ", Reason=NotAllowed");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.SkippedCount++;
                        DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] SkippedElementId=" +
                            FormatElementId(candidate.ElementId) +
                            ", Reason=" + (candidate.FromGroupMember ? "GroupMemberDeleteFailed" : "NotAllowed") +
                            ", Error=" + ex.Message);
                    }
                }

                tx.Commit();
            }

            DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] DeletedCount=" + result.DeletedCount.ToString(CultureInfo.InvariantCulture));
            DiagnosticRecorder.AppendDebug("[LiftIfcCleanup] Finished");
            return result;
        }

        private static List<LiftDoorCandidate> CollectCandidates(Document doc)
        {
            Dictionary<int, LiftDoorCandidate> candidates = new Dictionary<int, LiftDoorCandidate>();
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                TryAddCandidate(doc, candidates, element, false);
            }

            foreach (Group group in new FilteredElementCollector(doc).OfClass(typeof(Group)).Cast<Group>())
            {
                IList<ElementId> memberIds = group.GetMemberIds();
                foreach (ElementId memberId in memberIds ?? new List<ElementId>())
                {
                    TryAddCandidate(doc, candidates, doc.GetElement(memberId), true);
                }
            }

            return candidates.Values
                .OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TryAddCandidate(
            Document doc,
            Dictionary<int, LiftDoorCandidate> candidates,
            Element element,
            bool fromGroupMember)
        {
            if (doc == null || candidates == null || element == null || candidates.ContainsKey(element.Id.IntegerValue))
            {
                return;
            }

            if (!IsAllowedLiftDoorElement(element))
            {
                return;
            }

            List<string> names = CollectSearchNames(doc, element);
            if (!names.Any(IsLiftDoorName))
            {
                return;
            }

            candidates[element.Id.IntegerValue] = new LiftDoorCandidate
            {
                ElementId = element.Id,
                Name = names.FirstOrDefault(IsLiftDoorName) ?? element.Name ?? string.Empty,
                FromGroupMember = fromGroupMember
            };
        }

        private static bool IsAllowedLiftDoorElement(Element element)
        {
            if (!(element is FamilyInstance))
            {
                return false;
            }

            BuiltInCategory category = ToBuiltInCategory(element.Category);
            return category == BuiltInCategory.OST_Doors ||
                   category == BuiltInCategory.OST_GenericModel ||
                   category == BuiltInCategory.OST_SpecialityEquipment;
        }

        private static BuiltInCategory ToBuiltInCategory(Category category)
        {
            if (category == null)
            {
                return (BuiltInCategory)0;
            }

            return (BuiltInCategory)category.Id.IntegerValue;
        }

        private static List<string> CollectSearchNames(Document doc, Element element)
        {
            List<string> names = new List<string>();
            AddName(names, element != null ? element.Name : string.Empty);
            AddName(names, element != null && element.Category != null ? element.Category.Name : string.Empty);

            Element type = element != null && element.GetTypeId() != ElementId.InvalidElementId
                ? doc.GetElement(element.GetTypeId())
                : null;
            AddName(names, type != null ? type.Name : string.Empty);

            FamilyInstance familyInstance = element as FamilyInstance;
            if (familyInstance != null && familyInstance.Symbol != null)
            {
                AddName(names, familyInstance.Symbol.Name);
                AddName(names, familyInstance.Symbol.Family != null ? familyInstance.Symbol.Family.Name : string.Empty);
            }

            return names;
        }

        private static void AddName(List<string> names, string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        private static bool IsLiftDoorName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            return normalized.Contains("door") &&
                   (normalized.Contains("lift") ||
                    normalized.Contains("life") ||
                    normalized.Contains("elevator"));
        }

        private static string FormatElementId(ElementId id)
        {
            return id == null ? "-" : id.IntegerValue.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class LiftDoorCandidate
        {
            public ElementId ElementId { get; set; }
            public string Name { get; set; }
            public bool FromGroupMember { get; set; }
        }
    }
}
