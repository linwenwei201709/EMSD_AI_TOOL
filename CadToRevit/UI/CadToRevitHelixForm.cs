using Autodesk.Revit.DB;
using CadToRevit.Models;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Units;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using FontAwesome.Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CadToRevit.UI
{
    public enum HelixWizardAction
    {
        Cancel,
        Analyze,
        Preview,
        CreateElements
    }

    public sealed class CadToRevitHelixForm : System.Windows.Forms.Form
    {
        private sealed class IdNameOption
        {
            public ElementId Id { get; set; }

            public string Name { get; set; }

            public override string ToString()
            {
                return Name ?? string.Empty;
            }
        }

        private readonly ComboBox _cmbDwgLink = new ComboBox();
        private readonly ComboBox _cmbLevel = new ComboBox();
        private readonly ComboBox _cmbUnit = new ComboBox();
        private readonly DataGridView _gridMapBoard = new DataGridView();
        private readonly IconButton _btnAnalyze = new IconButton();
        private readonly IconButton _btnAddLayerMapping = new IconButton();
        private readonly IconButton _btnPreview = new IconButton();
        private readonly IconButton _btnCreateElements = new IconButton();
        private readonly IconButton _btnCancel = new IconButton();
        private readonly IconButton _btnExportProfile = new IconButton();
        private readonly IconButton _btnImportProfile = new IconButton();
        private readonly IconButton _btnCopyPerfLog = new IconButton();
        private readonly CheckBox _chkJoinWalls = new CheckBox();
        private readonly CheckBox _chkSafeMode = new CheckBox();
        private readonly GroupBox _grpAdvanced = new GroupBox();
        private readonly Label _lblAdvancedDesc1 = new Label();
        private readonly Label _lblAdvancedDesc2 = new Label();
        private readonly Label _lblAnalyzeStatus = new Label();
        private readonly Label _lblAnalyzeTime = new Label();
        private readonly Label _lblPreviewHint = new Label();
        private readonly VerticalDimensionSettings _verticalSettings;

        private readonly List<string> _layerOptions;
        private readonly List<string> _wallTypeNames;
        private readonly List<string> _columnTypeNames;
        private readonly List<string> _doorTypeNames;
        private readonly List<string> _windowTypeNames;
        private readonly List<string> _beamTypeNames;
        private readonly List<Level> _levels;
        private readonly List<ParameterOption> _parameterOptions;
        private readonly string _analyzeSummaryText;
        private readonly string _lastAnalyzeText;

        public HelixWizardAction Action { get; private set; } = HelixWizardAction.Cancel;

        public CadToRevitHelixForm(
            IEnumerable<ImportInstance> dwgLinks,
            ElementId selectedDwgId,
            IEnumerable<Level> levels,
            ElementId selectedLevelId,
            SourceUnit selectedUnit,
            IEnumerable<string> wallTypeNames,
            IEnumerable<string> columnTypeNames,
            IEnumerable<string> doorTypeNames,
            IEnumerable<string> windowTypeNames,
            IEnumerable<string> beamTypeNames,
            IEnumerable<ParameterOption> parameterOptions,
            IEnumerable<string> layerOptions,
            IEnumerable<MapRow> rows,
            bool joinWallsAfterCreate,
            bool safeModeEnabled,
            VerticalDimensionSettings verticalSettings,
            string analyzeSummaryText,
            string lastAnalyzeText)
        {
            _levels = (levels ?? Enumerable.Empty<Level>()).ToList();
            _layerOptions = (layerOptions ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _wallTypeNames = (wallTypeNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _columnTypeNames = (columnTypeNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _doorTypeNames = (doorTypeNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _windowTypeNames = (windowTypeNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _beamTypeNames = (beamTypeNames ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _parameterOptions = (parameterOptions ?? Enumerable.Empty<ParameterOption>()).ToList();
            _analyzeSummaryText = string.IsNullOrWhiteSpace(analyzeSummaryText) ? "Status: Not analyzed" : analyzeSummaryText;
            _lastAnalyzeText = string.IsNullOrWhiteSpace(lastAnalyzeText) ? "Last Analyze: N/A" : lastAnalyzeText;

            BuildLayout();
            BindHeader(dwgLinks, selectedDwgId, _levels, selectedLevelId, selectedUnit);
            BuildGridColumns();
            LoadRows(rows);
            _chkJoinWalls.Checked = joinWallsAfterCreate;
            _chkSafeMode.Checked = safeModeEnabled;
            _verticalSettings = CloneVerticalSettings(verticalSettings ?? new VerticalDimensionSettings());
        }

        public ElementId SelectedDwgId
        {
            get
            {
                IdNameOption item = _cmbDwgLink.SelectedItem as IdNameOption;
                return item != null ? item.Id : ElementId.InvalidElementId;
            }
        }

        public ElementId SelectedLevelId
        {
            get
            {
                IdNameOption item = _cmbLevel.SelectedItem as IdNameOption;
                return item != null ? item.Id : ElementId.InvalidElementId;
            }
        }

        public SourceUnit SelectedUnit
        {
            get
            {
                object value = _cmbUnit.SelectedItem;
                if (value is SourceUnit)
                {
                    return (SourceUnit)value;
                }

                return SourceUnit.Auto;
            }
        }

        public bool JoinWallsAfterCreate
        {
            get { return _chkJoinWalls.Checked; }
        }

        public bool SafeModeEnabled
        {
            get { return _chkSafeMode.Checked; }
        }

        public VerticalDimensionSettings VerticalSettings
        {
            get
            {
                return CloneVerticalSettings(_verticalSettings);
            }
        }

        public List<MapRow> GetMapRows()
        {
            List<MapRow> rows = new List<MapRow>();
            HashSet<string> dedupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow row in _gridMapBoard.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }

                string rawLayer = row.Cells["colLayer"].Value == null ? string.Empty : row.Cells["colLayer"].Value.ToString();
                if (string.IsNullOrWhiteSpace(rawLayer))
                {
                    continue;
                }

                string categoryText = row.Cells["colCategory"].Value == null ? "Walls" : row.Cells["colCategory"].Value.ToString();
                MapCategory category = ParseCategory(categoryText);
                string dedupKey = rawLayer.Trim() + "|" + category.ToString();
                if (!dedupKeys.Add(dedupKey))
                {
                    continue;
                }

                string typeName = row.Cells["colType"].Value == null ? string.Empty : row.Cells["colType"].Value.ToString();
                AdvancedSettingsRow settings = CloneSettings(row.Tag as AdvancedSettingsRow);

                rows.Add(new MapRow
                {
                    RawLayerName = rawLayer,
                    Category = category,
                    RevitTypeName = typeName,
                    ExpectedWidthMm = settings.DoorExpectedWidthMm,
                    Settings = settings
                });
            }

            return rows;
        }

        public HashSet<string> GetPreviewRawLayers()
        {
            HashSet<string> layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 棰勮榛樿瑕嗙�?Map Board 鍏ㄩ儴宸查厤缃浘灞傦紝閬垮厤浠呮樉绀哄綋鍓嶇劍鐐硅�?
            foreach (MapRow row in GetMapRows())
            {
                if (!string.IsNullOrWhiteSpace(row.RawLayerName))
                {
                    layers.Add(row.RawLayerName);
                }
            }

            return layers;
        }
        private void BuildLayout()
        {
            Text = "CadToRevit - Helix Style Wizard (M9-3) [" + BuildTitleStamp() + "]";
            Width = 1500;
            Height = 1000;
            MinimumSize = new System.Drawing.Size(1200, 820);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new System.Drawing.Font("Segoe UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Padding = new Padding(12);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));

            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.ColumnCount = 7;
            top.RowCount = 1;
            // 椤舵爮閲囩敤鍥哄畾鏍囩瀹藉害锛岄伩鍏嶇渷鐣ュ彿锛汥WG 涓嬫媺妗嗛€傚綋鏀剁獎�?
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));

            Label lblDwg = new Label();
            lblDwg.Text = "DWG";
            lblDwg.Dock = DockStyle.Fill;
            lblDwg.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblDwg.AutoSize = false;
            lblDwg.AutoEllipsis = false;
            lblDwg.Margin = new Padding(0, 0, 4, 0);
            _cmbDwgLink.Dock = DockStyle.Fill;
            _cmbDwgLink.Margin = new Padding(0, 6, 8, 6);
            _cmbDwgLink.DropDownStyle = ComboBoxStyle.DropDownList;

            Label lblLevel = new Label();
            lblLevel.Text = "Level";
            lblLevel.Dock = DockStyle.Fill;
            lblLevel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblLevel.AutoSize = false;
            lblLevel.AutoEllipsis = false;
            lblLevel.Margin = new Padding(0, 0, 4, 0);
            _cmbLevel.Dock = DockStyle.Fill;
            _cmbLevel.Margin = new Padding(0, 6, 8, 6);
            _cmbLevel.DropDownStyle = ComboBoxStyle.DropDownList;

            Label lblUnit = new Label();
            lblUnit.Text = "Unit";
            lblUnit.Dock = DockStyle.Fill;
            lblUnit.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            lblUnit.AutoSize = false;
            lblUnit.AutoEllipsis = false;
            lblUnit.Margin = new Padding(0, 0, 4, 0);
            _cmbUnit.Dock = DockStyle.Fill;
            _cmbUnit.Margin = new Padding(0, 6, 8, 6);
            _cmbUnit.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbUnit.Items.Add(SourceUnit.Auto);
            _cmbUnit.Items.Add(SourceUnit.Feet);
            _cmbUnit.Items.Add(SourceUnit.Inch);
            _cmbUnit.Items.Add(SourceUnit.Millimeter);
            _cmbUnit.Items.Add(SourceUnit.Meter);

            _btnAnalyze.Text = "Analyze DWG";
            _btnAnalyze.Dock = DockStyle.Fill;
            _btnAnalyze.Width = 180;
            _btnAnalyze.Height = 32;
            _btnAnalyze.Margin = new Padding(6, 6, 0, 6);
            ApplyButtonIconStyle(_btnAnalyze, IconChar.MagnifyingGlass);
            _btnAnalyze.Click += (s, e) =>
            {
                Action = HelixWizardAction.Analyze;
                DialogResult = DialogResult.OK;
                Close();
            };
            System.Diagnostics.Debug.WriteLine(_btnAnalyze.Text);
            top.Controls.Add(lblDwg, 0, 0);
            top.Controls.Add(_cmbDwgLink, 1, 0);
            top.Controls.Add(lblLevel, 2, 0);
            top.Controls.Add(_cmbLevel, 3, 0);
            top.Controls.Add(lblUnit, 4, 0);
            top.Controls.Add(_cmbUnit, 5, 0);
            top.Controls.Add(_btnAnalyze, 6, 0);

            TableLayoutPanel statusPanel = new TableLayoutPanel();
            statusPanel.Dock = DockStyle.Fill;
            statusPanel.BackColor = System.Drawing.Color.FromArgb(242, 242, 242);
            statusPanel.ColumnCount = 2;
            statusPanel.RowCount = 1;
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _lblAnalyzeStatus.Dock = DockStyle.Fill;
            _lblAnalyzeStatus.Text = string.IsNullOrWhiteSpace(_analyzeSummaryText) ? "Status: Not analyzed" : _analyzeSummaryText;
            _lblAnalyzeStatus.AutoEllipsis = true;
            _lblAnalyzeStatus.Font = new System.Drawing.Font("Segoe UI", 9F);
            _lblAnalyzeStatus.Padding = new Padding(8, 0, 0, 0);
            _lblAnalyzeStatus.AutoSize = false;
            _lblAnalyzeStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            _lblAnalyzeStatus.ForeColor = System.Drawing.Color.Black;
            _lblAnalyzeStatus.BackColor = System.Drawing.Color.FromArgb(242, 242, 242);
            _lblAnalyzeStatus.BorderStyle = BorderStyle.None;

            _lblAnalyzeTime.Dock = DockStyle.Fill;
            _lblAnalyzeTime.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            _lblAnalyzeTime.Text = _lastAnalyzeText;
            _lblAnalyzeTime.Font = new System.Drawing.Font("Segoe UI", 9F);
            _lblAnalyzeTime.AutoSize = false;
            _lblAnalyzeTime.ForeColor = System.Drawing.Color.Black;
            _lblAnalyzeTime.BackColor = System.Drawing.Color.FromArgb(242, 242, 242);
            _lblAnalyzeTime.BorderStyle = BorderStyle.None;
            statusPanel.Controls.Add(_lblAnalyzeStatus, 0, 0);
            statusPanel.Controls.Add(_lblAnalyzeTime, 1, 0);

            System.Windows.Forms.Panel gridPanel = new System.Windows.Forms.Panel();
            gridPanel.Dock = DockStyle.Fill;
            _gridMapBoard.Dock = DockStyle.Fill;
            _gridMapBoard.AllowUserToAddRows = false;
            _gridMapBoard.AllowUserToDeleteRows = false;
            _gridMapBoard.AllowUserToResizeRows = false;
            _gridMapBoard.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            _gridMapBoard.RowHeadersVisible = false;
            // 浣跨敤鍥哄畾鍒楀锛岀‘淇濆悇鍒楁瘮渚嬬ǔ瀹氫笖鏇磋创杩戠洰鏍囩晫闈�?
            _gridMapBoard.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _gridMapBoard.EnableHeadersVisualStyles = false;
            _gridMapBoard.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            _gridMapBoard.ColumnHeadersHeight = 50;
            _gridMapBoard.RowTemplate.Height = 46;
            _gridMapBoard.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _gridMapBoard.MultiSelect = false;
            _gridMapBoard.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            _gridMapBoard.CellContentClick += OnGridCellContentClick;
            _gridMapBoard.CellValueChanged += OnGridCellValueChanged;
            _gridMapBoard.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
            _gridMapBoard.DataError += (s, e) => { e.ThrowException = false; };
            _gridMapBoard.ColumnHeadersDefaultCellStyle.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold);
            _gridMapBoard.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 6, 6, 6);
            _gridMapBoard.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            _gridMapBoard.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _gridMapBoard.DefaultCellStyle.Padding = new Padding(6, 6, 6, 6);
            _gridMapBoard.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            // 鍘绘帀鏁磋閫変腑鏃剁殑钃濊壊鑳屾櫙锛屼繚鎸佹墍鏈夊垪瑙嗚涓€鑷淬€?
            _gridMapBoard.DefaultCellStyle.SelectionBackColor = _gridMapBoard.DefaultCellStyle.BackColor;
            _gridMapBoard.DefaultCellStyle.SelectionForeColor = _gridMapBoard.DefaultCellStyle.ForeColor;
            _gridMapBoard.RowsDefaultCellStyle.SelectionBackColor = _gridMapBoard.DefaultCellStyle.BackColor;
            _gridMapBoard.RowsDefaultCellStyle.SelectionForeColor = _gridMapBoard.DefaultCellStyle.ForeColor;
            gridPanel.Controls.Add(_gridMapBoard);

            _btnAddLayerMapping.Text = "Add Layer Mapping";
            _btnAddLayerMapping.Text = "Add Layer";
            _btnAddLayerMapping.Width = 210;
            _btnAddLayerMapping.Height = 43;
            ApplyButtonIconStyle(_btnAddLayerMapping, IconChar.Plus);
            _btnAddLayerMapping.Click += (s, e) => AddRow(null);
            _btnAddLayerMapping.Anchor = AnchorStyles.Left | AnchorStyles.Top;

            TableLayoutPanel actionRow = new TableLayoutPanel();
            actionRow.Dock = DockStyle.Fill;
            actionRow.ColumnCount = 3;
            actionRow.RowCount = 1;
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 620F));
            actionRow.Controls.Add(_btnAddLayerMapping, 0, 0);

            _grpAdvanced.Dock = DockStyle.Fill;
            _grpAdvanced.Text = "Advanced (Generation)";
            _grpAdvanced.Height = 92;

            TableLayoutPanel advLayout = new TableLayoutPanel();
            advLayout.Dock = DockStyle.Fill;
            advLayout.ColumnCount = 1;
            advLayout.RowCount = 3;
            advLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            advLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            advLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

            _chkSafeMode.Text = "Safe Mode (Recommended)";
            _chkSafeMode.AutoSize = true;
            _chkSafeMode.Dock = DockStyle.Fill;
            _chkSafeMode.Margin = new Padding(8, 0, 0, 0);

            _chkJoinWalls.Text = "Auto Join Walls After Create";
            _chkJoinWalls.AutoSize = true;
            _chkJoinWalls.Dock = DockStyle.Fill;
            _chkJoinWalls.Margin = new Padding(8, 0, 0, 0);

            _lblAdvancedDesc1.Dock = DockStyle.Fill;
            _lblAdvancedDesc1.Text = "Safe Mode: safer transaction strategy | Join: post-process join walls";
            _lblAdvancedDesc1.ForeColor = System.Drawing.Color.DimGray;
            _lblAdvancedDesc1.Font = new System.Drawing.Font("Segoe UI", 8.5F);
            _lblAdvancedDesc1.AutoEllipsis = true;
            _lblAdvancedDesc1.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            _lblAdvancedDesc1.Margin = new Padding(8, 0, 0, 0);

            _lblAdvancedDesc2.Visible = false;

            advLayout.Controls.Add(_chkSafeMode, 0, 0);
            advLayout.Controls.Add(_chkJoinWalls, 0, 1);
            advLayout.Controls.Add(_lblAdvancedDesc1, 0, 2);
            _grpAdvanced.Controls.Add(advLayout);
            actionRow.Controls.Add(_grpAdvanced, 1, 0);

            TableLayoutPanel buttonArea = new TableLayoutPanel();
            buttonArea.Dock = DockStyle.Fill;
            buttonArea.ColumnCount = 1;
            buttonArea.RowCount = 2;
            buttonArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            buttonArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));

            FlowLayoutPanel mainButtons = new FlowLayoutPanel();
            mainButtons.Dock = DockStyle.Fill;
            mainButtons.FlowDirection = FlowDirection.RightToLeft;
            mainButtons.WrapContents = false;

            FlowLayoutPanel toolButtons = new FlowLayoutPanel();
            toolButtons.Dock = DockStyle.Fill;
            toolButtons.FlowDirection = FlowDirection.RightToLeft;
            toolButtons.WrapContents = false;

            _btnPreview.Text = "Preview";
            _btnPreview.Width = 140;
            _btnPreview.Height = 43;
            ApplyButtonIconStyle(_btnPreview, IconChar.Eye);
            _btnPreview.Click += (s, e) =>
            {
                Action = HelixWizardAction.Preview;
                DialogResult = DialogResult.OK;
                Close();
            };

            _btnCreateElements.Text = "Create Elements";
            _btnCreateElements.Text = "Create Element";
            _btnCreateElements.Width = 240;
            _btnCreateElements.Height = 43;
            _btnCreateElements.AutoSize = false;
            _btnCreateElements.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);
            _btnCreateElements.ForeColor = System.Drawing.Color.White;
            _btnCreateElements.FlatStyle = FlatStyle.Flat;
            ApplyButtonIconStyle(_btnCreateElements, IconChar.Bolt);
            _btnCreateElements.IconColor = System.Drawing.Color.White;
            _btnCreateElements.Click += (s, e) =>
            {
                Action = HelixWizardAction.CreateElements;
                DialogResult = DialogResult.OK;
                Close();
            };

            _btnCancel.Text = "Cancel";
            _btnCancel.Width = 140;
            _btnCancel.Height = 43;
            ApplyButtonIconStyle(_btnCancel, IconChar.Xmark);
            _btnCancel.Click += (s, e) =>
            {
                Action = HelixWizardAction.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _btnExportProfile.Text = "Export Preset";
            _btnExportProfile.Width = 200;
            _btnExportProfile.Height = 43;
            ApplyButtonIconStyle(_btnExportProfile, IconChar.FileExport);
            _btnExportProfile.Click += (s, e) =>
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Filter = "JSON|*.json";
                    dlg.FileName = "layer_overrides_export.json";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        LayerOverrideStoreService.ExportProfile(dlg.FileName, GetMapRows());
                    }
                }
            };

            _btnImportProfile.Text = "Import Preset";
            _btnImportProfile.Width = 160;
            _btnImportProfile.Height = 43;
            ApplyButtonIconStyle(_btnImportProfile, IconChar.FileImport);
            _btnImportProfile.Visible = false;
            _btnImportProfile.Click += (s, e) =>
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Filter = "JSON|*.json";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        LayerOverrideStoreData imported = LayerOverrideStoreService.ImportProfileFull(dlg.FileName);
                        if ((imported.LayerOverrides == null || imported.LayerOverrides.Count == 0) &&
                            (imported.CategoryDefaults == null || imported.CategoryDefaults.Count == 0))
                        {
                            return;
                        }

                        foreach (DataGridViewRow gridRow in _gridMapBoard.Rows)
                        {
                            if (gridRow == null || gridRow.IsNewRow)
                            {
                                continue;
                            }

                            string layer = gridRow.Cells["colLayer"].Value == null ? string.Empty : gridRow.Cells["colLayer"].Value.ToString();
                            if (string.IsNullOrWhiteSpace(layer))
                            {
                                continue;
                            }

                            AdvancedSettingsRow settings;
                            if (imported.LayerOverrides != null &&
                                imported.LayerOverrides.TryGetValue(layer, out settings))
                            {
                                gridRow.Tag = CloneSettings(settings);
                                continue;
                            }

                            string categoryText = gridRow.Cells["colCategory"].Value == null ? "Walls" : gridRow.Cells["colCategory"].Value.ToString();
                            MapCategory category = ParseCategory(categoryText);
                            if (imported.CategoryDefaults != null &&
                                imported.CategoryDefaults.TryGetValue(category, out settings))
                            {
                                gridRow.Tag = CloneSettings(settings);
                            }
                        }
                    }
                }
            };

            _btnCopyPerfLog.Text = "Copy Perf Log";
            _btnCopyPerfLog.Width = 210;
            _btnCopyPerfLog.Height = 43;
            ApplyButtonIconStyle(_btnCopyPerfLog, IconChar.Clipboard);
            _btnCopyPerfLog.Click += (s, e) =>
            {
                string path = ProfilingLogService.LastLogPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    MessageBox.Show(this, "No profiling log yet.", "CadToRevit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Clipboard.SetText(path);
                MessageBox.Show(this, "Profiling log path copied.", "CadToRevit", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            mainButtons.Controls.Add(_btnCancel);
            mainButtons.Controls.Add(_btnCreateElements);
            mainButtons.Controls.Add(_btnPreview);
            toolButtons.Controls.Add(_btnCopyPerfLog);
            toolButtons.Controls.Add(_btnImportProfile);
            toolButtons.Controls.Add(_btnExportProfile);
            buttonArea.Controls.Add(mainButtons, 0, 0);
            buttonArea.Controls.Add(toolButtons, 0, 1);
            actionRow.Controls.Add(buttonArea, 2, 0);

            _lblPreviewHint.Dock = DockStyle.Fill;
            _lblPreviewHint.Font = new System.Drawing.Font("Segoe UI", 8.8F);
            _lblPreviewHint.ForeColor = System.Drawing.Color.DimGray;
            _lblPreviewHint.Text = "Preview uses cyan thick lines (HelixPreview). Click Preview again to refresh / clear.";
            _lblPreviewHint.AutoEllipsis = true;
            _lblPreviewHint.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(statusPanel, 0, 1);
            root.Controls.Add(gridPanel, 0, 2);
            root.Controls.Add(actionRow, 0, 3);
            root.Controls.Add(_lblPreviewHint, 0, 4);
            Controls.Add(root);
        }

        private static void ApplyButtonIconStyle(IconButton button, IconChar iconChar)
        {
            button.IconChar = iconChar;
            button.IconFont = IconFont.Auto;
            button.IconSize = 22;
            button.IconColor = System.Drawing.Color.FromArgb(64, 64, 64);
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            button.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            button.Padding = new Padding(10, 0, 10, 0);
        }

        private void BindHeader(
            IEnumerable<ImportInstance> dwgLinks,
            ElementId selectedDwgId,
            IEnumerable<Level> levels,
            ElementId selectedLevelId,
            SourceUnit selectedUnit)
        {
            List<IdNameOption> dwgItems = (dwgLinks ?? Enumerable.Empty<ImportInstance>())
                .Select(x => new IdNameOption
                {
                    Id = x.Id,
                    Name = BuildImportDisplayName(x)
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _cmbDwgLink.Items.AddRange(dwgItems.Cast<object>().ToArray());
            SelectComboById(_cmbDwgLink, selectedDwgId);
            if (_cmbDwgLink.SelectedIndex < 0 && _cmbDwgLink.Items.Count > 0)
            {
                _cmbDwgLink.SelectedIndex = 0;
            }

            List<IdNameOption> levelItems = (levels ?? Enumerable.Empty<Level>())
                .Select(x => new IdNameOption
                {
                    Id = x.Id,
                    Name = x.Name
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _cmbLevel.Items.AddRange(levelItems.Cast<object>().ToArray());
            SelectComboById(_cmbLevel, selectedLevelId);
            if (_cmbLevel.SelectedIndex < 0 && _cmbLevel.Items.Count > 0)
            {
                _cmbLevel.SelectedIndex = 0;
            }

            _cmbUnit.SelectedItem = selectedUnit;
            if (_cmbUnit.SelectedIndex < 0)
            {
                _cmbUnit.SelectedItem = SourceUnit.Auto;
            }
        }

        private static string BuildImportDisplayName(ImportInstance importInstance)
        {
            if (importInstance == null)
            {
                return string.Empty;
            }

            string name = importInstance.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                return "ImportInstance " + importInstance.Id.IntegerValue;
            }

            return name;
        }

        private static void SelectComboById(ComboBox combo, ElementId id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                IdNameOption item = combo.Items[i] as IdNameOption;
                if (item != null && item.Id != null && id != null && item.Id.IntegerValue == id.IntegerValue)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void BuildGridColumns()
        {
            _gridMapBoard.Columns.Clear();

            DataGridViewComboBoxColumn colLayer = new DataGridViewComboBoxColumn();
            colLayer.Name = "colLayer";
            colLayer.HeaderText = "Layer";
            colLayer.MinimumWidth = 220;
            colLayer.Width = 330;
            colLayer.DataSource = _layerOptions.Count > 0 ? _layerOptions : new List<string> { string.Empty };

            DataGridViewComboBoxColumn colCategory = new DataGridViewComboBoxColumn();
            colCategory.Name = "colCategory";
            colCategory.HeaderText = "Category";
            colCategory.MinimumWidth = 180;
            colCategory.Width = 220;
            colCategory.DataSource = new List<string> { "Walls", "Columns", "Doors", "Windows", "Beams" };

            DataGridViewComboBoxColumn colType = new DataGridViewComboBoxColumn();
            colType.Name = "colType";
            colType.HeaderText = "Family Type";
            colType.MinimumWidth = 260;
            colType.Width = 520;
            colType.DataSource = new List<string> { string.Empty };

            DataGridViewButtonColumn colAdvanced = new DataGridViewButtonColumn();
            colAdvanced.Name = "colAdvanced";
            colAdvanced.HeaderText = "Settings";
            colAdvanced.MinimumWidth = 150;
            colAdvanced.Width = 210;
            colAdvanced.Text = "Open";
            colAdvanced.FlatStyle = FlatStyle.Flat;
            colAdvanced.UseColumnTextForButtonValue = true;
            colAdvanced.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colAdvanced.DefaultCellStyle.BackColor = System.Drawing.SystemColors.Control;
            colAdvanced.DefaultCellStyle.SelectionBackColor = System.Drawing.SystemColors.Control;
            colAdvanced.DefaultCellStyle.ForeColor = System.Drawing.Color.Black;
            colAdvanced.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;

            DataGridViewButtonColumn colDelete = new DataGridViewButtonColumn();
            colDelete.Name = "colDelete";
            colDelete.HeaderText = "Remove";
            colDelete.MinimumWidth = 150;
            colDelete.Width = 160;
            colDelete.Text = "Delete";
            colDelete.FlatStyle = FlatStyle.Flat;
            colDelete.UseColumnTextForButtonValue = true;
            colDelete.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            colDelete.DefaultCellStyle.BackColor = System.Drawing.SystemColors.Control;
            colDelete.DefaultCellStyle.SelectionBackColor = System.Drawing.SystemColors.Control;
            colDelete.DefaultCellStyle.ForeColor = System.Drawing.Color.Black;
            colDelete.DefaultCellStyle.SelectionForeColor = System.Drawing.Color.Black;

            _gridMapBoard.Columns.Add(colLayer);
            _gridMapBoard.Columns.Add(colCategory);
            _gridMapBoard.Columns.Add(colType);
            _gridMapBoard.Columns.Add(colAdvanced);
            _gridMapBoard.Columns.Add(colDelete);
        }

        private void LoadRows(IEnumerable<MapRow> rows)
        {
            _gridMapBoard.Rows.Clear();
            List<MapRow> source = rows == null ? new List<MapRow>() : rows.ToList();
            if (source.Count == 0)
            {
                return;
            }

            foreach (MapRow row in source)
            {
                AddRow(row);
            }
        }

        private void AddRow(MapRow row)
        {
            int index = _gridMapBoard.Rows.Add();
            DataGridViewRow newRow = _gridMapBoard.Rows[index];
            // 缁熶竴璁剧疆琛岄珮锛岄伩鍏嶅唴瀹规嫢鎸ゃ€?
            newRow.Height = 46;

            string rawLayer = row != null ? row.RawLayerName : string.Empty;
            string category = row != null ? row.Category.ToString() : "Walls";
            string typeName = row != null ? row.RevitTypeName : string.Empty;

            newRow.Cells["colLayer"].Value = NormalizeLayer(rawLayer);
            newRow.Cells["colCategory"].Value = category;
            SetTypeCellOptions(newRow, ParseCategory(category), typeName);
            AdvancedSettingsRow settings = CloneSettings(row != null ? row.Settings : null);
            if (row != null && row.ExpectedWidthMm.HasValue && !settings.DoorExpectedWidthMm.HasValue)
            {
                settings.DoorExpectedWidthMm = row.ExpectedWidthMm;
            }

            newRow.Tag = settings;
        }

        private string NormalizeLayer(string layer)
        {
            if (string.IsNullOrWhiteSpace(layer))
            {
                return string.Empty;
            }

            string match = _layerOptions.FirstOrDefault(x => string.Equals(x, layer, StringComparison.OrdinalIgnoreCase));
            return match ?? string.Empty;
        }

        private static MapCategory ParseCategory(string value)
        {
            MapCategory category;
            if (Enum.TryParse(value, true, out category))
            {
                return category;
            }

            return MapCategory.Walls;
        }

        private void OnGridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_gridMapBoard.IsCurrentCellDirty)
            {
                _gridMapBoard.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private static string BuildTitleStamp()
        {
            try
            {
                // 涓枃娉ㄩ噴锛氭樉绀哄綋�?DLL 鏋勫缓鏃堕棿锛屼究浜庣‘璁ゅ凡鍔犺浇鏈€鏂扮増鏈�?
                string asm = Assembly.GetExecutingAssembly().Location;
                DateTime dt = File.GetLastWriteTime(asm);
                return dt.ToString("yyyyMMdd-HHmmss");
            }
            catch
            {
                return DateTime.Now.ToString("yyyyMMdd-HHmmss");
            }
        }
        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string columnName = _gridMapBoard.Columns[e.ColumnIndex].Name;
            if (!string.Equals(columnName, "colCategory", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DataGridViewRow row = _gridMapBoard.Rows[e.RowIndex];
            string categoryText = row.Cells["colCategory"].Value == null ? "Walls" : row.Cells["colCategory"].Value.ToString();
            SetTypeCellOptions(row, ParseCategory(categoryText), null);
        }

        private void SetTypeCellOptions(DataGridViewRow row, MapCategory category, string preferredTypeName)
        {
            if (row == null)
            {
                return;
            }

            List<string> options = GetTypeOptionsForCategory(category);
            if (options.Count == 0)
            {
                options = new List<string> { string.Empty };
            }

            DataGridViewComboBoxCell typeCell = row.Cells["colType"] as DataGridViewComboBoxCell;
            if (typeCell == null)
            {
                return;
            }

            typeCell.DataSource = options;
            string selected = string.Empty;
            if (!string.IsNullOrWhiteSpace(preferredTypeName))
            {
                selected = options.FirstOrDefault(x => string.Equals(x, preferredTypeName, StringComparison.OrdinalIgnoreCase));
            }

            if (string.IsNullOrWhiteSpace(selected))
            {
                selected = options[0];
            }

            typeCell.Value = selected;
        }

        private List<string> GetTypeOptionsForCategory(MapCategory category)
        {
            switch (category)
            {
                case MapCategory.Doors:
                    return _doorTypeNames.Count > 0 ? _doorTypeNames : new List<string>();
                case MapCategory.Windows:
                    return _windowTypeNames.Count > 0 ? _windowTypeNames : new List<string>();
                case MapCategory.Columns:
                    return _columnTypeNames.Count > 0 ? _columnTypeNames : new List<string>();
                case MapCategory.Beams:
                    return _beamTypeNames.Count > 0 ? _beamTypeNames : new List<string>();
                case MapCategory.Walls:
                case MapCategory.Floors:
                default:
                    return _wallTypeNames.Count > 0 ? _wallTypeNames : new List<string>();
            }
        }

        private void OnGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string name = _gridMapBoard.Columns[e.ColumnIndex].Name;
            if (string.Equals(name, "colDelete", StringComparison.OrdinalIgnoreCase))
            {
                _gridMapBoard.Rows.RemoveAt(e.RowIndex);
                return;
            }

            if (string.Equals(name, "colAdvanced", StringComparison.OrdinalIgnoreCase))
            {
                DataGridViewRow row = _gridMapBoard.Rows[e.RowIndex];
                AdvancedSettingsRow current = row.Tag as AdvancedSettingsRow;
                string categoryText = row.Cells["colCategory"].Value == null ? "Walls" : row.Cells["colCategory"].Value.ToString();
                MapCategory category = ParseCategory(categoryText);
                string rawLayer = row.Cells["colLayer"].Value == null ? string.Empty : row.Cells["colLayer"].Value.ToString();
                using (AdvancedSettingsForm form = new AdvancedSettingsForm(current, _parameterOptions, _levels, category, rawLayer))
                {
                    if (form.ShowDialog() == DialogResult.OK && form.Result != null)
                    {
                        row.Tag = CloneSettings(form.Result);
                    }
                }
            }
        }

        private static AdvancedSettingsRow CloneSettings(AdvancedSettingsRow source)
        {
            AdvancedSettingsRow target = new AdvancedSettingsRow();
            if (source == null)
            {
                return target;
            }

            JunctureSettings juncture = source.Juncture ?? new JunctureSettings();
            target.EnableLayerOverride = source.EnableLayerOverride;
            target.ApplyAsCategoryDefault = source.ApplyAsCategoryDefault;
            target.DoorExpectedWidthMm = source.DoorExpectedWidthMm;
            target.WallMinWallLengthMm = source.WallMinWallLengthMm;
            target.WallThicknessTolMm = source.WallThicknessTolMm;
            target.WallMaxWallThicknessMm = source.WallMaxWallThicknessMm;
            target.WallDefaultSingleWallThicknessMm = source.WallDefaultSingleWallThicknessMm;
            target.WallHeightMm = source.WallHeightMm;
            target.WallBaseOffsetMm = source.WallBaseOffsetMm;
            target.WallParallelAngleTolDeg = source.WallParallelAngleTolDeg;
            target.WallEndpointMergeTolMm = source.WallEndpointMergeTolMm;
            target.WallArcThicknessTolMm = source.WallArcThicknessTolMm;
            target.WallEndpointClusterTolMm = source.WallEndpointClusterTolMm;
            target.WallExtendSearchTolMm = source.WallExtendSearchTolMm;
            target.WallDuplicateTolMm = source.WallDuplicateTolMm;
            target.WallAngleSnapDeg = source.WallAngleSnapDeg;
            target.WallEnableOrthogonalSnap = source.WallEnableOrthogonalSnap;
            target.WallEnableExtendToIntersection = source.WallEnableExtendToIntersection;
            target.WallEnableEndpointClustering = source.WallEnableEndpointClustering;
            target.WallEnableDuplicateRemoval = source.WallEnableDuplicateRemoval;
            target.WallEnableExtendCollinear = source.WallEnableExtendCollinear;
            target.WallEnableMergeCollinear = source.WallEnableMergeCollinear;
            target.WallExtendCollinearTolMm = source.WallExtendCollinearTolMm;
            target.WallCollinearOffsetTolMm = source.WallCollinearOffsetTolMm;
            target.WallExtendProjectionTolMm = source.WallExtendProjectionTolMm;
            target.WallUseDirectionalClustering = source.WallUseDirectionalClustering;
            target.WallEnableAutoDoubleLineThickness = source.WallEnableAutoDoubleLineThickness;
            target.WallAutoThicknessTopK = source.WallAutoThicknessTopK;
            target.WallAutoThicknessBinMm = source.WallAutoThicknessBinMm;
            target.WallMinDoubleLineThicknessMm = source.WallMinDoubleLineThicknessMm;
            target.WallMinDoubleLineOverlapLenMm = source.WallMinDoubleLineOverlapLenMm;
            target.WallForceSingleLineMode = source.WallForceSingleLineMode;
            target.WallDoubleLineSingleWallPlaceMode = source.WallDoubleLineSingleWallPlaceMode;
            target.DoorHeightMm = source.DoorHeightMm;
            target.DoorSillHeightMm = source.DoorSillHeightMm;
            target.BeamMinLengthMm = source.BeamMinLengthMm;
            target.BeamElevationOffsetMm = source.BeamElevationOffsetMm;
            target.BeamEnableMergeCollinear = source.BeamEnableMergeCollinear;
            target.BeamEndpointMergeTolMm = source.BeamEndpointMergeTolMm;
            target.BeamParallelAngleTolDeg = source.BeamParallelAngleTolDeg;
            target.BeamAllowArc = source.BeamAllowArc;
            target.WindowHeightMm = source.WindowHeightMm;
            target.WindowSillHeightMm = source.WindowSillHeightMm;
            target.WindowUseSillPlusHeight = source.WindowUseSillPlusHeight;
            target.ColumnHeightMm = source.ColumnHeightMm;
            target.ColumnClusterAlgorithm = source.ColumnClusterAlgorithm;
            target.ColumnClusterTolMm = source.ColumnClusterTolMm;
            target.ColumnEndpointTolMm = source.ColumnEndpointTolMm;
            target.ColumnGapTolMm = source.ColumnGapTolMm;
            target.ColumnMinGroupSegments = source.ColumnMinGroupSegments;
            target.ColumnMinSizeMm = source.ColumnMinSizeMm;
            target.ColumnMaxSizeMm = source.ColumnMaxSizeMm;
            target.ColumnMinAreaM2 = source.ColumnMinAreaM2;
            target.ColumnMaxAspectRatio = source.ColumnMaxAspectRatio;
            target.ColumnMinFillRatio = source.ColumnMinFillRatio;
            target.ColumnEnableLongLineFilter = source.ColumnEnableLongLineFilter;
            target.ColumnMaxSegmentLengthMm = source.ColumnMaxSegmentLengthMm;
            target.ColumnEnableMerge = source.ColumnEnableMerge;
            target.ColumnMergeTolMm = source.ColumnMergeTolMm;
            target.ColumnMergeStrategy = source.ColumnMergeStrategy;
            target.ColumnDedupePlacedTolMm = source.ColumnDedupePlacedTolMm;
            target.ColumnAreaWeight = source.ColumnAreaWeight;
            target.ColumnSegmentCountWeight = source.ColumnSegmentCountWeight;
            target.ColumnRectnessWeight = source.ColumnRectnessWeight;
            target.ColumnLongLinePenalty = source.ColumnLongLinePenalty;
            target.ColumnIrregularEnable = source.ColumnIrregularEnable;
            target.ColumnIrregularMaxSizeMm = source.ColumnIrregularMaxSizeMm;
            target.ColumnIrregularGapTolMm = source.ColumnIrregularGapTolMm;
            target.ColumnIrregularMinAreaM2 = source.ColumnIrregularMinAreaM2;
            target.ColumnAttachToWallEnable = source.ColumnAttachToWallEnable;
            target.ColumnAttachToWallSnapTolMm = source.ColumnAttachToWallSnapTolMm;
            target.ColumnAttachToWallTarget = source.ColumnAttachToWallTarget;
            target.ColumnAttachToWallAllowOverlap = source.ColumnAttachToWallAllowOverlap;
            target.ColumnDebugDrawCandidates = source.ColumnDebugDrawCandidates;
            target.ColumnDebugDrawClusterId = source.ColumnDebugDrawClusterId;
            target.ColumnDebugDrawRejectReason = source.ColumnDebugDrawRejectReason;
            target.ColumnDebugExportReport = source.ColumnDebugExportReport;
            target.Juncture = new JunctureSettings
            {
                IgnoreSmallerThanMm = juncture.IgnoreSmallerThanMm,
                MinJunctureWidthMm = juncture.MinJunctureWidthMm,
                IgnoreLargerThanMm = juncture.IgnoreLargerThanMm,
                MaxJunctureWidthMm = juncture.MaxJunctureWidthMm
            };

            if (source.ParameterMappings != null)
            {
                foreach (ParameterMapping mapping in source.ParameterMappings)
                {
                    if (mapping == null)
                    {
                        continue;
                    }

                    target.ParameterMappings.Add(new ParameterMapping
                    {
                        ParameterName = mapping.ParameterName,
                        StorageType = mapping.StorageType,
                        Value = mapping.Value
                    });
                }
            }

            return target;
        }

        private static VerticalDimensionSettings CloneVerticalSettings(VerticalDimensionSettings settings)
        {
            if (settings == null)
            {
                return new VerticalDimensionSettings();
            }

            return new VerticalDimensionSettings
            {
                WallHeightMm = settings.WallHeightMm,
                WallBaseOffsetMm = settings.WallBaseOffsetMm,
                DoorHeightMm = settings.DoorHeightMm,
                DoorSillHeightMm = settings.DoorSillHeightMm,
                WindowHeightMm = settings.WindowHeightMm,
                WindowSillHeightMm = settings.WindowSillHeightMm,
                WindowHeadHeightMm = settings.WindowHeadHeightMm,
                PreferSillPlusHeight = settings.PreferSillPlusHeight
            };
        }
    }
}


