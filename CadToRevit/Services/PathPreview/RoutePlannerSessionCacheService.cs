using Autodesk.Revit.DB;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;

namespace CadToRevit.Services.PathPreview
{
    public sealed class RoutePlannerSessionState
    {
        public string DocumentKey { get; set; }

        public string SessionId { get; set; }

        public string IfcFilePath { get; set; }

        public DateTime LastExportUtc { get; set; }

        public int HeadroomHeightMm { get; set; }

        public int DoorWidthToleranceMm { get; set; }

        public bool IsDirty { get; set; }

        public string DirtyReason { get; set; }
    }

    public static class RoutePlannerSessionCacheService
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, RoutePlannerSessionState> Sessions =
            new Dictionary<string, RoutePlannerSessionState>(StringComparer.OrdinalIgnoreCase);

        public static RoutePlannerSessionState GetSession(Document doc)
        {
            string key = BuildDocumentKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            lock (SyncRoot)
            {
                RoutePlannerSessionState state;
                return Sessions.TryGetValue(key, out state) ? state : null;
            }
        }

        public static void SaveSession(Document doc, RoutePlannerSessionState state)
        {
            string key = BuildDocumentKey(doc);
            if (string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            state.DocumentKey = key;
            lock (SyncRoot)
            {
                Sessions[key] = state;
            }
        }

        public static void MarkDirty(Document doc, string reason)
        {
            string key = BuildDocumentKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (SyncRoot)
            {
                RoutePlannerSessionState state;
                if (!Sessions.TryGetValue(key, out state) || state == null)
                {
                    state = new RoutePlannerSessionState
                    {
                        DocumentKey = key,
                        HeadroomHeightMm = 2200,
                        DoorWidthToleranceMm = 0
                    };
                    Sessions[key] = state;
                }

                state.IsDirty = true;
                state.DirtyReason = reason ?? string.Empty;
            }

            DiagnosticRecorder.AppendDebug("[RoutePlannerSession] MarkDirty reason=" + (reason ?? string.Empty));
        }

        public static void ClearSession(Document doc)
        {
            string key = BuildDocumentKey(doc);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (SyncRoot)
            {
                Sessions.Remove(key);
            }
        }

        public static bool IsReusable(Document doc, out RoutePlannerSessionState state)
        {
            state = GetSession(doc);
            if (state == null)
            {
                return false;
            }

            if (state.IsDirty ||
                string.IsNullOrWhiteSpace(state.SessionId) ||
                string.IsNullOrWhiteSpace(state.IfcFilePath) ||
                !File.Exists(state.IfcFilePath))
            {
                return false;
            }

            return true;
        }

        public static string BuildDocumentKey(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(doc.PathName))
            {
                return doc.PathName.Trim();
            }

            return (doc.Title ?? string.Empty).Trim() + "#" + doc.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
