using CadToRevit.Infrastructure.Localization;
using System;
using System.Windows.Forms;

namespace CadToRevit.UI
{
    public sealed class GenerationProgressForm : Form
    {
        private readonly Label _lblStage = new Label();
        private readonly Label _lblDetail = new Label();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly Button _btnCancel = new Button();

        public bool IsCancellationRequested { get; private set; }

        public GenerationProgressForm()
        {
            Text = Loc.T("Progress.Title");
            Width = 560;
            Height = 220;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ControlBox = false;

            _lblStage.Left = 16;
            _lblStage.Top = 16;
            _lblStage.Width = 510;
            _lblStage.Text = Loc.T("Progress.Ready");

            _bar.Left = 16;
            _bar.Top = 46;
            _bar.Width = 510;
            _bar.Height = 24;
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            _bar.Value = 0;

            _lblDetail.Left = 16;
            _lblDetail.Top = 80;
            _lblDetail.Width = 510;
            _lblDetail.Text = string.Empty;

            _btnCancel.Left = 430;
            _btnCancel.Top = 108;
            _btnCancel.Width = 96;
            _btnCancel.Height = 28;
            _btnCancel.Text = Loc.T("Progress.Cancel");
            _btnCancel.Click += (s, e) =>
            {
                // Set a cancel flag and let caller stop safely.
                IsCancellationRequested = true;
                _btnCancel.Enabled = false;
                _btnCancel.Text = Loc.T("Progress.Cancelling");
            };

            Controls.Add(_lblStage);
            Controls.Add(_bar);
            Controls.Add(_lblDetail);
            Controls.Add(_btnCancel);
        }

        public void UpdateProgress(string stage, int current, int total, string detail)
        {
            _lblStage.Text = stage ?? string.Empty;
            _lblDetail.Text = detail ?? string.Empty;
            int safeTotal = total <= 0 ? 1 : total;
            int safeCurrent = current < 0 ? 0 : Math.Min(current, safeTotal);
            _bar.Value = (int)Math.Round((safeCurrent * 100.0) / safeTotal);
            Refresh();
            Application.DoEvents();
        }
    }
}
