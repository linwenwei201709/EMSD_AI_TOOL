using Autodesk.Revit.DB;
using CadToRevit.Models.Rooms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Control = System.Windows.Forms.Control;
using Form = System.Windows.Forms.Form;

namespace CadToRevit.UI
{
    public enum RoomRecognitionFormAction
    {
        Cancel,
        Scan,
        Recognize,
        Generate,
        FocusSelected,
        ExportCsv
    }

    public sealed class RoomRecognitionForm : Form
    {
        private sealed class LinkOption
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() { return Name ?? string.Empty; }
        }

        private sealed class LevelOption
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() { return Name ?? string.Empty; }
        }

        private sealed class WallTypeOption
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() { return Name ?? string.Empty; }
        }

        private readonly ComboBox _cmbLink = new ComboBox();
        private readonly ComboBox _cmbLevel = new ComboBox();
        private readonly ComboBox _cmbBoundaryLayer = new ComboBox();
        private readonly ComboBox _cmbNameLayer = new ComboBox();
        private readonly ComboBox _cmbWallType = new ComboBox();
        private readonly CheckBox _chkNoName = new CheckBox();
        private readonly CheckBox _chkCreateWalls = new CheckBox();
        private readonly CheckBox _chkAvoidDuplicateWalls = new CheckBox();
        private readonly TextBox _txtCloseTol = new TextBox();
        private readonly TextBox _txtMaxPatch = new TextBox();
        private readonly TextBox _txtMinArea = new TextBox();
        private readonly TextBox _txtWallHeight = new TextBox();
        private readonly TextBox _txtMinWallSegment = new TextBox();
        private readonly DataGridView _grid = new DataGridView();
        private readonly Button _btnScan = new Button();
        private readonly Button _btnRecognize = new Button();
        private readonly Button _btnGenerate = new Button();
        private readonly Button _btnFocus = new Button();
        private readonly Button _btnExport = new Button();
        private readonly Button _btnCancel = new Button();

        public RoomRecognitionFormAction Action { get; private set; } = RoomRecognitionFormAction.Cancel;

        public ElementId SelectedCadLinkId
        {
            get
            {
                LinkOption option = _cmbLink.SelectedItem as LinkOption;
                return option == null ? ElementId.InvalidElementId : option.Id;
            }
        }

        public ElementId SelectedLevelId
        {
            get
            {
                LevelOption option = _cmbLevel.SelectedItem as LevelOption;
                return option == null ? ElementId.InvalidElementId : option.Id;
            }
        }

        public string BoundaryLayerName => _cmbBoundaryLayer.SelectedItem == null ? string.Empty : _cmbBoundaryLayer.SelectedItem.ToString();

        public string RoomTextLayerName => _cmbNameLayer.SelectedItem == null ? string.Empty : _cmbNameLayer.SelectedItem.ToString();

        public bool NoRoomName => _chkNoName.Checked;

        public double CloseTolMm => ParseOrDefault(_txtCloseTol.Text, 10.0);

        public double MaxPatchMm => ParseOrDefault(_txtMaxPatch.Text, 300.0);

        public double MinAreaM2 => ParseOrDefault(_txtMinArea.Text, 1.0);

        public bool CreateWalls => _chkCreateWalls.Checked;

        public ElementId SelectedWallTypeId
        {
            get
            {
                WallTypeOption option = _cmbWallType.SelectedItem as WallTypeOption;
                return option == null ? ElementId.InvalidElementId : option.Id;
            }
        }

        public double WallHeightMm => ParseOrDefault(_txtWallHeight.Text, 4000.0);

        public double MinWallSegmentMm => ParseOrDefault(_txtMinWallSegment.Text, 600.0);

        public bool AvoidDuplicateWalls => _chkAvoidDuplicateWalls.Checked;

        public string SelectedRoomKey
        {
            get
            {
                if (_grid.CurrentRow == null)
                {
                    return string.Empty;
                }

                object v = _grid.CurrentRow.Cells["colKey"].Value;
                return v == null ? string.Empty : v.ToString();
            }
        }

        public List<RoomCandidate> Candidates { get; private set; } = new List<RoomCandidate>();

        public RoomRecognitionForm(
            IEnumerable<ImportInstance> links,
            IEnumerable<Level> levels,
            IEnumerable<string> layerNames,
            IEnumerable<WallType> wallTypes,
            ElementId selectedCadLinkId,
            ElementId selectedLevelId,
            string boundaryLayerName,
            string roomTextLayerName,
            bool noRoomName,
            double closeTolMm,
            double maxPatchMm,
            double minAreaM2,
            bool createWalls,
            ElementId selectedWallTypeId,
            double wallHeightMm,
            double minWallSegmentMm,
            bool avoidDuplicateWalls,
            List<RoomCandidate> candidates)
        {
            Text = "Room Recognition";
            Width = 1160;
            Height = 760;
            StartPosition = FormStartPosition.CenterParent;
            Font = new System.Drawing.Font("Segoe UI", 10F);
            BuildLayout();

            BindLinks(links ?? new List<ImportInstance>(), selectedCadLinkId);
            BindLevels(levels ?? new List<Level>(), selectedLevelId);
            BindLayers(layerNames ?? new List<string>(), boundaryLayerName, roomTextLayerName);
            BindWallTypes(wallTypes ?? new List<WallType>(), selectedWallTypeId);
            _chkNoName.Checked = noRoomName;
            _txtCloseTol.Text = closeTolMm.ToString("F2");
            _txtMaxPatch.Text = maxPatchMm.ToString("F2");
            _txtMinArea.Text = minAreaM2.ToString("F2");
            _chkCreateWalls.Checked = createWalls;
            _txtWallHeight.Text = wallHeightMm.ToString("F0");
            _txtMinWallSegment.Text = minWallSegmentMm.ToString("F0");
            _chkAvoidDuplicateWalls.Checked = avoidDuplicateWalls;
            SetCandidates(candidates ?? new List<RoomCandidate>());
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 3;
            root.ColumnCount = 1;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.Padding = new Padding(12);

            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.ColumnCount = 8;
            for (int i = 0; i < 8; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
            top.RowCount = 5;
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _cmbLink.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbBoundaryLayer.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbNameLayer.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbWallType.DropDownStyle = ComboBoxStyle.DropDownList;

            AddLabeled(top, "CAD Link", _cmbLink, 0, 0);
            AddLabeled(top, "Level", _cmbLevel, 2, 0);
            AddLabeled(top, "Boundary Layer (Required)", _cmbBoundaryLayer, 4, 0);
            AddLabeled(top, "Name Layer (Optional)", _cmbNameLayer, 6, 0);

            AddLabeled(top, "Close Tolerance(mm)", _txtCloseTol, 0, 2);
            AddLabeled(top, "Max Patch(mm)", _txtMaxPatch, 2, 2);
            AddLabeled(top, "Min Area(m2)", _txtMinArea, 4, 2);
            _chkNoName.Text = "No room name (auto numbering)";
            _chkNoName.AutoSize = true;
            top.Controls.Add(_chkNoName, 6, 2);
            top.SetColumnSpan(_chkNoName, 2);

            _chkCreateWalls.Text = "Create Room Walls";
            _chkCreateWalls.AutoSize = true;
            top.Controls.Add(_chkCreateWalls, 0, 3);
            top.SetColumnSpan(_chkCreateWalls, 2);
            AddLabeled(top, "Wall Type", _cmbWallType, 2, 3);
            AddLabeled(top, "Wall Height(mm)", _txtWallHeight, 4, 3);
            AddLabeled(top, "Min Segment(mm)", _txtMinWallSegment, 6, 3);

            _chkAvoidDuplicateWalls.Text = "Avoid Duplicated Walls";
            _chkAvoidDuplicateWalls.AutoSize = true;
            top.Controls.Add(_chkAvoidDuplicateWalls, 0, 4);
            top.SetColumnSpan(_chkAvoidDuplicateWalls, 2);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Top;
            actions.Height = 42;
            actions.WrapContents = false;
            actions.FlowDirection = FlowDirection.LeftToRight;

            BuildButton(_btnScan, "Scan Layers", RoomRecognitionFormAction.Scan);
            BuildButton(_btnRecognize, "Recognize Rooms", RoomRecognitionFormAction.Recognize);
            BuildButton(_btnGenerate, "Generate to Revit", RoomRecognitionFormAction.Generate);
            BuildButton(_btnFocus, "Focus Selected", RoomRecognitionFormAction.FocusSelected);
            BuildButton(_btnExport, "Export CSV", RoomRecognitionFormAction.ExportCsv);
            _btnCancel.Text = "Close";
            _btnCancel.Width = 120;
            _btnCancel.Height = 32;
            _btnCancel.Click += (s, e) =>
            {
                Action = RoomRecognitionFormAction.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            actions.Controls.Add(_btnScan);
            actions.Controls.Add(_btnRecognize);
            actions.Controls.Add(_btnGenerate);
            actions.Controls.Add(_btnFocus);
            actions.Controls.Add(_btnExport);
            actions.Controls.Add(_btnCancel);

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("colKey", "Key");
            _grid.Columns.Add("colName", "Name");
            _grid.Columns.Add("colArea", "AreaM2");
            _grid.Columns.Add("colStatus", "Status");
            _grid.Columns.Add("colGap", "CloseGapMm");
            DataGridViewCheckBoxColumn created = new DataGridViewCheckBoxColumn
            {
                Name = "colCreated",
                HeaderText = "Created"
            };
            _grid.Columns.Add(created);
            _grid.Columns.Add("colRevitId", "RevitId");
            _grid.Columns["colKey"].Visible = false;
            _grid.Columns["colRevitId"].Visible = false;
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                Submit(RoomRecognitionFormAction.FocusSelected);
            };

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(actions, 0, 1);
            root.Controls.Add(_grid, 0, 2);
            Controls.Add(root);
        }

        private void BuildButton(Button button, string text, RoomRecognitionFormAction action)
        {
            button.Text = text;
            button.Width = 130;
            button.Height = 32;
            button.Click += (s, e) => Submit(action);
        }

        private void Submit(RoomRecognitionFormAction action)
        {
            if (action != RoomRecognitionFormAction.Cancel)
            {
                if (_cmbBoundaryLayer.SelectedItem == null)
                {
                    MessageBox.Show("Please select boundary layer.", "Invalid Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Candidates = ReadCandidatesFromGrid();
            }

            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AddLabeled(TableLayoutPanel table, string label, Control editor, int col, int row)
        {
            Label lbl = new Label
            {
                Text = label,
                AutoSize = true,
                Margin = new Padding(2, 8, 2, 4)
            };
            editor.Dock = DockStyle.Top;
            editor.Margin = new Padding(2);
            table.Controls.Add(lbl, col, row);
            table.Controls.Add(editor, col + 1, row);
        }

        private void BindLinks(IEnumerable<ImportInstance> links, ElementId selected)
        {
            _cmbLink.Items.Clear();
            foreach (ImportInstance link in links)
            {
                if (link == null) continue;
                _cmbLink.Items.Add(new LinkOption
                {
                    Id = link.Id,
                    Name = (link.Name ?? "ImportInstance") + " [" + link.Id.IntegerValue + "]"
                });
            }

            if (_cmbLink.Items.Count == 0) return;
            int index = 0;
            for (int i = 0; i < _cmbLink.Items.Count; i++)
            {
                LinkOption o = _cmbLink.Items[i] as LinkOption;
                if (o != null && o.Id == selected) { index = i; break; }
            }

            _cmbLink.SelectedIndex = index;
        }

        private void BindLevels(IEnumerable<Level> levels, ElementId selected)
        {
            _cmbLevel.Items.Clear();
            foreach (Level level in levels)
            {
                if (level == null) continue;
                _cmbLevel.Items.Add(new LevelOption { Id = level.Id, Name = level.Name });
            }

            if (_cmbLevel.Items.Count == 0) return;
            int index = 0;
            for (int i = 0; i < _cmbLevel.Items.Count; i++)
            {
                LevelOption o = _cmbLevel.Items[i] as LevelOption;
                if (o != null && o.Id == selected) { index = i; break; }
            }

            _cmbLevel.SelectedIndex = index;
        }

        private void BindLayers(IEnumerable<string> layers, string boundaryLayer, string textLayer)
        {
            _cmbBoundaryLayer.Items.Clear();
            _cmbNameLayer.Items.Clear();
            foreach (string layer in layers.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x))
            {
                _cmbBoundaryLayer.Items.Add(layer);
                _cmbNameLayer.Items.Add(layer);
            }

            SelectCombo(_cmbBoundaryLayer, boundaryLayer);
            SelectCombo(_cmbNameLayer, textLayer);
        }

        private void BindWallTypes(IEnumerable<WallType> wallTypes, ElementId selected)
        {
            _cmbWallType.Items.Clear();
            foreach (WallType wallType in wallTypes ?? new List<WallType>())
            {
                if (wallType == null)
                {
                    continue;
                }

                _cmbWallType.Items.Add(new WallTypeOption
                {
                    Id = wallType.Id,
                    Name = wallType.Name
                });
            }

            if (_cmbWallType.Items.Count == 0)
            {
                return;
            }

            int index = 0;
            for (int i = 0; i < _cmbWallType.Items.Count; i++)
            {
                WallTypeOption option = _cmbWallType.Items[i] as WallTypeOption;
                if (option == null)
                {
                    continue;
                }

                if (option.Id == selected)
                {
                    index = i;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(option.Name) &&
                    option.Name.IndexOf("90", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    index = i;
                }
            }

            _cmbWallType.SelectedIndex = index;
        }

        private static void SelectCombo(ComboBox combo, string value)
        {
            if (combo.Items.Count == 0)
            {
                return;
            }

            int index = 0;
            if (!string.IsNullOrWhiteSpace(value))
            {
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (string.Equals(combo.Items[i].ToString(), value, StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
            }

            combo.SelectedIndex = index;
        }

        private void SetCandidates(List<RoomCandidate> candidates)
        {
            Candidates = candidates ?? new List<RoomCandidate>();
            _grid.Rows.Clear();
            foreach (RoomCandidate c in Candidates)
            {
                _grid.Rows.Add(
                    c.Key,
                    c.Name,
                    c.AreaM2.ToString("F2"),
                    c.Status.ToString(),
                    c.CloseGapMm.ToString("F1"),
                    c.Created,
                    c.RevitRoomId == null || c.RevitRoomId == ElementId.InvalidElementId ? string.Empty : c.RevitRoomId.IntegerValue.ToString());
            }
        }

        private List<RoomCandidate> ReadCandidatesFromGrid()
        {
            Dictionary<string, RoomCandidate> byKey = (Candidates ?? new List<RoomCandidate>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(x => x.Key, x => x, StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row == null || row.Cells["colKey"] == null)
                {
                    continue;
                }

                string key = row.Cells["colKey"].Value == null ? string.Empty : row.Cells["colKey"].Value.ToString();
                if (string.IsNullOrWhiteSpace(key) || !byKey.ContainsKey(key))
                {
                    continue;
                }

                byKey[key].Name = row.Cells["colName"].Value == null ? byKey[key].Name : row.Cells["colName"].Value.ToString();
            }

            return byKey.Values.ToList();
        }

        private static double ParseOrDefault(string text, double fallback)
        {
            double value;
            return double.TryParse(text, out value) ? value : fallback;
        }
    }
}