namespace CadToRevit.Services.CadRuntime
{
    internal enum CadRuntimeStatus
    {
        Unknown = 0,
        Ready = 1,
        NotInstalled = 2,
        InstallLocationMissing = 3,
        DllMissing = 4,
        UnsupportedVersion = 5,
        Broken = 6
    }
}
