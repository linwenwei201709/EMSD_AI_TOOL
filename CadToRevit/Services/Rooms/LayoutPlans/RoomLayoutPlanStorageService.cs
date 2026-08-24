using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Rooms.LayoutPlans;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms.LayoutPlans
{
    public static class RoomLayoutPlanStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("2C2F65D0-3E62-4C4F-A8F4-3A09C75F9C11");
        private const string SchemaName = "CadToRevitRoomLayoutPlanStore";
        private const string FieldName = "JsonPayload";

        public static RoomLayoutPlanStorePayload Load(Document doc)
        {
            string json = ReadRaw(doc);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateEmptyPayload();
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(RoomLayoutPlanStorePayload));

                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    RoomLayoutPlanStorePayload payload =
                        serializer.ReadObject(ms) as RoomLayoutPlanStorePayload;

                    return Normalize(payload);
                }
            }
            catch
            {
                return CreateEmptyPayload();
            }
        }

        public static void Save(Document doc, RoomLayoutPlanStorePayload payload)
        {
            if (doc == null || doc.ProjectInformation == null)
            {
                return;
            }

            RoomLayoutPlanStorePayload normalized = Normalize(payload);
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

        public static RoomLayoutPlanStorePayload Upsert(Document doc, RoomLayoutPlanDto plan)
        {
            RoomLayoutPlanStorePayload payload = Load(doc);
            if (plan == null)
            {
                return payload;
            }

            if (string.IsNullOrWhiteSpace(plan.LayoutId))
            {
                plan.LayoutId = Guid.NewGuid().ToString("N");
            }

            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (string.IsNullOrWhiteSpace(plan.CreatedAt))
            {
                plan.CreatedAt = now;
            }

            plan.UpdatedAt = now;
            plan.ActiveGeneratedElements = plan.ActiveGeneratedElements ?? new LayoutGeneratedElementsDto();

            int existingIndex = payload.Plans.FindIndex(x =>
                string.Equals(x.LayoutId, plan.LayoutId, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                payload.Plans[existingIndex] = plan;
            }
            else
            {
                payload.Plans.Insert(0, plan);
            }

            Save(doc, payload);
            return payload;
        }

        public static RoomLayoutPlanStorePayload Delete(Document doc, string layoutId)
        {
            RoomLayoutPlanStorePayload payload = Load(doc);
            if (string.IsNullOrWhiteSpace(layoutId))
            {
                return payload;
            }

            payload.Plans.RemoveAll(x =>
                string.Equals(x.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));

            Save(doc, payload);
            return payload;
        }

        public static RoomLayoutPlanDto Find(Document doc, string layoutId)
        {
            if (string.IsNullOrWhiteSpace(layoutId))
            {
                return null;
            }

            RoomLayoutPlanStorePayload payload = Load(doc);
            return payload.Plans.FirstOrDefault(x =>
                string.Equals(x.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase));
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

        private static string Serialize(RoomLayoutPlanStorePayload payload)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(RoomLayoutPlanStorePayload));

            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, Normalize(payload));
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static RoomLayoutPlanStorePayload CreateEmptyPayload()
        {
            return new RoomLayoutPlanStorePayload
            {
                Version = "1.0",
                UpdatedAt = string.Empty
            };
        }

        private static RoomLayoutPlanStorePayload Normalize(RoomLayoutPlanStorePayload payload)
        {
            RoomLayoutPlanStorePayload result = payload ?? CreateEmptyPayload();

            if (string.IsNullOrWhiteSpace(result.Version))
            {
                result.Version = "1.0";
            }

            if (result.Plans == null)
            {
                result.Plans = new List<RoomLayoutPlanDto>();
            }

            if (result.ActiveLayoutIdByRoomKey == null)
            {
                result.ActiveLayoutIdByRoomKey =
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (RoomLayoutPlanDto plan in result.Plans.Where(x => x != null))
            {
                plan.SadWall = plan.SadWall ?? new LayoutWallSelectionDto();
                plan.RadWall = plan.RadWall ?? new LayoutWallSelectionDto();
                plan.ChwsWall = plan.ChwsWall ?? new LayoutWallSelectionDto();
                plan.ChwrWall = plan.ChwrWall ?? new LayoutWallSelectionDto();
                plan.ActiveGeneratedElements = plan.ActiveGeneratedElements ?? new LayoutGeneratedElementsDto();
                if (plan.EquipmentValidation != null && plan.EquipmentValidation.Reasons == null)
                {
                    plan.EquipmentValidation.Reasons = new List<string>();
                }
            }

            return result;
        }
    }
}
