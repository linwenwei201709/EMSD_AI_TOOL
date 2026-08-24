using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CadToRevit.Infrastructure.Localization;
using CadToRevit.Services.CadRuntime;
using CadToRevit.Services.Workflow;
using CadToRevit.UI.Dockable;
using CadToRevit.UI.PathObstacles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;

namespace CadToRevit
{
    public class App : IExternalApplication
    {
        private static bool _roomPanesInitializedHidden;
        private static bool _pathObstaclePaneInitializedHidden;
        private static readonly HashSet<int> _newDocumentPreviewPaneHidden = new HashSet<int>();
        private static readonly HashSet<int> _readyForImportDocumentKeys = new HashSet<int>();

        private static PushButton _importDwgButton;
        private static PushButton _refreshDwgButton;
        private static PushButton _mainPanelButton;
        private static PushButton _detectRoomsButton;
        private static PushButton _restrictedAreaButton;
        private static PushButton _layoutPlanButton;
        private static PushButton _deliveryRouteButton;
        private static PushButton _exportIfcButton;
        private static PushButton _exportDrawingButton;
        private static PushButton _settingsButton;
        private static PushButton _familyLibraryButton;
        private static PushButton _toggleClearanceButton;
        private static PushButton _routeApiButton;
        private static PushButton _helpButton;
        private static PushButton _probeRoomButton;
        private static PushButton _analyzeRoomsButton;
        private static PushButton _rvtModelImportButton;
        private static PushButton _routePlannerButton;
        private static bool _assemblyResolveRegistered;
        private static bool _resolvingAutoCadManagedAssembly;
        private static string _lastRevitSelectionSignature = string.Empty;
        private static volatile bool _isShuttingDown;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                _isShuttingDown = false;
                RegisterAssemblyResolve();

                string cultureName = RevitLanguageMapper.MapToCultureName(application.ControlledApplication.Language);
                LocalizationService.Initialize(cultureName);

                string tabName = Loc.T("Ribbon.Tab.CadToRevit");
                string toolsPanelName = "Model Generation";// Loc.T("Ribbon.Panel.Tools");
                string pathToolsPanelName = Loc.T("Ribbon.Panel.PathTools");
                string integrationToolsPanelName = Loc.T("Ribbon.Panel.IntegrationTools");
                string configPanelName = Loc.T(LocalizedKeys.Ribbon.PanelConfig);
                string part3PanelName = Loc.T("Ribbon.Panel.Part3Tools");
                string roomPanelName = "Room & Restricted";// Loc.T("Ribbon.Panel.TestTools");
                string mEPLayoutName = "MEP Layout";
                try
                {
                    application.CreateRibbonTab(tabName);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    // Tab already exists in this session.
                }

                RibbonPanel panel = application.CreateRibbonPanel(tabName, toolsPanelName);
                RibbonPanel roomRestrictedPanel = application.CreateRibbonPanel(tabName, roomPanelName);
                RibbonPanel mEPLayoutPanel = application.CreateRibbonPanel(tabName, mEPLayoutName);
                RibbonPanel deliverablesPanel = application.CreateRibbonPanel(tabName, "Deliverables"); 
                RibbonPanel viewSystemPanel = application.CreateRibbonPanel(tabName, "View & System");
                //RibbonPanel integrationPanel = application.CreateRibbonPanel(tabName, integrationToolsPanelName);
                //RibbonPanel configPanel = application.CreateRibbonPanel(tabName, configPanelName);
                //RibbonPanel part3Panel = application.CreateRibbonPanel(tabName, part3PanelName);
                //RibbonPanel roomPanel = application.CreateRibbonPanel(tabName, roomPanelName);
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                PreviewPaneRuntime.InitializeExternalEvent();
                application.RegisterDockablePane(PreviewPaneRuntime.PaneId, Loc.T("DockablePane.Title.CadImportWizard"), new PreviewPaneProvider());
                RoomRecognitionPaneRuntime.InitializeExternalEvent();  
                PathObstacleRuntime.InitializeExternalEvent();
                application.RegisterDockablePane(RoomRecognitionPaneRuntime.LeftPaneId, Loc.T("DockablePane.RoomList.Title"), new RoomListPaneProvider());
                application.RegisterDockablePane(RoomRecognitionPaneRuntime.RightPaneId, "Layout Plans", new RoomDetailPaneProvider());
                application.RegisterDockablePane(DeliveryRoutePaneRuntime.PaneId, "Delivery Route", new DeliveryRoutePaneProvider());
                application.RegisterDockablePane(PathObstacleRuntime.PaneId, "Restricted Area", new PathObstaclePaneProvider());
                application.ViewActivated += OnViewActivated;
                application.Idling += OnIdling;
                application.ControlledApplication.DocumentCreated += OnDocumentCreated;
                application.ControlledApplication.DocumentOpened += OnDocumentOpened;
                application.ControlledApplication.DocumentClosing += OnDocumentClosing;
                application.ControlledApplication.DocumentClosed += OnDocumentClosed;

