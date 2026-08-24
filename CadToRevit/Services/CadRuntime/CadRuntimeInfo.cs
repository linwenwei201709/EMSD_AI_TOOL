namespace CadToRevit.Services.CadRuntime
{
    internal sealed class CadRuntimeInfo
    {
        public CadRuntimeStatus Status { get; set; } = CadRuntimeStatus.Unknown;

        public string ProductRootKey { get; set; }
        public string ProductKeyName { get; set; }
        public string ReleaseKeyName { get; set; }
        public string RegistryViewName { get; set; }
        public string InstallLocation { get; set; }

        public string AcCoreMgdPath { get; set; }
        public string AcDbMgdPath { get; set; }
        public string AcMgdPath { get; set; }

        public string FileVersion { get; set; }
        public string Message { get; set; }

        public bool IsReady => Status == CadRuntimeStatus.Ready;

        public override string ToString()
        {
            return "Status=" + Status +
                   ", Target=" + CadRuntimeTarget.ProductName +
                   ", ReleaseKey=" + CadRuntimeTarget.ReleaseKey +
                   ", RegistryView=" + (RegistryViewName ?? string.Empty) +
                   ", InstallLocation=" + (InstallLocation ?? string.Empty) +
                   ", Version=" + (FileVersion ?? string.Empty) +
                   ", Message=" + (Message ?? string.Empty);
        }
    }
}
