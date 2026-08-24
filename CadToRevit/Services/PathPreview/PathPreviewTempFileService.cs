using CadToRevit.Services.Diagnostics;
using System;
using System.IO;

namespace CadToRevit.Services.PathPreview
{
    internal static class PathPreviewTempFileService
    {
        internal sealed class PathPreviewSession
        {
            public string SessionId { get; set; }
            public string SessionFolder { get; set; }
            public string TempIfcPath { get; set; }
            public string PreviewProjectPath { get; set; }
            public string LinkedModelRvtPath { get; set; }
            public string LogPath { get; set; }
        }

        internal static PathPreviewSession CreateSession()
        {
            string sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string root = Path.Combine(Path.GetTempPath(), PathPreviewConstants.TempRootFolderName, PathPreviewConstants.TempFeatureFolderName, sessionId);
            EnsureDirectory(root);

            return new PathPreviewSession
            {
                SessionId = sessionId,
                SessionFolder = root,
                TempIfcPath = BuildTempIfcPath(root, sessionId),
                PreviewProjectPath = BuildPreviewProjectPath(root, sessionId),
                LinkedModelRvtPath = BuildLinkedModelRvtPath(root, sessionId),
                LogPath = Path.Combine(DiagnosticRecorder.GetLogDirectory(), "PathPreview_" + sessionId + ".log")
            };
        }

        internal static void EnsureDirectory(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        internal static string BuildTempIfcPath(string folder, string sessionId)
        {
            return Path.Combine(folder, PathPreviewConstants.TempIfcFilePrefix + sessionId + ".ifc");
        }

        internal static string BuildPreviewProjectPath(string folder, string sessionId)
        {
            return Path.Combine(folder, PathPreviewConstants.TempPreviewProjectPrefix + sessionId + ".rvt");
        }

        internal static string BuildLinkedModelRvtPath(string folder, string sessionId)
        {
            return Path.Combine(folder, PathPreviewConstants.TempLinkedModelRvtPrefix + sessionId + ".rvt");
        }
    }
}
