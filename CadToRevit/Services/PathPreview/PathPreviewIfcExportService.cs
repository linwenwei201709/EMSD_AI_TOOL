using Autodesk.Revit.DB;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewIfcExportService
    {
        internal sealed class ExportResult
        {
            public bool Success { get; set; }
            public string ExportPath { get; set; }
            public string Error { get; set; }
        }

        internal static ExportResult ExportToTempIfc(Document sourceDoc, string tempIfcPath)
        {
            PathPreviewTempFileService.EnsureDirectory(System.IO.Path.GetDirectoryName(tempIfcPath));
            IfcPathExportService.IfcPathExportResult result = IfcPathExportService.Export(sourceDoc, tempIfcPath);
            return new ExportResult
            {
                Success = result != null && result.Success,
                ExportPath = tempIfcPath,
                Error = result == null ? "IFC export returned null result." : result.Error
            };
        }
    }
}
