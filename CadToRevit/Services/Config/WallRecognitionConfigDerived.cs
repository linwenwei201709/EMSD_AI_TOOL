namespace CadToRevit.Services.Config
{
    public sealed class WallRecognitionConfigDerived
    {
        public double MinWallLengthFt { get; set; }

        public double ParallelAngleTolDeg { get; set; }

        public double WallThicknessTolFt { get; set; }

        public double EndpointMergeTolFt { get; set; }

        public double ArcThicknessTolFt { get; set; }

        public double MaxWallThicknessFt { get; set; }

        public double DefaultSingleWallThicknessFt { get; set; }

        public double TopologyEndpointClusterTolFt { get; set; }

        public double TopologyExtendSearchTolFt { get; set; }

        public double TopologyDuplicateTolFt { get; set; }

        public double TopologyAngleSnapDeg { get; set; }

        public bool EnableTopologyOrthogonalSnap { get; set; }

        public bool EnableTopologyExtendToIntersection { get; set; }

        public bool EnableTopologyEndpointClustering { get; set; }

        public bool EnableTopologyDuplicateRemoval { get; set; }

        public bool EnableTopologyExtendCollinear { get; set; }

        public bool EnableMergeCollinear { get; set; }

        public double TopologyExtendCollinearTolFt { get; set; }

        public double TopologyCollinearOffsetTolFt { get; set; }

        public double TopologyExtendProjectionTolFt { get; set; }

        public bool TopologyUseDirectionalClustering { get; set; }

        public double TopologyIgnoreSmallerThanFt { get; set; }

        public double TopologyMinJunctureWidthFt { get; set; }

        public double TopologyIgnoreLargerThanFt { get; set; }

        public double TopologyMaxJunctureWidthFt { get; set; }
    }
}
