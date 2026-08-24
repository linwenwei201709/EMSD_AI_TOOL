using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CadToRevit.Services.RouteApi
{
    public static class RouteApiProcessService
    {
        private const string ApiBaseUrl = "http://127.0.0.1:8000";
        private const string HealthCheckUrl = ApiBaseUrl + "/api/health";
        private const int HealthCheckTimeoutSeconds = 5;
        private const int StartupWaitSeconds = 45;
        private const int MaxLogLines = 2000;

        private static readonly object SyncRoot = new object();
        private static readonly List<string> LogBuffer = new List<string>();
        private static Process _process;
        private static bool _startedByPlugin;
        private static RouteApiStatus _status = RouteApiStatus.Stopped;

        public static event EventHandler<string> LogReceived;
        public static event EventHandler<RouteApiStatus> StatusChanged;

        public static string ApiUrl => ApiBaseUrl;

        public static string[] GetLogSnapshot()
        {
            lock (SyncRoot)
            {
                return LogBuffer.ToArray();
            }
        }

        public static void ClearLog()
        {
            lock (SyncRoot)
            {
                LogBuffer.Clear();
            }
        }

        public static RouteApiStatus Status
        {
            get
            {
                lock (SyncRoot)
                {
                    return _status;
                }
            }
        }

        public static string ResolveExecutablePath()
        {
            string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(pluginDir ?? string.Empty, "RouteApi", "AHU_API.exe");
        }

        public static void Start()
        {
            string apiExePath = ResolveExecutablePath();
            lock (SyncRoot)
            {
                if (_process != null && !_process.HasExited)
                {
                    if (IsHealthCheckOk())
                    {
                        SetStatusNoLock(RouteApiStatus.Running);
                        AppendLog("Route API is already running.");
                        return;
                    }

                    SetStatusNoLock(RouteApiStatus.Error);
                    AppendLog("[ERR] Route API process is running but health check failed. Please restart it from the plugin.");
                    return;
                }
            }

            KillMatchingRouteApiProcesses(apiExePath);

            if (IsHealthCheckOk())
            {
                SetStatus(RouteApiStatus.ExternalStaleRunning);
                AppendLog("Existing Route API process detected. Please restart it from the plugin.");
                return;
            }

            if (!File.Exists(apiExePath))
            {
                SetStatus(RouteApiStatus.Error);
                AppendLog("Route API executable was not found.");
                AppendLog("Expected path: " + apiExePath);
                return;
            }

            SetStatus(RouteApiStatus.Starting);
            AppendLog("Starting Route API...");
            AppendLog("Executable: " + apiExePath);

            string apiWorkingDirectory = Path.GetDirectoryName(apiExePath) ?? string.Empty;
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = apiExePath,
                WorkingDirectory = apiWorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONLEGACYWINDOWSSTDIO"] = "0";

            Process process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += OnOutputDataReceived;
            process.ErrorDataReceived += OnErrorDataReceived;
            process.Exited += OnProcessExited;

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                lock (SyncRoot)
                {
                    _process = process;
                    _startedByPlugin = true;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }

                SetStatus(RouteApiStatus.Error);
                AppendLog("[ERR] Failed to start Route API: " + ex.Message);
                return;
            }

            Task.Run(() => WaitForReady());
        }

        public static void Stop()
        {
            Process process;
            lock (SyncRoot)
            {
                process = _process;
            }

            if (process == null || process.HasExited)
            {
                SetStatus(RouteApiStatus.Stopped);
                AppendLog("Route API is not running by this plugin.");
                return;
            }

            AppendLog("Stopping Route API...");
            try
            {
                try
                {
                    process.CloseMainWindow();
                }
                catch
                {
                }

                KillProcessTree(process);
            }
            catch (Exception ex)
            {
                AppendLog("[ERR] Failed to stop Route API: " + ex.Message);
                SetStatus(RouteApiStatus.Error);
                return;
            }
            finally
            {
                CleanupProcess(process);
                lock (SyncRoot)
                {
                    if (ReferenceEquals(_process, process))
                    {
                        _process = null;
                        _startedByPlugin = false;
                    }
                }
            }

            SetStatus(RouteApiStatus.Stopped);
            AppendLog("Route API stopped.");
        }

        public static void Restart()
        {
            Stop();
            KillMatchingRouteApiProcesses(ResolveExecutablePath());
            Start();
        }

        public static bool HealthCheck()
        {
            return RefreshStatusFromHealth(true);
        }

        public static bool RefreshStatusFromHealth()
        {
            return RefreshStatusFromHealth(false);
        }

        public static void StopApi()
        {
            bool shouldStop;
            lock (SyncRoot)
            {
                shouldStop = _startedByPlugin && _process != null && !_process.HasExited;
            }

            if (shouldStop)
            {
                Stop();
            }
        }

        public static void StopIfStartedByPlugin()
        {
            StopApi();
        }

        private static void WaitForReady()
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(StartupWaitSeconds);
            while (DateTime.UtcNow <= deadline)
            {
                Process process;
                lock (SyncRoot)
                {
                    process = _process;
                }

                if (process == null || process.HasExited)
                {
                    SetStatus(RouteApiStatus.Error);
                    AppendLog("[ERR] Route API exited before it became ready.");
                    return;
                }

                if (IsHealthCheckOk())
                {
                    SetStatus(RouteApiStatus.Running);
                    AppendLog("Route API is ready.");
                    return;
                }

                Thread.Sleep(1000);
            }

            SetStatus(RouteApiStatus.Error);
            AppendLog("[ERR] Route API started but health check did not respond in time.");
            AppendLog("Please check the API log.");
        }

        private static bool IsHealthCheckOk()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(HealthCheckTimeoutSeconds);
                    using (HttpResponseMessage response = client.GetAsync(HealthCheckUrl).GetAwaiter().GetResult())
                    {
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool RefreshStatusFromHealth(bool appendLog)
        {
            bool ok = IsHealthCheckOk();
            if (!ok)
            {
                SetStatus(RouteApiStatus.Error);
                if (appendLog)
                {
                    AppendLog("Health Check: Failed. Please restart the Route API from the plugin.");
                }

                return false;
            }

            bool ownedProcessRunning;
            lock (SyncRoot)
            {
                ownedProcessRunning = _startedByPlugin && _process != null && !_process.HasExited;
            }

            if (ownedProcessRunning)
            {
                SetStatus(RouteApiStatus.Running);
                if (appendLog)
                {
                    AppendLog("Health Check: OK");
                }

                return true;
            }

            SetStatus(RouteApiStatus.ExternalStaleRunning);
            AppendLog("Existing Route API process detected. Please restart it from the plugin.");
            return true;
        }

        private static void KillMatchingRouteApiProcesses(string apiExePath)
        {
            if (string.IsNullOrWhiteSpace(apiExePath))
            {
                return;
            }

            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(apiExePath)))
            {
                try
                {
                    if (IsCurrentProcess(process))
                    {
                        continue;
                    }

                    string processPath = GetProcessPath(process);
                    if (!string.Equals(processPath, apiExePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    AppendLog("Stopping stale Route API process: " + process.Id);
                    KillProcessTree(process);
                }
                catch (Exception ex)
                {
                    AppendLog("[ERR] Failed to stop stale Route API process: " + ex.Message);
                }
                finally
                {
                    try
                    {
                        process.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool IsCurrentProcess(Process process)
        {
            lock (SyncRoot)
            {
                return _process != null && ReferenceEquals(_process, process);
            }
        }

        private static string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule.FileName;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void KillProcessTree(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            try
            {
                process.CloseMainWindow();
            }
            catch
            {
            }

            try
            {
                if (process.WaitForExit(3000))
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                ProcessStartInfo taskkillInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/PID " + process.Id + " /T /F",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (Process taskkill = Process.Start(taskkillInfo))
                {
                    if (taskkill != null)
                    {
                        taskkill.WaitForExit(5000);
                    }
                }
            }
            catch
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                }
            }

            try
            {
                process.WaitForExit(3000);
            }
            catch
            {
            }
        }

        private static void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog("[OUT] " + e.Data);
            }
        }

        private static void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AppendLog("[ERR] " + e.Data);
            }
        }

        private static void OnProcessExited(object sender, EventArgs e)
        {
            Process process = sender as Process;
            CleanupProcess(process);
            lock (SyncRoot)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _startedByPlugin = false;
                }
            }

            SetStatus(RouteApiStatus.Stopped);
            AppendLog("Route API process exited.");
        }

        private static void CleanupProcess(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                process.OutputDataReceived -= OnOutputDataReceived;
                process.ErrorDataReceived -= OnErrorDataReceived;
                process.Exited -= OnProcessExited;
                process.Dispose();
            }
            catch
            {
            }
        }

        private static void SetStatus(RouteApiStatus status)
        {
            lock (SyncRoot)
            {
                SetStatusNoLock(status);
            }
        }

        private static void SetStatusNoLock(RouteApiStatus status)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
            StatusChanged?.Invoke(null, status);
        }

        private static void AppendLog(string line)
        {
            string formattedLine = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + (line ?? string.Empty);
            lock (SyncRoot)
            {
                LogBuffer.Add(formattedLine);
                while (LogBuffer.Count > MaxLogLines)
                {
                    LogBuffer.RemoveAt(0);
                }
            }

            DiagnosticRecorder.AppendLine(
                "RouteApi_" + DateTime.Now.ToString("yyyyMMdd") + ".log",
                formattedLine);
            LogReceived?.Invoke(null, formattedLine);
        }
    }
}
