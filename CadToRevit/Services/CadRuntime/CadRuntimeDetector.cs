using Microsoft.Win32;
using CadToRevit.Services.Diagnostics;
using System;
using System.Diagnostics;
using System.IO;

namespace CadToRevit.Services.CadRuntime
{
    internal static class CadRuntimeDetector
    {
        private const string AutodeskAutoCadRoot = @"SOFTWARE\Autodesk\AutoCAD";

        private static CadRuntimeInfo _cached;
        private static DateTime _cachedAtUtc;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        internal static CadRuntimeInfo Detect(bool forceRefresh = false)
        {
            if (!forceRefresh && _cached != null && (DateTime.UtcNow - _cachedAtUtc) < CacheDuration)
            {
                return _cached;
            }

            CadRuntimeInfo info = DetectCore();
            WriteRuntimeLog(info);
            _cached = info;
            _cachedAtUtc = DateTime.UtcNow;
            return info;
        }

        private static CadRuntimeInfo DetectCore()
        {
            try
            {
                CadRuntimeInfo info = TryDetectFromRegistryView(RegistryView.Registry64);
                if (info != null && info.Status != CadRuntimeStatus.NotInstalled)
                {
                    return info;
                }

                info = TryDetectFromRegistryView(RegistryView.Registry32);
                if (info != null && info.Status != CadRuntimeStatus.NotInstalled)
                {
                    return info;
                }

                return new CadRuntimeInfo
                {
                    Status = CadRuntimeStatus.NotInstalled,
                    ReleaseKeyName = CadRuntimeTarget.ReleaseKey,
                    Message = CadRuntimeTarget.ProductName + " registry key was not found."
                };
            }
            catch (Exception ex)
            {
                return new CadRuntimeInfo
                {
                    Status = CadRuntimeStatus.Broken,
                    ReleaseKeyName = CadRuntimeTarget.ReleaseKey,
                    Message = "CAD runtime detection failed: " + ex.Message
                };
            }
        }

        private static CadRuntimeInfo TryDetectFromRegistryView(RegistryView view)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey autoCadRoot = baseKey.OpenSubKey(AutodeskAutoCadRoot, false))
            {
                if (autoCadRoot == null)
                {
                    return new CadRuntimeInfo
                    {
                        Status = CadRuntimeStatus.NotInstalled,
                        RegistryViewName = view.ToString(),
                        ReleaseKeyName = CadRuntimeTarget.ReleaseKey,
                        Message = "AutoCAD registry root key was not found."
                    };
                }

                using (RegistryKey releaseKey = autoCadRoot.OpenSubKey(CadRuntimeTarget.ReleaseKey, false))
                {
                    if (releaseKey == null)
                    {
                        return new CadRuntimeInfo
                        {
                            Status = CadRuntimeStatus.NotInstalled,
                            RegistryViewName = view.ToString(),
                            ReleaseKeyName = CadRuntimeTarget.ReleaseKey,
                            Message = CadRuntimeTarget.ProductName + " release key " + CadRuntimeTarget.ReleaseKey + " was not found."
                        };
                    }

                    foreach (string productKeyName in releaseKey.GetSubKeyNames())
                    {
                        using (RegistryKey productKey = releaseKey.OpenSubKey(productKeyName, false))
                        {
                            if (productKey == null)
                            {
                                continue;
                            }

                            string location = productKey.GetValue("Location") as string;
                            if (string.IsNullOrWhiteSpace(location))
                            {
                                continue;
                            }

                            return BuildResult(location, CadRuntimeTarget.ReleaseKey, productKeyName, view.ToString());
                        }
                    }
                }
            }

