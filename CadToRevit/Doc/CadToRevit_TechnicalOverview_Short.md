## CadToRevit 简化技术说明书（简版）

### 1) 项目是什么
`CadToRevit` 是一个 Autodesk Revit 插件（DLL），用于将 DWG/CAD 的图层、几何与文本信息转为 Revit 内的预览与生成逻辑，并包含房间语义识别与 3D 可视化。

### 2) 开发技术栈
- **语言**：C#
- **目标框架**：`.NET Framework 4.8`（`CadToRevit.csproj`）
- **Revit 二次开发**：
  - `IExternalApplication`
  - `IExternalCommand`
- **UI**：
  - **WPF DockablePane**（停靠面板）
  - 少量 **WinForms**：用于对话框/文件选择等（如 `MessageBox`、`OpenFileDialog`）
- **外部依赖**：
  - `RevitAPI.dll` / `RevitAPIUI.dll`
  - AutoCAD .NET 组件（`AcCoreMgd` / `AcDbMgd` / `acmgd`，用于 DWG 文本读取）
  - `FontAwesome.Sharp.dll`（UI 图标）

### 3) 关键架构模式
- **UI 与 Revit API 解耦**：WPF 通过 `ExternalEvent + IExternalEventHandler` 把请求放入队列，再由 Revit 主线程执行业务逻辑。
- **模块分层**：
  - Commands：按钮入口
  - UI/Dockable：WPF 界面与 ViewModel
  - Services：DWG/CAD 读取、预览、创建、房间语义识别、3D 可视化
  - Diagnostics：日志记录与排障

### 4) 主要功能模块（按业务）
- **DWG 导入**
- **DWG 文本读取**
- **图层映射/预览**
- **墙/门/窗/柱/梁/楼板等生成向导**
- **房间语义识别（seed + flood-fill）**：
  - seed
  - 识别
  - 持久化
- **房间 3D 可视化**

### 5) 日志与排障




