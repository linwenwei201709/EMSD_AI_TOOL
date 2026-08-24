using Autodesk.Revit.DB;
using CadToRevit.Models.Units;
using System;
using System.Collections.Generic;
using System.IO;

namespace CadToRevit.Services
{
    public sealed class DwgSessionInfo
    {
        public ElementId LinkInstanceId { get; set; }

        public string FilePath { get; set; }

        public DateTime ImportTime { get; set; }

        public List<string> DwgLayers { get; set; } = new List<string>();

        public SourceUnit SourceUnit { get; set; } = SourceUnit.Millimeter;

        public string SourceUnitEvidence { get; set; } = string.Empty;

        public string LastKnownFingerprint { get; set; }

        public long LastKnownFileSize { get; set; }

        public long LastKnownWriteTimeUtcTicks { get; set; }
    }

    public static class DwgSessionManager
    {
        private static readonly Dictionary<string, DwgSessionInfo> SessionByDocument =
            new Dictionary<string, DwgSessionInfo>(StringComparer.OrdinalIgnoreCase);

        public static void Set(Document doc, DwgSessionInfo info)
        {
            if (doc == null || info == null)
            {
                return;
            }

            SessionByDocument[BuildDocKey(doc)] = info;
        }

        public static DwgSessionInfo Get(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            DwgSessionInfo value;
            return SessionByDocument.TryGetValue(BuildDocKey(doc), out value) ? value : null;
        }

        public static void Clear(Document doc)
        {
            if (doc == null)
            {
                return;
            }

            SessionByDocument.Remove(BuildDocKey(doc));
        }

        public static bool TryCaptureFileFingerprint(
            string filePath,
            out string fingerprint,
            out long fileSize,
            out long lastWriteTimeUtcTicks)
        {
            fingerprint = null;
            fileSize = 0;
            lastWriteTimeUtcTicks = 0;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            FileInfo fileInfo = new FileInfo(filePath);
            fileSize = fileInfo.Length;
            lastWriteTimeUtcTicks = fileInfo.LastWriteTimeUtc.Ticks;
            fingerprint = lastWriteTimeUtcTicks + "|" + fileSize;
            return true;
        }

        public static void ApplyFileFingerprint(DwgSessionInfo session, string filePath)
        {
            if (session == null)
            {
                return;
            }

            if (!TryCaptureFileFingerprint(filePath, out string fingerprint, out long fileSize, out long writeTicks))
            {
                return;
            }

            session.LastKnownFingerprint = fingerprint;
            session.LastKnownFileSize = fileSize;
            session.LastKnownWriteTimeUtcTicks = writeTicks;
        }

        private static string BuildDocKey(Document doc)
        {
            string path = doc.PathName ?? string.Empty;
            string title = doc.Title ?? string.Empty;
            return path + "|" + title;
        }
    }
}
