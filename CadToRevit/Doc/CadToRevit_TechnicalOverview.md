## CadToRevit 技术栈与系统说明书（Technical Overview）

### 1. 项目概述
`CadToRevit` 是一个面向 Autodesk Revit 的桌面插件，主要完成 DWG 导入后基于图层/几何/文本信息的识别、参数化映射、以及在 Revit 中进行几何预览与元素生成（含房间语义识别与 3D 可视化）。

项目以 Revit 二次开发形式落地：通过 Ribbon 按钮与 DockablePane（停靠面板）提供交互入口，并通过 `ExternalEvent` 在 Revit 主线程执行 API 操作。

### 2. 开发语言与目标框架
- **语言**：C#
- **.NET Framework**：`v4.8`
  - 来自：`CadToRevit.csproj` 中 `TargetFrameworkVersion=v4.8`
- **Revit API**：
  - 引用：`RevitAPI.dll`、`RevitAPIUI.dll`（以本地相对路径引用）

### 3. 关键技术与组件（按功能分层）

#### 3.1 Revit 集成层（Application / Commands）
- **外部应用入口**：实现 `IExternalApplication`
  - 文件：`CadToRevit/App.cs`
  - 职责：
    - 初始化多语言资源（Localization）
    - 创建 Ribbon Tab 与按钮
    - 注册 DockablePane（WPF）
    - 监听 `ViewActivated` 事件做启动期显示/刷新
- **外部命令入口**：实现 `IExternalCommand`
  - 由 Ribbon 按钮触发
  - 示例：
    - `Commands/DwgImportCommand.cs`：导入 DWG（导入 + 状态刷新）
    - `Commands/ShowPreviewPaneCommand.cs`：展示预览面板
    - `Commands/WallWizardCommand.cs`：生成向导主流程（与房间种子提取相关）
    - `Commands/TargetRoomModelRecognitionCommand.cs`：房间语义模型级识别（seed + flood-fill）

#### 3.2 UI 交互层（WPF DockablePane / Window）
- **WPF DockablePane（停靠面板）**
  - `PreviewPaneRuntime` / `PreviewPaneProvider`：DWG 导入向导、图层映射与预览/生成控制
  - `RoomRecognitionPaneRuntime` / `RoomListPaneProvider` / `RoomDetailPaneProvider`：房间识别结果列表与详情
- **ExternalEvent 与 UI 解耦**
  - WPF 点击不会直接调用 Revit API
  - 通过 `ExternalEvent` 请求队列化到 Revit 主线程执行
  - 关键文件：
    - `UI/Dockable/PreviewPaneRuntime.cs`
    - `UI/Dockable/PreviewPaneExternalEventHandler.cs`
    - `UI/Dockable/RoomRecognitionPaneRuntime.cs`
    - `UI/Dockable/RoomRecognitionExternalEventHandler.cs`
- **WinForms**
  - 工程中仍使用少量 WinForms（例如 `OpenFileDialog`、`MessageBox`），作为对话框/选择器用途。

#### 3.3 Revit 主线程执行层（ExternalEvent Handler）
- 典型模式：
  1. UI 发请求（封装 request type 与参数）
  2. `ConcurrentQueue` 入队
  3. `ExternalEvent.Raise()`
  4. 在 `IExternalEventHandler.Execute(...)` 中从队列取请求执行
  5. 返回 response，UI 再刷新

#### 3.4 业务服务层（DWG/CAD / 识别 / 预览 / 创建 / 可视化）

**(1) DWG/CAD 数据读取与坐标变换**
- DWG 导入：`Services/Dwg/DwgImportService.cs`
- DWG 文本读取（优先直接读 DWG 数据库，避免导入几何文本丢失）：
  - `Services/Dwg/DwgTextReader.cs`
- 从 Revit import 几何 fallback 提取文本：
  - `Services/Cad/CadTextBuilder.cs`
- CAD 图层与线段构造/缩放：
  - `Services/CadSegmentBuilder.cs`
  - `Services/Cad/CadDatasetBuilder.cs`
  - `Services/Cad/CadDatasetScaler.cs`
- 坐标变换：
  - `Services/Dwg/DwgTransformResolver.cs`

