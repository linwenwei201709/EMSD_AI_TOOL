using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using WinForms = System.Windows.Forms;

namespace CadToRevit.Infrastructure.UI
{
    /// <summary>
    /// Unified UI message API for TaskDialog and MessageBox.
    /// </summary>
    public static class UiMessageService
    {
        public static void Info(string titleKey, string messageKey, params object[] args)
        {
            ShowMessageBox(titleKey, messageKey, WinForms.MessageBoxIcon.Information, args);
        }

        public static void Warning(string titleKey, string messageKey, params object[] args)
        {
            ShowMessageBox(titleKey, messageKey, WinForms.MessageBoxIcon.Warning, args);
        }

        public static void Error(string titleKey, string messageKey, params object[] args)
        {
            ShowMessageBox(titleKey, messageKey, WinForms.MessageBoxIcon.Error, args);
        }

        public static TaskDialogResult ShowTaskDialog(string titleKey, string messageKey, params object[] args)
        {
            string title = Loc.T(titleKey);
            string message = Loc.T(messageKey, args);
            return TaskDialog.Show(title, message);
        }

        public static TaskDialogResult ShowTaskDialogText(string titleKey, string messageText)
        {
            string title = Loc.T(titleKey);
            return TaskDialog.Show(title, messageText ?? string.Empty);
        }

        private static void ShowMessageBox(string titleKey, string messageKey, WinForms.MessageBoxIcon icon, params object[] args)
        {
            string title = Loc.T(titleKey);
            string message = Loc.T(messageKey, args);
            WinForms.MessageBox.Show(message, title, WinForms.MessageBoxButtons.OK, icon);
        }
    }
}
