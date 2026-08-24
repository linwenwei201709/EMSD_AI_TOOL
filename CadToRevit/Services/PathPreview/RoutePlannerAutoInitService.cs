using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Commands;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace CadToRevit.Services.PathPreview
{
    public sealed class RoutePlannerInitResult
    {
        public bool Success { get; set; }

        public string SessionId { get; set; }

        public string IfcFilePath { get; set; }

        public bool Reused { get; set; }

        public bool ApiUnavailable { get; set; }

        public bool ExportFailed { get; set; }

        public bool InitFailed { get; set; }

        public string Message { get; set; }
    }

    public static class RoutePlannerAutoInitService
    {
        private const string HealthCheckUrl = "http://127.0.0.1:8000/api/health";
        private const string ProjectInitUrl = "http://127.0.0.1:8000/api/init";
        private const int DefaultHeadroomHeightMm = 2200;
        private const int DefaultDoorWidthToleranceMm = 0;

        public static RoutePlannerInitResult EnsureInitialized(Document doc, UIDocument uiDoc)
        {
            if (doc == null)
            {
                return Failure("No active Revit document is available.");
            }

            if (!CheckHealth())
            {
                return Failure(
                    "Route API is not available. Please start Route API Console first.",
                    true,
                    false,
                    false);
            }

            RoutePlannerSessionState reusableState;
            if (RoutePlannerSessionCacheService.IsReusable(doc, out reusableState))
            {
                DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] ReuseSession sessionId=" + reusableState.SessionId);
                ProjectInitializationCommand.SaveRuntimeSession(
                    reusableState.SessionId,
                    reusableState.IfcFilePath,
                    string.Empty,
                    reusableState.HeadroomHeightMm.ToString(CultureInfo.InvariantCulture),
                    reusableState.DoorWidthToleranceMm.ToString(CultureInfo.InvariantCulture));

                return new RoutePlannerInitResult
                {
                    Success = true,
                    Reused = true,
                    SessionId = reusableState.SessionId,
                    IfcFilePath = reusableState.IfcFilePath,
                    Message = string.Empty
                };
            }

            RoutePlannerSessionState existingState = RoutePlannerSessionCacheService.GetSession(doc);
            string sessionId = ResolveOrCreateSessionId(doc, existingState);
            string ifcPath = BuildTemporaryIfcPath(doc, sessionId);
            EnsureDirectory(ifcPath);

            DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] ExportIfc path=" + ifcPath);
            IfcPathExportService.IfcPathExportResult exportResult = IfcPathExportService.Export(doc, ifcPath);
            if (exportResult == null || !exportResult.Success || !File.Exists(ifcPath))
            {
                DiagnosticRecorder.AppendDebug(
                    "[RoutePlannerAutoInit] ExportFailed error=" +
                    (exportResult == null ? "(null result)" : exportResult.Error ?? string.Empty));
                return Failure(
                    "Failed to export temporary IFC for route planning." + Environment.NewLine +
                    "Please check the model and try again.",
                    false,
                    true,
                    false);
            }

            string requestJson = BuildProjectInitRequestJson(ifcPath, sessionId);
            DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] InitRequest=" + requestJson);

            ProjectInitResponseDto initResponse;
            string initResponseText;
            if (!PostProjectInit(requestJson, out initResponse, out initResponseText))
            {
                DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] InitFailed response=" + (initResponseText ?? string.Empty));
                return Failure(
                    "Route planner initialization failed." + Environment.NewLine +
                    "Please check the Route API Console log for details.",
                    false,
                    false,
                    true);
            }

            string returnedSessionId = initResponse != null && !string.IsNullOrWhiteSpace(initResponse.SessionId)
                ? initResponse.SessionId.Trim()
                : sessionId;

            RoutePlannerSessionState newState = new RoutePlannerSessionState
            {
                DocumentKey = RoutePlannerSessionCacheService.BuildDocumentKey(doc),
                SessionId = returnedSessionId,
                IfcFilePath = ifcPath,
                LastExportUtc = DateTime.UtcNow,
                HeadroomHeightMm = DefaultHeadroomHeightMm,
                DoorWidthToleranceMm = DefaultDoorWidthToleranceMm,
                IsDirty = false,
                DirtyReason = string.Empty
            };
            RoutePlannerSessionCacheService.SaveSession(doc, newState);
            ProjectInitializationCommand.SaveRuntimeSession(
                returnedSessionId,
                ifcPath,
                string.Empty,
                DefaultHeadroomHeightMm.ToString(CultureInfo.InvariantCulture),
                DefaultDoorWidthToleranceMm.ToString(CultureInfo.InvariantCulture));

            DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] InitSuccess sessionId=" + returnedSessionId + ", ifc=" + ifcPath);
            return new RoutePlannerInitResult
            {
                Success = true,
                SessionId = returnedSessionId,
                IfcFilePath = ifcPath,
                Reused = false,
                Message = string.Empty
            };
        }

        private static bool CheckHealth()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    using (HttpResponseMessage response = client.GetAsync(HealthCheckUrl).GetAwaiter().GetResult())
                    {
                        DiagnosticRecorder.AppendDebug(
                            "[RoutePlannerAutoInit] HealthStatus=" +
                            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                            " " +
                            response.StatusCode.ToString());
                        return response.IsSuccessStatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] HealthFailed message=" + ex.Message);
                return false;
            }
        }

        private static bool PostProjectInit(string requestJson, out ProjectInitResponseDto payload, out string responseText)
        {
            payload = null;
            responseText = null;
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(5);
                    using (StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = client.PostAsync(ProjectInitUrl, content).GetAwaiter().GetResult())
                    {
                        responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        DiagnosticRecorder.AppendDebug(
                            "[RoutePlannerAutoInit] InitStatus=" +
                            ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                            " " +
                            response.StatusCode.ToString());
                        DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] InitResponse=" + (responseText ?? string.Empty));
                        if (!response.IsSuccessStatusCode)
                        {
                            return false;
                        }

                        payload = DeserializeInitResponse(responseText);
                        return payload != null &&
                            string.Equals(payload.Status, "success", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                responseText = ex.Message;
                DiagnosticRecorder.AppendDebug("[RoutePlannerAutoInit] InitException=" + ex.Message);
                return false;
            }
        }

        private static ProjectInitResponseDto DeserializeInitResponse(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(responseText)))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProjectInitResponseDto));
                    return serializer.ReadObject(stream) as ProjectInitResponseDto;
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ResolveOrCreateSessionId(Document doc, RoutePlannerSessionState existingState)
        {
            if (existingState != null && !string.IsNullOrWhiteSpace(existingState.SessionId))
            {
                return existingState.SessionId.Trim();
            }

            return "revit_" + BuildDocumentHash(doc) + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        private static string BuildTemporaryIfcPath(Document doc, string sessionId)
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory = Path.Combine(root, "EMSD AI Tool", "RoutePlannerCache");
            string safeSessionId = MakeFileNameSafe(string.IsNullOrWhiteSpace(sessionId) ? BuildDocumentHash(doc) : sessionId);
            string fileName = "route_model_" + safeSessionId + ".ifc";
            return Path.Combine(directory, fileName);
        }

        private static void EnsureDirectory(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static string BuildProjectInitRequestJson(string ifcFilePath, string sessionId)
        {
            return "{" +
                   "\"ifc_file_path\":\"" + EscapeJson(ifcFilePath) + "\"," +
                   "\"session_id\":\"" + EscapeJson(sessionId) + "\"" +
                   "}";
        }

        private static string BuildDocumentHash(Document doc)
        {
            string source = doc == null
                ? Guid.NewGuid().ToString("N")
                : (!string.IsNullOrWhiteSpace(doc.PathName) ? doc.PathName : doc.Title + "#" + doc.GetHashCode().ToString(CultureInfo.InvariantCulture));

            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(source ?? string.Empty));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < 8 && i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static string MakeFileNameSafe(string value)
        {
            string text = value ?? string.Empty;
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(invalid, '_');
            }
            return text;
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static RoutePlannerInitResult Failure(
            string message,
            bool apiUnavailable,
            bool exportFailed,
            bool initFailed)
        {
            return new RoutePlannerInitResult
            {
                Success = false,
                Message = message,
                ApiUnavailable = apiUnavailable,
                ExportFailed = exportFailed,
                InitFailed = initFailed
            };
        }

        private static RoutePlannerInitResult Failure(string message)
        {
            return Failure(message, false, false, false);
        }

        [DataContract]
        private sealed class ProjectInitResponseDto
        {
            [DataMember(Name = "status")]
            public string Status { get; set; }

            [DataMember(Name = "session_id")]
            public string SessionId { get; set; }
        }
    }
}