**(2) 预览绘制与清除**
- `Services/Preview/PreviewService.cs`
  - 在“平面图/剖面/明细”等支持视图类型下，将 CAD 线段以 `DetailCurve` 形式绘制

**(3) 房间语义识别（Semantic Layer）**
- 种子提取（从 CAD 文本抽取目标房间锚点）：
  - `Services/Rooms/TargetRoomSeedExtractor.cs`
- 模型级识别（seed flood-fill）：
  - `Services/Rooms/TargetRoomModelRecognitionService.cs`
  - 局部边界线采集：`ModelBoundaryCollector.cs`
  - 门洞闭合线：`DoorClosureBuilder.cs`
  - 洪泛区域检测与轮廓提取：`ModelFloodFillService.cs` + `GridContourExtractor.cs`
- 持久化：
  - `TargetRoomSeedStorageService.cs`（seed）
  - `RoomSemanticStorageService.cs`（识别结果）

**(4) 房间 3D 可视化**
- `Services/Rooms/Room3DVisualizationService.cs`
  - 区域：`DirectShape`
  - marker：`DirectShape`
  - 文本：创建 `EMSD_Room3DText.rfa` 的 `FamilyInstance` 并写入参数
  - 文本写入细节：`Room3DVisualizationTextService.cs`

### 4. 外部依赖（Dependencies）
从 `CadToRevit.csproj` 可见主要引用：
- `RevitAPI` / `RevitAPIUI`
- WPF 组件：`PresentationFramework`、`PresentationCore`、`WindowsBase`
- WinForms：`System.Windows.Forms`（少量）
- AutoCAD .NET：`AcCoreMgd` / `AcDbMgd` / `acmgd`（用于 DWG 文本读取）
- `FontAwesome.Sharp.dll`（UI 字形图标）

### 5. 配置、资源与日志
- **JSON 配置**（构建后拷贝输出目录）：
  - `GenerationGuardConfig.json`
  - `ColumnRecognitionConfig.json`
  - `layer-mapping.json`
  - `WallRecognitionConfig.json`
- **多语言资源**：
  - `Resources/Strings.resx`
  - `Resources/Strings.zh-Hans.resx`
  - `Resources/Strings.zh-Hant.resx`
- **日志**：
  - `Services/Diagnostics/DiagnosticRecorder.cs`
  - 默认路径：`CadToRevit/LOG/mvp1_debug_yyyyMMdd.log`
  - 常用前缀：
    - `[Room3DVis]` 房间 3D 可视化
    - `[RoomText]` DWG 文本读取
    - `[TargetSeed]` 种子提取与保存

### 6. 维护与排障要点（面向开发/运维）
1. **按钮/停靠面板无反应**
   - 检查 ExternalEvent 是否初始化成功（`App.cs` 注册）
   - 看日志是否有对应 request 入队与执行异常
2. **DWG 导入后识别异常**
   - 检查导入成功与否（`DwgImportService` + 日志）
   - 检查单位风险（`DwgImportResult.UnitSuspicious`）
   - 检查图层名与映射配置是否匹配
3. **房间文字/族参数未生效**
   - 优先检查：
     - 参数是否存在（参数名是否精确一致）
     - 参数是实例参数还是类型参数（Instance vs Type）
     - `StorageType` 是否为 `String`
   - 再用日志搜索 `[Room3DVis][ROOMNAMEPARAM]` / `[Room3DVis][TextParam]`

### 7. 相关关键源码入口（用于快速定位）
- `CadToRevit/App.cs`：Ribbon + DockablePane 注册
- `CadToRevit/Commands/*`：按钮入口命令
- `CadToRevit/UI/Dockable/*`：WPF 停靠面板（Provider/Runtime/ViewModel/ExternalEvent）
- `CadToRevit/Services/Dwg/*`：DWG 导入、文本读取、坐标变换
- `CadToRevit/Services/Preview/*`：预览绘制
- `CadToRevit/Services/Rooms/*`：房间语义 seed/识别/可视化/持久化

## CadToRevit 技术栈与系统说明书（Technical Overview）

### 1. 项目概述
`CadToRevit` 是一个面向 Autodesk Revit 的桌面插件，主要完成 DWG 导入后基于图层/几何/文本信息的识别、参数化映射、以及在 Revit 中进行几何预览与元素生成（含房间语义识别与 3D 可视化）。

