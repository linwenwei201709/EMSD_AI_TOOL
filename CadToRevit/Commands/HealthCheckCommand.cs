using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class HealthCheckCommand : IExternalCommand
    {
        private const string HealthCheckUrl = "http://127.0.0.1:8000/api/health";

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            try
            {
                HealthCheckResponse response = FetchHealthCheck();
                bool isHealthy = response != null &&
                    string.Equals(response.Status, "ok", StringComparison.OrdinalIgnoreCase);

                string dialogMessage = isHealthy
                    ? Loc.T("Dialog.HealthCheck.Success", response.ServerTime ?? string.Empty, response.Sessions)
                    : Loc.T(
                        "Dialog.HealthCheck.Failed",
                        response == null ? string.Empty : (response.Status ?? string.Empty),
                        response == null ? string.Empty : (response.RawJson ?? string.Empty));

                TaskDialog.Show(Loc.T("Dialog.HealthCheck.Title"), dialogMessage);
                return isHealthy ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(
                    Loc.T("Dialog.HealthCheck.Title"),
                    Loc.T("Dialog.HealthCheck.Error", ex.Message));
                return Result.Failed;
            }
        }

        private static HealthCheckResponse FetchHealthCheck()
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                string json = client.GetStringAsync(HealthCheckUrl).GetAwaiter().GetResult();
                HealthCheckResponse response = DeserializeResponse(json) ?? new HealthCheckResponse();
                response.RawJson = json;
                return response;
            }
        }

        private static HealthCheckResponse DeserializeResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(HealthCheckResponse));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as HealthCheckResponse;
            }
        }

        [DataContract]
        private sealed class HealthCheckResponse
        {
            [DataMember(Name = "status")]
            public string Status { get; set; }

            [DataMember(Name = "sessions")]
            public int Sessions { get; set; }

            [DataMember(Name = "server_time")]
            public string ServerTime { get; set; }

            public string RawJson { get; set; }
        }
    }
}
