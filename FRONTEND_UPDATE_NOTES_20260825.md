# EMSD AI Tool 前端更新说明

版本日期：2026-08-25  
分支：`compat/from-colleague-main-20260825`  
基线：同事仓库 `main`，提交 `c66a1aea40787664be1fee7796a8c235bcf8c235`  
当前提交：`75482866f2ceb565cb41a434ca6ba4435a16aea0`

## 本次更新目的

在不改变同事原有 UI 布局、按钮流程和路径算法的前提下，补齐当前后端接口所需的前端数据映射，并减少路径预览中的重复标记点。

## 功能更新

### 1. 房间门信息传递

- 房间识别后，将已解析的门中心、门宽、门高和门方向传入房间适配请求。
- 优先使用当前房间实际解析出的门信息；旧字段仍保留作为兼容回退。
- 解决房间能识别但房间适配请求没有有效门元数据的问题。

涉及文件：

- `CadToRevit/UI/Dockable/RoomRecognitionPaneRuntime.cs`
- `CadToRevit/UI/Dockable/RoomRecognitionPaneModels.cs`
- `CadToRevit/UI/Dockable/RoomDetailPaneViewModel.cs`
- `CadToRevit/Services/Rooms/EquipmentValidation/AhuPlacementValidationService.cs`
- `CadToRevit/Models/Rooms/EquipmentValidation/AhuPlacementValidationRequest.cs`

### 2. DWG 门候选结果接入

- 支持从已导入 DWG 的门候选结果中解析门中心、宽度、方向和边界信息。
- 增加常见 DWG 门图层名称的兼容匹配，避免因图层命名不同而漏掉门候选。
- 当 Revit 原生门信息不足时，使用导入 DWG 的候选门作为适配请求输入。

涉及文件：

- `CadToRevit/UI/Dockable/RoomRecognitionPaneRuntime.cs`
- `CadToRevit/Services/Rooms/EquipmentValidation/AhuPlacementValidationService.cs`

### 3. AHU 模块和房间适配请求兼容

- 保留 IFC 毫米坐标作为 API 边界单位。
- 继续传递房间高度、维护距离、贴墙/对门设置、禁区和六模块多边形数据。
- 优先使用后端设备目录返回的 Excel 模块布局；后端不可用时保留原有本地目录回退。
- 后端返回可行放置点时，前端继续使用该点进行 Revit 放置。

### 4. 路径预览显示优化

- 路径验证仍检查所有后端返回的运输组。
- 预览显示只选取最大的已验证运输组，避免同一条路径叠加显示多个重复设备路径。
- 路径实体几何仍使用后端完整路径点，不改变实际计算结果。
- 黑色路径点按约 1 米间距抽样，同时保留起点和终点，减少视图中的点数量。

涉及文件：

- `CadToRevit/Services/PathPreview/CalculatePathApiService.cs`
- `CadToRevit/Services/PathPreview/Path3DVisualizationService.cs`
- `CadToRevit/Services/PathPreview/PathPreviewConstants.cs`

## 明确未修改的内容

- 未重构 IFC/Revit 坐标系统；坐标转换仍集中在 API 边界。
- 未替换后端路径规划算法。
- 未修改同事的 XAML/UI 布局和原有按钮交互流程。
- 未修改 IFC 原文件、Revit 族定义或设备尺寸表。
- 未把“路径显示抽样”用于碰撞检测；抽样只影响预览标记点。

## 版本控制与验证

- 本分支直接从同事最新 `main` 提交创建，merge-base 为：
  `c66a1aea40787664be1fee7796a8c235bcf8c235`。
- `git diff --check` 已通过。
- GitHub 分支已上传：
  `compat/from-colleague-main-20260825`
- 需要在 Revit 2024 中验证时，应使用与本分支源码对应的 Revit 2024 构建产物，并在插件安全提示中选择 **Always Load**。

## 回退方式

如需回退本次前端更新，可将插件源码切回同事基线提交 `c66a1ae`；本次更新的五个功能提交均为独立提交，便于逐项回退。
