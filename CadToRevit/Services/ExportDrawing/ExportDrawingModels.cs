using System;
using System.Collections.Generic;

namespace CadToRevit.Services.ExportDrawing
{
    public sealed class ExportDrawingViewImage
    {
        public string ViewName { get; set; }

        public string ImagePath { get; set; }

        public int PageNumber { get; set; }
    }

    public sealed class ExportDrawingImageResult
    {
        public List<ExportDrawingViewImage> Views { get; set; } =
            new List<ExportDrawingViewImage>();
    }

    public sealed class ExportDrawingPdfResult
    {
        public string PdfPath { get; set; }

        public string FileName { get; set; }

        public DateTime GeneratedAt { get; set; }
    }
}
