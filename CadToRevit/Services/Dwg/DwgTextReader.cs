using Autodesk.Revit.DB;
using CadToRevit.Models.Cad;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using AcGe = Autodesk.AutoCAD.Geometry;
using AcRt = Autodesk.AutoCAD.Runtime;

namespace CadToRevit.Services.Dwg
{
    public static class DwgTextReader
    {
        private const int PreviewCount = 5;
        private const int LocaleLcid = 1033;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, List<CadText>> Cache = new Dictionary<string, List<CadText>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object RuntimeInitLock = new object();
        private static bool _runtimeInitialized;
        private static AcDb.HostApplicationServices _runtimeHost;

        public static List<CadText> ReadRoomNameTexts(
            string dwgPath,
            string roomNameLayer,
            Transform cadToRevit,
            DwgSourceUnitContext unitContext,
            Action<string> log)
        {
            // Read room labels directly from DWG database to avoid Revit-geometry text loss.
            bool readAllLayers = string.IsNullOrWhiteSpace(roomNameLayer);
            string layerFilter = readAllLayers ? string.Empty : roomNameLayer.Trim();
            if (string.IsNullOrWhiteSpace(dwgPath) || !File.Exists(dwgPath))
            {
                log?.Invoke("[RoomText] DwgTextReader: DWG path is invalid.");
                return new List<CadText>();
            }

            string cacheLayerKey = readAllLayers ? "*ALL_TEXTS*" : layerFilter;
            string logLayer = readAllLayers ? "(ALL_TEXTS)" : layerFilter;
            string cacheKey = BuildCacheKey(dwgPath, cacheLayerKey, unitContext);
            List<CadText> cached = TryGetCache(cacheKey);
            if (cached != null)
            {
                log?.Invoke("[RoomText] DwgTextReader: cache hit, Layer=" + logLayer + ", Extracted=" + cached.Count + ".");
                return CloneTexts(cached);
            }

            List<CadText> results = new List<CadText>();
            try
            {
                EnsureRuntimeInitialized(log);
                using (AcDb.Database db = new AcDb.Database(false, true))
                {
                    db.ReadDwgFile(dwgPath, AcDb.FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    db.CloseInput(true);

                    AcDb.Database oldWorkingDb = null;
                    try
                    {
                        oldWorkingDb = AcDb.HostApplicationServices.WorkingDatabase;
                    }
                    catch
                    {
                        oldWorkingDb = null;
                    }

                    try
                    {
                        AcDb.HostApplicationServices.WorkingDatabase = db;
                    }
                    catch (Exception ex)
                    {
                        log?.Invoke("[RoomText] DwgTextReader: set WorkingDatabase failed: " + ex.Message);
                    }

                    AcDb.TransactionManager tm = db.TransactionManager;
                    if (tm == null)
                    {
                        log?.Invoke("[RoomText] DwgTextReader: TransactionManager is null.");
                        return new List<CadText>();
                    }

                    using (AcDb.Transaction tr = tm.StartOpenCloseTransaction())
                    {
                        AcDb.BlockTable bt = tr.GetObject(db.BlockTableId, AcDb.OpenMode.ForRead, false) as AcDb.BlockTable;
                        if (bt == null || !bt.Has(AcDb.BlockTableRecord.ModelSpace))
                        {
                            log?.Invoke("[RoomText] DwgTextReader: ModelSpace not found.");
                        }
                        else
                        {
                            AcDb.BlockTableRecord model =
                                tr.GetObject(bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForRead) as AcDb.BlockTableRecord;
                            if (model == null)
                            {
                                log?.Invoke("[RoomText] DwgTextReader: ModelSpace record is null.");
                            }
                            else
                            {
                                foreach (AcDb.ObjectId id in model)
                                {
                                    AcDb.Entity entity = tr.GetObject(id, AcDb.OpenMode.ForRead, false) as AcDb.Entity;
                                    if (entity == null)
                                    {
                                        continue;
                                    }

                                    ExtractEntityText(db, tr, entity, AcGe.Matrix3d.Identity, layerFilter, cadToRevit, unitContext, results);
                                }
                            }
                        }

                        tr.Commit();
                    }

                    try
                    {
                        AcDb.HostApplicationServices.WorkingDatabase = oldWorkingDb;
                    }
                    catch
                    {
                        // Ignore restore failure.
                    }
                }

                PutCache(cacheKey, results);
                log?.Invoke("[RoomText] DwgTextReader: Path=" + dwgPath + ", Layer=" + logLayer + ", Extracted=" + results.Count + ".");
                if (results.Count > 0)
                {
                    string fullDumpPath = DumpAllTextsToFile(results, readAllLayers ? "ALL_TEXTS" : layerFilter);
                    if (!string.IsNullOrWhiteSpace(fullDumpPath))
                    {
                        log?.Invoke("[RoomText] DwgTextReader full dump: " + fullDumpPath);
                    }

                    List<string> sample = results
                        .Take(PreviewCount)
                        .Select(x => Truncate(x.Text, 30) + " @ (" + x.Position.X.ToString("F2") + "," + x.Position.Y.ToString("F2") + ")")
                        .ToList();
                    log?.Invoke("[RoomText] DwgTextReader samples: " + string.Join(" | ", sample));

                    List<string> sampleTransform = results
                        .Take(3)
                        .Select(x => "Sample: \"" + Truncate(x.Text, 30) + "\" pCad=(" +
                                     x.RawCadX.ToString("F2") + "," + x.RawCadY.ToString("F2") + "," + x.RawCadZ.ToString("F2") +
                                     ") pFeet=(" + x.CadFeetX.ToString("F2") + "," + x.CadFeetY.ToString("F2") + "," + x.CadFeetZ.ToString("F2") +
                                     ") pRevit=(" + x.Position.X.ToString("F2") + "," + x.Position.Y.ToString("F2") + "," + x.Position.Z.ToString("F2") + ")")
                        .ToList();
                    log?.Invoke("[RoomText] DwgTextReader transform samples: " + string.Join(" | ", sampleTransform));
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("[RoomText] DwgTextReader failed: " + ex.Message);
                return new List<CadText>();
            }

            return CloneTexts(results);
        }

        private static void ExtractEntityText(
            AcDb.Database db,
            AcDb.Transaction tr,
            AcDb.Entity entity,
            AcGe.Matrix3d currentTransform,
            string roomNameLayer,
            Transform cadToRevit,
            DwgSourceUnitContext unitContext,
            List<CadText> output)
        {
            if (entity == null || output == null)
            {
                return;
            }

            if (entity is AcDb.DBText dbText)
            {
                TryAddText(dbText.Layer, dbText.TextString, dbText.Position.TransformBy(currentTransform), roomNameLayer, cadToRevit, unitContext, output);
                return;
            }

            if (entity is AcDb.MText mText)
            {
                string text = !string.IsNullOrWhiteSpace(mText.Contents) ? mText.Contents : mText.Text;
                TryAddText(mText.Layer, text, mText.Location.TransformBy(currentTransform), roomNameLayer, cadToRevit, unitContext, output);
                return;
            }

            if (entity is AcDb.BlockReference blockRef)
            {
                // Recursively walk nested block definitions and accumulate transforms.
                AcDb.BlockTableRecord nested =
                    tr.GetObject(blockRef.BlockTableRecord, AcDb.OpenMode.ForRead, false) as AcDb.BlockTableRecord;
                if (nested == null)
                {
                    return;
                }

                AcGe.Matrix3d nextTransform = currentTransform * blockRef.BlockTransform;
                foreach (AcDb.ObjectId nestedId in nested)
                {
                    AcDb.Entity nestedEntity = tr.GetObject(nestedId, AcDb.OpenMode.ForRead, false) as AcDb.Entity;
                    if (nestedEntity == null)
                    {
                        continue;
                    }

                    ExtractEntityText(db, tr, nestedEntity, nextTransform, roomNameLayer, cadToRevit, unitContext, output);
                }
            }
        }

        private static void TryAddText(
            string layer,
            string rawText,
            AcGe.Point3d point,
            string roomNameLayer,
            Transform cadToRevit,
            DwgSourceUnitContext unitContext,
            List<CadText> output)
        {
            bool useLayerFilter = !string.IsNullOrWhiteSpace(roomNameLayer);
            if (!string.Equals(layer, roomNameLayer, StringComparison.OrdinalIgnoreCase))
            {
                if (useLayerFilter)
                {
                    return;
                }
            }

            string cleaned = CleanText(rawText);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return;
            }

            XYZ p = new XYZ(point.X, point.Y, point.Z);
            double scaleToFeet = unitContext != null ? unitContext.ScaleToFeet : DwgSourceUnitContextFactory.Create(CadToRevit.Models.Units.SourceUnit.Millimeter, "MissingTextUnitContext").ScaleToFeet;
            // Raw DWG text coordinates are not Revit ImportInstance geometry.
            // They must be converted from the DWG source unit to Revit internal feet.
            XYZ pFeet = new XYZ(p.X * scaleToFeet, p.Y * scaleToFeet, p.Z * scaleToFeet);
            XYZ pRevit = pFeet;
            if (cadToRevit != null)
            {
                // Align AutoCAD coordinates to the actual Revit import placement.
                pRevit = cadToRevit.OfPoint(pFeet);
            }

            output.Add(new CadText
            {
                RawLayerName = layer ?? string.Empty,
                Text = cleaned,
                Position = pRevit,
                RawCadX = p.X,
                RawCadY = p.Y,
                RawCadZ = p.Z,
                CadFeetX = pFeet.X,
                CadFeetY = pFeet.Y,
                CadFeetZ = pFeet.Z,
                RotationRad = 0.0
            });
        }

        private static string CleanText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string s = raw.Replace("\\P", "\n").Replace("\\p", "\n");
            s = s.Replace("{", string.Empty).Replace("}", string.Empty);
            return s.Trim();
        }

