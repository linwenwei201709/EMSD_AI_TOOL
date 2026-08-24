using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.IO;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewLinkedModelService
    {
        internal static RevitLinkInstance LinkRvt(Document previewDoc, string linkedModelRvtPath)
        {
            if (previewDoc == null)
            {
                throw new InvalidOperationException("Link RVT failed: preview document is null.");
            }

            if (string.IsNullOrWhiteSpace(linkedModelRvtPath) || !File.Exists(linkedModelRvtPath))
            {
                throw new InvalidOperationException("Linked preview model RVT does not exist: " + (linkedModelRvtPath ?? string.Empty));
            }

            try
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(linkedModelRvtPath);
                RevitLinkOptions options = new RevitLinkOptions(false);
                LinkLoadResult result = RevitLinkType.Create(previewDoc, modelPath, options);
                if (result == null || result.ElementId == null || result.ElementId == ElementId.InvalidElementId)
                {
                    throw new InvalidOperationException("Revit link creation failed: returned link type is invalid.");
                }

                RevitLinkInstance instance = RevitLinkInstance.Create(previewDoc, result.ElementId);
                if (instance == null)
                {
                    throw new InvalidOperationException("Revit link instance creation failed: returned instance is null.");
                }

                DiagnosticRecorder.AppendDebug("[PathPreview] LinkRvt.Create.Success linkTypeId=" + result.ElementId.IntegerValue + ", instanceId=" + instance.Id.IntegerValue);
                return instance;
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[PathPreview] LinkRvt.Failed error=" + ex);
                throw new InvalidOperationException("Link RVT failed: " + ex.Message, ex);
            }
        }
    }
}
