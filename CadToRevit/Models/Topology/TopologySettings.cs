using System.Runtime.Serialization;

namespace CadToRevit.Models.Topology
{
    [DataContract]
    /// <summary>
    /// 拓扑修复配置（序列化模型，单位以 mm/deg 为主）。
    /// </summary>
    public class TopologySettings
    {
        [DataMember(Name = "EndpointClusterTolMm")]
        /// <summary>端点聚类容差（mm）。</summary>
        public double EndpointClusterTolMm { get; set; } = 15.0;

        [DataMember(Name = "ExtendSearchTolMm")]
        /// <summary>延伸到交点的搜索半径（mm）。</summary>
        public double ExtendSearchTolMm { get; set; } = 30.0;

        [DataMember(Name = "DuplicateTolMm")]
        /// <summary>重复线判定容差（mm）。</summary>
        public double DuplicateTolMm { get; set; } = 6.0;

        [DataMember(Name = "AngleSnapDeg")]
        /// <summary>角度吸附容差（deg）。</summary>
        public double AngleSnapDeg { get; set; } = 0.5;

        [DataMember(Name = "EnableOrthogonalSnap")]
        /// <summary>是否启用正交吸附。</summary>
        public bool EnableOrthogonalSnap { get; set; } = true;

        [DataMember(Name = "EnableExtendToIntersection")]
        /// <summary>是否启用延伸到交点。</summary>
        public bool EnableExtendToIntersection { get; set; } = true;

        [DataMember(Name = "EnableEndpointClustering")]
        /// <summary>是否启用端点聚类。</summary>
        public bool EnableEndpointClustering { get; set; } = true;

        [DataMember(Name = "EnableDuplicateRemoval")]
        /// <summary>是否启用重复线去重。</summary>
        public bool EnableDuplicateRemoval { get; set; } = true;

        [DataMember(Name = "EnableExtendCollinear")]
        /// <summary>是否启用共线延伸修复。</summary>
        public bool EnableExtendCollinear { get; set; } = false;

        [DataMember(Name = "ExtendCollinearTolMm")]
        /// <summary>共线延伸最大容差（mm）。</summary>
        public double ExtendCollinearTolMm { get; set; } = 150.0;

        [DataMember(Name = "CollinearOffsetTolMm")]
        /// <summary>共线偏移容差（mm）。</summary>
        public double CollinearOffsetTolMm { get; set; } = 30.0;

        [DataMember(Name = "ExtendProjectionTolMm")]
        /// <summary>延伸投影偏差容差（mm）。</summary>
        public double ExtendProjectionTolMm { get; set; } = 80.0;

        [DataMember(Name = "UseDirectionalClustering")]
        /// <summary>是否按方向分组后再进行端点聚类。</summary>
        public bool UseDirectionalClustering { get; set; } = false;
    }
}
