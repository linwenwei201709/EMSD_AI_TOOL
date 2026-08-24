using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Rooms.DeliveryRoutes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.DeliveryRoutes
{
    public static class DeliveryRouteStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("9C8E274C-5F22-4F7D-9A0A-44A6C29F8B91");
        private const string SchemaName = "CadToRevitDeliveryRouteStore";
        private const string FieldName = "JsonPayload";

        public static DeliveryRouteStorePayload Load(Document doc)
        {
            string json = ReadRaw(doc);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateEmptyPayload();
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(DeliveryRouteStorePayload));
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return Normalize(serializer.ReadObject(ms) as DeliveryRouteStorePayload);
                }
            }
            catch
            {
                return CreateEmptyPayload();
            }
        }

        public static void Save(Document doc, DeliveryRouteStorePayload payload)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            DeliveryRouteStorePayload normalized = Normalize(payload);
            normalized.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string json = Serialize(normalized);
            Schema schema = EnsureSchema();
            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return;
            }

            Entity entity = new Entity(schema);
            entity.Set(field, json ?? string.Empty);
            doc.ProjectInformation.SetEntity(entity);
        }

        public static DeliveryRouteStorePayload Upsert(Document doc, DeliveryRouteRecordDto route)
        {
            DeliveryRouteStorePayload payload = Load(doc);
            if (route == null)
            {
                return payload;
            }

            if (string.IsNullOrWhiteSpace(route.RouteId))
            {
                route.RouteId = Guid.NewGuid().ToString("N");
            }

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (string.IsNullOrWhiteSpace(route.CreatedAt))
            {
                route.CreatedAt = now;
            }

            route.UpdatedAt = now;
            route.SubModules = route.SubModules ?? new List<DeliveryRouteSubModuleDto>();

            int existingIndex = payload.Routes.FindIndex(x =>
                string.Equals(x.RouteId, route.RouteId, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                payload.Routes[existingIndex] = route;
            }
            else
            {
                payload.Routes.Insert(0, route);
            }

            Save(doc, payload);
            return payload;
        }

        public static DeliveryRouteStorePayload Delete(Document doc, string routeId)
        {
            DeliveryRouteStorePayload payload = Load(doc);
            if (!string.IsNullOrWhiteSpace(routeId))
            {
                payload.Routes.RemoveAll(x => string.Equals(x.RouteId, routeId, StringComparison.OrdinalIgnoreCase));
                Save(doc, payload);
            }

            return payload;
        }

        private static string ReadRaw(Document doc)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return string.Empty;
            }

            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
            {
                return string.Empty;
            }

            Entity entity = doc.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid())
            {
                return string.Empty;
            }

            Field field = schema.GetField(FieldName);
            if (field == null)
            {
                return string.Empty;
            }

            try
            {
                return entity.Get<string>(field) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static Schema EnsureSchema()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null)
            {
                return existing;
            }

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId("EMSD");
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }

        private static string Serialize(DeliveryRouteStorePayload payload)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(DeliveryRouteStorePayload));
            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, Normalize(payload));
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static DeliveryRouteStorePayload CreateEmptyPayload()
        {
            return new DeliveryRouteStorePayload
            {
                Version = "1.0",
                UpdatedAt = string.Empty
            };
        }

        private static DeliveryRouteStorePayload Normalize(DeliveryRouteStorePayload payload)
        {
            DeliveryRouteStorePayload result = payload ?? CreateEmptyPayload();
            if (string.IsNullOrWhiteSpace(result.Version))
            {
                result.Version = "1.0";
            }

            result.Routes = result.Routes ?? new List<DeliveryRouteRecordDto>();
            foreach (DeliveryRouteRecordDto route in result.Routes.Where(x => x != null))
            {
                route.SubModules = route.SubModules ?? new List<DeliveryRouteSubModuleDto>();
                if (string.IsNullOrWhiteSpace(route.StartLocationType))
                {
                    route.StartLocationType = "Lift";
                }
            }

            return result;
        }
    }
}
