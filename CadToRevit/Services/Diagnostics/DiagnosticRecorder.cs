using CadToRevit.Models;
using CadToRevit.Models.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CadToRevit.Services.Diagnostics
{
    public static class DiagnosticRecorder
    {
        private const string ProductFolderName = "EMSD AI Tool";
        private const string LogsFolderName = "Logs";
        private static readonly object LogFileSync = new object();

        /// <summary>
        /// Returns the installation-independent per-user log directory:
        /// %LOCALAPPDATA%\EMSD AI Tool\Logs
        /// </summary>
        public static string GetLogDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            string root = Path.Combine(localAppData, ProductFolderName, LogsFolderName);
            Directory.CreateDirectory(root);
            return root;
        }

        public static string GetLogPath(string fileName)
        {
            string safeFileName = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                throw new ArgumentException("A log file name is required.", "fileName");
            }

            return Path.Combine(GetLogDirectory(), safeFileName);
        }

        public static string AppendLine(string fileName, string line)
        {
            try
            {
                string path = GetLogPath(fileName);
                lock (LogFileSync)
                {
                    File.AppendAllText(path, (line ?? string.Empty) + Environment.NewLine, Encoding.UTF8);
                }

                return path;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Packages all files generated today under the unified log directory into a standard ZIP archive.
        /// ZIP files are excluded so repeated exports do not embed older export packages.
        /// This implementation writes stored ZIP entries and does not require an additional compression assembly.
        /// </summary>
        public static bool TryExportTodayLogs(string outputZipPath, out int exportedFileCount)
        {
            exportedFileCount = 0;
            if (string.IsNullOrWhiteSpace(outputZipPath))
            {
                throw new ArgumentException("An output ZIP path is required.", "outputZipPath");
            }

            string logDirectory = GetLogDirectory();
            string fullOutputPath = Path.GetFullPath(outputZipPath);
            DateTime today = DateTime.Today;

            List<string> sourceFiles = Directory
                .GetFiles(logDirectory, "*", SearchOption.AllDirectories)
                .Where(path =>
                    !string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFullPath(path), fullOutputPath, StringComparison.OrdinalIgnoreCase) &&
                    File.GetLastWriteTime(path).Date == today)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourceFiles.Count == 0)
            {
                return false;
            }

            string outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            string temporaryPath = fullOutputPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            try
            {
                WriteStoredZip(temporaryPath, logDirectory, sourceFiles);
                if (File.Exists(fullOutputPath))
                {
                    File.Delete(fullOutputPath);
                }

                File.Move(temporaryPath, fullOutputPath);
                exportedFileCount = sourceFiles.Count;
                return true;
            }
            catch
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                }

                throw;
            }
        }

        private static void WriteStoredZip(string zipPath, string rootDirectory, IList<string> sourceFiles)
        {
            List<StoredZipEntry> entries = new List<StoredZipEntry>();

            using (FileStream zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(zipStream, Encoding.UTF8))
            {
                foreach (string sourcePath in sourceFiles)
                {
                    byte[] content = ReadFileSnapshot(sourcePath);
                    string relativePath = MakeRelativePath(rootDirectory, sourcePath).Replace('\\', '/');
                    byte[] nameBytes = Encoding.UTF8.GetBytes(relativePath);
                    DateTime modified = File.GetLastWriteTime(sourcePath);
                    ushort dosTime;
                    ushort dosDate;
                    ToDosTimestamp(modified, out dosTime, out dosDate);
                    uint crc32 = ComputeCrc32(content);
                    long localHeaderOffset = zipStream.Position;

                    writer.Write(0x04034b50u);
                    writer.Write((ushort)20);
                    writer.Write((ushort)0x0800);
                    writer.Write((ushort)0);
                    writer.Write(dosTime);
                    writer.Write(dosDate);
                    writer.Write(crc32);
                    writer.Write((uint)content.Length);
                    writer.Write((uint)content.Length);
                    writer.Write((ushort)nameBytes.Length);
                    writer.Write((ushort)0);
                    writer.Write(nameBytes);
                    writer.Write(content);

                    entries.Add(new StoredZipEntry
                    {
                        NameBytes = nameBytes,
                        Crc32 = crc32,
                        Size = (uint)content.Length,
                        DosTime = dosTime,
                        DosDate = dosDate,
                        LocalHeaderOffset = (uint)localHeaderOffset
                    });
                }

                long centralDirectoryOffset = zipStream.Position;
                foreach (StoredZipEntry entry in entries)
                {
                    writer.Write(0x02014b50u);
                    writer.Write((ushort)20);
                    writer.Write((ushort)20);
                    writer.Write((ushort)0x0800);
                    writer.Write((ushort)0);
                    writer.Write(entry.DosTime);
                    writer.Write(entry.DosDate);
                    writer.Write(entry.Crc32);
                    writer.Write(entry.Size);
                    writer.Write(entry.Size);
                    writer.Write((ushort)entry.NameBytes.Length);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write(0u);
                    writer.Write(entry.LocalHeaderOffset);
                    writer.Write(entry.NameBytes);
                }

                long centralDirectorySize = zipStream.Position - centralDirectoryOffset;
                writer.Write(0x06054b50u);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)entries.Count);
                writer.Write((ushort)entries.Count);
                writer.Write((uint)centralDirectorySize);
                writer.Write((uint)centralDirectoryOffset);
                writer.Write((ushort)0);
            }
        }

        private static byte[] ReadFileSnapshot(string filePath)
        {
            using (FileStream source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (MemoryStream buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                if (buffer.Length > uint.MaxValue)
                {
                    throw new IOException("A log file is too large for ZIP export: " + filePath);
                }

                return buffer.ToArray();
            }
        }

        private static string MakeRelativePath(string rootDirectory, string filePath)
        {
            string root = Path.GetFullPath(rootDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(root.Length);
            }

            return Path.GetFileName(fullPath);
        }

        private static void ToDosTimestamp(DateTime value, out ushort dosTime, out ushort dosDate)
        {
            DateTime local = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            int year = Math.Max(1980, Math.Min(2107, local.Year));
            dosTime = (ushort)((local.Hour << 11) | (local.Minute << 5) | (local.Second / 2));
            dosDate = (ushort)(((year - 1980) << 9) | (local.Month << 5) | local.Day);
        }

        private static uint ComputeCrc32(byte[] data)
        {
            uint crc = 0xffffffffu;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int bit = 0; bit < 8; bit++)
                {
                    uint mask = (uint)-(int)(crc & 1u);
                    crc = (crc >> 1) ^ (0xedb88320u & mask);
                }
            }

            return ~crc;
        }

        private sealed class StoredZipEntry
        {
            public byte[] NameBytes { get; set; }
            public uint Crc32 { get; set; }
            public uint Size { get; set; }
            public ushort DosTime { get; set; }
            public ushort DosDate { get; set; }
            public uint LocalHeaderOffset { get; set; }
        }

        private static string ResolveOutputRoot()
        {
            return GetLogDirectory();
        }

        public static string AppendDebug(string message)
        {
            try
            {
                string root = ResolveOutputRoot();
                Directory.CreateDirectory(root);
                string logPath = Path.Combine(root, "mvp1_debug_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + (message ?? string.Empty) + Environment.NewLine;
                lock (LogFileSync)
                {
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }

                return logPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string Write(
            UnitContext unitContext,
            IEnumerable<string> selectedLayers,
            WallRecognitionResult recognition,
            int createdWallCount,
            IEnumerable<string> failureReasons)
        {
            string root = ResolveOutputRoot();
            Directory.CreateDirectory(root);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string jsonPath = Path.Combine(root, "mvp1_wall_diag_" + stamp + ".json");
            string txtPath = Path.Combine(root, "mvp1_wall_diag_" + stamp + ".txt");

            string json = BuildJson(unitContext, selectedLayers, recognition, createdWallCount, failureReasons);
            string txt = BuildTxt(unitContext, selectedLayers, recognition, createdWallCount, failureReasons);
            File.WriteAllText(jsonPath, json, Encoding.UTF8);
            File.WriteAllText(txtPath, txt, Encoding.UTF8);
            return jsonPath;
        }

        private static string BuildJson(
            UnitContext unitContext,
            IEnumerable<string> selectedLayers,
            WallRecognitionResult recognition,
            int createdWallCount,
            IEnumerable<string> failureReasons)
        {
            recognition = recognition ?? new WallRecognitionResult();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"unit\": \"" + Escape(unitContext == null ? "" : unitContext.SourceUnit.ToString()) + "\",");
            sb.AppendLine("  \"scaleToFeet\": " + (unitContext == null ? 1.0 : unitContext.ScaleToFeet).ToString("F8") + ",");
            sb.AppendLine("  \"selectedLayers\": [" + string.Join(", ", (selectedLayers ?? Enumerable.Empty<string>()).Select(x => "\"" + Escape(x) + "\"")) + "],");
            sb.AppendLine("  \"centerlineCount\": " + recognition.Centerlines.Count + ",");
            sb.AppendLine("  \"mergedWalls\": " + recognition.MergedWalls + ",");
            sb.AppendLine("  \"refinedWalls\": " + recognition.RefinedWalls + ",");
            sb.AppendLine("  \"clusteredEndpointCount\": " + recognition.ClusteredEndpointCount + ",");
            sb.AppendLine("  \"extendedEndpointCount\": " + recognition.ExtendedEndpointCount + ",");
            sb.AppendLine("  \"duplicateRemovedCount\": " + recognition.DuplicateRemovedCount + ",");
            sb.AppendLine("  \"offAxisSnappedCount\": " + recognition.OffAxisSnappedCount + ",");
            sb.AppendLine("  \"createdWallCount\": " + createdWallCount + ",");
            sb.AppendLine("  \"failureReasons\": [" + string.Join(", ", (failureReasons ?? Enumerable.Empty<string>()).Select(x => "\"" + Escape(x) + "\"")) + "]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildTxt(
            UnitContext unitContext,
            IEnumerable<string> selectedLayers,
            WallRecognitionResult recognition,
            int createdWallCount,
            IEnumerable<string> failureReasons)
        {
            recognition = recognition ?? new WallRecognitionResult();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Unit: " + (unitContext == null ? "(null)" : unitContext.SourceUnit.ToString()));
            sb.AppendLine("ScaleToFeet: " + (unitContext == null ? 1.0 : unitContext.ScaleToFeet).ToString("F8"));
            sb.AppendLine("SelectedLayers: " + string.Join(", ", selectedLayers ?? Enumerable.Empty<string>()));
            sb.AppendLine("CenterlineCount: " + recognition.Centerlines.Count);
            sb.AppendLine("MergedWalls: " + recognition.MergedWalls);
            sb.AppendLine("RefinedWalls: " + recognition.RefinedWalls);
            sb.AppendLine("ClusteredEndpointCount: " + recognition.ClusteredEndpointCount);
            sb.AppendLine("ExtendedEndpointCount: " + recognition.ExtendedEndpointCount);
            sb.AppendLine("DuplicateRemovedCount: " + recognition.DuplicateRemovedCount);
            sb.AppendLine("OffAxisSnappedCount: " + recognition.OffAxisSnappedCount);
            sb.AppendLine("CreatedWallCount: " + createdWallCount);
            sb.AppendLine("FailureReasons: " + string.Join(" | ", failureReasons ?? Enumerable.Empty<string>()));

            // 已移除评分体系，此处不再输出候选评分明细。
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (s == null)
            {
                return "";
            }

            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
