using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using CadToRevit.Models.Rooms;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CadToRevit.Services.Rooms
{
    public static class ConnectivitySizeOptionsStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("8D12A1C7-F7CF-43A9-8FE3-3DF8D1263C9B");
        private const string SchemaName = "CadToRevitConnectivitySizeOptionsStore";
        private const string FieldName = "JsonPayload";
        private const double Tolerance = 0.001;

        public static ConnectivitySizeOptionsPayload Load(Document doc)
        {
            string json = ReadRaw(doc);
            if (string.IsNullOrWhiteSpace(json))
            {
                return CreateEmptyPayload();
            }

            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(ConnectivitySizeOptionsPayload));

                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    ConnectivitySizeOptionsPayload payload =
                        serializer.ReadObject(ms) as ConnectivitySizeOptionsPayload;
                    return Normalize(payload);
                }
            }
            catch
            {
                return CreateEmptyPayload();
            }
        }

        public static bool AddDuctSize(
            Document doc,
            double lengthMm,
            double widthMm,
            out ConnectivitySizeOptionsPayload payload,
            out string error)
        {
            payload = Load(doc);
            error = string.Empty;

            if (doc == null || doc.ProjectInformation == null)
            {
                error = "No active project document.";
                return false;
            }

            if (!IsPositiveFinite(lengthMm) || !IsPositiveFinite(widthMm))
            {
                error = "Invalid duct size.";
                return false;
            }

            payload = Normalize(payload);
            if (!payload.DuctSizes.Any(x => AreEqual(x.LengthMm, lengthMm) && AreEqual(x.WidthMm, widthMm)))
            {
                payload.DuctSizes.Add(new RectangularDuctSizeDto
                {
                    LengthMm = lengthMm,
                    WidthMm = widthMm
                });
            }

            return Save(doc, payload, out error);
        }

        public static bool AddPipeSize(
            Document doc,
            double diameterMm,
            out ConnectivitySizeOptionsPayload payload,
            out string error)
        {
            payload = Load(doc);
            error = string.Empty;

            if (doc == null || doc.ProjectInformation == null)
            {
                error = "No active project document.";
                return false;
            }

            if (!IsPositiveFinite(diameterMm))
            {
                error = "Invalid pipe size.";
                return false;
            }

            payload = Normalize(payload);
            if (!payload.PipeSizesMm.Any(x => AreEqual(x, diameterMm)))
            {
                payload.PipeSizesMm.Add(diameterMm);
            }

            return Save(doc, payload, out error);
        }

        public static ConnectivitySizeOptionsPayload Normalize(ConnectivitySizeOptionsPayload payload)
        {
            ConnectivitySizeOptionsPayload result = payload ?? CreateEmptyPayload();
            if (string.IsNullOrWhiteSpace(result.Version))
            {
                result.Version = "1.0";
            }

            result.DuctSizes = DeduplicateDuctSizes(result.DuctSizes);
            result.PipeSizesMm = DeduplicatePipeSizes(result.PipeSizesMm);
            return result;
        }

        private static bool Save(Document doc, ConnectivitySizeOptionsPayload payload, out string error)
        {
            error = string.Empty;
            if (doc == null || doc.ProjectInformation == null)
            {
                error = "No active project document.";
                return false;
            }

            try
            {
                ConnectivitySizeOptionsPayload normalized = Normalize(payload);
                string json = Serialize(normalized);
                Schema schema = EnsureSchema();
                Field field = schema.GetField(FieldName);
                if (field == null)
                {
                    error = "Storage field unavailable.";
                    return false;
                }

                using (Transaction tx = new Transaction(doc, "Save Connectivity Size Options"))
                {
                    tx.Start();
                    Entity entity = new Entity(schema);
                    entity.Set(field, json ?? string.Empty);
                    doc.ProjectInformation.SetEntity(entity);
                    tx.Commit();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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

        private static string Serialize(ConnectivitySizeOptionsPayload payload)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(ConnectivitySizeOptionsPayload));

            using (MemoryStream ms = new MemoryStream())
            {
                serializer.WriteObject(ms, Normalize(payload));
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static ConnectivitySizeOptionsPayload CreateEmptyPayload()
        {
            return new ConnectivitySizeOptionsPayload
            {
                Version = "1.0",
                DuctSizes = new List<RectangularDuctSizeDto>(),
                PipeSizesMm = new List<double>()
            };
        }

        private static List<RectangularDuctSizeDto> DeduplicateDuctSizes(IEnumerable<RectangularDuctSizeDto> sizes)
        {
            List<RectangularDuctSizeDto> result = new List<RectangularDuctSizeDto>();
            foreach (RectangularDuctSizeDto size in sizes ?? Enumerable.Empty<RectangularDuctSizeDto>())
            {
                if (size == null || !IsPositiveFinite(size.LengthMm) || !IsPositiveFinite(size.WidthMm))
                {
                    continue;
                }

                double lengthMm = Math.Round(size.LengthMm, 3);
                double widthMm = Math.Round(size.WidthMm, 3);
                if (result.Any(x => AreEqual(x.LengthMm, lengthMm) && AreEqual(x.WidthMm, widthMm)))
                {
                    continue;
                }

                result.Add(new RectangularDuctSizeDto
                {
                    LengthMm = lengthMm,
                    WidthMm = widthMm
                });
            }

            return result
                .OrderBy(x => x.LengthMm)
                .ThenBy(x => x.WidthMm)
                .ToList();
        }

        private static List<double> DeduplicatePipeSizes(IEnumerable<double> sizes)
        {
            List<double> result = new List<double>();
            foreach (double size in sizes ?? Enumerable.Empty<double>())
            {
                if (!IsPositiveFinite(size))
                {
                    continue;
                }

                double diameterMm = Math.Round(size, 3);
                if (!result.Any(x => AreEqual(x, diameterMm)))
                {
                    result.Add(diameterMm);
                }
            }

            return result.OrderBy(x => x).ToList();
        }

        private static bool IsPositiveFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;
        }

        private static bool AreEqual(double left, double right)
        {
            return Math.Abs(left - right) < Tolerance;
        }
    }
}