                // Keep button   text and tooltip fully localized via resource keys.
                _importDwgButton = AddButton(panel, "CadToRevit_ImportDwg", "Import Dwg", assemblyPath, "CadToRevit.Commands.DwgImportCommand", "Ribbon.Button.ImportDwg.Tooltip", "ImportCAD.png");
                _refreshDwgButton = AddButton(panel, "CadToRevit_RefreshDwg", "Refresh Dwg", assemblyPath, "CadToRevit.Commands.RefreshCurrentDwgLinkCommand", "Ribbon.Button.RefreshDwg.Tooltip", "RefreshDwg.png");
                _rvtModelImportButton = AddButton(panel, "CadToRevit_RvtModelImport", "Import Rvt", assemblyPath, "CadToRevit.Commands.RvtModelImportCommand", "Ribbon.Button.RvtModelImport.Tooltip", "ImportRvt.png");
                //AddButton(panel, "CadToRevit_WallWizard", "BIM\u751f\u6210\u5411\u5bfc", assemblyPath, "CadToRevit.Commands.WallWizardCommand", "\u6267\u884c BIM \u751f\u6210\u5411\u5bfc\u3002", "import_dwg_dpi96.png");
                _mainPanelButton = AddButton(panel, "CadToRevit_ShowPreviewPane", "Model Generator", assemblyPath, "CadToRevit.Commands.ShowPreviewPaneCommand", "Ribbon.Button.MainPanel.Tooltip", "Model Generator.png");
                //AddButton(panel, "CadToRevit_CreateCeilingsFromWalls", "Ceiling", assemblyPath, "CadToRevit.Commands.CreateCeilingsFromWallsCommand", "\u68c0\u6d4b\u95ed\u5408\u533a\u57df\u5e76\u751f\u6210\u5929\u82b1\u677f\u3002", "Backgroup96.png");
                // Create Ground moved from Ribbon to CAD Import Wizard status panel.
                //AddButton(roomPanel, "CadToRevit_SetDoorWidthTest", "Ribbon.Button.SetDoorWidth", assemblyPath, "CadToRevit.Commands.SetDoorWidthTestCommand", "Ribbon.Button.SetDoorWidth.Tooltip", "import_dwg_dpi96.png");
                //AddButton(panel, "CadToRevit_ExportIfcForPath", "Export Drawing", assemblyPath, "CadToRevit.Commands.ExportIfcForPathCommand", "Ribbon.Button.ExportIfc.Tooltip", "ImportCAD.png");