        private static string BuildCacheKey(string path, string layer, DwgSourceUnitContext unitContext)
        {
            // Cache by path + file timestamp + target layer for current session reuse.
            DateTime ts = File.GetLastWriteTimeUtc(path);
            string unitKey = unitContext != null ? unitContext.SourceUnit.ToString() : "MillimeterFallback";
            return Path.GetFullPath(path) + "|" + ts.Ticks + "|" + (layer ?? string.Empty).ToUpperInvariant() + "|" + unitKey;
        }

        private static List<CadText> TryGetCache(string key)
        {
            lock (CacheLock)
            {
                return Cache.TryGetValue(key, out List<CadText> value) ? value : null;
            }
        }

        private static void PutCache(string key, List<CadText> value)
        {
            lock (CacheLock)
            {
                Cache[key] = value ?? new List<CadText>();
            }
        }

        private static List<CadText> CloneTexts(List<CadText> source)
        {
            List<CadText> result = new List<CadText>();
            foreach (CadText t in source ?? new List<CadText>())
            {
                if (t == null || t.Position == null)
                {
                    continue;
                }

                result.Add(new CadText
                {
                    RawLayerName = t.RawLayerName,
                    Text = t.Text,
                    Position = new XYZ(t.Position.X, t.Position.Y, t.Position.Z),
                    RotationRad = t.RotationRad,
                    RawCadX = t.RawCadX,
                    RawCadY = t.RawCadY,
                    RawCadZ = t.RawCadZ,
                    CadFeetX = t.CadFeetX,
                    CadFeetY = t.CadFeetY,
                    CadFeetZ = t.CadFeetZ
                });
            }

            return result;
        }

