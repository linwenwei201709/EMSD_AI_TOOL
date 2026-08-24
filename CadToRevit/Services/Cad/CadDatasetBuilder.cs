using CadToRevit.Models.Cad;
using System.Collections.Generic;

namespace CadToRevit.Services.Cad
{
    public static class CadDatasetBuilder
    {
        public static CadDataset Build(CadSegmentBuildResult buildResult)
        {
            CadDataset dataset = new CadDataset();
            if (buildResult == null || buildResult.Segments == null)
            {
                return dataset;
            }

            foreach (CadSegment segment in buildResult.Segments)
            {
                dataset.Segments.Add(segment);
                string key = string.IsNullOrWhiteSpace(segment.RawLayerName) ? "UNKNOWN" : segment.RawLayerName;
                if (!dataset.SegmentsByRawLayer.ContainsKey(key))
                {
                    dataset.SegmentsByRawLayer[key] = new List<CadSegment>();
                }

                dataset.SegmentsByRawLayer[key].Add(segment);
            }

            return dataset;
        }
    }
}