                //AddButton(panel, "CadToRevit_Help", "Ribbon.Button.Help", assemblyPath, "CadToRevit.Commands.HelpCommand", "Ribbon.Button.Help.Tooltip", "Help.png");
                //AddButtonLiteral(panel, "CadToRevit_CreateDynamicGenericWallTest", "测试动态墙", assemblyPath, "CadToRevit.Commands.CreateDynamicGenericWallTestCommand", "创建一?10m x 140mm x 3000mm 的动态常规墙，用于验?API", "Help.png");
                //AddButtonLiteral(panel, "CadToRevit_CreateFirstFamilyTypeWallTest", "测试FamilyType建墙", assemblyPath, "CadToRevit.Commands.CreateFirstFamilyTypeWallTestCommand", "直接使用当前文档中的第一?Basic WallType 创建测试?, "Help.png");
                // AddButton(roomPanel, "CadToRevit_RoomRecognition", "\u623f\u95f4\u8bc6\u522b", assemblyPath, "CadToRevit.Commands.RoomRecognitionCommand", "\u8bc6\u522b\u623f\u95f4\u8fb9\u754c\u5e76\u751f\u6210 Revit Room\u3002", "import_dwg_dpi96.png");
                _detectRoomsButton = AddButton(roomRestrictedPanel, "CadToRevit_TargetRoomModelRecognition", "Room & Lift", assemblyPath, "CadToRevit.Commands.TargetRoomModelRecognitionCommand", "Ribbon.Button.RoomRec.Tooltip", "Space Manager.png");
                _restrictedAreaButton = AddButtonLiteral(roomRestrictedPanel, "CadToRevit_PathObstacleManager", "Restricted Area", assemblyPath, "CadToRevit.Commands.PathObstacleManagerCommand", "Manage named path obstacles. Locate or delete existing no-go zones used for route planning.", "Keep Out Zones.png");

                _layoutPlanButton = AddButtonLiteral(mEPLayoutPanel, "CadToRevit_LayoutPlan", "Layout Plan", assemblyPath, "CadToRevit.Commands.ShowLayoutPlansCommand", "Open saved layout plans and equipment route planning.", "Equipment.png");
                _deliveryRouteButton = AddButtonLiteral(mEPLayoutPanel, "CadToRevit_DeliveryRoute", "Delivery Route", assemblyPath, "CadToRevit.Commands.ShowDeliveryRoutesCommand", "Open delivery route planning.", "Routing.png");

                _exportIfcButton = AddButton(deliverablesPanel, "CadToRevit_ExportIfcForPath", "Export IFC", assemblyPath, "CadToRevit.Commands.ExportIfcForPathCommand", "Ribbon.Button.ExportIfc.Tooltip", "Export IFC.png");
                _exportDrawingButton = AddButtonLiteral(deliverablesPanel, "CadToRevit_ExportDrawing", "Export Drawing", assemblyPath, "CadToRevit.Commands.ExportDrawingCommand", "Export the current model as a five-view PDF drawing.", "Export Drawing.png");
                //AddButtonLiteral(panel, "CadToRevit_ManualRoom", "Manual\nRoom", assemblyPath, "CadToRevit.Commands.ManualRoomCommand", "Create a manual room from selected boundary walls.", "RegRoom.png");
                //_probeRoomButton = AddButton(part3Panel, "CadToRevit_PickRoomAtPoint", LocalizedKeys.RoomProbe.RibbonButton, assemblyPath, "CadToRevit.Commands.PickRoomAtPointCommand", LocalizedKeys.RoomProbe.RibbonButtonTooltip, "RegRoom.png");
                //_analyzeRoomsButton = AddButtonLiteral(part3Panel, "CadToRevit_AnalyzeRooms", "Analyze Rooms", assemblyPath, "CadToRevit.Commands.AnalyzeRoomsCommand", "Automatically analyze candidate rooms in the current level or active view range.", "RegRoom.png");
                //AddButtonLiteral(panel, "CadToRevit_GenerateVentilationDuct", "生成通风?, assemblyPath, "CadToRevit.Commands.GenerateVentilationDuctCommand", "选择 AHU 设备与墙面点，自动生成刚性风管?, "Help.png");
                //AddButton(pathPanel, "CadToRevit_ShowPathPreview", "Ribbon.Button.PathPreview", assemblyPath, "CadToRevit.Commands.ShowPathPreviewCommand", "Ribbon.Button.PathPreview.Tooltip", "Help.png");
                //AddButtonLiteral(pathPanel, "CadToRevit_ShowMultiPaths", "多条路径", assemblyPath, "CadToRevit.Commands.ShowMultiPathsCommand", "在当?Revit 文档中直接绘制两条演示路径?, "Help.png");
               //AddButton(pathPanel, "CadToRevit_PickPathCoordinates", "Ribbon.Button.PickPathCoordinates", assemblyPath, "CadToRevit.Commands.PickPathCoordinatesCommand", "Ribbon.Button.PickPathCoordinates.Tooltip", "Help.png");
                //AddButton(integrationPanel, "CadToRevit_HealthCheck", "Ribbon.Button.HealthCheck", assemblyPath, "CadToRevit.Commands.HealthCheckCommand", "Ribbon.Button.HealthCheck.Tooltip", "Help.png");
                //AddButton(integrationPanel, "CadToRevit_ProjectInitialization", "Ribbon.Button.ProjectInit", assemblyPath, "CadToRevit.Commands.ProjectInitializationCommand", "Ribbon.Button.ProjectInit.Tooltip", "ProjectInit.png");
                //AddButtonLiteral(integrationPanel, "CadToRevit_DrawPathObstacle", "Path\nObstacle", assemblyPath, "CadToRevit.Commands.DrawPathObstacleCommand", "Pick 3 or more points to create a semi-transparent IFC-exportable no-go zone for path recognition.", "Backgroup96.png");
               
