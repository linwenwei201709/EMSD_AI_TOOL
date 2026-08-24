using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadToRevit.Models.Rooms.LayoutPlans
{
    [DataContract]
    public sealed class LayoutGeneratedElementsDto
    {
        [DataMember]
        public LayoutElementRefDto EquipmentInstance { get; set; } = new LayoutElementRefDto();

        [DataMember]
        public List<LayoutElementRefDto> DuctElements { get; set; } =
            new List<LayoutElementRefDto>();

        [DataMember]
        public List<LayoutElementRefDto> PipeElements { get; set; } =
            new List<LayoutElementRefDto>();
    }
}
