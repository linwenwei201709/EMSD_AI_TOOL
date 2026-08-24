namespace CadToRevit.Models
{
    /// <summary>
    /// Door symbol family kind used for hard routing/isolation.
    /// </summary>
    public enum DoorSymbolFamilyKind
    {
        Unknown = 0,
        StandardArcDoor = 1,
        MinimalArcDoorNoWallCrossing = 2,
        ComplexStandardDoorNoWallCrossing = 3,
        MinimalDoubleArcDoorNoWallCrossing = 4,
        ComplexStandardDoorNoWallCrossingR3CD = 5,
        DoubleArcDoorWithWallCrossing = 6,
        TripleArcDoorWithWallCrossing = 7
    }
}
