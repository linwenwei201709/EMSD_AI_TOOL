using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms
{
    [DataContract]
    public sealed class ConnectivitySizeOptionsPayload
    {
        [DataMember]
        public string Version { get; set; }

        [DataMember]
        public List<RectangularDuctSizeDto> DuctSizes { get; set; } = new List<RectangularDuctSizeDto>();

        [DataMember]
        public List<double> PipeSizesMm { get; set; } = new List<double>();
    }

    [DataContract]
    public sealed class RectangularDuctSizeDto
    {
        [DataMember]
        public double LengthMm { get; set; }

        [DataMember]
        public double WidthMm { get; set; }
    }
}
