using CadToRevit.Models.Rooms.Semantic;
using CadToRevit.Services.Rooms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DB = Autodesk.Revit.DB;
using RvtUI = Autodesk.Revit.UI;

namespace CadToRevit.UI
{
    public sealed class TargetRoomRecognitionResultForm : Form
    {
        private readonly DB.Document _doc;
        private readonly RvtUI.UIDocument _uiDoc;
        private readonly TargetRoomModelRecognitionService.RecognitionSummary _summary;
        private readonly Dictionary<string, List<DB.ElementId>> _roomRangeElementIds;

        public TargetRoomRecognitionResultForm(
            DB.Document doc,
            RvtUI.UIDocument uiDoc,
            TargetRoomModelRecognitionService.RecognitionSummary summary,
            Dictionary<string, List<DB.ElementId>> roomRangeElementIds)
        {
            _doc = doc;
            _uiDoc = uiDoc;
            _summary = summary ?? new TargetRoomModelRecognitionService.RecognitionSummary();
            _roomRangeElementIds = roomRangeElementIds ?? new Dictionary<string, List<DB.ElementId>>(StringComparer.OrdinalIgnoreCase);

            InitializeLayout();
            BuildCards();
        }

        private void InitializeLayout()
        {
            Text = "新版房间识别结果";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 640;
            Height = 780;
            MinimumSize = new Size(520, 500);
            AutoScaleMode = AutoScaleMode.Dpi;

            FlowLayoutPanel cardsPanel = new FlowLayoutPanel
            {
                Name = "CardsPanel",
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(245, 247, 250),
                Padding = new Padding(12, 8, 12, 12)
            };
            Controls.Add(cardsPanel);

            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.White
            };
            Controls.Add(headerPanel);

            Label titleLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 40,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "房间识别列表"
            };
            headerPanel.Controls.Add(titleLabel);

