using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace CadToRevit.Models.Mapping
{
    /// <summary>
    /// Beam creation result for one layer execution.
    /// </summary>
    public sealed class BeamCreateResult
    {
        public int CreatedCount { get; set; }

        public List<ElementId> CreatedElementIds { get; set; } = new List<ElementId>();

        public List<string> Errors { get; set; } = new List<string>();
    }
}
