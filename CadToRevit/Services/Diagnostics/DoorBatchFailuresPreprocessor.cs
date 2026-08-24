using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace CadToRevit.Services.Diagnostics
{
    /// <summary>
    /// Door batch failure preprocessor that auto-cleans known cut-host errors.
    /// </summary>
    public sealed class DoorBatchFailuresPreprocessor : IFailuresPreprocessor
    {
        private readonly Document _doc;

        public DoorBatchFailuresPreprocessor(Document doc)
        {
            _doc = doc;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            if (failuresAccessor == null)
            {
                return FailureProcessingResult.Continue;
            }

            bool hasUnhandledError = false;
            bool deletedAnyDoorInstance = false;
            IList<FailureMessageAccessor> messages = failuresAccessor.GetFailureMessages();
            if (messages == null || messages.Count == 0)
            {
                return FailureProcessingResult.Continue;
            }

            foreach (FailureMessageAccessor f in messages)
            {
                if (f == null)
                {
                    continue;
                }

                FailureSeverity severity = f.GetSeverity();
                string id = string.Empty;
                string text = string.Empty;
                try
                {
                    id = f.GetFailureDefinitionId().Guid.ToString();
                }
                catch
                {
                }

                try
                {
                    text = f.GetDescriptionText() ?? string.Empty;
                }
                catch
                {
                }

                DiagnosticRecorder.AppendDebug("[DoorFailureIntercept] Severity=" + severity + ", Id=" + id + ", Text=" + text);

                if (severity == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(f);
                    continue;
                }

                bool isCutHostError =
                    text.IndexOf("cannot cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("can't cut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("out of wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("outside of wall", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isCutHostError)
                {
                    hasUnhandledError = true;
                    continue;
                }

                try
                {
                    ICollection<ElementId> failing = f.GetFailingElementIds();
                    if (failing != null && failing.Count > 0)
                    {
                        foreach (ElementId failingId in failing)
                        {
                            if (failingId == null || failingId == ElementId.InvalidElementId || _doc == null)
                            {
                                continue;
                            }

                            Element e = _doc.GetElement(failingId);
                            if (e == null)
                            {
                                continue;
                            }

                            string className = e.GetType().Name;
                            string categoryName = e.Category == null ? string.Empty : (e.Category.Name ?? string.Empty);
                            int categoryId = e.Category == null ? int.MinValue : e.Category.Id.IntegerValue;
                            bool isWall = e is Wall;
                            bool isDoorInstance = e is FamilyInstance &&
                                                  e.Category != null &&
                                                  categoryId == (int)BuiltInCategory.OST_Doors;

                            if (isWall)
                            {
                                DiagnosticRecorder.AppendDebug(
                                    "[DoorFailureSkipDelete] ElementId=" + failingId.IntegerValue +
                                    ", Class=" + className +
                                    ", Category=" + categoryName +
                                    ", Reason=Never delete wall in door failure preprocessor.");
                                continue;
                            }

                            if (!isDoorInstance)
                            {
                                DiagnosticRecorder.AppendDebug(
                                    "[DoorFailureSkipDelete] ElementId=" + failingId.IntegerValue +
                                    ", Class=" + className +
                                    ", Category=" + categoryName +
                                    ", Reason=Not a door instance.");
                                continue;
                            }

                            bool deleted = false;
                            try
                            {
                                failuresAccessor.DeleteElements(new List<ElementId> { failingId });
                                deleted = true;
                            }
                            catch (Exception exDelete)
                            {
                                DiagnosticRecorder.AppendDebug(
                                    "[DoorFailureAutoDelete] ElementId=" + failingId.IntegerValue +
                                    ", Class=" + className +
                                    ", Category=" + categoryName +
                                    ", Deleted=False" +
                                    ", Reason=" + exDelete.Message);
                            }

                            if (deleted)
                            {
                                deletedAnyDoorInstance = true;
                                DiagnosticRecorder.AppendDebug(
                                    "[DoorFailureAutoDelete] ElementId=" + failingId.IntegerValue +
                                    ", Class=" + className +
                                    ", Category=" + categoryName +
                                    ", Deleted=True");
                            }
                        }
                    }

                    // Try resolve the message explicitly after deletion.
                    try
                    {
                        failuresAccessor.ResolveFailure(f);
                    }
                    catch
                    {
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticRecorder.AppendDebug("[DoorFailureAutoDelete] Failed: " + ex.Message);
                    hasUnhandledError = true;
                }
            }

            if (hasUnhandledError)
            {
                return FailureProcessingResult.ProceedWithRollBack;
            }

            if (deletedAnyDoorInstance)
            {
                return FailureProcessingResult.ProceedWithCommit;
            }

            return FailureProcessingResult.Continue;
        }
    }
}