                //AddButtonLiteral(integrationPanel, "CadToRevit_LinkRvtToRoom", "Link RVT to Room", assemblyPath, "CadToRevit.Commands.LinkRvtToRoomCommand", "Select a RVT file and link it into the current model at the selected room center.", "ImportCAD.png");
                 //AddButtonLiteral(integrationPanel, "CadToRevit_CreateAhuTestRvt", "Create AHU Test RVT", assemblyPath, "CadToRevit.Commands.CreateAhuTestRvtCommand", "Create a clean simplified AHU RVT test model with core box, front marker, service clearance and frame lines.", "AHU.png");
                //AddButtonLiteral(integrationPanel, "CadToRevit_CalculatePathApiTest", "Calculate Path Test", assemblyPath, "CadToRevit.Commands.CalculatePathApiTestCommand", "Pick start and goal points on floor surfaces and call /api/calculate_path.", "Help.png");

                _settingsButton = AddButton(viewSystemPanel, "CadToRevit_ShowGlobalSettings", "Settings", assemblyPath, "CadToRevit.Commands.ShowGlobalSettingsCommand", LocalizedKeys.Ribbon.ButtonGlobalSettingsTooltip, "Settings.png");
                _familyLibraryButton = AddButton(viewSystemPanel, "CadToRevit_OpenFamilyLibraryManager", "Family Library", assemblyPath, "CadToRevit.Commands.OpenFamilyLibraryManagerCommand", LocalizedKeys.Ribbon.ButtonFamilyLibraryManagerTooltip, "Equipment.png");
                _toggleClearanceButton = AddButtonLiteral(viewSystemPanel, "CadToRevit_ToggleAhuMaintenanceSpace", "Toggle Clearance", assemblyPath, "CadToRevit.Commands.ToggleAhuMaintenanceSpaceCommand", "Show or hide maintenance space for selected or all EMSD AHU equipment.", "Toggle Clearance.png");
                _routeApiButton = AddButtonLiteral(viewSystemPanel, "CadToRevit_RouteApiConsole", "Route API", assemblyPath, "CadToRevit.Commands.RouteApiConsoleCommand", "Start, stop and monitor the local route planning API.", "Routing.png");
                _helpButton = AddButtonLiteral(viewSystemPanel, "CadToRevit_Help", "Help", assemblyPath, "CadToRevit.Commands.HelpCommand", "Open the local EMSD AI Tool Help Center.", "Help.png");

                //_routePlannerButton = AddButton(part3Panel, "CadToRevit_RoutePlanner", "Ribbon.Button.RoutePlanner", assemblyPath, "CadToRevit.Commands.RoutePlannerCommand", "Ribbon.Button.RoutePlanner.Tooltip", "RoutePlanner.png");

                // No active project exists during add-in startup. Keep every EMSD command
                // disabled until DocumentCreated / DocumentOpened / ViewActivated resolves
                // the active document state.
                UpdateRibbonButtonAvailability(null);

