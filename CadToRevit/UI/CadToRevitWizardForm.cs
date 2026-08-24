using CadToRevit.Models.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace CadToRevit.UI
{
    public enum WizardAction
    {
        Cancel,
        Preview,
        CreateWalls
    }

    public partial class CadToRevitWizardForm : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly ComboBox _unitCombo = new ComboBox();
        private readonly Label _hint = new Label();
        private readonly Button _previewBtn = new Button();
        private readonly Button _createBtn = new Button();
        private readonly Button _cancelBtn = new Button();

        public WizardAction Action { get; private set; } = WizardAction.Cancel;

        public SourceUnit SelectedUnit
        {
            get
            {
                object value = _unitCombo.SelectedItem;
                if (value is SourceUnit)
                {
                    return (SourceUnit)value;
                }

                return SourceUnit.Auto;
            }
        }

        public HashSet<string> SelectedRawLayers
        {
            get
            {
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewRow row in _grid.Rows)
                {
                    bool use = false;
                    if (row.Cells[0].Value is bool)
                    {
                        use = (bool)row.Cells[0].Value;
                    }

                    if (!use)
                    {
                        continue;
                    }

                    string rawLayer = row.Cells[1].Value == null ? "" : row.Cells[1].Value.ToString();
                    if (!string.IsNullOrWhiteSpace(rawLayer))
                    {
                        set.Add(rawLayer);
                    }
                }

                return set;
            }
        }

        public CadToRevitWizardForm(
            IEnumerable<string> rawLayers,
            SourceUnit selectedUnit,
            ISet<string> selectedLayers)
        {
            Text = "CadToRevit MVP-1";
            Width = 980;
            Height = 620;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            _hint.Left = 12;
            _hint.Top = 12;
            _hint.Width = 940;
            _hint.Text = "Please manually select CAD layers for preview or creation.";
            Controls.Add(_hint);

            Label unitLabel = new Label();
            unitLabel.Left = 12;
            unitLabel.Top = 40;
            unitLabel.Text = "Source Unit:";
            unitLabel.Width = 120;
            Controls.Add(unitLabel);

            _unitCombo.Left = 130;
            _unitCombo.Top = 36;
            _unitCombo.Width = 180;
            _unitCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _unitCombo.Items.Add(SourceUnit.Auto);
            _unitCombo.Items.Add(SourceUnit.Feet);
            _unitCombo.Items.Add(SourceUnit.Inch);
            _unitCombo.Items.Add(SourceUnit.Millimeter);
            _unitCombo.Items.Add(SourceUnit.Meter);
            _unitCombo.SelectedItem = selectedUnit;
            Controls.Add(_unitCombo);

            _grid.Left = 12;
            _grid.Top = 70;
            _grid.Width = 940;
            _grid.Height = 460;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Use", Width = 50 });
            _grid.Columns.Add("RawLayer", "RawLayer");
            Controls.Add(_grid);

            foreach (string rawLayer in (rawLayers ?? Enumerable.Empty<string>()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                bool use = selectedLayers != null && selectedLayers.Contains(rawLayer);
                _grid.Rows.Add(use, rawLayer);
            }

            _previewBtn.Text = "Preview";
            _previewBtn.Left = 550;
            _previewBtn.Top = 540;
            _previewBtn.Width = 120;
            _previewBtn.Click += (s, e) =>
            {
                Action = WizardAction.Preview;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_previewBtn);

            _createBtn.Text = "Create Walls";
            _createBtn.Left = 680;
            _createBtn.Top = 540;
            _createBtn.Width = 130;
            _createBtn.Click += (s, e) =>
            {
                if (SelectedRawLayers.Count > 3)
                {
                    MessageBox.Show(
                        "You selected more than 3 layers. This may include non-wall layers and cause overlap/disconnection.",
                        "Layer Selection",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                Action = WizardAction.CreateWalls;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_createBtn);

            _cancelBtn.Text = "Cancel";
            _cancelBtn.Left = 820;
            _cancelBtn.Top = 540;
            _cancelBtn.Width = 130;
            _cancelBtn.Click += (s, e) =>
            {
                Action = WizardAction.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(_cancelBtn);
        }
    }
}
