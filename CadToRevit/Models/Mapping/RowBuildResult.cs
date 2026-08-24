using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// Build result for one mapping row during sync generation.
    /// </summary>
    public sealed class RowBuildResult
    {
        public string RowKey { get; set; }

        public string RawLayerName { get; set; }

        public MapCategory Category { get; set; }

        public int CreatedCount { get; set; }

        public List<ElementId> CreatedElementIds { get; set; } = new List<ElementId>();

        public List<string> Errors { get; set; } = new List<string>();
    }
}