项目以 Revit 二次开发形式落地：通过 Ribbon 按钮与 DockablePane（停靠面板）提供交互入口，并通过 `ExternalEvent` 在 Revit 主线程执行 API 操作。

### 2. 开发语言与目标框架
- **语言**：C#
- **.NET Framework**：`v4.8`
  - 来自：`CadToRevit.csproj` 中 `TargetFrameworkVersion=v4.8`
- **Revit API**：
  - 引用：`RevitAPI.dll`、`RevitAPIUI.dll`（以本地相对路径引用）

### 3. 关键技术与组件（按功能分层）

#### 3.1 Revit 集成层（Commands / Application）
- **外部应用入口**：实现 `IExternalApplication`
  - 文件：`CadToRevit/App.cs`
  - 职责：
    - 初始化多语言资源（Localization）
    - 创建 Ribbon Tab 与面板按钮
    - 注册 DockablePane（WPF）
    - 监听 `ViewActivated` 事件，进行停靠面板显示/刷新
- **外部命令入口**：实现 `IExternalCommand`
  - 由 Ribbon 按钮触发
  - 典型文件：
    - `Commands/DwgImportCommand.cs`：导入 DWG（DWG import + 状态刷新）
    - `Commands/ShowPreviewPaneCommand.cs`：展示预览面板
    - `Commands/WallWizardCommand.cs`：墙/门/窗/柱梁等生成向导主流程（与房间种子提取相关）
    - `Commands/TargetRoomModelRecognitionCommand.cs`：房间语义模型级识别（seed + flood-fill + 结果落地）

#### 3.2 UI 交互层（WPF DockablePane / Window）
- **WPF DockablePane**
  - `PreviewPaneRuntime` + `PreviewPaneProvider`：CAD 导入向导/图层映射/预览与生成控制
  - `RoomRecognitionPaneRuntime` + `RoomListPaneProvider` / `RoomDetailPaneProvider`：房间识别结果列表与详情
- **ExternalEvent 与 UI 解耦**
  - WPF 点击命令不会直接调用 Revit API
  - 通过 `ExternalEvent` 将请求队列化传递给 `IExternalEventHandler`
  - 关键文件：
    - `UI/Dockable/PreviewPaneRuntime.cs`
    - `UI/Dockable/PreviewPaneExternalEventHandler.cs`
    - `UI/Dockable/RoomRecognitionPaneRuntime.cs`
    - `UI/Dockable/RoomRecognitionExternalEventHandler.cs`
- **非 WPF 部分**
  - 项目仍包含少量 WinForms 使用（如 `MessageBox` / 文件选择器）
  - 但你当前主流程核心交互以 WPF 停靠面板为主。

#### 3.3 Revit 主线程执行层（ExternalEvent Handler）
- 典型模式：
  1. UI 发请求：封装 `PreviewPaneRequestType` 或 `RoomRecognitionPaneRequestType`
  2. `ConcurrentQueue` 入队
  3. ExternalEvent Raise
  4. Revit 主线程执行 `PreviewPaneDataService` / 房间语义服务
  5. 返回结果 `PreviewPaneResponse` / 刷新 UI

#### 3.4 业务服务层（CAD / DWG / 识别 / 创建 / 可视化）

**(1) DWG / CAD 数据读取与坐标变换**
- DWG 导入：`Services/Dwg/DwgImportService.cs`
- 读取 DWG 文本（优先从 DWG 数据源提取，避免 Revit 几何文本丢失）：
  - `Services/Dwg/DwgTextReader.cs`
- 文本提取 fallback（从 Revit import 几何中提取）：
  - `Services/Cad/CadTextBuilder.cs`
- CAD 图层/线段构造与缩放：
  - `Services/CadSegmentBuilder.cs`、`Services/Cad/CadDatasetBuilder.cs`、`Services/Cad/CadDatasetScaler.cs`
- 坐标变换：
  - `Services/Dwg/DwgTransformResolver.cs`

**(2) 图层映射与识别分析**
- 图层标准校验与结果窗口：
  - `UI/Dockable/LayerAnalysisResultWindow.xaml.cs`
  - `Services/LayerStandardAnalyzer.cs`（间接调用）

