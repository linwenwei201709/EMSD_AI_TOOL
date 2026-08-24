using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CadToRevit.Models.Cad;
using CadToRevit.Models.Rooms;
using CadToRevit.Services;
using CadToRevit.Services.Cad;
using CadToRevit.Services.Rooms;
using CadToRevit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class RoomRecognitionCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc.Document;

            List<ImportInstance> links = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>()
                .ToList();
            if (links.Count == 0)
            {
                WinForms.MessageBox.Show("No CAD Link found. Please run DWG import first.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return Result.Cancelled;
            }

            List<Level> levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => x.Elevation)
                .ToList();
            if (levels.Count == 0)
            {
                WinForms.MessageBox.Show("No Level found.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return Result.Cancelled;
            }

            List<WallType> wallTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(WallType))
                .Cast<WallType>()
                .Where(x => x != null)
                .OrderBy(x => x.Name)
                .ToList();
            if (wallTypes.Count == 0)
            {
                WinForms.MessageBox.Show("No wall type found.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return Result.Cancelled;
            }

            ElementId selectedLinkId = links.First().Id;
            ElementId selectedLevelId = ResolveDefaultLevelId(doc, levels);
            string boundaryLayerName = string.Empty;
            string roomTextLayerName = string.Empty;
            bool noRoomName = true;
            double closeTolMm = 10.0;
            double maxPatchMm = 300.0;
            double minAreaM2 = 1.0;
            bool createWalls = false;
            ElementId selectedWallTypeId = ResolveDefaultWallTypeId(wallTypes);
            double wallHeightMm = 4000.0;
            double minWallSegmentMm = 600.0;
            bool avoidDuplicateWalls = true;

            List<RoomCandidate> candidates = new List<RoomCandidate>();
            Dictionary<string, ElementId> roomKeyToRevitRoomId = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);
            List<ElementId> createdSeparationLineIds = new List<ElementId>();

            while (true)
            {
                ImportInstance currentLink = links.FirstOrDefault(x => x.Id == selectedLinkId) ?? links.First();
                List<string> layerOptions = BuildLayerOptions(doc, currentLink);
                if (string.IsNullOrWhiteSpace(boundaryLayerName))
                {
                    boundaryLayerName = GuessBoundaryLayer(layerOptions);
                }

                if (string.IsNullOrWhiteSpace(roomTextLayerName))
                {
                    roomTextLayerName = GuessRoomTextLayer(layerOptions);
                }

                using (RoomRecognitionForm form = new RoomRecognitionForm(
                    links,
                    levels,
                    layerOptions,
                    wallTypes,
                    selectedLinkId,
                    selectedLevelId,
                    boundaryLayerName,
                    roomTextLayerName,
                    noRoomName,
                    closeTolMm,
                    maxPatchMm,
                    minAreaM2,
                    createWalls,
                    selectedWallTypeId,
                    wallHeightMm,
                    minWallSegmentMm,
                    avoidDuplicateWalls,
                    candidates))
                {
                    WinForms.DialogResult dr = form.ShowDialog();
                    if (dr != WinForms.DialogResult.OK || form.Action == RoomRecognitionFormAction.Cancel)
                    {
                        return Result.Cancelled;
                    }

                    selectedLinkId = form.SelectedCadLinkId;
                    selectedLevelId = form.SelectedLevelId;
                    boundaryLayerName = form.BoundaryLayerName;
                    roomTextLayerName = form.RoomTextLayerName;
                    noRoomName = form.NoRoomName;
                    closeTolMm = form.CloseTolMm;
                    maxPatchMm = form.MaxPatchMm;
                    minAreaM2 = form.MinAreaM2;
                    createWalls = form.CreateWalls;
                    selectedWallTypeId = form.SelectedWallTypeId;
                    wallHeightMm = form.WallHeightMm;
                    minWallSegmentMm = form.MinWallSegmentMm;
                    avoidDuplicateWalls = form.AvoidDuplicateWalls;
                    candidates = form.Candidates ?? new List<RoomCandidate>();

                    if (form.Action == RoomRecognitionFormAction.Scan)
                    {
                        RunScan(doc, selectedLinkId, boundaryLayerName, closeTolMm, maxPatchMm, minAreaM2);
                        continue;
                    }

                    if (form.Action == RoomRecognitionFormAction.Recognize)
                    {
                        candidates = RunRecognize(doc, selectedLinkId, boundaryLayerName, closeTolMm, maxPatchMm, minAreaM2, noRoomName);
                        continue;
                    }

                    if (form.Action == RoomRecognitionFormAction.FocusSelected)
                    {
                        RoomCandidate selected = candidates.FirstOrDefault(x => string.Equals(x.Key, form.SelectedRoomKey, StringComparison.OrdinalIgnoreCase));
                        if (selected == null)
                        {
                            WinForms.MessageBox.Show("Please select one room row first.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                            continue;
                        }

                        ElementId roomId;
                        if (roomKeyToRevitRoomId.TryGetValue(selected.Key, out roomId))
                        {
                            selected.RevitRoomId = roomId;
                        }

                        RevitFocusService.Focus(uiDoc, selected);
                        continue;
                    }

                    if (form.Action == RoomRecognitionFormAction.ExportCsv)
                    {
                        ExportCsv(candidates);
                        continue;
                    }

                    if (form.Action == RoomRecognitionFormAction.Generate)
                    {
                        if (candidates.Count == 0)
                        {
                            WinForms.MessageBox.Show("Please run Recognize first.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                            continue;
                        }

                        Level level = levels.FirstOrDefault(x => x.Id == selectedLevelId);
                        RoomWallCreateOptions wallOptions = new RoomWallCreateOptions
                        {
                            CreateWalls = createWalls,
                            WallTypeId = selectedWallTypeId,
                            WallHeightMm = wallHeightMm,
                            MinWallSegmentMm = minWallSegmentMm,
                            AvoidDuplicateWalls = avoidDuplicateWalls
                        };
                        RoomCreateResult create = RevitRoomCreateService.Create(doc, level, candidates, wallOptions);
                        foreach (KeyValuePair<string, ElementId> kv in create.RoomKeyToRevitRoomId)
                        {
                            roomKeyToRevitRoomId[kv.Key] = kv.Value;
                        }

                        createdSeparationLineIds = create.CreatedSeparationLineIds ?? new List<ElementId>();
                        string resultText =
                            "Generated." + Environment.NewLine +
                            "Room count: " + create.CreatedRoomCount + Environment.NewLine +
                            "Separation lines: " + createdSeparationLineIds.Count;
                        if (createWalls)
                        {
                            resultText += Environment.NewLine +
                                          "Wall count: " + create.CreatedWallCount +
                                          " (Failed: " + create.FailedWallCount + ")";
                        }

                        WinForms.MessageBox.Show(resultText, "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
                        continue;
                    }
                }
            }
        }

        private static void RunScan(
            Document doc,
            ElementId linkId,
            string boundaryLayerName,
            double closeTolMm,
            double maxPatchMm,
            double minAreaM2)
        {
            ImportInstance link = doc.GetElement(linkId) as ImportInstance;
            if (link == null)
            {
                WinForms.MessageBox.Show("CAD Link is invalid.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return;
            }

            CadDataset dataset = BuildDataset(doc, link);
            int segCount = (dataset.Segments ?? new List<CadSegment>())
                .Count(x => x != null && string.Equals(x.RawLayerName, boundaryLayerName, StringComparison.OrdinalIgnoreCase));
            List<RoomCandidate> found = RoomBoundaryLoopService.Detect(dataset, boundaryLayerName, closeTolMm, maxPatchMm, minAreaM2);
            int closedCount = found.Count(x => x.Status != RoomBoundaryStatus.NeedsFix);
            WinForms.MessageBox.Show(
                "Scan done." + Environment.NewLine +
                "Boundary segments: " + segCount + Environment.NewLine +
                "Candidate loops: " + found.Count + Environment.NewLine +
                "Usable rooms: " + closedCount,
                "Room Recognition",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
        }

        private static List<RoomCandidate> RunRecognize(
            Document doc,
            ElementId linkId,
            string boundaryLayerName,
            double closeTolMm,
            double maxPatchMm,
            double minAreaM2,
            bool noRoomName)
        {
            ImportInstance link = doc.GetElement(linkId) as ImportInstance;
            if (link == null)
            {
                WinForms.MessageBox.Show("CAD Link is invalid.", "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
                return new List<RoomCandidate>();
            }

            CadDataset dataset = BuildDataset(doc, link);
            List<RoomCandidate> result = RoomBoundaryLoopService.Detect(dataset, boundaryLayerName, closeTolMm, maxPatchMm, minAreaM2);
            if (noRoomName)
            {
                int index = 1;
                foreach (RoomCandidate c in result.Where(x => x.Status != RoomBoundaryStatus.NeedsFix).OrderByDescending(x => x.AreaM2))
                {
                    c.Name = "ROOM-" + index.ToString("000");
                    c.Number = index.ToString("000");
                    index++;
                }
            }

            WinForms.MessageBox.Show(
                "Recognize done." + Environment.NewLine +
                "Candidates: " + result.Count + Environment.NewLine +
                "Creatable: " + result.Count(x => x.Status != RoomBoundaryStatus.NeedsFix),
                "Room Recognition",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return result;
        }

        private static void ExportCsv(List<RoomCandidate> candidates)
        {
            using (WinForms.SaveFileDialog dlg = new WinForms.SaveFileDialog())
            {
                dlg.Title = "Export Room Recognition Result";
                dlg.Filter = "CSV (*.csv)|*.csv";
                dlg.FileName = "room_recognition.csv";
                if (dlg.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return;
                }

                List<string> lines = new List<string> { "Key,Name,AreaM2,Status,CloseGapMm,Created,RevitId" };
                foreach (RoomCandidate c in candidates ?? new List<RoomCandidate>())
                {
                    lines.Add(
                        Escape(c.Key) + "," +
                        Escape(c.Name) + "," +
                        c.AreaM2.ToString("F3") + "," +
                        c.Status + "," +
                        c.CloseGapMm.ToString("F1") + "," +
                        (c.Created ? "1" : "0") + "," +
                        (c.RevitRoomId == null || c.RevitRoomId == ElementId.InvalidElementId ? string.Empty : c.RevitRoomId.IntegerValue.ToString()));
                }

                File.WriteAllLines(dlg.FileName, lines);
                WinForms.MessageBox.Show("Exported: " + dlg.FileName, "Room Recognition", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Information);
            }
        }

        private static string Escape(string s)
        {
            string v = s ?? string.Empty;
            if (v.Contains(",") || v.Contains("\""))
            {
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            }

            return v;
        }

        private static CadDataset BuildDataset(Document doc, ImportInstance link)
        {
            CadSegmentBuildResult build = CadSegmentBuilder.BuildSegments(doc, link, null);
            return CadDatasetBuilder.Build(build);
        }

        private static List<string> BuildLayerOptions(Document doc, ImportInstance link)
        {
            CadDataset dataset = BuildDataset(doc, link);
            return (dataset.Segments ?? new List<CadSegment>())
                .Select(x => x.RawLayerName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GuessBoundaryLayer(List<string> layers)
        {
            return layers.FirstOrDefault(x =>
                x.IndexOf("AREA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.IndexOf("UFA", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? layers.FirstOrDefault()
                ?? string.Empty;
        }

        private static string GuessRoomTextLayer(List<string> layers)
        {
            return layers.FirstOrDefault(x =>
                x.IndexOf("ROOM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.IndexOf("NAME", StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.IndexOf("TEXT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.IndexOf("UFA", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? string.Empty;
        }

        private static ElementId ResolveDefaultWallTypeId(List<WallType> wallTypes)
        {
            WallType by90 = (wallTypes ?? new List<WallType>())
                .FirstOrDefault(x => x != null && !string.IsNullOrWhiteSpace(x.Name) && x.Name.IndexOf("90", StringComparison.OrdinalIgnoreCase) >= 0);
            if (by90 != null)
            {
                return by90.Id;
            }

            WallType first = (wallTypes ?? new List<WallType>()).FirstOrDefault();
            return first == null ? ElementId.InvalidElementId : first.Id;
        }

        private static ElementId ResolveDefaultLevelId(Document doc, List<Level> levels)
        {
            View active = doc.ActiveView;
            if (active != null && active.GenLevel != null)
            {
                return active.GenLevel.Id;
            }

            Level first = levels.FirstOrDefault();
            return first == null ? ElementId.InvalidElementId : first.Id;
        }
    }
}