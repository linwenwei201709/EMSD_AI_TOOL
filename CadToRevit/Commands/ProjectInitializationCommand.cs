using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Infrastructure.UI;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using CadToRevit.Services.Dwg;
using CadToRevit.UI.Common;
using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Linq;
using WinForms = System.Windows.Forms;

namespace CadToRevit.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class ProjectInitializationCommand : IExternalCommand
    {
        private const string ProjectInitUrl = "http://127.0.0.1:8000/api/init";
        private static readonly Guid ProjectInitSessionSchemaGuid = new Guid("D7EA2D24-5E2F-4A3F-8A2C-B10A8FC27D65");
        private const string ProjectInitSessionSchemaName = "CadToRevitProjectInitSession";
        private const string ProjectInitSessionVendorId = "EMSD";
        private const string SessionIdFieldName = "session_id";
        private const string ProjectIdFieldName = "project_id";
        private const string LastInitUtcFieldName = "last_init_utc";
        private const string SaveSessionSuccessMessage = "Saved session_id for current Revit session.";
        private const string ParseResponseErrorMessage = "Project initialization response could not be parsed.";
        private const string MissingSessionIdErrorMessage = "Initialization succeeded but no session_id was returned.";
        private const string SaveSessionFailedErrorMessage = "session_id was returned, but saving it to the current Revit session failed.";
        private const string FriendlySuccessMessage = "Route planner initialized successfully. You can now define start and target points to generate the delivery route.";
        private static ProjectInitRuntimeState _lastRuntimeState;

        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            UIDocument uiDoc = commandData.Application.ActiveUIDocument;
            Document doc = uiDoc == null ? null : uiDoc.Document;

            ProjectInitRuntimeState cachedState = _lastRuntimeState;
            string initialDwgFilePath = cachedState != null && !string.IsNullOrWhiteSpace(cachedState.DwgFilePath)
                ? cachedState.DwgFilePath
                : ResolveCurrentDwgPath(uiDoc, doc);

            using (ProjectInitializationForm form = new ProjectInitializationForm(
                ResolveProjectIdentifier(cachedState),
                initialDwgFilePath,
                cachedState == null ? null : cachedState.IfcFilePath,
                cachedState == null ? null : cachedState.HeadroomHeightText,
                cachedState == null ? null : cachedState.DoorWidthToleranceMmText))
            {
                if (form.ShowDialog() != WinForms.DialogResult.OK)
                {
                    return Result.Cancelled;
                }

                string requestJson = form.BuildRequestJson();
                string responseText;
                BusyProgressWindow progress = null;
                try
                {
                    progress = BusyProgressWindow.Show(
                        commandData.Application,
                        "EMSD AI Tool",
                        "Initializing project..." + Environment.NewLine + "Please wait.");

                    responseText = PostProjectInitialization(requestJson);
                }
                catch (Exception ex)
                {
                    if (progress != null)
                    {
                        progress.Dispose();
                        progress = null;
                    }

                    ShowErrorDialog(commandData.Application, ex.Message);
                    return Result.Failed;
                }
                finally
                {
                    if (progress != null)
                    {
                        progress.Dispose();
                    }
                }

                string status;
                string sessionId;
                if (!TryParseInitResponse(responseText, out status, out sessionId))
                {
                    ShowErrorDialog(commandData.Application, ParseResponseErrorMessage);
                    return Result.Failed;
                }
                if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        sessionId = form.ProjectIdentifierText;
                    }
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        ShowErrorDialog(commandData.Application, MissingSessionIdErrorMessage);
                        return Result.Failed;
                    }
                    try
                    {
                        SaveSessionToRuntime(form, sessionId);
                    }
                    catch (Exception ex)
                    {
                        ShowErrorDialog(commandData.Application, SaveSessionFailedErrorMessage + Environment.NewLine + ex.Message);
                        return Result.Failed;
                    }
                    ShowResponseDialog(commandData.Application, responseText, SaveSessionSuccessMessage, true);
                    return Result.Succeeded;
                }
                ShowResponseDialog(commandData.Application, responseText, null, false);
                return Result.Succeeded;
            }
        }

        private static string PostProjectInitialization(string requestJson)
        {
            using (HttpClient client = new HttpClient())
            using (StringContent content = new StringContent(requestJson, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = client.PostAsync(ProjectInitUrl, content).GetAwaiter().GetResult())
            {
                string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(
                        "HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                        ": " + responseText);
                }

                return string.IsNullOrWhiteSpace(responseText) ? "(empty)" : responseText;
            }
        }

        private static bool TryParseInitResponse(string responseText, out string status, out string sessionId)
        {
            status = null;
            sessionId = null;
            if (string.IsNullOrWhiteSpace(responseText) || string.Equals(responseText, "(empty)", StringComparison.Ordinal))
            {
                return false;
            }
            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(ProjectInitializationResponseDto));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(responseText)))
                {
                    ProjectInitializationResponseDto payload = serializer.ReadObject(stream) as ProjectInitializationResponseDto;
                    if (payload == null)
                    {
                        return false;
                    }
                    status = string.IsNullOrWhiteSpace(payload.Status) ? null : payload.Status.Trim();
                    sessionId = string.IsNullOrWhiteSpace(payload.SessionId) ? null : payload.SessionId.Trim();
                    return !string.IsNullOrWhiteSpace(status);
                }
            }
            catch
            {
                return false;
            }
        }
        public static bool TryGetSavedSessionId(Document doc, out string sessionId)
        {
            sessionId = null;
            ProjectInitRuntimeState state = _lastRuntimeState;
            if (state == null || string.IsNullOrWhiteSpace(state.SessionId))
            {
                return false;
            }

            sessionId = state.SessionId.Trim();
            return true;
        }

        public static void SaveRuntimeSession(
            string sessionId,
            string ifcFilePath,
            string dwgFilePath,
            string headroomHeightText,
            string doorWidthToleranceMmText)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            _lastRuntimeState = new ProjectInitRuntimeState
            {
                SessionId = sessionId.Trim(),
                IfcFilePath = ifcFilePath ?? string.Empty,
                DwgFilePath = dwgFilePath ?? string.Empty,
                HeadroomHeightText = string.IsNullOrWhiteSpace(headroomHeightText) ? "2200" : headroomHeightText,
                DoorWidthToleranceMmText = string.IsNullOrWhiteSpace(doorWidthToleranceMmText) ? "0" : doorWidthToleranceMmText,
                LastInitUtc = DateTime.UtcNow
            };
        }

        private static void SaveSessionToRuntime(ProjectInitializationForm form, string sessionId)
        {
            if (form == null)
            {
                throw new InvalidOperationException("Project initialization form is unavailable.");
            }
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("session_id is empty.");
            }

            _lastRuntimeState = new ProjectInitRuntimeState
            {
                SessionId = sessionId.Trim(),
                IfcFilePath = form.IfcFilePathText,
                DwgFilePath = form.DwgFilePathText,
                HeadroomHeightText = form.HeadroomHeightText,
                DoorWidthToleranceMmText = form.DoorWidthToleranceMmText,
                LastInitUtc = DateTime.UtcNow
            };
        }

        private static void SaveSessionToDocument(Document doc, string projectId, string sessionId)
        {
            if (doc == null)
            {
                throw new InvalidOperationException("No active Revit document is available.");
            }
            if (doc.ProjectInformation == null)
            {
                throw new InvalidOperationException("The current Revit document does not expose Project Information.");
            }
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("session_id is empty.");
            }
            Schema schema = EnsureProjectInitSessionSchema();
            Field sessionField = schema.GetField(SessionIdFieldName);
            Field projectField = schema.GetField(ProjectIdFieldName);
            Field lastInitField = schema.GetField(LastInitUtcFieldName);
            if (sessionField == null || projectField == null || lastInitField == null)
            {
                throw new InvalidOperationException("Project init session schema fields are unavailable.");
            }
            using (Transaction tx = new Transaction(doc, "Save Project Init Session"))
            {
                tx.Start();
                Entity entity = new Entity(schema);
                entity.Set(sessionField, sessionId.Trim());
                entity.Set(projectField, (projectId ?? string.Empty).Trim());
                entity.Set(lastInitField, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                doc.ProjectInformation.SetEntity(entity);
                tx.Commit();
            }
        }
        private static Schema EnsureProjectInitSessionSchema()
        {
            Schema schema = Schema.Lookup(ProjectInitSessionSchemaGuid);
            if (schema != null)
            {
                return schema;
            }
            SchemaBuilder builder = new SchemaBuilder(ProjectInitSessionSchemaGuid);
            builder.SetSchemaName(ProjectInitSessionSchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId(ProjectInitSessionVendorId);
            builder.AddSimpleField(SessionIdFieldName, typeof(string));
            builder.AddSimpleField(ProjectIdFieldName, typeof(string));
            builder.AddSimpleField(LastInitUtcFieldName, typeof(string));
            return builder.Finish();
        }
        private static void ShowResponseDialog(UIApplication uiApp, string responseText, string additionalMessage, bool success)
        {
            string message = success
                ? FriendlySuccessMessage
                : BuildInitResultDisplayText(responseText, additionalMessage);
            if (success)
            {
                DiagnosticRecorder.AppendDebug("[ProjectInit] response=" + (responseText ?? string.Empty));
                if (!string.IsNullOrWhiteSpace(additionalMessage))
                {
                    DiagnosticRecorder.AppendDebug("[ProjectInit] " + additionalMessage.Trim());
                }
                LocalizedDialogService.Success(uiApp, message);
                return;
            }

            LocalizedDialogService.Error(uiApp, message);
        }

        private static void ShowErrorDialog(UIApplication uiApp, string errorText)
        {
            string message = Loc.T("Dialog.ProjectInit.ErrorSummary") + Environment.NewLine + Environment.NewLine + (errorText ?? string.Empty);
            LocalizedDialogService.Error(uiApp, message);
        }

        private static string BuildInitResultDisplayText(string responseText, string additionalMessage = null)
        {
            string[] propertyNames = new[]
            {
                "status",
                "session_id",
                "map_bounds",
                "grid_shape",
                "physical_size",
                "resolution_mm"
            };

            StringBuilder builder = new StringBuilder();
            foreach (string propertyName in propertyNames)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(propertyName);
                builder.Append(": ");
                builder.Append(ExtractTopLevelJsonValue(responseText, propertyName) ?? "(missing)");
            }

            if (!string.IsNullOrWhiteSpace(additionalMessage))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append(additionalMessage.Trim());
            }
            return builder.ToString();
        }

        private static string ExtractTopLevelJsonValue(string json, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            string propertyToken = "\"" + propertyName + "\"";
            int propertyIndex = json.IndexOf(propertyToken, StringComparison.Ordinal);
            if (propertyIndex < 0)
            {
                return null;
            }

            int colonIndex = json.IndexOf(':', propertyIndex + propertyToken.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            int valueStart = SkipJsonWhitespace(json, colonIndex + 1);
            if (valueStart >= json.Length)
            {
                return null;
            }

            int valueEnd = FindJsonValueEnd(json, valueStart);
            if (valueEnd <= valueStart)
            {
                return null;
            }

            return json.Substring(valueStart, valueEnd - valueStart).Trim();
        }

        private static int SkipJsonWhitespace(string text, int startIndex)
        {
            int index = startIndex;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            return index;
        }

        private static int FindJsonValueEnd(string json, int startIndex)
        {
            char firstChar = json[startIndex];
            if (firstChar == '"')
            {
                return FindJsonStringEnd(json, startIndex);
            }

            if (firstChar == '{' || firstChar == '[')
            {
                return FindJsonCompositeEnd(json, startIndex);
            }

            int index = startIndex;
            while (index < json.Length)
            {
                char current = json[index];
                if (current == ',' || current == '}' || current == ']')
                {
                    break;
                }

                index++;
            }

            return index;
        }

        private static int FindJsonStringEnd(string json, int startIndex)
        {
            bool escaped = false;
            for (int index = startIndex + 1; index < json.Length; index++)
            {
                char current = json[index];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    return index + 1;
                }
            }

            return json.Length;
        }

        private static int FindJsonCompositeEnd(string json, int startIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int index = startIndex; index < json.Length; index++)
            {
                char current = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{' || current == '[')
                {
                    depth++;
                    continue;
                }

                if (current == '}' || current == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return index + 1;
                    }
                }
            }

            return json.Length;
        }

        private static string ResolveProjectName(Document doc)
        {
            if (doc == null)
            {
                return string.Empty;
            }

            string projectName = null;
            try
            {
                projectName = doc.ProjectInformation == null ? null : doc.ProjectInformation.Name;
            }
            catch
            {
                projectName = null;
            }

            if (!string.IsNullOrWhiteSpace(projectName))
            {
                return projectName.Trim();
            }

            return (doc.Title ?? string.Empty).Trim();
        }

        private static string ResolveProjectIdentifier(ProjectInitRuntimeState cachedState)
        {
            if (cachedState != null && !string.IsNullOrWhiteSpace(cachedState.SessionId))
            {
                return cachedState.SessionId.Trim();
            }

            // Use a 32-character GUID string without separators.
            return Guid.NewGuid().ToString("N");
        }

        private static string ResolveCurrentDwgPath(UIDocument uiDoc, Document doc)
        {
            ImportInstance importInstance = GetSelectedImportInstance(uiDoc) ?? GetFirstImportInstance(doc);
            if (importInstance == null)
            {
                return string.Empty;
            }

            return DwgPathResolver.TryGetDwgPath(doc, importInstance) ?? string.Empty;
        }

        private static ImportInstance GetSelectedImportInstance(UIDocument uiDoc)
        {
            if (uiDoc == null)
            {
                return null;
            }

            foreach (ElementId id in uiDoc.Selection.GetElementIds())
            {
                ImportInstance instance = uiDoc.Document.GetElement(id) as ImportInstance;
                if (instance != null)
                {
                    return instance;
                }
            }

            return null;
        }

        private static ImportInstance GetFirstImportInstance(Document doc)
        {
            if (doc == null)
            {
                return null;
            }

            return DwgImportService.GetLinkedImportInstances(doc).FirstOrDefault();
        }

        private sealed class ProjectInitRuntimeState
        {
            public string SessionId { get; set; }
            public string IfcFilePath { get; set; }
            public string DwgFilePath { get; set; }
            public string HeadroomHeightText { get; set; }
            public string DoorWidthToleranceMmText { get; set; }
            public DateTime LastInitUtc { get; set; }
        }

        [DataContract]
        private sealed class ProjectInitializationResponseDto
        {
            [DataMember(Name = "status")]
            public string Status { get; set; }
            [DataMember(Name = "session_id")]
            public string SessionId { get; set; }
        }
        private sealed class ProjectInitializationForm : WinForms.Form
        {
            private readonly WinForms.TextBox _headroomTextBox = new WinForms.TextBox();
            private readonly WinForms.TextBox _doorWidthToleranceTextBox = new WinForms.TextBox();
            private readonly WinForms.TextBox _dwgFilePathTextBox = new WinForms.TextBox();
            private readonly WinForms.TextBox _ifcFilePathTextBox = new WinForms.TextBox();
            private readonly WinForms.Button _browseIfcButton = new WinForms.Button();
            private readonly WinForms.TextBox _projectIdentifierTextBox = new WinForms.TextBox();
            private readonly WinForms.Button _okButton = new WinForms.Button();
            private readonly WinForms.Button _closeButton = new WinForms.Button();

            internal ProjectInitializationForm(string projectIdentifier, string dwgFilePath, string ifcFilePath, string headroomHeightText, string doorWidthToleranceText)
            {
                Text = Loc.T("Dialog.ProjectInit.Title");
                StartPosition = WinForms.FormStartPosition.CenterScreen;
                FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                AutoScaleMode = WinForms.AutoScaleMode.Dpi;
                ClientSize = new System.Drawing.Size(760, 470);
                Padding = new WinForms.Padding(18, 16, 18, 16);

                SuspendLayout();

                int labelLeft = 22;
                int inputLeft = 22;
                int inputWidth = ClientSize.Width - 44;
                int browseButtonWidth = 96;
                int browseButtonGap = 10;
                int pathInputWidth = inputWidth - browseButtonWidth - browseButtonGap;
                int top = 24;
                int inputTopOffset = 30;
                int rowGap = 66;
                int inputHeight = 30;
                int buttonWidth = 96;
                int buttonHeight = 34;
                int buttonTop = ClientSize.Height - buttonHeight - 18;

                WinForms.Label headroomLabel = CreateLabel(Loc.T("Dialog.ProjectInit.HeadroomHeight"), labelLeft, top);
                ConfigureTextBox(_headroomTextBox, inputLeft, top + inputTopOffset, inputWidth, inputHeight);
                _headroomTextBox.Text = string.IsNullOrWhiteSpace(headroomHeightText) ? "2200" : headroomHeightText.Trim();
                _headroomTextBox.Font = new System.Drawing.Font(_headroomTextBox.Font.FontFamily, 12F);

                top += rowGap;
                WinForms.Label toleranceLabel = CreateLabel(Loc.T("Dialog.ProjectInit.DoorWidthToleranceMm"), labelLeft, top);
                ConfigureTextBox(_doorWidthToleranceTextBox, inputLeft, top + inputTopOffset, inputWidth, inputHeight);
                _doorWidthToleranceTextBox.Text = string.IsNullOrWhiteSpace(doorWidthToleranceText) ? "0" : doorWidthToleranceText.Trim();
                _doorWidthToleranceTextBox.Font = new System.Drawing.Font(_doorWidthToleranceTextBox.Font.FontFamily, 12F);

                top += rowGap;
                WinForms.Label dwgPathLabel = CreateLabel(Loc.T("Dialog.ProjectInit.DwgFilePath"), labelLeft, top);
                ConfigureTextBox(_dwgFilePathTextBox, inputLeft, top + inputTopOffset, inputWidth, inputHeight);
                _dwgFilePathTextBox.Text = dwgFilePath ?? string.Empty;
                _dwgFilePathTextBox.ReadOnly = true;

                top += rowGap;
                WinForms.Label ifcPathLabel = CreateLabel("IFC File Path", labelLeft, top);
                ConfigureTextBox(_ifcFilePathTextBox, inputLeft, top + inputTopOffset, pathInputWidth, inputHeight);
                _ifcFilePathTextBox.Text = ifcFilePath ?? string.Empty;
                _ifcFilePathTextBox.ReadOnly = false;

                _browseIfcButton.Text = "Browse...";
                _browseIfcButton.Left = inputLeft + pathInputWidth + browseButtonGap;
                _browseIfcButton.Top = top + inputTopOffset - 1;
                _browseIfcButton.Width = browseButtonWidth;
                _browseIfcButton.Height = inputHeight + 2;
                _browseIfcButton.Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Right;
                _browseIfcButton.Click += OnBrowseIfcClick;

                top += rowGap;
                WinForms.Label projectIdentifierLabel = CreateLabel(Loc.T("Dialog.ProjectInit.ProjectIdentifier"), labelLeft, top);
                ConfigureTextBox(_projectIdentifierTextBox, inputLeft, top + inputTopOffset, inputWidth, inputHeight);
                _projectIdentifierTextBox.Text = projectIdentifier ?? string.Empty;

                _okButton.Text = Loc.T("Dialog.ProjectInit.Ok");
                _okButton.Left = ClientSize.Width - buttonWidth * 2 - 16;
                _okButton.Top = buttonTop;
                _okButton.Width = buttonWidth;
                _okButton.Height = buttonHeight;
                _okButton.Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right;
                _okButton.DialogResult = WinForms.DialogResult.OK;
                _okButton.Click += OnConfirmClick;

                _closeButton.Text = Loc.T("Dialog.ProjectInit.Close");
                _closeButton.Left = ClientSize.Width - buttonWidth - 10;
                _closeButton.Top = buttonTop;
                _closeButton.Width = buttonWidth;
                _closeButton.Height = buttonHeight;
                _closeButton.Anchor = WinForms.AnchorStyles.Bottom | WinForms.AnchorStyles.Right;
                _closeButton.DialogResult = WinForms.DialogResult.Cancel;

                AcceptButton = _okButton;
                CancelButton = _closeButton;

                Controls.Add(headroomLabel);
                Controls.Add(_headroomTextBox);
                Controls.Add(toleranceLabel);
                Controls.Add(_doorWidthToleranceTextBox);
                Controls.Add(dwgPathLabel);
                Controls.Add(_dwgFilePathTextBox);
                Controls.Add(ifcPathLabel);
                Controls.Add(_ifcFilePathTextBox);
                Controls.Add(_browseIfcButton);
                Controls.Add(projectIdentifierLabel);
                Controls.Add(_projectIdentifierTextBox);
                Controls.Add(_okButton);
                Controls.Add(_closeButton);

                ResumeLayout(false);
                PerformLayout();
            }

            private static void ConfigureTextBox(WinForms.TextBox textBox, int left, int top, int width, int height)
            {
                textBox.Left = left;
                textBox.Top = top;
                textBox.Width = width;
                textBox.Height = height;
                textBox.Margin = new WinForms.Padding(0);
                textBox.Anchor = WinForms.AnchorStyles.Top | WinForms.AnchorStyles.Left | WinForms.AnchorStyles.Right;
            }

            private static WinForms.Label CreateLabel(string text, int left, int top)
            {
                WinForms.Label label = new WinForms.Label();
                label.AutoSize = true;
                label.Left = left;
                label.Top = top;
                label.Margin = new WinForms.Padding(0);
                label.Font = new System.Drawing.Font(label.Font, System.Drawing.FontStyle.Regular);
                label.Text = text;
                return label;
            }

            internal string HeadroomHeightText
            {
                get { return _headroomTextBox.Text.Trim(); }
            }

            internal string DoorWidthToleranceMmText
            {
                get { return _doorWidthToleranceTextBox.Text.Trim(); }
            }

            internal string DwgFilePathText
            {
                get { return _dwgFilePathTextBox.Text.Trim(); }
            }

            internal string IfcFilePathText
            {
                get { return _ifcFilePathTextBox.Text.Trim(); }
            }

            internal string ProjectIdentifierText
            {
                get { return _projectIdentifierTextBox.Text.Trim(); }
            }

            internal string BuildRequestJson()
            {
                return "{" +
                       "\"ifc_file_path\":\"" + EscapeJson(IfcFilePathText) + "\"," +
                       "\"session_id\":\"" + EscapeJson(ProjectIdentifierText) + "\"" +
                       "}";
            }

            private static string EscapeJson(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }

                return value
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");
            }

            private void OnBrowseIfcClick(object sender, EventArgs e)
            {
                using (WinForms.OpenFileDialog dialog = new WinForms.OpenFileDialog())
                {
                    dialog.Title = "Select IFC File";
                    dialog.Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*";
                    dialog.CheckFileExists = true;
                    dialog.CheckPathExists = true;
                    dialog.Multiselect = false;
                    string currentPath = IfcFilePathText;
                    if (!string.IsNullOrWhiteSpace(currentPath))
                    {
                        try
                        {
                            string currentDirectory = Path.GetDirectoryName(currentPath);
                            if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
                            {
                                dialog.InitialDirectory = currentDirectory;
                            }
                        }
                        catch
                        {
                            // Keep OpenFileDialog default directory.
                        }
                    }

                    if (dialog.ShowDialog(this) == WinForms.DialogResult.OK)
                    {
                        _ifcFilePathTextBox.Text = dialog.FileName ?? string.Empty;
                    }
                }
            }

            private void OnConfirmClick(object sender, EventArgs e)
            {
                double headroomHeight;
                bool isValid = double.TryParse(
                    HeadroomHeightText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out headroomHeight);

                if (!isValid)
                {
                    isValid = double.TryParse(
                        HeadroomHeightText,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out headroomHeight);
                }

                if (!isValid)
                {
                    WinForms.MessageBox.Show(
                        this,
                        Loc.T("Dialog.ProjectInit.InvalidHeadroomHeight"),
                        Loc.T("Dialog.ProjectInit.Title"),
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Warning);
                    DialogResult = WinForms.DialogResult.None;
                    return;
                }

                double doorWidthTolerance;
                bool toleranceValid = double.TryParse(
                    DoorWidthToleranceMmText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out doorWidthTolerance);

                if (!toleranceValid)
                {
                    toleranceValid = double.TryParse(
                        DoorWidthToleranceMmText,
                        NumberStyles.Float,
                        CultureInfo.CurrentCulture,
                        out doorWidthTolerance);
                }

                if (!toleranceValid)
                {
                    WinForms.MessageBox.Show(
                        this,
                        Loc.T("Dialog.ProjectInit.InvalidDoorWidthToleranceMm"),
                        Loc.T("Dialog.ProjectInit.Title"),
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Warning);
                    DialogResult = WinForms.DialogResult.None;
                    return;
                }

                if (string.IsNullOrWhiteSpace(IfcFilePathText) || !File.Exists(IfcFilePathText))
                {
                    WinForms.MessageBox.Show(
                        this,
                        "Please select a valid IFC file.",
                        Loc.T("Dialog.ProjectInit.Title"),
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Warning);
                    DialogResult = WinForms.DialogResult.None;
                    return;
                }

                if (!string.Equals(Path.GetExtension(IfcFilePathText), ".ifc", StringComparison.OrdinalIgnoreCase))
                {
                    WinForms.MessageBox.Show(
                        this,
                        "Please select an .ifc file.",
                        Loc.T("Dialog.ProjectInit.Title"),
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Warning);
                    DialogResult = WinForms.DialogResult.None;
                    return;
                }

                if (string.IsNullOrWhiteSpace(ProjectIdentifierText))
                {
                    WinForms.MessageBox.Show(
                        this,
                        "Project Identifier is required and will be used as session_id.",
                        Loc.T("Dialog.ProjectInit.Title"),
                        WinForms.MessageBoxButtons.OK,
                        WinForms.MessageBoxIcon.Warning);
                    DialogResult = WinForms.DialogResult.None;
                    return;
                }

                _headroomTextBox.Text = headroomHeight.ToString("0.###", CultureInfo.InvariantCulture);
                _doorWidthToleranceTextBox.Text = doorWidthTolerance.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
    }
}
