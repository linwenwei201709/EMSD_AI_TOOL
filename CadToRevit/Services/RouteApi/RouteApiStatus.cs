namespace CadToRevit.Services.RouteApi
{
    public enum RouteApiStatus
    {
        Stopped,
        Starting,
        Running,
        RunningExternal,
        ExternalStaleRunning,
        Error
    }
}
