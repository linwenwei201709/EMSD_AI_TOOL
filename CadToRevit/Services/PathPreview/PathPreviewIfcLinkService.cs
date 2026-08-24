using Autodesk.Revit.DB;
using System;
using System.IO;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewIfcLinkService
    {
        internal static RevitLinkInstance LinkIfc(Document previewDoc, string linkedModelRvtPath)
        {
            if (previewDoc == null)
            {
                throw new InvalidOperationException("Link RVT failed: preview document is null.");
            }

            if (string.IsNullOrWhiteSpace(linkedModelRvtPath) || !File.Exists(linkedModelRvtPath))
            {
                throw new InvalidOperationException("Linked preview model RVT does not exist: " + (linkedModelRvtPath ?? string.Empty));
            }

            return PathPreviewLinkedModelService.LinkRvt(previewDoc, linkedModelRvtPath);
        }
    }
}