            Label summaryLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(80, 80, 80),
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 2, 0, 0),
                Text = ((_summary.Message ?? string.Empty).Trim()) + Environment.NewLine + "Errors: " + _summary.Errors.Count
            };
            headerPanel.Controls.Add(summaryLabel);
        }

        private void BuildCards()
        {
            FlowLayoutPanel cardsPanel = Controls.Find("CardsPanel", true).FirstOrDefault() as FlowLayoutPanel;
            if (cardsPanel == null)
            {
                return;
            }

            List<RoomSemanticRecord> rooms = (_summary.RunResult != null ? _summary.RunResult.Rooms : null) ?? new List<RoomSemanticRecord>();
            List<RoomSemanticRecord> matchedRooms = rooms
                .Where(x => x != null && (x.Status ?? string.Empty).StartsWith("Matched", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.AreaM2)
                .ToList();

            if (matchedRooms.Count == 0)
            {
                cardsPanel.Controls.Add(CreateEmptyCard());
                return;
            }

            foreach (RoomSemanticRecord room in matchedRooms)
            {
                cardsPanel.Controls.Add(CreateRoomCard(room));
            }
        }

        private Control CreateRoomCard(RoomSemanticRecord room)
        {
            FlowLayoutPanel cardsPanel = Controls.Find("CardsPanel", true).FirstOrDefault() as FlowLayoutPanel;
            int cardWidth = cardsPanel != null ? Math.Max(460, cardsPanel.ClientSize.Width - 28) : 580;
            Panel card = new Panel
            {
                Width = cardWidth,
                Height = 176,
                Margin = new Padding(0, 0, 0, 12),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            card.Cursor = Cursors.Hand;
            card.Tag = room;

            Label nameLabel = new Label
            {
                Left = 16,
                Top = 14,
                Width = cardWidth - 32,
                Height = 40,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 64, 84),
                Text = BuildTitle(room)
            };
            nameLabel.Cursor = Cursors.Hand;
            card.Controls.Add(nameLabel);

            Label areaKeyLabel = CreateKeyLabel("Area:", 16, 58);
            Label areaValueLabel = CreateValueLabel(FormatArea(room.AreaM2), cardWidth - 196, 62);
            card.Controls.Add(areaKeyLabel);
            card.Controls.Add(areaValueLabel);

            Label ceilKeyLabel = CreateKeyLabel("Ceiling:", 16, 98);
            Label ceilValueLabel = CreateValueLabel("N/A", cardWidth - 196, 98);
            card.Controls.Add(ceilKeyLabel);
            card.Controls.Add(ceilValueLabel);

            Label levelKeyLabel = CreateKeyLabel("Level:", 16, 134);
            Label levelValueLabel = CreateValueLabel(ResolveLevelName(room), cardWidth - 196, 134);
            card.Controls.Add(levelKeyLabel);
            card.Controls.Add(levelValueLabel);

            // Allow clicking any text area in the card to locate the room.
            BindCardClick(card, room);

            return card;
        }

        private static Label CreateKeyLabel(string text, int left, int top)
        {
            Label label = new Label
            {
                Left = left,
                Top = top,
                Width = 130,
                Height = 28,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(98, 108, 121),
                Text = text
            };
            label.Cursor = Cursors.Hand;
            return label;
        }

        private static Label CreateValueLabel(string text, int left, int top)
        {
            Label label = new Label
            {
                Left = left,
                Top = top,
                Width = 170,
                Height = 28,
                Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 64, 84),
                TextAlign = ContentAlignment.TopRight,
                Text = text
            };
            label.Cursor = Cursors.Hand;
            return label;
        }

        private static Control CreateEmptyCard()
        {
            Label label = new Label
            {
                AutoSize = false,
                Width = 580,
                Height = 80,
                Padding = new Padding(16),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                ForeColor = Color.FromArgb(98, 108, 121),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "No matched target rooms."
            };
            return label;
        }

        private string BuildTitle(RoomSemanticRecord room)
        {
            string roomName = room != null ? (room.RoomName ?? string.Empty).Trim() : string.Empty;
            string targetType = room != null ? (room.TargetRoomType ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(roomName))
            {
                roomName = "Unnamed Room";
            }

            return string.IsNullOrWhiteSpace(targetType)
                ? roomName
                : targetType + " | " + roomName;
        }

        private static string FormatArea(double areaM2)
        {
            if (areaM2 <= 0)
            {
                return "N/A";
            }

            return areaM2.ToString("F1") + " m2";
        }

        // Resolve level name from seed-to-level mapping captured during recognition.
        private string ResolveLevelName(RoomSemanticRecord room)
        {
            if (room == null || string.IsNullOrWhiteSpace(room.Key) || _doc == null)
            {
                return "N/A";
            }

            int levelIdValue;
            if (!_summary.SeedLevelIdByKey.TryGetValue(room.Key, out levelIdValue) || levelIdValue <= 0)
            {
                return "N/A";
            }

            DB.Level level = _doc.GetElement(new DB.ElementId(levelIdValue)) as DB.Level;
            return level != null && !string.IsNullOrWhiteSpace(level.Name) ? level.Name : "N/A";
        }

        private void BindCardClick(Control card, RoomSemanticRecord room)
        {
            if (card == null || room == null)
            {
                return;
            }

            EventHandler handler = (sender, args) => LocateRoom(room);
            card.Click += handler;
            foreach (Control child in card.Controls)
            {
                child.Click += handler;
            }
        }

        // Focus the room by selecting and showing its drawn boundary lines.
        private void LocateRoom(RoomSemanticRecord room)
        {
            if (room == null || string.IsNullOrWhiteSpace(room.Key) || _uiDoc == null)
            {
                return;
            }

            DB.View activeView = _doc != null ? _doc.ActiveView : null;
            if (activeView is DB.View3D)
            {
                // In 3D, semantic focus is the primary path and does not depend on detail curves.
                RevitRoomSemanticFocusService.Focus(_uiDoc, room);
                return;
            }

            if (_roomRangeElementIds.TryGetValue(room.Key, out List<DB.ElementId> ids) && ids != null && ids.Count > 0)
            {
                List<DB.ElementId> validIds = ids
                    .Where(x => x != null && x != DB.ElementId.InvalidElementId && _doc != null && _doc.GetElement(x) != null)
                    .Distinct()
                    .ToList();
                if (validIds.Count > 0)
                {
                    _uiDoc.Selection.SetElementIds(validIds);
                    _uiDoc.ShowElements(validIds);
                    return;
                }
            }

            // Fallback keeps locate usable in 2D when range curves are missing/invalid.
            RevitRoomSemanticFocusService.Focus(_uiDoc, room);
        }
    }
}
