using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CadToRevit.Services.Dwg
{
    public static class DwgPathResolver
    {
        public static string TryGetDwgPath(Document doc, ImportInstance import, Action<string> log = null)
        {
            // Resolve in priority order to maximize stability across Revit versions.
            if (doc == null || import == null)
            {
                log?.Invoke("[RoomText] PathResolver: doc/import is null.");
                return null;
            }

            string bySession = TryFromSession(doc, import, log);
            if (!string.IsNullOrWhiteSpace(bySession))
            {
                return bySession;
            }

            string byExternal = TryFromExternalReference(doc, import, log);
            if (!string.IsNullOrWhiteSpace(byExternal))
            {
                return byExternal;
            }

            string byParams = TryFromParameters(doc, import, log);
            if (!string.IsNullOrWhiteSpace(byParams))
            {
                return byParams;
            }

            log?.Invoke("[RoomText] Cannot resolve DWG path from ImportInstance.");
            return null;
        }

        private static string TryFromSession(Document doc, ImportInstance import, Action<string> log)
        {
            DwgSessionInfo session = DwgSessionManager.Get(doc);
            if (session == null)
            {
                return null;
            }

            if (session.LinkInstanceId != null &&
                import.Id != null &&
                session.LinkInstanceId.IntegerValue != import.Id.IntegerValue)
            {
                return null;
            }

            string resolved = NormalizePath(session.FilePath, doc);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                log?.Invoke("[RoomText] PathResolver: resolved from DwgSessionManager -> " + resolved);
                return resolved;
            }

            return null;
        }

        private static string TryFromExternalReference(Document doc, ImportInstance import, Action<string> log)
        {
            try
            {
                ElementId typeId = import.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    return null;
                }

                ExternalFileReference ext = ExternalFileUtils.GetExternalFileReference(doc, typeId);
                if (ext == null)
                {
                    return null;
                }

                ModelPath path = ext.GetAbsolutePath();
                string userPath = path != null ? ModelPathUtils.ConvertModelPathToUserVisiblePath(path) : null;
                string resolved = NormalizePath(userPath, doc);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    log?.Invoke("[RoomText] PathResolver: resolved from ExternalFileReference -> " + resolved);
                    return resolved;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("[RoomText] PathResolver external reference failed: " + ex.Message);
            }

            return null;
        }

        private static string TryFromParameters(Document doc, ImportInstance import, Action<string> log)
        {
            IEnumerable<Element> candidates = new[]
            {
                import,
                import.GetTypeId() != null && import.GetTypeId() != ElementId.InvalidElementId ? doc.GetElement(import.GetTypeId()) : null
            }.Where(x => x != null);

            foreach (Element element in candidates)
            {
                foreach (Parameter p in element.Parameters)
                {
                    if (p == null || p.StorageType != StorageType.String)
                    {
                        continue;
                    }

                    string raw = p.AsString();
                    string resolved = NormalizePath(raw, doc);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        log?.Invoke("[RoomText] PathResolver: resolved from parameter -> " + resolved);
                        return resolved;
                    }
                }
            }

            return null;
        }

        private static string NormalizePath(string raw, Document doc)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            string trimmed = raw.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            if (trimmed.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) && File.Exists(trimmed))
            {
                return Path.GetFullPath(trimmed);
            }

            if (!trimmed.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string docDir = null;
            try
            {
                string pathName = doc != null ? doc.PathName : null;
                if (!string.IsNullOrWhiteSpace(pathName))
                {
                    docDir = Path.GetDirectoryName(pathName);
                }
            }
            catch
            {
                docDir = null;
            }

            if (!string.IsNullOrWhiteSpace(docDir))
            {
                string combined = Path.Combine(docDir, trimmed);
                if (File.Exists(combined))
                {
                    return Path.GetFullPath(combined);
                }
            }

            return null;
        }
    }
}
