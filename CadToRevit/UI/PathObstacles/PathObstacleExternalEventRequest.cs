using CadToRevit.Models;

namespace CadToRevit.UI.PathObstacles
{
    internal enum PathObstacleRequestType
    {
        None,
        Refresh,
        Locate,
        Delete,
        DeleteAll,
        Rename,
        BeginDrawing,
        PickNextPoint,
        FinishDrawing,
        CancelDrawing
    }

    internal sealed class PathObstacleExternalEventRequest
    {
        public PathObstacleRequestType Type { get; set; }
        public PathObstacleRecord Record { get; set; }
        public string NewName { get; set; }
    }
}
