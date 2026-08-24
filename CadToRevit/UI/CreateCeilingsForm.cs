using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CadToRevit.UI
{
    public enum CreateCeilingsFormAction
    {
        Cancel,
        Detect,
        PreviewRepair,
        Create
    }

    public enum CeilingGenerationMode
    {
        RoomCircuits,
        OuterBoundary
    }

    public sealed class CreateCeilingsForm : System.Windows.Forms.Form
    {
        private sealed class IdNameOption
        {
            public ElementId Id { get; set; }
            public string Name { get; set; }
            public override string ToString() { return Name ?? string.Empty; }
        }

        private readonly ComboBox _cmbMode = new ComboBox();
        private readonly ComboBox _cmbLevel = new ComboBox();
        private readonly ComboBox _cmbCeilingType = new ComboBox();
        private readonly TextBox _txtHeightMm = new TextBox();
        private readonly TextBox _txtGapTolMm = new TextBox();
        private readonly TextBox _txtMinArea = new TextBox();
        private readonly CheckBox _chkCluster = new CheckBox();
        private readonly CheckBox _chkExtend = new CheckBox();
        private readonly CheckBox _chkBridge = new CheckBox();
        private readonly CheckBox _chkAutoCleanup = new CheckBox();
        private readonly Button _btnDetect = new Button();
        private readonly Button _btnPreview = new Button();
        private readonly Button _btnCreate = new Button();
        private readonly Button _btnCancel = new Button();

        public CreateCeilingsFormAction Action { get; private set; } = CreateCeilingsFormAction.Cancel;
        public CeilingGenerationMode GenerationMode { get; private set; } = CeilingGenerationMode.RoomCircuits;
        public ElementId SelectedLevelId { get; private set; } = ElementId.InvalidElementId;
        public ElementId SelectedCeilingTypeId { get; private set; } = ElementId.InvalidElementId;
        public double CeilingHeightMm { get; private set; } = 2800.0;
        public double GapToleranceMm { get; private set; } = 50.0;
        public double MinAreaM2 { get; private set; } = 1.0;
        public bool EnableCluster { get; private set; } = true;
        public bool EnableExtend { get; private set; } = true;
        public bool EnableBridge { get; private set; } = true;
        public bool AutoCleanupTempLines { get; private set; } = true;

        public CreateCeilingsForm(
            IEnumerable<Level> levels,
            IEnumerable<CeilingType> ceilingTypes,
            CeilingGenerationMode generationMode,
            ElementId selectedLevelId,
            ElementId selectedCeilingTypeId,
            double heightMm,
            double gapToleranceMm,
            double minAreaM2,
            bool enableCluster,
            bool enableExtend,
            bool enableBridge,
            bool autoCleanupTempLines)
        {
            BuildLayout();
            Bind(
                levels,
                ceilingTypes,
                generationMode,
                selectedLevelId,
                selectedCeilingTypeId,
                heightMm,
                gapToleranceMm,
                minAreaM2,
                enableCluster,
                enableExtend,
                enableBridge,
                autoCleanupTempLines);
        }

        private void BuildLayout()
        {
            Text = "Auto Ceiling";
            Width = 560;
            Height = 500;
            MaximizeBox = false;
            MinimizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;

            Label lblMode = new Label { Left = 18, Top = 20, Width = 150, Text = "Generation Mode:" };
            _cmbMode.Left = 170; _cmbMode.Top = 16; _cmbMode.Width = 360; _cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbMode.Items.Add("Room Circuits");
            _cmbMode.Items.Add("Outer Boundary");

            Label lblLevel = new Label { Left = 18, Top = 56, Width = 150, Text = "Level:" };
            _cmbLevel.Left = 170; _cmbLevel.Top = 52; _cmbLevel.Width = 360; _cmbLevel.DropDownStyle = ComboBoxStyle.DropDownList;

            Label lblType = new Label { Left = 18, Top = 92, Width = 150, Text = "Ceiling Type:" };
            _cmbCeilingType.Left = 170; _cmbCeilingType.Top = 88; _cmbCeilingType.Width = 360; _cmbCeilingType.DropDownStyle = ComboBoxStyle.DropDownList;

            Label lblHeight = new Label { Left = 18, Top = 128, Width = 150, Text = "Height (mm):" };
            _txtHeightMm.Left = 170; _txtHeightMm.Top = 124; _txtHeightMm.Width = 120;

            Label lblGap = new Label { Left = 18, Top = 164, Width = 150, Text = "Gap Tol (mm):" };
            _txtGapTolMm.Left = 170; _txtGapTolMm.Top = 160; _txtGapTolMm.Width = 120;

            Label lblArea = new Label { Left = 18, Top = 200, Width = 150, Text = "Min Area (m2):" };
            _txtMinArea.Left = 170; _txtMinArea.Top = 196; _txtMinArea.Width = 120;

            GroupBox grp = new GroupBox
            {
                Left = 18,
                Top = 236,
                Width = 512,
                Height = 120,
                Text = "Gap Preview Options"
            };
            _chkCluster.Left = 16; _chkCluster.Top = 28; _chkCluster.Width = 220; _chkCluster.Text = "Endpoint Clustering";
            _chkExtend.Left = 256; _chkExtend.Top = 28; _chkExtend.Width = 220; _chkExtend.Text = "Extend To Intersection";
            _chkBridge.Left = 16; _chkBridge.Top = 60; _chkBridge.Width = 220; _chkBridge.Text = "Gap Bridging";
            _chkAutoCleanup.Left = 256; _chkAutoCleanup.Top = 60; _chkAutoCleanup.Width = 240; _chkAutoCleanup.Text = "Auto Cleanup Temp Lines";
            grp.Controls.Add(_chkCluster);
            grp.Controls.Add(_chkExtend);
            grp.Controls.Add(_chkBridge);
            grp.Controls.Add(_chkAutoCleanup);

            _btnDetect.Text = "Detect";
            _btnDetect.Left = 18; _btnDetect.Top = 378; _btnDetect.Width = 110;
            _btnDetect.Click += (s, e) => Submit(CreateCeilingsFormAction.Detect);

            _btnPreview.Text = "Preview";
            _btnPreview.Left = 140; _btnPreview.Top = 378; _btnPreview.Width = 150;
            _btnPreview.Click += (s, e) => Submit(CreateCeilingsFormAction.PreviewRepair);

            _btnCreate.Text = "Create";
            _btnCreate.Left = 302; _btnCreate.Top = 378; _btnCreate.Width = 110;
            _btnCreate.Click += (s, e) => Submit(CreateCeilingsFormAction.Create);

            _btnCancel.Text = "Close";
            _btnCancel.Left = 420; _btnCancel.Top = 378; _btnCancel.Width = 110;
            _btnCancel.Click += (s, e) =>
            {
                Action = CreateCeilingsFormAction.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(lblMode);
            Controls.Add(_cmbMode);
            Controls.Add(lblLevel);
            Controls.Add(_cmbLevel);
            Controls.Add(lblType);
            Controls.Add(_cmbCeilingType);
            Controls.Add(lblHeight);
            Controls.Add(_txtHeightMm);
            Controls.Add(lblGap);
            Controls.Add(_txtGapTolMm);
            Controls.Add(lblArea);
            Controls.Add(_txtMinArea);
            Controls.Add(grp);
            Controls.Add(_btnDetect);
            Controls.Add(_btnPreview);
            Controls.Add(_btnCreate);
            Controls.Add(_btnCancel);
        }

        private void Bind(
            IEnumerable<Level> levels,
            IEnumerable<CeilingType> ceilingTypes,
            CeilingGenerationMode generationMode,
            ElementId selectedLevelId,
            ElementId selectedCeilingTypeId,
            double heightMm,
            double gapToleranceMm,
            double minAreaM2,
            bool enableCluster,
            bool enableExtend,
            bool enableBridge,
            bool autoCleanupTempLines)
        {
            _cmbMode.SelectedIndex = generationMode == CeilingGenerationMode.OuterBoundary ? 1 : 0;

            List<IdNameOption> levelOptions = (levels ?? new List<Level>())
                .Select(x => new IdNameOption { Id = x.Id, Name = x.Name })
                .OrderBy(x => x.Name)
                .ToList();
            _cmbLevel.Items.AddRange(levelOptions.Cast<object>().ToArray());
            SelectById(_cmbLevel, selectedLevelId);
            if (_cmbLevel.SelectedIndex < 0 && _cmbLevel.Items.Count > 0) _cmbLevel.SelectedIndex = 0;

            List<IdNameOption> typeOptions = (ceilingTypes ?? new List<CeilingType>())
                .Select(x => new IdNameOption { Id = x.Id, Name = x.Name })
                .OrderBy(x => x.Name)
                .ToList();
            _cmbCeilingType.Items.AddRange(typeOptions.Cast<object>().ToArray());
            SelectById(_cmbCeilingType, selectedCeilingTypeId);
            if (_cmbCeilingType.SelectedIndex < 0 && _cmbCeilingType.Items.Count > 0) _cmbCeilingType.SelectedIndex = 0;

            _txtHeightMm.Text = heightMm.ToString("F0");
            _txtGapTolMm.Text = gapToleranceMm.ToString("F0");
            _txtMinArea.Text = minAreaM2.ToString("F2");
            _chkCluster.Checked = enableCluster;
            _chkExtend.Checked = enableExtend;
            _chkBridge.Checked = enableBridge;
            _chkAutoCleanup.Checked = autoCleanupTempLines;
        }

        private static void SelectById(ComboBox combo, ElementId id)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                IdNameOption option = combo.Items[i] as IdNameOption;
                if (option != null && id != null && option.Id != null && option.Id.IntegerValue == id.IntegerValue)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void Submit(CreateCeilingsFormAction action)
        {
            IdNameOption level = _cmbLevel.SelectedItem as IdNameOption;
            IdNameOption type = _cmbCeilingType.SelectedItem as IdNameOption;
            double height;
            double gap;
            double minArea;
            if (level == null || type == null ||
                !double.TryParse(_txtHeightMm.Text, out height) ||
                !double.TryParse(_txtGapTolMm.Text, out gap) ||
                !double.TryParse(_txtMinArea.Text, out minArea) ||
                height <= 0 || gap <= 0 || minArea < 0)
            {
                MessageBox.Show(this, "Invalid input.", "CadToRevit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GenerationMode = _cmbMode.SelectedIndex == 1 ? CeilingGenerationMode.OuterBoundary : CeilingGenerationMode.RoomCircuits;
            SelectedLevelId = level.Id;
            SelectedCeilingTypeId = type.Id;
            CeilingHeightMm = height;
            GapToleranceMm = gap;
            MinAreaM2 = minArea;
            EnableCluster = _chkCluster.Checked;
            EnableExtend = _chkExtend.Checked;
            EnableBridge = _chkBridge.Checked;
            AutoCleanupTempLines = _chkAutoCleanup.Checked;
            Action = action;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