**(3) 预览绘制与清除**
- `Services/Preview/PreviewService.cs`
  - 将 CAD 线段以 `DetailCurve` 形式绘制到当前“平面/剖面/明细”等可用视图
  - 以及清除预览元素

**(4) 房间语义识别（语义层）**
- 种子提取（从 CAD 文本提取目标房间文字点）：
  - `Services/Rooms/TargetRoomSeedExtractor.cs`
- 模型级房间识别：
  - `Services/Rooms/TargetRoomModelRecognitionService.cs`
  - 局部模型边界线采集：`ModelBoundaryCollector.cs`
  - 门洞闭合线构建：`DoorClosureBuilder.cs`
  - 洪泛识别与轮廓提取：`ModelFloodFillService.cs` + `GridContourExtractor.cs`
- 结果持久化：
  - `Services/Rooms/TargetRoomSeedStorageService.cs`（seed 存储到 Extensible Storage）
  - `Services/Rooms/RoomSemanticStorageService.cs`（语义结果存储到 Extensible Storage）

**(5) 房间 3D 可视化**
- `Services/Rooms/Room3DVisualizationService.cs`
  - 区域：`DirectShape`（由几何构建器生成实体）
  - marker：`DirectShape`
  - 文本：加载/创建 `EMSD_Room3DText.rfa` 并写入参数（如 `ROOMNAMEPARAM` 等）
  - 关键文本写入：
    - `Services/Rooms/Room3DVisualizationTextService.cs`

### 4. 外部依赖（Dependencies）
项目在 `.csproj` 中的主要引用/依赖：
- `RevitAPI` / `RevitAPIUI`
- `System.Windows.Forms`（少量）
- `PresentationFramework` / `PresentationCore` / `WindowsBase`（WPF）
- `FontAwesome.Sharp.dll`（图标字形渲染）
- AutoCAD .NET SDK 相关引用：
  - `AcCoreMgd` / `AcDbMgd` / `acmgd`（用于 DWG 文本读取）

### 5. 配置、资源与日志
- **配置 JSON**（随构建拷贝输出目录）：
  - `GenerationGuardConfig.json`
  - `WallRecognitionConfig.json`
  - `ColumnRecognitionConfig.json`
  - `layer-mapping.json`
  - 以及若干用于材料/几何/规则的资源
- **多语言资源**：
  - `Resources/Strings.resx`
  - `Resources/Strings.zh-Hans.resx`
  - `Resources/Strings.zh-Hant.resx`
- **日志诊断**：
  - `Services/Diagnostics/DiagnosticRecorder.cs`
  - 默认写到：`CadToRevit/LOG/mvp1_debug_yyyyMMdd.log`
  - 用于定位 ExternalEvent 请求、房间语义识别失败原因、文本参数写入情况等。

### 6. 运行与排障建议（面向维护者）
1. **遇到 UI/按钮无反应**
   - 先查看是否 ExternalEvent 初始化（`App.cs` 启动时注册）
   - 再看日志里是否有对应请求入队与执行异常
2. **遇到 DWG 导入后识别结果异常**
   - 检查：
     - DWG 导入是否成功（`DwgImportCommand` + 日志）
     - 单位是否异常（`DwgImportResult.UnitSuspicious`）
     - 图层名匹配是否正确（`layer-mapping.json` / 标准分析）
3. **遇到房间文字/参数写入不生效**
   - 优先检查：
     - 目标参数是否存在于 `FamilyInstance` 或 `FamilySymbol`（Instance vs Type）
     - `Room3DVisualizationTextService` 对参数名的查找逻辑与族参数归属
   - 再用日志搜索 `[Room3DVis][ROOMNAMEPARAM]`、`[Room3DVis][TextParam]`

### 7. 相关关键源码入口（便于定位）
- `CadToRevit/App.cs`：Ribbon + DockablePane 注册
- `CadToRevit/Commands/*`：按钮入口命令
- `CadToRevit/UI/Dockable/*`：WPF 停靠面板（ViewModel + Provider + Runtime）
- `CadToRevit/Services/Dwg/*`：DWG 导入、文本读取与坐标变换
- `CadToRevit/Services/Preview/*`：预览绘制
- `CadToRevit/Services/Rooms/*`：房间语义 seed/识别/可视化/持久化