            return new CadRuntimeInfo
            {
                Status = CadRuntimeStatus.InstallLocationMissing,
                RegistryViewName = view.ToString(),
                ReleaseKeyName = CadRuntimeTarget.ReleaseKey,
                Message = CadRuntimeTarget.ProductName + " keys exist but no valid install location was found."
            };
        }

        private static CadRuntimeInfo BuildResult(string installLocation, string releaseKeyName, string productKeyName, string registryViewName)
        {
            CadRuntimeInfo info = new CadRuntimeInfo
            {
                ReleaseKeyName = releaseKeyName,
                ProductKeyName = productKeyName,
                ProductRootKey = AutodeskAutoCadRoot,
                RegistryViewName = registryViewName,
                InstallLocation = installLocation,
                AcCoreMgdPath = Path.Combine(installLocation, "AcCoreMgd.dll"),
                AcDbMgdPath = Path.Combine(installLocation, "AcDbMgd.dll"),
                AcMgdPath = Path.Combine(installLocation, "AcMgd.dll")
            };

            if (!Directory.Exists(installLocation))
            {
                info.Status = CadRuntimeStatus.InstallLocationMissing;
                info.Message = "Registry location exists but install directory is missing: " + installLocation;
                return info;
            }

            string missingDlls = GetMissingDlls(info);
            if (!string.IsNullOrEmpty(missingDlls))
            {
                info.Status = CadRuntimeStatus.DllMissing;
                info.Message = CadRuntimeTarget.ProductName + " install directory exists but CAD .NET DLLs are incomplete. Missing=" + missingDlls;
                return info;
            }

            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(info.AcCoreMgdPath);
            info.FileVersion = fvi != null ? fvi.FileVersion : null;

            if (!IsSupportedVersion(fvi))
            {
                info.Status = CadRuntimeStatus.UnsupportedVersion;
                info.Message = "Detected CAD DLL version is outside the supported " + CadRuntimeTarget.ProductName + " range.";
                return info;
            }

            info.Status = CadRuntimeStatus.Ready;
            info.Message = "Detected supported " + CadRuntimeTarget.ProductName + " runtime.";
            return info;
        }

        private static bool IsSupportedVersion(FileVersionInfo fvi)
        {
            if (fvi == null)
            {
                return false;
            }

            // Use major/minor parts to avoid locale-specific version string parsing issues.
            return fvi.FileMajorPart == CadRuntimeTarget.FileMajor && fvi.FileMinorPart == CadRuntimeTarget.FileMinor;
        }

        private static string GetMissingDlls(CadRuntimeInfo info)
        {
            string missing = string.Empty;
            AppendMissingDll(ref missing, info.AcCoreMgdPath);
            AppendMissingDll(ref missing, info.AcDbMgdPath);
            AppendMissingDll(ref missing, info.AcMgdPath);
            return missing;
        }

        private static void AppendMissingDll(ref string missing, string path)
        {
            if (File.Exists(path))
            {
                return;
            }

            if (!string.IsNullOrEmpty(missing))
            {
                missing += ",";
            }

            missing += Path.GetFileName(path);
        }

        private static void WriteRuntimeLog(CadRuntimeInfo info)
        {
            try
            {
                DiagnosticRecorder.AppendDebug(
                    "[CADRuntime] Target=" + CadRuntimeTarget.ProductName +
                    " | ReleaseKey=" + CadRuntimeTarget.ReleaseKey +
                    " | RegistryView=" + (info != null ? (info.RegistryViewName ?? string.Empty) : string.Empty) +
                    " | InstallLocation=" + (info != null ? (info.InstallLocation ?? string.Empty) : string.Empty) +
                    " | AcCoreMgd=" + GetDllState(info != null ? info.AcCoreMgdPath : null) +
                    " | AcDbMgd=" + GetDllState(info != null ? info.AcDbMgdPath : null) +
                    " | AcMgd=" + GetDllState(info != null ? info.AcMgdPath : null) +
                    " | DetectedVersion=" + (info != null ? (info.FileVersion ?? string.Empty) : string.Empty) +
                    " | Status=" + (info != null ? info.Status.ToString() : CadRuntimeStatus.Unknown.ToString()) +
                    " | Reason=" + (info != null ? (info.Message ?? string.Empty) : string.Empty));
            }
            catch
            {
            }
        }

        private static string GetDllState(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "Unknown";
            }

            return File.Exists(path) ? "Found" : "Missing";
        }
    }
}