        private static string Truncate(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length <= maxChars)
            {
                return s;
            }

            return s.Substring(0, maxChars) + "...";
        }

        private static string DumpAllTextsToFile(List<CadText> texts, string layer)
        {
            try
            {
                string logRoot = DiagnosticRecorder.GetLogDirectory();
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string fileName = "mvp1_room_texts_" + stamp + ".csv";
                string fullPath = Path.Combine(logRoot, fileName);

                List<string> lines = new List<string>
                {
                    "Index,Layer,Text,X,Y,Z"
                };
                int index = 1;
                foreach (CadText t in texts ?? new List<CadText>())
                {
                    if (t == null || t.Position == null)
                    {
                        continue;
                    }

                    lines.Add(
                        index + "," +
                        EscapeCsv(string.IsNullOrWhiteSpace(t.RawLayerName) ? layer : t.RawLayerName) + "," +
                        EscapeCsv(t.Text) + "," +
                        t.Position.X.ToString("F6") + "," +
                        t.Position.Y.ToString("F6") + "," +
                        t.Position.Z.ToString("F6"));
                    index++;
                }

                File.WriteAllLines(fullPath, lines, Encoding.UTF8);
                return fullPath;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string EscapeCsv(string s)
        {
            string value = s ?? string.Empty;
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static void EnsureRuntimeInitialized(Action<string> log)
        {
            if (_runtimeInitialized)
            {
                return;
            }

            lock (RuntimeInitLock)
            {
                if (_runtimeInitialized)
                {
                    return;
                }

                try
                {
                    _runtimeHost = new RevitHostServices();
                    AcRt.RuntimeSystem.Initialize(_runtimeHost, LocaleLcid);
                    _runtimeInitialized = true;
                    log?.Invoke("[RoomText] AutoCAD Runtime initialized for DWG database read.");
                }
                catch (Exception ex)
                {
                    log?.Invoke("[RoomText] AutoCAD Runtime init failed: " + ex.Message);
                }
            }
        }

        private sealed class RevitHostServices : AcDb.HostApplicationServices
        {
            public override string FindFile(string fileName, AcDb.Database database, AcDb.FindFileHint hint)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    return string.Empty;
                }

                if (File.Exists(fileName))
                {
                    return fileName;
                }

                return fileName;
            }
        }
    }
}
