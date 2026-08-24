using System;

namespace CadToRevit.Models
{
    public sealed class PathObstacleRecord
    {
        public string ObstacleId { get; set; }
        public string Name { get; set; }
        public int ElementIdValue { get; set; }
        public string UniqueId { get; set; }
        public string LevelName { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
