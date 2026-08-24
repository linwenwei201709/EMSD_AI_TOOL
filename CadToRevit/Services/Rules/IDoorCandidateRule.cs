using CadToRevit.Models;
using System.Collections.Generic;

namespace CadToRevit.Services.Rules
{
    public interface IDoorCandidateRule
    {
        string Name { get; }

        IEnumerable<DoorCandidate> GenerateCandidates(List<CadSegment> doorSegments, DoorDetectSettings settings);
    }
}
