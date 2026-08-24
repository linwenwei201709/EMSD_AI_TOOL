using CadToRevit.Services.Diagnostics;
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadToRevit.Services.Config
{
    [DataContract]
    public sealed class GenerationGuardConfig
    {
        [DataMember(Name = "MaxSegmentsPreview")]
        public int MaxSegmentsPreview { get; set; } = 3000;

        [DataMember(Name = "MaxSegmentsHardStop")]
        public int MaxSegmentsHardStop { get; set; } = 8000;

        [DataMember(Name = "MaxEstimatedWalls")]
        public int MaxEstimatedWalls { get; set; } = 2000;

        [DataMember(Name = "DefaultMinLengthMm")]
        public double DefaultMinLengthMm { get; set; } = 200.0;

        [DataMember(Name = "HighRiskMinLengthMm")]
        public double HighRiskMinLengthMm { get; set; } = 500.0;

        [DataMember(Name = "BatchSize")]
        public int BatchSize { get; set; } = 200;
    }

    public static class GenerationGuardConfigProvider
    {
        public static GenerationGuardConfig Load()
        {
            GenerationGuardConfig fallback = new GenerationGuardConfig();
            string dllDir = null;
            try
            {
                dllDir = Path.GetDirectoryName(typeof(GenerationGuardConfigProvider).Assembly.Location);
            }
            catch
            {
                dllDir = null;
            }

            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EMSD",
                "CadToRevit");

            string[] candidates = new[]
            {
                string.IsNullOrWhiteSpace(dllDir) ? null : Path.Combine(dllDir, "GenerationGuardConfig.json"),
                Path.Combine(appDataDir, "GenerationGuardConfig.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GenerationGuardConfig.json")
            };

            string path = candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));
            if (string.IsNullOrWhiteSpace(path))
            {
                return fallback;
            }

            try
            {
                using (FileStream fs = File.OpenRead(path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GenerationGuardConfig));
                    GenerationGuardConfig loaded = serializer.ReadObject(fs) as GenerationGuardConfig;
                    return loaded ?? fallback;
                }
            }
            catch (Exception ex)
            {
                DiagnosticRecorder.AppendDebug("[GenerationGuardConfig] load failed: " + ex.Message);
                return fallback;
            }
        }
    }
}