                // Start the local Route API silently in the background.
                // RouteApiProcessService resolves:
                //   <plugin root>\RouteApi\AHU_API.exe
                // and starts it with CreateNoWindow=true, so normal users no
                // longer need to open Route API Console and click Start API.
                StartRouteApiSilently();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                WriteStartupLog(ex);
                return Result.Failed;
            }
        }

        private static void StartRouteApiSilently()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                if (_isShuttingDown)
                {
                    return;
                }

                try
                {
                    CadToRevit.Services.RouteApi.RouteApiProcessService.Start();
                }
                catch (Exception ex)
                {
                    // API startup failure must not prevent Revit or the add-in
                    // from loading. The error remains available through the
                    // Route API Console/log for diagnosis.
                    try
                    {
                        WriteStartupLog(
                            new InvalidOperationException(
                                "Failed to auto-start RouteApi\\AHU_API.exe.",
                                ex));
                    }
                    catch
                    {
                    }
                }
            });
        }

        private static void WriteStartupLog(Exception ex)
        {
            try
            {
                string logDir = CadToRevit.Services.Diagnostics.DiagnosticRecorder.GetLogDirectory();
                string logPath = Path.Combine(logDir, "CadToRevit_Startup.log");
                string message =
                    "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " ====" + Environment.NewLine +
                    ex + Environment.NewLine + Environment.NewLine;
                File.AppendAllText(logPath, message);
            }
            catch
            {
                // Avoid blocking Revit startup diagnostics if logging itself fails.
            }
        }

        private static void RegisterAssemblyResolve()
        {
            if (_assemblyResolveRegistered)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveAddinAssembly;
            _assemblyResolveRegistered = true;
        }

        private static Assembly ResolveAddinAssembly(object sender, ResolveEventArgs args)
        {
            try
            {
                string assemblyName = new AssemblyName(args.Name).Name + ".dll";
                if (IsAutoCadManagedAssembly(assemblyName))
                {
                    return ResolveAutoCadManagedAssembly(assemblyName);
                }

                string addinDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(addinDir))
                {
                    return null;
                }

                string candidate = Path.Combine(addinDir, assemblyName);
                if (!File.Exists(candidate))
                {
                    return null;
                }

                return Assembly.LoadFrom(candidate);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAutoCadManagedAssembly(string assemblyFileName)
        {
            return string.Equals(assemblyFileName, "AcCoreMgd.dll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assemblyFileName, "AcDbMgd.dll", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(assemblyFileName, "AcMgd.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static Assembly ResolveAutoCadManagedAssembly(string assemblyFileName)
        {
            if (_resolvingAutoCadManagedAssembly)
            {
                return null;
            }

            try
            {
                _resolvingAutoCadManagedAssembly = true;
                CadRuntimeInfo runtime = CadRuntimeDetector.Detect();
                if (runtime == null || !runtime.IsReady || string.IsNullOrWhiteSpace(runtime.InstallLocation))
                {
                    return null;
                }

                string dllPath = Path.Combine(runtime.InstallLocation, assemblyFileName);
                if (!File.Exists(dllPath))
                {
                    return null;
                }

                return Assembly.LoadFrom(dllPath);
            }
            catch
            {
                return null;
            }
            finally
            {
                _resolvingAutoCadManagedAssembly = false;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            _isShuttingDown = true;
            application.ViewActivated -= OnViewActivated;
            application.Idling -= OnIdling;
            application.ControlledApplication.DocumentCreated -= OnDocumentCreated;
            application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
            application.ControlledApplication.DocumentClosing -= OnDocumentClosing;
            application.ControlledApplication.DocumentClosed -= OnDocumentClosed;
            CadToRevit.Services.RouteApi.RouteApiProcessService.StopApi();
            return Result.Succeeded;
        }

        private static void OnDocumentCreated(object sender, DocumentCreatedEventArgs e)
        {
            Document doc = e != null ? e.Document : null;
            if (!IsSupportedProjectDocument(doc))
            {
                UpdateRibbonButtonAvailability(doc);
                return;
            }

            // A document created through Revit New is the only unmarked document
            // allowed to enter an EMSD workflow. The marker is session-only until
            // Import DWG or Import RVT succeeds and writes the persistent mode.
            lock (_readyForImportDocumentKeys)
            {
                _readyForImportDocumentKeys.Add(GetDocumentKey(doc));
            }

            UpdateRibbonButtonAvailability(doc);
        }

        private static void OnDocumentOpened(object sender, DocumentOpenedEventArgs e)
        {
            Document doc = e != null ? e.Document : null;
            if (doc != null)
            {
                // Documents opened through Revit Open are never treated as import-ready
                // merely because they have no workflow marker. Existing EMSD projects
                // are still restored because the persistent mode is checked first.
                lock (_readyForImportDocumentKeys)
                {
                    _readyForImportDocumentKeys.Remove(GetDocumentKey(doc));
                }
            }

            UpdateRibbonButtonAvailability(doc);
        }

        private static void OnDocumentClosing(object sender, DocumentClosingEventArgs e)
        {
            Document doc = e != null ? e.Document : null;
            if (doc == null)
            {
                return;
            }

            int docKey = GetDocumentKey(doc);
            lock (_readyForImportDocumentKeys)
            {
                _readyForImportDocumentKeys.Remove(docKey);
            }

            _newDocumentPreviewPaneHidden.Remove(docKey);
        }

        private static void OnDocumentClosed(object sender, DocumentClosedEventArgs e)
        {
            // The closed document object is no longer available here. Disable every
            // command immediately; ViewActivated will restore the appropriate state
            // if Revit activates another still-open document.
            UpdateRibbonButtonAvailability(null);
        }

        private static void OnIdling(object sender, IdlingEventArgs e)
        {
            try
            {
                UIApplication uiApp = sender as UIApplication;
                UIDocument uiDoc = uiApp != null ? uiApp.ActiveUIDocument : null;
                string signature = uiDoc != null && uiDoc.Selection != null
                    ? string.Join(",", uiDoc.Selection.GetElementIds().Select(x => x.IntegerValue.ToString()).OrderBy(x => x))
                    : string.Empty;

                if (string.Equals(signature, _lastRevitSelectionSignature, StringComparison.Ordinal))
                {
                    return;
                }

                _lastRevitSelectionSignature = signature;
                PreviewPaneRuntime.UpdateRevitSelectionCount(uiApp);
            }
            catch
            {
                if (!string.IsNullOrEmpty(_lastRevitSelectionSignature))
                {
                    _lastRevitSelectionSignature = string.Empty;
                    PreviewPaneRuntime.UpdateRevitSelectionCount(null);
                }
            }
        }
        private static void OnViewActivated(object sender, ViewActivatedEventArgs e)
        {
            if (e != null && e.Document != null)
            {
                try
                {
                    UIApplication uiApp = new UIApplication(e.Document.Application);

                    // Do not auto-show the CAD Import Wizard on startup/new blank projects.
                    // Revit will restore the dockable pane state for saved projects by itself.
                    // For a brand-new unsaved project, hide the pane once so the default workspace stays clean
                    // until the user clicks an EMSD command such as Main Panel or Import DWG.
                    int docKey = e.Document.GetHashCode();
                    bool isUnsavedNewProject = string.IsNullOrWhiteSpace(e.Document.PathName);
                    if (isUnsavedNewProject && !_newDocumentPreviewPaneHidden.Contains(docKey))
                    {
                        DockablePane previewPane = uiApp.GetDockablePane(PreviewPaneRuntime.PaneId);
                        previewPane?.Hide();
                        _newDocumentPreviewPaneHidden.Add(docKey);
                    }
                }
                catch
                {
                    // Ignore startup pane-hide failures.
                }
            }

            if (!_roomPanesInitializedHidden && e != null && e.Document != null)
            {
                try
                {
                    UIApplication uiApp = new UIApplication(e.Document.Application);
                    RoomRecognitionPaneRuntime.HidePanes(uiApp);
                    _roomPanesInitializedHidden = true;
                }
                catch
                {
                    // Ignore startup pane-hide failures.
                }
            }

            // Revit may restore the last dockable-pane visibility from the
            // previous session. Restricted Area must stay hidden at startup
            // until the user explicitly clicks the Obstacle Manager ribbon.
            if (!_pathObstaclePaneInitializedHidden && e != null && e.Document != null)
            {
                try
                {
                    UIApplication uiApp = new UIApplication(e.Document.Application);
                    PathObstacleRuntime.HidePane(uiApp);
                    _pathObstaclePaneInitializedHidden = true;
                }
                catch
                {
                    // Ignore startup pane-hide failures.
                }
            }

            UpdateRibbonButtonAvailability(e?.Document);
            _ = PreviewPaneRuntime.ViewModel.RefreshPaneStateAsync();
        }

        private enum RibbonDocumentState
        {
            NoDocument = 0,
            BlockedOrdinaryDocument = 1,
            ReadyForImport = 2,
            DwgWorkflow = 3,
            RvtWorkflow = 4
        }

        public static void UpdateRibbonButtonAvailability(Autodesk.Revit.DB.Document doc)
        {
            DisableAllRibbonButtons();

            RibbonDocumentState state = ResolveRibbonDocumentState(doc);
            switch (state)
            {
                case RibbonDocumentState.NoDocument:
                case RibbonDocumentState.BlockedOrdinaryDocument:
                    // No document, family documents, and ordinary RVT files opened
                    // without an EMSD workflow marker stay completely isolated.
                    return;

                case RibbonDocumentState.ReadyForImport:
                    EnableReadyForImportButtons();
                    return;

                case RibbonDocumentState.DwgWorkflow:
                    EnableDwgWorkflowButtons();
                    return;

                case RibbonDocumentState.RvtWorkflow:
                    EnableRvtWorkflowButtons();
                    return;
            }
        }

        private static RibbonDocumentState ResolveRibbonDocumentState(Document doc)
        {
            if (!IsSupportedProjectDocument(doc))
            {
                return RibbonDocumentState.NoDocument;
            }

            ProjectWorkflowMode mode = ProjectWorkflowMode.None;
            try
            {
                mode = ProjectWorkflowModeStoreService.GetMode(doc);
            }
            catch
            {
                mode = ProjectWorkflowMode.None;
            }

            // Persistent EMSD markers always take priority. This restores the
            // correct workflow when a previously processed RVT is opened again.
            if (mode == ProjectWorkflowMode.DwgImportMode)
            {
                return RibbonDocumentState.DwgWorkflow;
            }

            if (mode == ProjectWorkflowMode.RvtModelImportMode)
            {
                return RibbonDocumentState.RvtWorkflow;
            }

            lock (_readyForImportDocumentKeys)
            {
                if (_readyForImportDocumentKeys.Contains(GetDocumentKey(doc)))
                {
                    return RibbonDocumentState.ReadyForImport;
                }
            }

            // Any unmarked project that was not created during this Revit session
            // is treated as an ordinary Open document and all EMSD commands remain disabled.
            return RibbonDocumentState.BlockedOrdinaryDocument;
        }

        private static bool IsSupportedProjectDocument(Document doc)
        {
            return doc != null && doc.IsValidObject && !doc.IsFamilyDocument;
        }

        private static int GetDocumentKey(Document doc)
        {
            return doc != null ? doc.GetHashCode() : 0;
        }

        private static void DisableAllRibbonButtons()
        {
            SetButtonEnabled(_importDwgButton, false);
            SetButtonEnabled(_refreshDwgButton, false);
            SetButtonEnabled(_rvtModelImportButton, false);
            SetButtonEnabled(_mainPanelButton, false);
            SetButtonEnabled(_detectRoomsButton, false);
            SetButtonEnabled(_restrictedAreaButton, false);
            SetButtonEnabled(_layoutPlanButton, false);
            SetButtonEnabled(_deliveryRouteButton, false);
            SetButtonEnabled(_exportIfcButton, false);
            SetButtonEnabled(_exportDrawingButton, false);
            SetButtonEnabled(_settingsButton, false);
            SetButtonEnabled(_familyLibraryButton, false);
            SetButtonEnabled(_toggleClearanceButton, false);
            SetButtonEnabled(_routeApiButton, false);
            SetButtonEnabled(_helpButton, false);
            SetButtonEnabled(_probeRoomButton, false);
            SetButtonEnabled(_analyzeRoomsButton, false);
            SetButtonEnabled(_routePlannerButton, false);
        }

        private static void EnableReadyForImportButtons()
        {
            SetButtonEnabled(_importDwgButton, true);
            SetButtonEnabled(_rvtModelImportButton, true);
            SetButtonEnabled(_settingsButton, true);
            SetButtonEnabled(_familyLibraryButton, true);
            SetButtonEnabled(_routeApiButton, true);
            SetButtonEnabled(_helpButton, true);
        }

        private static void EnableDwgWorkflowButtons()
        {
            SetButtonEnabled(_refreshDwgButton, true);
            SetButtonEnabled(_mainPanelButton, true);
            SetButtonEnabled(_detectRoomsButton, true);
            SetButtonEnabled(_restrictedAreaButton, true);
            SetButtonEnabled(_layoutPlanButton, true);
            SetButtonEnabled(_deliveryRouteButton, true);
            SetButtonEnabled(_exportIfcButton, true);
            SetButtonEnabled(_exportDrawingButton, true);
            SetButtonEnabled(_settingsButton, true);
            SetButtonEnabled(_familyLibraryButton, true);
            SetButtonEnabled(_toggleClearanceButton, true);
            SetButtonEnabled(_routeApiButton, true);
            SetButtonEnabled(_helpButton, true);
        }

        private static void EnableRvtWorkflowButtons()
        {
            SetButtonEnabled(_detectRoomsButton, true);
            SetButtonEnabled(_restrictedAreaButton, true);
            SetButtonEnabled(_layoutPlanButton, true);
            SetButtonEnabled(_deliveryRouteButton, true);
            SetButtonEnabled(_exportIfcButton, true);
            SetButtonEnabled(_exportDrawingButton, true);
            SetButtonEnabled(_settingsButton, true);
            SetButtonEnabled(_familyLibraryButton, true);
            SetButtonEnabled(_toggleClearanceButton, true);
            SetButtonEnabled(_routeApiButton, true);
            SetButtonEnabled(_helpButton, true);
            SetButtonEnabled(_probeRoomButton, true);
            SetButtonEnabled(_analyzeRoomsButton, true);
            SetButtonEnabled(_routePlannerButton, true);
        }

        private static void SetButtonEnabled(PushButton button, bool enabled)
        {
            if (button == null)
            {
                return;
            }

            try
            {
                button.Enabled = enabled;
            }
            catch
            {
                // Ribbon item may not be fully initialized during startup; ignore and retry on next activation.
            }
        }

        private static PushButton AddButton(
            RibbonPanel panel,
            string buttonId,
            string textKey,
            string assemblyPath,
            string commandClass,
            string tooltipKey,
            string iconFileName)
        {
            string buttonText = Loc.T(textKey);
            string tooltip = Loc.T(tooltipKey);

            PushButtonData buttonData = new PushButtonData(
                buttonId,
                buttonText,
                assemblyPath,
                commandClass);
            TrySetButtonIcon(buttonData, assemblyPath, iconFileName);

            PushButton button = panel.AddItem(buttonData) as PushButton;
            if (button != null)
            {
                button.ToolTip = tooltip;
            }

            return button;
        }

        private static PushButton AddButtonLiteral(
            RibbonPanel panel,
            string buttonId,
            string buttonText,
            string assemblyPath,
            string commandClass,
            string tooltip,
            string iconFileName)
        {
            PushButtonData buttonData = new PushButtonData(
                buttonId,
                buttonText,
                assemblyPath,
                commandClass);
            TrySetButtonIcon(buttonData, assemblyPath, iconFileName);

            PushButton button = panel.AddItem(buttonData) as PushButton;
            if (button != null)
            {
                button.ToolTip = tooltip;
            }

            return button;
        }

        private static void TrySetButtonIcon(PushButtonData buttonData, string assemblyPath, string iconFileName)
        {
            // Load icon from output Resources folder using file name.
            string folder = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            string iconPath = Path.Combine(folder, "Resources", iconFileName);
            if (!File.Exists(iconPath))
            {
                return;
            }

            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(iconPath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();

            buttonData.LargeImage = image;
            buttonData.Image = image;
        }
    }
}
