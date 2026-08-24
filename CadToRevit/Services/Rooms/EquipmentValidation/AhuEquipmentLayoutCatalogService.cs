using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.EquipmentValidation
{
    // Small compatibility adapter for the colleague branch.  It consumes only
    // the optional layout_modules portion of the existing backend catalogue and
    // leaves the colleague's local family catalog/editor untouched.
    internal static class AhuEquipmentLayoutCatalogService
    {
        private const string CatalogUrl = "http://127.0.0.1:8000/api/equipment/catalog";

        internal sealed class LayoutModule
        {
            public string Key { get; set; }
            public string Name { get; set; }
            public int LengthMillimeters { get; set; }
            public int WidthMillimeters { get; set; }
            public int HeightMillimeters { get; set; }
            public List<double[]> Points { get; set; } = new List<double[]>();
        }

        [DataContract]
        private sealed class CatalogResponse
        {
            [DataMember(Name = "models")]
            public List<CatalogModel> Models { get; set; }
        }

        [DataContract]
        private sealed class CatalogModel
        {
            [DataMember(Name = "model_id")]
            public int ModelId { get; set; }

            [DataMember(Name = "layout_modules")]
            public CatalogLayout Layout { get; set; }
        }

        [DataContract]
        private sealed class CatalogLayout
        {
            [DataMember(Name = "modules")]
            public List<CatalogLayoutModule> Modules { get; set; }
        }

        [DataContract]
        private sealed class CatalogLayoutModule
        {
            [DataMember(Name = "key")]
            public string Key { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "length_mm")]
            public int LengthMillimeters { get; set; }

            [DataMember(Name = "width_mm")]
            public int WidthMillimeters { get; set; }

            [DataMember(Name = "height_mm")]
            public int HeightMillimeters { get; set; }

            [DataMember(Name = "points")]
            public List<double[]> Points { get; set; }
        }

        internal static IReadOnlyList<LayoutModule> TryGetLayout(int modelId)
        {
            if (modelId < 1 || modelId > 10)
            {
                return new List<LayoutModule>();
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    string json = client.GetStringAsync(CatalogUrl).GetAwaiter().GetResult();
                    CatalogResponse response = Deserialize(json);
                    CatalogModel model = response != null && response.Models != null
                        ? response.Models.Find(x => x != null && x.ModelId == modelId)
                        : null;
                    if (model == null || model.Layout == null || model.Layout.Modules == null)
                    {
                        return new List<LayoutModule>();
                    }

                    List<LayoutModule> result = new List<LayoutModule>();
                    foreach (CatalogLayoutModule module in model.Layout.Modules)
                    {
                        if (module == null || string.IsNullOrWhiteSpace(module.Key) ||
                            module.Points == null || module.Points.Count < 4 ||
                            module.LengthMillimeters <= 0 || module.WidthMillimeters <= 0)
                        {
                            continue;
                        }

                        result.Add(new LayoutModule
                        {
                            Key = module.Key.Trim(),
                            Name = module.Name ?? string.Empty,
                            LengthMillimeters = module.LengthMillimeters,
                            WidthMillimeters = module.WidthMillimeters,
                            HeightMillimeters = module.HeightMillimeters,
                            Points = new List<double[]>(module.Points)
                        });
                    }

                    DiagnosticRecorder.AppendDebug(
                        "[AhuEquipmentLayoutCatalog] modelId=" + modelId +
                        ", layoutCount=" + result.Count);
                    return result;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug(
                    "[AhuEquipmentLayoutCatalog] unavailable modelId=" + modelId +
                    ", error=" + ex.Message);
                return new List<LayoutModule>();
            }
        }

        private static CatalogResponse Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(CatalogResponse));
                return serializer.ReadObject(stream) as CatalogResponse;
            }
        }
    }
}
