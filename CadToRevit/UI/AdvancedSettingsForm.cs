using Autodesk.Revit.DB;
using CadToRevit.Models.Mapping;
using CadToRevit.Models.Topology;
using CadToRevit.Services;
using CadToRevit.Services.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using WinPanel = System.Windows.Forms.Panel;

namespace CadToRevit.UI
{
    public sealed class AdvancedSettingsForm : System.Windows.Forms.Form
    {
        private sealed class LevelOption
        {
            public ElementId Id { get; set; }

            public string Name { get; set; }

            public override string ToString()
            {
                return Name ?? string.Empty;
            }
        }

        private readonly List<ParameterOption> _parameterOptions;
        private readonly List<LevelOption> _levelOptions;
        private readonly List<ParameterMapping> _mappings;
        private readonly MapCategory _category;
        private readonly string _rawLayerName;

        private readonly Label _lblLayerInfo = new Label();
        private readonly Label _lblCategoryInfo = new Label();
        private readonly CheckBox _chkEnableOverride = new CheckBox();
        private readonly CheckBox _chkCategoryDefault = new CheckBox();

        private readonly TabControl _tabs = new TabControl();
        private readonly TabPage _tabGeneral = new TabPage("普通模式");
        private readonly TabPage _tabExpert = new TabPage("专家模式");
        private readonly TabPage _tabRevit = new TabPage("Revit 参数");
        private readonly TabPage _tabDebug = new TabPage("调试信息");

        private readonly GroupBox _grpDoor = new GroupBox();
        private readonly GroupBox _grpWindow = new GroupBox();
        private readonly GroupBox _grpBeam = new GroupBox();
        private readonly GroupBox _grpColumnGeneral = new GroupBox();
        private readonly GroupBox _grpColumnExpert = new GroupBox();
        private readonly GroupBox _grpColumnAttach = new GroupBox();
        private readonly GroupBox _grpColumnDebug = new GroupBox();
        private readonly GroupBox _grpWallBasic = new GroupBox();
        private readonly GroupBox _grpWallExpert = new GroupBox();
        private readonly GroupBox _grpJuncture = new GroupBox();

        private readonly TextBox _txtDoorExpectedWidth = new TextBox();
        private readonly TextBox _txtDoorHeight = new TextBox();
        private readonly TextBox _txtDoorSillHeight = new TextBox();

        private readonly TextBox _txtWindowHeight = new TextBox();
        private readonly TextBox _txtWindowSillHeight = new TextBox();
        private readonly CheckBox _chkWindowUseSillPlusHeight = new CheckBox();
        private readonly TextBox _txtBeamMinLength = new TextBox();
        private readonly TextBox _txtBeamElevationOffset = new TextBox();
        private readonly CheckBox _chkBeamEnableMergeCollinear = new CheckBox();
        private readonly TextBox _txtBeamEndpointMergeTol = new TextBox();
        private readonly TextBox _txtBeamParallelAngleTol = new TextBox();
        private readonly CheckBox _chkBeamAllowArc = new CheckBox();
        private readonly ComboBox _cmbColumnAlgorithm = new ComboBox();
        private readonly TextBox _txtColumnClusterTol = new TextBox();
        private readonly TextBox _txtColumnEndpointTol = new TextBox();
        private readonly TextBox _txtColumnGapTol = new TextBox();
        private readonly TextBox _txtColumnMinGroupSegments = new TextBox();
        private readonly TextBox _txtColumnMinSize = new TextBox();
        private readonly TextBox _txtColumnMaxSize = new TextBox();
        private readonly TextBox _txtColumnMinArea = new TextBox();
        private readonly TextBox _txtColumnMaxAspect = new TextBox();
        private readonly TextBox _txtColumnMinFill = new TextBox();
        private readonly CheckBox _chkColumnEnableLongLineFilter = new CheckBox();
        private readonly TextBox _txtColumnMaxSegmentLength = new TextBox();
        private readonly CheckBox _chkColumnEnableMerge = new CheckBox();
        private readonly TextBox _txtColumnMergeTol = new TextBox();
        private readonly ComboBox _cmbColumnMergeStrategy = new ComboBox();
        private readonly TextBox _txtColumnDedupePlacedTol = new TextBox();
        private readonly TextBox _txtColumnAreaWeight = new TextBox();
        private readonly TextBox _txtColumnSegmentCountWeight = new TextBox();
        private readonly TextBox _txtColumnRectnessWeight = new TextBox();
        private readonly TextBox _txtColumnLongLinePenalty = new TextBox();
        private readonly CheckBox _chkColumnIrregularEnable = new CheckBox();
        private readonly TextBox _txtColumnIrregularMaxSize = new TextBox();
        private readonly TextBox _txtColumnIrregularGapTol = new TextBox();
        private readonly TextBox _txtColumnIrregularMinArea = new TextBox();
        private readonly CheckBox _chkColumnAttachEnable = new CheckBox();
        private readonly TextBox _txtColumnAttachSnapTol = new TextBox();
        private readonly ComboBox _cmbColumnAttachTarget = new ComboBox();
        private readonly CheckBox _chkColumnAttachAllowOverlap = new CheckBox();
        private readonly CheckBox _chkColumnDebugDrawCandidates = new CheckBox();
        private readonly CheckBox _chkColumnDebugDrawClusterId = new CheckBox();
        private readonly CheckBox _chkColumnDebugDrawRejectReason = new CheckBox();
        private readonly CheckBox _chkColumnDebugExportReport = new CheckBox();

        private readonly TextBox _txtWallMinLength = new TextBox();
        private readonly TextBox _txtWallHeight = new TextBox();
        private readonly TextBox _txtWallBaseOffset = new TextBox();
        private readonly TextBox _txtWallThicknessTol = new TextBox();
        private readonly TextBox _txtWallMaxThickness = new TextBox();
        private readonly TextBox _txtWallDefaultSingleThickness = new TextBox();
        private readonly TextBox _txtWallParallelAngleTol = new TextBox();
        private readonly TextBox _txtWallEndpointMergeTol = new TextBox();
        private readonly TextBox _txtWallArcThicknessTol = new TextBox();

        private readonly TextBox _txtEndpointClusterTol = new TextBox();
        private readonly TextBox _txtExtendSearchTol = new TextBox();
        private readonly TextBox _txtDuplicateTol = new TextBox();
        private readonly TextBox _txtAngleSnapDeg = new TextBox();
        private readonly TextBox _txtExtendCollinearTol = new TextBox();
        private readonly TextBox _txtCollinearOffsetTol = new TextBox();
        private readonly TextBox _txtExtendProjectionTol = new TextBox();
        private readonly CheckBox _chkAutoDoubleThickness = new CheckBox();
        private readonly TextBox _txtAutoThicknessTopK = new TextBox();
        private readonly TextBox _txtAutoThicknessBin = new TextBox();
        private readonly TextBox _txtMinDoubleThickness = new TextBox();
        private readonly TextBox _txtMinDoubleOverlap = new TextBox();

        private readonly CheckBox _chkExtendToIntersection = new CheckBox();
        private readonly CheckBox _chkEndpointClustering = new CheckBox();
        private readonly CheckBox _chkDuplicateRemoval = new CheckBox();
        private readonly CheckBox _chkOrthogonalSnap = new CheckBox();
        private readonly CheckBox _chkExtendCollinear = new CheckBox();
        private readonly CheckBox _chkMergeCollinear = new CheckBox();
        private readonly CheckBox _chkDirectionalClustering = new CheckBox();

        private readonly TextBox _txtIgnoreSmall = new TextBox();
        private readonly TextBox _txtMinWidth = new TextBox();
        private readonly TextBox _txtIgnoreLarge = new TextBox();
        private readonly TextBox _txtMaxWidth = new TextBox();

        private readonly ComboBox _cmbParam = new ComboBox();
        private readonly TextBox _txtValue = new TextBox();
        private readonly ComboBox _cmbLevelValue = new ComboBox();
        private readonly Button _btnAdd = new Button();
        private readonly DataGridView _grid = new DataGridView();

        private readonly TextBox _txtDebug = new TextBox();

        private readonly Button _btnApply = new Button();
        private readonly Button _btnOk = new Button();
        private readonly Button _btnCancel = new Button();

        public AdvancedSettingsRow Result { get; private set; }

        public AdvancedSettingsForm(
            AdvancedSettingsRow current,
            IEnumerable<ParameterOption> parameterOptions,
            IEnumerable<Level> levels,
            MapCategory category,
            string rawLayerName)
        {
            _category = category;
            _rawLayerName = rawLayerName ?? string.Empty;
            _parameterOptions = (parameterOptions ?? Enumerable.Empty<ParameterOption>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ParameterName))
                .GroupBy(x => x.ParameterName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.ParameterName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _levelOptions = (levels ?? Enumerable.Empty<Level>())
                .Select(x => new LevelOption { Id = x.Id, Name = x.Name })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _mappings = CloneMappings(current != null ? current.ParameterMappings : null);

            BuildLayout();
            BindValues(current);
            BindParameterOptions();
            ReloadGrid();
            UpdateCategoryUi();
            UpdateDebugInfo();
        }

        private void BuildLayout()
        {
            Text = "Layer Settings";
            Width = 980;
            Height = 780;
            MinimumSize = new System.Drawing.Size(900, 700);
            StartPosition = FormStartPosition.CenterParent;
            Font = new System.Drawing.Font("Segoe UI", 10F);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Padding = new Padding(12);

            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top;
            top.Height = 92;
            top.ColumnCount = 2;
            top.RowCount = 1;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));

            FlowLayoutPanel infoFlow = new FlowLayoutPanel();
            infoFlow.Dock = DockStyle.Fill;
            infoFlow.FlowDirection = FlowDirection.TopDown;
            infoFlow.WrapContents = false;
            infoFlow.AutoScroll = false;
            infoFlow.Padding = new Padding(4, 6, 0, 0);

            _lblLayerInfo.AutoSize = true;
            _lblLayerInfo.Text = "图层: " + _rawLayerName;

            _lblCategoryInfo.AutoSize = true;
            _lblCategoryInfo.Text = "类别: " + _category;

            infoFlow.Controls.Add(_lblLayerInfo);
            infoFlow.Controls.Add(_lblCategoryInfo);

            FlowLayoutPanel optionFlow = new FlowLayoutPanel();
            optionFlow.Dock = DockStyle.Fill;
            optionFlow.FlowDirection = FlowDirection.TopDown;
            optionFlow.WrapContents = false;
            optionFlow.AutoScroll = false;
            optionFlow.Padding = new Padding(8, 4, 0, 0);

            _chkEnableOverride.AutoSize = true;
            _chkEnableOverride.Text = "启用图层覆盖";
            _chkEnableOverride.Margin = new Padding(3, 2, 3, 2);

            _chkCategoryDefault.AutoSize = true;
            _chkCategoryDefault.Text = "设为类别默认";
            _chkCategoryDefault.Margin = new Padding(3, 6, 3, 2);

            optionFlow.Controls.Add(_chkEnableOverride);
            optionFlow.Controls.Add(_chkCategoryDefault);

            top.Controls.Add(infoFlow, 0, 0);
            top.Controls.Add(optionFlow, 1, 0);

            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(_tabGeneral);
            _tabs.TabPages.Add(_tabExpert);
            _tabs.TabPages.Add(_tabRevit);
            _tabs.TabPages.Add(_tabDebug);

            BuildGeneralTab();
            BuildExpertTab();
            BuildRevitTab();
            BuildDebugTab();

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.AutoSize = true;
            footer.WrapContents = false;
            footer.Padding = new Padding(0, 8, 0, 0);

            _btnCancel.Text = "Cancel";
            _btnCancel.Width = 100;
            _btnCancel.Height = 32;
            _btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            _btnOk.Text = "OK";
            _btnOk.Width = 100;
            _btnOk.Height = 32;
            _btnOk.Click += OnOk;

            _btnApply.Text = "Apply";
            _btnApply.Width = 100;
            _btnApply.Height = 32;
            _btnApply.Click += OnApply;

            footer.Controls.Add(_btnCancel);
            footer.Controls.Add(_btnOk);
            footer.Controls.Add(_btnApply);

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private void BuildGeneralTab()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _tabGeneral.Controls.Add(panel);

            _grpDoor.Text = "门参数";
            _grpDoor.AutoSize = true;
            _grpDoor.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpDoor.Width = 900;
            _grpDoor.Padding = new Padding(12);

            TableLayoutPanel doorTable = CreateFormTable();
            AddFieldRow(
                doorTable,
                "期望门宽 (DoorExpectedWidthMm)",
                _txtDoorExpectedWidth,
                "mm",
                "说明: 用于门候选宽度约束；为空时使用识别默认逻辑。建议 600~1200。");
            AddFieldRow(
                doorTable,
                "门高度 (DoorHeightMm)",
                _txtDoorHeight,
                "mm",
                "说明: 仅作用于当前门图层。");
            AddFieldRow(
                doorTable,
                "门槛高度 (DoorSillHeightMm)",
                _txtDoorSillHeight,
                "mm",
                "说明: 门底离地高度，AI 路径场景建议 0。");
            _grpDoor.Controls.Add(doorTable);

            _grpWindow.Text = "窗参数";
            _grpWindow.AutoSize = true;
            _grpWindow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpWindow.Width = 900;
            _grpWindow.Padding = new Padding(12);
            TableLayoutPanel winTable = CreateFormTable();
            AddFieldRow(winTable, "窗高度 (WindowHeightMm)", _txtWindowHeight, "mm", "说明: 仅作用于当前窗图层。");
            AddFieldRow(winTable, "窗台高度 (WindowSillHeightMm)", _txtWindowSillHeight, "mm", "说明: 当前窗图层的窗台标高。");
            AddCheckRow(winTable, _chkWindowUseSillPlusHeight, "使用窗台高度 + 窗高 推算 HeadHeight");
            _grpWindow.Controls.Add(winTable);

            _grpBeam.Text = "梁参数";
            _grpBeam.AutoSize = true;
            _grpBeam.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpBeam.Width = 900;
            _grpBeam.Padding = new Padding(12);
            TableLayoutPanel beamTable = CreateFormTable();
            AddFieldRow(beamTable, "梁最小长度 (BeamMinLengthMm)", _txtBeamMinLength, "mm", "说明: 小于此长度的线段将被忽略。建议 600~1200。");
            AddFieldRow(beamTable, "梁标高偏移 (BeamElevationOffsetMm)", _txtBeamElevationOffset, "mm", "说明: 相对所选 Level 的 Z 偏移，正值向上。");
            AddCheckRow(beamTable, _chkBeamEnableMergeCollinear, "启用共线合并 (BeamEnableMergeCollinear)");
            AddFieldRow(beamTable, "端点合并容差 (BeamEndpointMergeTolMm)", _txtBeamEndpointMergeTol, "mm", "说明: 合并碎线梁时端点容差。");
            AddFieldRow(beamTable, "角度容差 (BeamParallelAngleTolDeg)", _txtBeamParallelAngleTol, "deg", "说明: 平行判定角度容差。");
            AddCheckRow(beamTable, _chkBeamAllowArc, "允许弧梁 (BeamAllowArc)");
            _grpBeam.Controls.Add(beamTable);

            _grpColumnGeneral.Text = "柱识别基础参数";
            _grpColumnGeneral.AutoSize = true;
            _grpColumnGeneral.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpColumnGeneral.Width = 900;
            _grpColumnGeneral.Padding = new Padding(12);
            TableLayoutPanel colBasic = CreateFormTable();
            _cmbColumnAlgorithm.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbColumnAlgorithm.Items.AddRange(new object[] { "MidpointBFS", "EndpointGraph" });
            if (_cmbColumnAlgorithm.Items.Count > 0) _cmbColumnAlgorithm.SelectedIndex = 0;
            AddComboRow(colBasic, "聚类算法 (ColumnClusterAlgorithm)", _cmbColumnAlgorithm, "说明: 普通模式建议 MidpointBFS。");
            AddFieldRow(colBasic, "聚类距离 (ColumnClusterTolMm)", _txtColumnClusterTol, "mm", "说明: 建议 300~500。");
            AddFieldRow(colBasic, "最小线段数 (ColumnMinGroupSegments)", _txtColumnMinGroupSegments, "count", "说明: 建议 8。");
            AddFieldRow(colBasic, "最小尺寸 (ColumnMinSizeMm)", _txtColumnMinSize, "mm", "说明: 建议 200。");
            AddFieldRow(colBasic, "最大尺寸 (ColumnMaxSizeMm)", _txtColumnMaxSize, "mm", "说明: 建议 1200。");
            AddCheckRow(colBasic, _chkColumnEnableLongLineFilter, "过滤超长线段 (ColumnEnableLongLineFilter)");
            AddFieldRow(colBasic, "最大线长 (ColumnMaxSegmentLengthMm)", _txtColumnMaxSegmentLength, "mm", "说明: 建议 2000。");
            AddCheckRow(colBasic, _chkColumnEnableMerge, "启用候选合并/去重 (ColumnEnableMerge)");
            AddFieldRow(colBasic, "合并距离 (ColumnMergeTolMm)", _txtColumnMergeTol, "mm", "说明: 建议 300。");
            _cmbColumnMergeStrategy.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbColumnMergeStrategy.Items.AddRange(new object[] { "KeepBest", "UnionBbox", "MaxArea" });
            if (_cmbColumnMergeStrategy.Items.Count > 0) _cmbColumnMergeStrategy.SelectedIndex = 0;
            AddComboRow(colBasic, "合并策略 (ColumnMergeStrategy)", _cmbColumnMergeStrategy, "说明: 商业场景推荐 KeepBest。");
            _grpColumnGeneral.Controls.Add(colBasic);

            _grpWallBasic.Text = "墙识别基础参数";
            _grpWallBasic.AutoSize = true;
            _grpWallBasic.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpWallBasic.Width = 900;
            _grpWallBasic.Padding = new Padding(12);

            TableLayoutPanel wallTable = CreateFormTable();
            AddFieldRow(wallTable, "墙高度 (WallHeightMm)", _txtWallHeight, "mm", "说明: 仅作用于当前墙图层。");
            AddFieldRow(wallTable, "墙底部偏移 (WallBaseOffsetMm)", _txtWallBaseOffset, "mm", "说明: 当前墙图层底部偏移。");
            AddFieldRow(wallTable, "最小墙段长度 (MinWallLengthMm)", _txtWallMinLength, "mm", "说明: 小于该长度的墙段将忽略。建议 200~1000。");
            AddFieldRow(wallTable, "墙厚匹配容差 (WallThicknessTolMm)", _txtWallThicknessTol, "mm", "说明: 双线墙厚度允许误差。建议 20~150。");
            AddFieldRow(wallTable, "最大允许墙厚 (MaxWallThicknessMm)", _txtWallMaxThickness, "mm", "说明: 超过该值不识别为墙。建议 300~600。");
            AddFieldRow(wallTable, "单线墙默认厚度 (DefaultSingleWallThicknessMm)", _txtWallDefaultSingleThickness, "mm", "说明: 仅影响单线墙，不影响双线墙。");
            AddFieldRow(wallTable, "平行判定角度容差 (ParallelAngleTolDeg)", _txtWallParallelAngleTol, "deg", "说明: 两线是否平行的角度误差。建议 1~5。");
            AddFieldRow(wallTable, "端点合并容差 (EndpointMergeTolMm)", _txtWallEndpointMergeTol, "mm", "说明: 端点距离小于该值时合并。建议 10~80。");
            AddFieldRow(wallTable, "弧墙厚度容差 (ArcThicknessTolMm)", _txtWallArcThicknessTol, "mm", "说明: 弧墙厚识别容差；无弧墙可留空。");
            _grpWallBasic.Controls.Add(wallTable);

            panel.Controls.Add(_grpWallBasic);
            panel.Controls.Add(_grpColumnGeneral);
            panel.Controls.Add(_grpDoor);
            panel.Controls.Add(_grpWindow);
            panel.Controls.Add(_grpBeam);
        }

        private void BuildExpertTab()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            _tabExpert.Controls.Add(panel);

            _grpWallExpert.Text = "高级拓扑处理设置（专家模式）";
            _grpWallExpert.AutoSize = true;
            _grpWallExpert.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpWallExpert.Width = 900;
            _grpWallExpert.Padding = new Padding(12);

            TableLayoutPanel table = CreateFormTable();
            AddFieldRow(table, "端点聚类容差 (EndpointClusterTolMm)", _txtEndpointClusterTol, "mm", "说明: 用于碎线端点聚类。建议 10~50。");
            AddFieldRow(table, "搜索交点距离 (ExtendSearchTolMm)", _txtExtendSearchTol, "mm", "说明: 延伸到交点的搜索范围。建议 20~120。");
            AddFieldRow(table, "重复线容差 (DuplicateTolMm)", _txtDuplicateTol, "mm", "说明: 重复线去重容差。建议 2~20。");
            AddFieldRow(table, "角度吸附容差 (AngleSnapDeg)", _txtAngleSnapDeg, "deg", "说明: 角度吸附阈值。建议 0.2~2。");
            AddFieldRow(table, "共线延伸容差 (ExtendCollinearTolMm)", _txtExtendCollinearTol, "mm", "说明: 共线修复关键参数。建议 80~300。");
            AddFieldRow(table, "共线偏移容差 (CollinearOffsetTolMm)", _txtCollinearOffsetTol, "mm", "说明: 判断共线偏移的容差。");
            AddFieldRow(table, "投影延伸容差 (ExtendProjectionTolMm)", _txtExtendProjectionTol, "mm", "说明: 延伸时投影容差。");
            AddCheckRow(table, _chkAutoDoubleThickness, "Enable auto double-line thickness (EnableAutoDoubleLineThickness)");
            AddFieldRow(table, "Auto thickness top K (AutoThicknessTopK)", _txtAutoThicknessTopK, "count", "Desc: top peaks for double-line thickness detection.");
            AddFieldRow(table, "Auto thickness bin (AutoThicknessBinMm)", _txtAutoThicknessBin, "mm", "Desc: histogram bin size.");
            AddFieldRow(table, "Min double thickness (MinDoubleLineThicknessMm)", _txtMinDoubleThickness, "mm", "Desc: min candidate thickness.");
            AddFieldRow(table, "Min double overlap (MinDoubleLineOverlapLenMm)", _txtMinDoubleOverlap, "mm", "Desc: min overlap length for pair.");

            AddCheckRow(table, _chkEndpointClustering, "启用端点聚类 (EnableEndpointClustering)");
            AddCheckRow(table, _chkExtendToIntersection, "延伸到交点 (EnableExtendToIntersection)");
            AddCheckRow(table, _chkDuplicateRemoval, "启用重复线去重 (EnableDuplicateRemoval)");
            AddCheckRow(table, _chkOrthogonalSnap, "启用正交吸附 (EnableOrthogonalSnap)");
            AddCheckRow(table, _chkExtendCollinear, "启用共线延伸 (EnableExtendCollinear)");
            AddCheckRow(table, _chkMergeCollinear, "启用共线合并 (EnableMergeCollinear)");
            AddCheckRow(table, _chkDirectionalClustering, "使用方向聚类 (UseDirectionalClustering)");

            _grpWallExpert.Controls.Add(table);

            _grpColumnExpert.Text = "柱识别专家参数";
            _grpColumnExpert.AutoSize = true;
            _grpColumnExpert.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpColumnExpert.Width = 900;
            _grpColumnExpert.Padding = new Padding(12);
            TableLayoutPanel colExpert = CreateFormTable();
            AddFieldRow(colExpert, "端点容差 (ColumnEndpointTolMm)", _txtColumnEndpointTol, "mm", "说明: EndpointGraph 模式使用。");
            AddFieldRow(colExpert, "断裂容差 (ColumnGapTolMm)", _txtColumnGapTol, "mm", "说明: 分段不闭合时可放宽。");
            AddFieldRow(colExpert, "最小面积 (ColumnMinAreaM2)", _txtColumnMinArea, "m2", "说明: 建议 0.04。");
            AddFieldRow(colExpert, "最大长宽比 (ColumnMaxAspectRatio)", _txtColumnMaxAspect, "ratio", "说明: 建议 4.0。");
            AddFieldRow(colExpert, "最小填充率 (ColumnMinFillRatio)", _txtColumnMinFill, "ratio", "说明: 建议 0.25。");
            AddFieldRow(colExpert, "已放置去重距离 (ColumnDedupePlacedTolMm)", _txtColumnDedupePlacedTol, "mm", "说明: 建议 150。");
            AddFieldRow(colExpert, "面积权重 (ColumnAreaWeight)", _txtColumnAreaWeight, "w", "说明: 推荐 1.0。");
            AddFieldRow(colExpert, "线段数权重 (ColumnSegmentCountWeight)", _txtColumnSegmentCountWeight, "w", "说明: 推荐 0.6。");
            AddFieldRow(colExpert, "矩形度权重 (ColumnRectnessWeight)", _txtColumnRectnessWeight, "w", "说明: 推荐 0.8。");
            AddFieldRow(colExpert, "长线惩罚 (ColumnLongLinePenalty)", _txtColumnLongLinePenalty, "w", "说明: 推荐 1.2。");
            AddCheckRow(colExpert, _chkColumnIrregularEnable, "启用异形柱 (ColumnIrregularEnable)");
            AddFieldRow(colExpert, "异形最大尺寸 (ColumnIrregularMaxSizeMm)", _txtColumnIrregularMaxSize, "mm", "说明: 建议 1800。");
            AddFieldRow(colExpert, "异形缺口容差 (ColumnIrregularGapTolMm)", _txtColumnIrregularGapTol, "mm", "说明: 建议 30。");
            AddFieldRow(colExpert, "异形最小面积 (ColumnIrregularMinAreaM2)", _txtColumnIrregularMinArea, "m2", "说明: 建议 0.03。");
            _grpColumnExpert.Controls.Add(colExpert);

            _grpColumnAttach.Text = "柱贴墙参数";
            _grpColumnAttach.AutoSize = true;
            _grpColumnAttach.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpColumnAttach.Width = 900;
            _grpColumnAttach.Padding = new Padding(12);
            TableLayoutPanel colAttach = CreateFormTable();
            AddCheckRow(colAttach, _chkColumnAttachEnable, "启用贴墙吸附 (ColumnAttachToWallEnable)");
            AddFieldRow(colAttach, "吸附距离 (ColumnAttachToWallSnapTolMm)", _txtColumnAttachSnapTol, "mm", "说明: 建议 250。");
            _cmbColumnAttachTarget.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbColumnAttachTarget.Items.AddRange(new object[] { "WallCenterline", "WallFace" });
            if (_cmbColumnAttachTarget.Items.Count > 0) _cmbColumnAttachTarget.SelectedIndex = 0;
            AddComboRow(colAttach, "吸附目标 (ColumnAttachToWallTarget)", _cmbColumnAttachTarget, "说明: 当前优先支持 WallCenterline。");
            AddCheckRow(colAttach, _chkColumnAttachAllowOverlap, "允许与墙重叠 (ColumnAttachToWallAllowOverlap)");
            _grpColumnAttach.Controls.Add(colAttach);

            _grpColumnDebug.Text = "柱调试参数";
            _grpColumnDebug.AutoSize = true;
            _grpColumnDebug.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpColumnDebug.Width = 900;
            _grpColumnDebug.Padding = new Padding(12);
            TableLayoutPanel colDebug = CreateFormTable();
            AddCheckRow(colDebug, _chkColumnDebugDrawCandidates, "显示候选框 (ColumnDebugDrawCandidates)");
            AddCheckRow(colDebug, _chkColumnDebugDrawClusterId, "显示候选编号 (ColumnDebugDrawClusterId)");
            AddCheckRow(colDebug, _chkColumnDebugDrawRejectReason, "显示剔除原因 (ColumnDebugDrawRejectReason)");
            AddCheckRow(colDebug, _chkColumnDebugExportReport, "导出 JSON 报告 (ColumnDebugExportReport)");
            _grpColumnDebug.Controls.Add(colDebug);

            _grpJuncture.Text = "连接修复参数";
            _grpJuncture.AutoSize = true;
            _grpJuncture.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _grpJuncture.Width = 900;
            _grpJuncture.Padding = new Padding(12);

            TableLayoutPanel juncture = CreateFormTable();
            AddFieldRow(juncture, "忽略小于 (IgnoreSmallerThanMm)", _txtIgnoreSmall, "mm", "说明: 小于该值的连接修复将忽略。");
            AddFieldRow(juncture, "最小连接宽度 (MinJunctureWidthMm)", _txtMinWidth, "mm", "说明: 连接最小宽度阈值。");
            AddFieldRow(juncture, "忽略大于 (IgnoreLargerThanMm)", _txtIgnoreLarge, "mm", "说明: 大于该值的连接修复将忽略。");
            AddFieldRow(juncture, "最大连接宽度 (MaxJunctureWidthMm)", _txtMaxWidth, "mm", "说明: 连接最大宽度阈值。");
            _grpJuncture.Controls.Add(juncture);

            panel.Controls.Add(_grpJuncture);
            panel.Controls.Add(_grpWallExpert);
            panel.Controls.Add(_grpColumnExpert);
            panel.Controls.Add(_grpColumnAttach);
            panel.Controls.Add(_grpColumnDebug);
        }

        private void BuildRevitTab()
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Padding = new Padding(10);

            WinPanel top = new WinPanel { Dock = DockStyle.Top, Height = 40 };
            Label lblParam = new Label { Left = 0, Top = 10, Width = 170, Text = "选择参数" };
            _cmbParam.Left = 170;
            _cmbParam.Top = 6;
            _cmbParam.Width = 260;
            _cmbParam.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbParam.SelectedIndexChanged += (s, e) => RefreshValueEditor();

            Label lblValue = new Label { Left = 440, Top = 10, Width = 60, Text = "值" };
            _txtValue.Left = 500;
            _txtValue.Top = 6;
            _txtValue.Width = 220;

            _cmbLevelValue.Left = 500;
            _cmbLevelValue.Top = 6;
            _cmbLevelValue.Width = 220;
            _cmbLevelValue.DropDownStyle = ComboBoxStyle.DropDownList;

            _btnAdd.Text = "Add";
            _btnAdd.Left = 730;
            _btnAdd.Top = 6;
            _btnAdd.Width = 90;
            _btnAdd.Height = 28;
            _btnAdd.Click += OnAddMapping;

            top.Controls.Add(lblParam);
            top.Controls.Add(_cmbParam);
            top.Controls.Add(lblValue);
            top.Controls.Add(_txtValue);
            top.Controls.Add(_cmbLevelValue);
            top.Controls.Add(_btnAdd);

            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.EnableHeadersVisualStyles = false;
            _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.EnableResizing;
            _grid.ColumnHeadersHeight = 34;
            _grid.RowTemplate.Height = 32;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.CellContentClick += OnGridCellContentClick;
            _grid.Columns.Add("colParam", "Parameter");
            _grid.Columns.Add("colType", "StorageType");
            _grid.Columns.Add("colValue", "Value");
            DataGridViewButtonColumn colDelete = new DataGridViewButtonColumn();
            colDelete.Name = "colDelete";
            colDelete.HeaderText = "Delete";
            colDelete.Text = "Delete";
            colDelete.UseColumnTextForButtonValue = true;
            _grid.Columns.Add(colDelete);

            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(_grid, 0, 1);
            _tabRevit.Controls.Add(layout);
        }

        private void BuildDebugTab()
        {
            _txtDebug.Dock = DockStyle.Fill;
            _txtDebug.Multiline = true;
            _txtDebug.ReadOnly = true;
            _txtDebug.ScrollBars = ScrollBars.Vertical;
            _txtDebug.Font = new System.Drawing.Font("Consolas", 10F);
            _tabDebug.Padding = new Padding(10);
            _tabDebug.Controls.Add(_txtDebug);
        }

        private static TableLayoutPanel CreateFormTable()
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.AutoSize = true;
            table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            table.ColumnCount = 3;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return table;
        }

        private static void AddFieldRow(TableLayoutPanel table, string name, TextBox input, string unit, string desc)
        {
            int row = table.RowCount;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowCount += 2;

            Label lbl = new Label();
            lbl.Text = name;
            lbl.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            lbl.Dock = DockStyle.Fill;
            lbl.AutoSize = true;

            input.Dock = DockStyle.Left;
            input.Width = 150;

            Label lblUnit = new Label();
            lblUnit.Text = unit;
            lblUnit.AutoSize = true;
            lblUnit.Dock = DockStyle.Left;
            lblUnit.Padding = new Padding(8, 6, 0, 0);

            Label lblDesc = new Label();
            lblDesc.Text = desc;
            lblDesc.AutoSize = true;
            lblDesc.Dock = DockStyle.Fill;
            lblDesc.ForeColor = System.Drawing.Color.DimGray;
            lblDesc.Padding = new Padding(8, 0, 0, 8);

            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(input, 1, row);
            table.Controls.Add(lblUnit, 2, row);
            table.Controls.Add(lblDesc, 0, row + 1);
            table.SetColumnSpan(lblDesc, 3);
        }

        private static void AddComboRow(TableLayoutPanel table, string name, ComboBox input, string desc)
        {
            int row = table.RowCount;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowCount += 2;

            Label lbl = new Label();
            lbl.Text = name;
            lbl.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            lbl.Dock = DockStyle.Fill;
            lbl.AutoSize = true;

            input.Dock = DockStyle.Left;
            input.Width = 220;

            Label placeHolder = new Label();
            placeHolder.Text = string.Empty;
            placeHolder.AutoSize = true;
            placeHolder.Dock = DockStyle.Left;

            Label lblDesc = new Label();
            lblDesc.Text = desc;
            lblDesc.AutoSize = true;
            lblDesc.Dock = DockStyle.Fill;
            lblDesc.ForeColor = System.Drawing.Color.DimGray;
            lblDesc.Padding = new Padding(8, 0, 0, 8);

            table.Controls.Add(lbl, 0, row);
            table.Controls.Add(input, 1, row);
            table.Controls.Add(placeHolder, 2, row);
            table.Controls.Add(lblDesc, 0, row + 1);
            table.SetColumnSpan(lblDesc, 3);
        }

        private static void AddCheckRow(TableLayoutPanel table, CheckBox check, string text)
        {
            int row = table.RowCount;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowCount += 1;
            check.Text = text;
            check.AutoSize = true;
            check.Dock = DockStyle.Left;
            check.Margin = new Padding(335, 4, 0, 4);
            table.Controls.Add(check, 0, row);
            table.SetColumnSpan(check, 3);
        }

        private void BindValues(AdvancedSettingsRow current)
        {
            WallRecognitionConfig cfg = WallRecognitionConfigProvider.Load();
            TopologySettings topo = cfg != null && cfg.Topology != null ? cfg.Topology : new TopologySettings();
            JunctureSettings juncture = current != null && current.Juncture != null ? current.Juncture : new JunctureSettings();
            _chkEnableOverride.Checked = current != null && current.EnableLayerOverride;
            _chkCategoryDefault.Checked = current != null && current.ApplyAsCategoryDefault;

            _txtDoorExpectedWidth.Text = Format(current != null ? current.DoorExpectedWidthMm : null);
            _txtDoorHeight.Text = Format(Coalesce(current != null ? current.DoorHeightMm : null, 2100.0));
            _txtDoorSillHeight.Text = Format(Coalesce(current != null ? current.DoorSillHeightMm : null, 0.0));

            _txtWindowHeight.Text = Format(Coalesce(current != null ? current.WindowHeightMm : null, 1500.0));
            _txtWindowSillHeight.Text = Format(Coalesce(current != null ? current.WindowSillHeightMm : null, 900.0));
            _chkWindowUseSillPlusHeight.Checked = Coalesce(current != null ? current.WindowUseSillPlusHeight : null, true);
            _txtBeamMinLength.Text = Format(Coalesce(current != null ? current.BeamMinLengthMm : null, 800.0));
            _txtBeamElevationOffset.Text = Format(Coalesce(current != null ? current.BeamElevationOffsetMm : null, 3000.0));
            _chkBeamEnableMergeCollinear.Checked = Coalesce(current != null ? current.BeamEnableMergeCollinear : null, true);
            _txtBeamEndpointMergeTol.Text = Format(Coalesce(current != null ? current.BeamEndpointMergeTolMm : null, 10.0));
            _txtBeamParallelAngleTol.Text = Format(Coalesce(current != null ? current.BeamParallelAngleTolDeg : null, 3.0));
            _chkBeamAllowArc.Checked = Coalesce(current != null ? current.BeamAllowArc : null, false);
            _cmbColumnAlgorithm.SelectedItem = string.IsNullOrWhiteSpace(current != null ? current.ColumnClusterAlgorithm : null)
                ? "MidpointBFS"
                : current.ColumnClusterAlgorithm;
            if (_cmbColumnAlgorithm.SelectedIndex < 0) _cmbColumnAlgorithm.SelectedItem = "MidpointBFS";
            _txtColumnClusterTol.Text = Format(Coalesce(current != null ? current.ColumnClusterTolMm : null, 350.0));
            _txtColumnEndpointTol.Text = Format(Coalesce(current != null ? current.ColumnEndpointTolMm : null, 30.0));
            _txtColumnGapTol.Text = Format(Coalesce(current != null ? current.ColumnGapTolMm : null, 50.0));
            _txtColumnMinGroupSegments.Text = (current != null && current.ColumnMinGroupSegments.HasValue ? current.ColumnMinGroupSegments.Value : 8).ToString();
            _txtColumnMinSize.Text = Format(Coalesce(current != null ? current.ColumnMinSizeMm : null, 200.0));
            _txtColumnMaxSize.Text = Format(Coalesce(current != null ? current.ColumnMaxSizeMm : null, 1200.0));
            _txtColumnMinArea.Text = Format(Coalesce(current != null ? current.ColumnMinAreaM2 : null, 0.04));
            _txtColumnMaxAspect.Text = Format(Coalesce(current != null ? current.ColumnMaxAspectRatio : null, 4.0));
            _txtColumnMinFill.Text = Format(Coalesce(current != null ? current.ColumnMinFillRatio : null, 0.25));
            _chkColumnEnableLongLineFilter.Checked = Coalesce(current != null ? current.ColumnEnableLongLineFilter : null, true);
            _txtColumnMaxSegmentLength.Text = Format(Coalesce(current != null ? current.ColumnMaxSegmentLengthMm : null, 2000.0));
            _chkColumnEnableMerge.Checked = Coalesce(current != null ? current.ColumnEnableMerge : null, true);
            _txtColumnMergeTol.Text = Format(Coalesce(current != null ? current.ColumnMergeTolMm : null, 300.0));
            _cmbColumnMergeStrategy.SelectedItem = string.IsNullOrWhiteSpace(current != null ? current.ColumnMergeStrategy : null)
                ? "KeepBest"
                : current.ColumnMergeStrategy;
            if (_cmbColumnMergeStrategy.SelectedIndex < 0) _cmbColumnMergeStrategy.SelectedItem = "KeepBest";
            _txtColumnDedupePlacedTol.Text = Format(Coalesce(current != null ? current.ColumnDedupePlacedTolMm : null, 150.0));
            _txtColumnAreaWeight.Text = Format(Coalesce(current != null ? current.ColumnAreaWeight : null, 1.0));
            _txtColumnSegmentCountWeight.Text = Format(Coalesce(current != null ? current.ColumnSegmentCountWeight : null, 0.6));
            _txtColumnRectnessWeight.Text = Format(Coalesce(current != null ? current.ColumnRectnessWeight : null, 0.8));
            _txtColumnLongLinePenalty.Text = Format(Coalesce(current != null ? current.ColumnLongLinePenalty : null, 1.2));
            _chkColumnIrregularEnable.Checked = Coalesce(current != null ? current.ColumnIrregularEnable : null, true);
            _txtColumnIrregularMaxSize.Text = Format(Coalesce(current != null ? current.ColumnIrregularMaxSizeMm : null, 1800.0));
            _txtColumnIrregularGapTol.Text = Format(Coalesce(current != null ? current.ColumnIrregularGapTolMm : null, 30.0));
            _txtColumnIrregularMinArea.Text = Format(Coalesce(current != null ? current.ColumnIrregularMinAreaM2 : null, 0.03));
            _chkColumnAttachEnable.Checked = Coalesce(current != null ? current.ColumnAttachToWallEnable : null, true);
            _txtColumnAttachSnapTol.Text = Format(Coalesce(current != null ? current.ColumnAttachToWallSnapTolMm : null, 250.0));
            _cmbColumnAttachTarget.SelectedItem = string.IsNullOrWhiteSpace(current != null ? current.ColumnAttachToWallTarget : null)
                ? "WallCenterline"
                : current.ColumnAttachToWallTarget;
            if (_cmbColumnAttachTarget.SelectedIndex < 0) _cmbColumnAttachTarget.SelectedItem = "WallCenterline";
            _chkColumnAttachAllowOverlap.Checked = Coalesce(current != null ? current.ColumnAttachToWallAllowOverlap : null, false);
            _chkColumnDebugDrawCandidates.Checked = Coalesce(current != null ? current.ColumnDebugDrawCandidates : null, false);
            _chkColumnDebugDrawClusterId.Checked = Coalesce(current != null ? current.ColumnDebugDrawClusterId : null, false);
            _chkColumnDebugDrawRejectReason.Checked = Coalesce(current != null ? current.ColumnDebugDrawRejectReason : null, false);
            _chkColumnDebugExportReport.Checked = Coalesce(current != null ? current.ColumnDebugExportReport : null, true);

            _txtWallHeight.Text = Format(Coalesce(current != null ? current.WallHeightMm : null, 4000.0));
            _txtWallBaseOffset.Text = Format(Coalesce(current != null ? current.WallBaseOffsetMm : null, 0.0));

            _txtWallMinLength.Text = Format(Coalesce(current != null ? current.WallMinWallLengthMm : null, cfg != null ? cfg.MinWallLengthMm : 1500.0));
            _txtWallThicknessTol.Text = Format(Coalesce(current != null ? current.WallThicknessTolMm : null, cfg != null ? cfg.WallThicknessTolMm : 20.0));
            _txtWallMaxThickness.Text = Format(Coalesce(current != null ? current.WallMaxWallThicknessMm : null, cfg != null ? cfg.MaxWallThicknessMm : 500.0));
            _txtWallDefaultSingleThickness.Text = Format(Coalesce(current != null ? current.WallDefaultSingleWallThicknessMm : null, cfg != null ? cfg.DefaultSingleWallThicknessMm : 200.0));
            _txtWallParallelAngleTol.Text = Format(Coalesce(current != null ? current.WallParallelAngleTolDeg : null, cfg != null ? cfg.ParallelAngleTolDeg : 2.0));
            _txtWallEndpointMergeTol.Text = Format(Coalesce(current != null ? current.WallEndpointMergeTolMm : null, cfg != null ? cfg.EndpointMergeTolMm : 50.0));
            _txtWallArcThicknessTol.Text = Format(Coalesce(current != null ? current.WallArcThicknessTolMm : null, cfg != null ? cfg.ArcThicknessTolMm : 20.0));

            _txtEndpointClusterTol.Text = Format(Coalesce(current != null ? current.WallEndpointClusterTolMm : null, topo.EndpointClusterTolMm));
            _txtExtendSearchTol.Text = Format(Coalesce(current != null ? current.WallExtendSearchTolMm : null, topo.ExtendSearchTolMm));
            _txtDuplicateTol.Text = Format(Coalesce(current != null ? current.WallDuplicateTolMm : null, topo.DuplicateTolMm));
            _txtAngleSnapDeg.Text = Format(Coalesce(current != null ? current.WallAngleSnapDeg : null, topo.AngleSnapDeg));
            _txtExtendCollinearTol.Text = Format(Coalesce(current != null ? current.WallExtendCollinearTolMm : null, topo.ExtendCollinearTolMm));
            _txtCollinearOffsetTol.Text = Format(Coalesce(current != null ? current.WallCollinearOffsetTolMm : null, topo.CollinearOffsetTolMm));
            _txtExtendProjectionTol.Text = Format(Coalesce(current != null ? current.WallExtendProjectionTolMm : null, topo.ExtendProjectionTolMm));
            _chkAutoDoubleThickness.Checked = Coalesce(current != null ? current.WallEnableAutoDoubleLineThickness : null, true);
            _txtAutoThicknessTopK.Text = (current != null && current.WallAutoThicknessTopK.HasValue ? current.WallAutoThicknessTopK.Value : 3).ToString();
            _txtAutoThicknessBin.Text = Format(Coalesce(current != null ? current.WallAutoThicknessBinMm : null, 10.0));
            _txtMinDoubleThickness.Text = Format(Coalesce(current != null ? current.WallMinDoubleLineThicknessMm : null, 60.0));
            _txtMinDoubleOverlap.Text = Format(Coalesce(current != null ? current.WallMinDoubleLineOverlapLenMm : null, 300.0));

            _chkExtendToIntersection.Checked = Coalesce(current != null ? current.WallEnableExtendToIntersection : null, topo.EnableExtendToIntersection);
            _chkEndpointClustering.Checked = Coalesce(current != null ? current.WallEnableEndpointClustering : null, topo.EnableEndpointClustering);
            _chkDuplicateRemoval.Checked = Coalesce(current != null ? current.WallEnableDuplicateRemoval : null, topo.EnableDuplicateRemoval);
            _chkOrthogonalSnap.Checked = Coalesce(current != null ? current.WallEnableOrthogonalSnap : null, topo.EnableOrthogonalSnap);
            _chkExtendCollinear.Checked = Coalesce(current != null ? current.WallEnableExtendCollinear : null, topo.EnableExtendCollinear);
            _chkMergeCollinear.Checked = Coalesce(current != null ? current.WallEnableMergeCollinear : null, cfg != null ? cfg.EnableMergeCollinear : false);
            _chkDirectionalClustering.Checked = Coalesce(current != null ? current.WallUseDirectionalClustering : null, topo.UseDirectionalClustering);

            _txtIgnoreSmall.Text = juncture.IgnoreSmallerThanMm.ToString("F2");
            _txtMinWidth.Text = juncture.MinJunctureWidthMm.ToString("F2");
            _txtIgnoreLarge.Text = juncture.IgnoreLargerThanMm.ToString("F2");
            _txtMaxWidth.Text = juncture.MaxJunctureWidthMm.ToString("F2");
        }

        private void UpdateCategoryUi()
        {
            bool isWall = _category == MapCategory.Walls;
            bool isDoor = _category == MapCategory.Doors;
            bool isWindow = _category == MapCategory.Windows;
            bool isBeam = _category == MapCategory.Beams;
            bool isColumn = _category == MapCategory.Columns;

            _grpDoor.Visible = isDoor;
            _grpWindow.Visible = isWindow;
            _grpBeam.Visible = isBeam;
            _grpColumnGeneral.Visible = isColumn;
            _grpWallBasic.Visible = isWall;
            _grpWallExpert.Visible = isWall;
            _grpJuncture.Visible = isWall;
            _grpColumnExpert.Visible = false;
            _grpColumnAttach.Visible = isColumn;
            _grpColumnDebug.Visible = isColumn;

            SetExpertTabVisible(isWall);
            _tabExpert.Enabled = isWall;
            _chkCategoryDefault.Enabled = isWall || isDoor || _category == MapCategory.Windows || isBeam || isColumn;
        }

        private void SetExpertTabVisible(bool visible)
        {
            bool contains = _tabs.TabPages.Contains(_tabExpert);
            if (visible)
            {
                if (!contains)
                {
                    int index = _tabs.TabPages.Contains(_tabGeneral)
                        ? _tabs.TabPages.IndexOf(_tabGeneral) + 1
                        : _tabs.TabPages.Count;
                    _tabs.TabPages.Insert(index, _tabExpert);
                }

                return;
            }

            if (contains)
            {
                if (_tabs.SelectedTab == _tabExpert)
                {
                    _tabs.SelectedTab = _tabGeneral;
                }

                _tabs.TabPages.Remove(_tabExpert);
            }
        }

        private void UpdateDebugInfo()
        {
            string logRoot = DiagnosticRecorder.GetLogDirectory();
            _txtDebug.Text =
                "Layer: " + _rawLayerName + Environment.NewLine +
                "Category: " + _category + Environment.NewLine +
                "" + Environment.NewLine +
                "调试日志目录:" + Environment.NewLine +
                logRoot + Environment.NewLine +
                "" + Environment.NewLine +
                "说明:" + Environment.NewLine +
                "1) Analyze/Create 后会在 LOG 目录生成诊断文件。" + Environment.NewLine +
                "2) 墙统计查看: mvp1_wall_diag_*.json / *.txt" + Environment.NewLine +
                "3) 实时调试查看: mvp1_debug_YYYYMMDD.log";
        }

        private void BindParameterOptions()
        {
            _cmbParam.Items.Clear();
            foreach (ParameterOption option in _parameterOptions)
            {
                _cmbParam.Items.Add(option.ParameterName);
            }

            if (_cmbParam.Items.Count > 0)
            {
                _cmbParam.SelectedIndex = 0;
            }

            _cmbLevelValue.Items.Clear();
            foreach (LevelOption level in _levelOptions)
            {
                _cmbLevelValue.Items.Add(level);
            }

            if (_cmbLevelValue.Items.Count > 0)
            {
                _cmbLevelValue.SelectedIndex = 0;
            }

            RefreshValueEditor();
        }

        private void RefreshValueEditor()
        {
            ParameterOption option = GetSelectedOption();
            bool useLevelCombo = option != null && option.IsLevelElementId;
            _cmbLevelValue.Visible = useLevelCombo;
            _txtValue.Visible = !useLevelCombo;
        }

        private ParameterOption GetSelectedOption()
        {
            string name = _cmbParam.SelectedItem == null ? string.Empty : _cmbParam.SelectedItem.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return _parameterOptions.FirstOrDefault(x => string.Equals(x.ParameterName, name, StringComparison.OrdinalIgnoreCase));
        }

        private void OnAddMapping(object sender, EventArgs e)
        {
            ParameterOption option = GetSelectedOption();
            if (option == null)
            {
                MessageBox.Show("Please select a parameter.", "Layer Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            object value;
            if (option.IsLevelElementId)
            {
                LevelOption level = _cmbLevelValue.SelectedItem as LevelOption;
                if (level == null || string.IsNullOrWhiteSpace(level.Name))
                {
                    MessageBox.Show("Please select a level.", "Layer Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                value = level.Name;
            }
            else
            {
                string text = _txtValue.Text == null ? string.Empty : _txtValue.Text.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    MessageBox.Show("Please enter value.", "Layer Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                value = text;
            }

            _mappings.Add(new ParameterMapping
            {
                ParameterName = option.ParameterName,
                StorageType = option.StorageType,
                Value = value
            });
            ReloadGrid();
            _txtValue.Text = string.Empty;
        }

        private void ReloadGrid()
        {
            _grid.Rows.Clear();
            for (int i = 0; i < _mappings.Count; i++)
            {
                ParameterMapping mapping = _mappings[i];
                string valueText = mapping.Value == null ? string.Empty : mapping.Value.ToString();
                _grid.Rows.Add(mapping.ParameterName, mapping.StorageType, valueText);
            }
        }

        private void OnGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            string name = _grid.Columns[e.ColumnIndex].Name;
            if (!string.Equals(name, "colDelete", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (e.RowIndex >= 0 && e.RowIndex < _mappings.Count)
            {
                _mappings.RemoveAt(e.RowIndex);
                ReloadGrid();
            }
        }

        private void OnApply(object sender, EventArgs e)
        {
            AdvancedSettingsRow parsed;
            string error;
            if (!TryBuildResult(out parsed, out error))
            {
                MessageBox.Show("参数校验失败：" + error, "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = parsed;
            MessageBox.Show(
                "已将当前参数应用到本次会话。" + Environment.NewLine +
                "提示：勾选“启用图层覆盖”时，关闭主向导窗口后会自动保存到：" + Environment.NewLine +
                "%AppData%\\CadToRevit\\HelixWizard\\Overrides\\layer_overrides.json" + Environment.NewLine +
                "（并同步到 RVT Extensible Storage）",
                "已应用（未保存）",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnOk(object sender, EventArgs e)
        {
            AdvancedSettingsRow parsed;
            string error;
            if (!TryBuildResult(out parsed, out error))
            {
                MessageBox.Show("参数校验失败：" + error, "输入无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = parsed;
            MessageBox.Show(
                "已更新当前图层设置。" + Environment.NewLine +
                "提示：勾选“启用图层覆盖”时，关闭主向导窗口后会自动保存覆盖配置。",
                "已更新图层设置",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool TryBuildResult(out AdvancedSettingsRow result, out string error)
        {
            result = null;
            error = string.Empty;

            double ignoreSmall;
            double minWidth;
            double ignoreLarge;
            double maxWidth;
            if (!double.TryParse(_txtIgnoreSmall.Text, out ignoreSmall) ||
                !double.TryParse(_txtMinWidth.Text, out minWidth) ||
                !double.TryParse(_txtIgnoreLarge.Text, out ignoreLarge) ||
                !double.TryParse(_txtMaxWidth.Text, out maxWidth))
            {
                error = "连接修复参数必须为数字。";
                return false;
            }

            int minGroupSegments;
            if (!int.TryParse(_txtColumnMinGroupSegments.Text, out minGroupSegments))
            {
                minGroupSegments = 8;
            }
            int autoTopK;
            if (!int.TryParse(_txtAutoThicknessTopK.Text, out autoTopK))
            {
                autoTopK = 3;
            }
            autoTopK = Math.Max(1, autoTopK);

            result = new AdvancedSettingsRow
            {
                EnableLayerOverride = _chkEnableOverride.Checked,
                ApplyAsCategoryDefault = _chkCategoryDefault.Checked,
                DoorExpectedWidthMm = ParseNullable(_txtDoorExpectedWidth.Text),
                DoorHeightMm = ParseNullable(_txtDoorHeight.Text),
                DoorSillHeightMm = ParseNullable(_txtDoorSillHeight.Text),
                BeamMinLengthMm = ParseNullable(_txtBeamMinLength.Text),
                BeamElevationOffsetMm = ParseNullable(_txtBeamElevationOffset.Text),
                BeamEnableMergeCollinear = _chkBeamEnableMergeCollinear.Checked,
                BeamEndpointMergeTolMm = ParseNullable(_txtBeamEndpointMergeTol.Text),
                BeamParallelAngleTolDeg = ParseNullable(_txtBeamParallelAngleTol.Text),
                BeamAllowArc = _chkBeamAllowArc.Checked,
                WindowHeightMm = ParseNullable(_txtWindowHeight.Text),
                WindowSillHeightMm = ParseNullable(_txtWindowSillHeight.Text),
                WindowUseSillPlusHeight = _chkWindowUseSillPlusHeight.Checked,
                ColumnClusterAlgorithm = _cmbColumnAlgorithm.SelectedItem == null ? "MidpointBFS" : _cmbColumnAlgorithm.SelectedItem.ToString(),
                ColumnClusterTolMm = ParseNullable(_txtColumnClusterTol.Text),
                ColumnEndpointTolMm = ParseNullable(_txtColumnEndpointTol.Text),
                ColumnGapTolMm = ParseNullable(_txtColumnGapTol.Text),
                ColumnMinGroupSegments = minGroupSegments,
                ColumnMinSizeMm = ParseNullable(_txtColumnMinSize.Text),
                ColumnMaxSizeMm = ParseNullable(_txtColumnMaxSize.Text),
                ColumnMinAreaM2 = ParseNullable(_txtColumnMinArea.Text),
                ColumnMaxAspectRatio = ParseNullable(_txtColumnMaxAspect.Text),
                ColumnMinFillRatio = ParseNullable(_txtColumnMinFill.Text),
                ColumnEnableLongLineFilter = _chkColumnEnableLongLineFilter.Checked,
                ColumnMaxSegmentLengthMm = ParseNullable(_txtColumnMaxSegmentLength.Text),
                ColumnEnableMerge = _chkColumnEnableMerge.Checked,
                ColumnMergeTolMm = ParseNullable(_txtColumnMergeTol.Text),
                ColumnMergeStrategy = _cmbColumnMergeStrategy.SelectedItem == null ? "KeepBest" : _cmbColumnMergeStrategy.SelectedItem.ToString(),
                ColumnDedupePlacedTolMm = ParseNullable(_txtColumnDedupePlacedTol.Text),
                ColumnAreaWeight = ParseNullable(_txtColumnAreaWeight.Text),
                ColumnSegmentCountWeight = ParseNullable(_txtColumnSegmentCountWeight.Text),
                ColumnRectnessWeight = ParseNullable(_txtColumnRectnessWeight.Text),
                ColumnLongLinePenalty = ParseNullable(_txtColumnLongLinePenalty.Text),
                ColumnIrregularEnable = _chkColumnIrregularEnable.Checked,
                ColumnIrregularMaxSizeMm = ParseNullable(_txtColumnIrregularMaxSize.Text),
                ColumnIrregularGapTolMm = ParseNullable(_txtColumnIrregularGapTol.Text),
                ColumnIrregularMinAreaM2 = ParseNullable(_txtColumnIrregularMinArea.Text),
                ColumnAttachToWallEnable = _chkColumnAttachEnable.Checked,
                ColumnAttachToWallSnapTolMm = ParseNullable(_txtColumnAttachSnapTol.Text),
                ColumnAttachToWallTarget = _cmbColumnAttachTarget.SelectedItem == null ? "WallCenterline" : _cmbColumnAttachTarget.SelectedItem.ToString(),
                ColumnAttachToWallAllowOverlap = _chkColumnAttachAllowOverlap.Checked,
                ColumnDebugDrawCandidates = _chkColumnDebugDrawCandidates.Checked,
                ColumnDebugDrawClusterId = _chkColumnDebugDrawClusterId.Checked,
                ColumnDebugDrawRejectReason = _chkColumnDebugDrawRejectReason.Checked,
                ColumnDebugExportReport = _chkColumnDebugExportReport.Checked,
                WallHeightMm = ParseNullable(_txtWallHeight.Text),
                WallBaseOffsetMm = ParseNullable(_txtWallBaseOffset.Text),
                WallMinWallLengthMm = ParseNullable(_txtWallMinLength.Text),
                WallThicknessTolMm = ParseNullable(_txtWallThicknessTol.Text),
                WallMaxWallThicknessMm = ParseNullable(_txtWallMaxThickness.Text),
                WallDefaultSingleWallThicknessMm = ParseNullable(_txtWallDefaultSingleThickness.Text),
                WallParallelAngleTolDeg = ParseNullable(_txtWallParallelAngleTol.Text),
                WallEndpointMergeTolMm = ParseNullable(_txtWallEndpointMergeTol.Text),
                WallArcThicknessTolMm = ParseNullable(_txtWallArcThicknessTol.Text),
                WallEndpointClusterTolMm = ParseNullable(_txtEndpointClusterTol.Text),
                WallExtendSearchTolMm = ParseNullable(_txtExtendSearchTol.Text),
                WallDuplicateTolMm = ParseNullable(_txtDuplicateTol.Text),
                WallAngleSnapDeg = ParseNullable(_txtAngleSnapDeg.Text),
                WallEnableOrthogonalSnap = _chkOrthogonalSnap.Checked,
                WallEnableExtendToIntersection = _chkExtendToIntersection.Checked,
                WallEnableEndpointClustering = _chkEndpointClustering.Checked,
                WallEnableDuplicateRemoval = _chkDuplicateRemoval.Checked,
                WallEnableExtendCollinear = _chkExtendCollinear.Checked,
                WallEnableMergeCollinear = _chkMergeCollinear.Checked,
                WallExtendCollinearTolMm = ParseNullable(_txtExtendCollinearTol.Text),
                WallCollinearOffsetTolMm = ParseNullable(_txtCollinearOffsetTol.Text),
                WallExtendProjectionTolMm = ParseNullable(_txtExtendProjectionTol.Text),
                WallUseDirectionalClustering = _chkDirectionalClustering.Checked,
                WallEnableAutoDoubleLineThickness = _chkAutoDoubleThickness.Checked,
                WallAutoThicknessTopK = autoTopK,
                WallAutoThicknessBinMm = ParseNullable(_txtAutoThicknessBin.Text),
                WallMinDoubleLineThicknessMm = ParseNullable(_txtMinDoubleThickness.Text),
                WallMinDoubleLineOverlapLenMm = ParseNullable(_txtMinDoubleOverlap.Text),
                Juncture = new JunctureSettings
                {
                    IgnoreSmallerThanMm = ignoreSmall,
                    MinJunctureWidthMm = minWidth,
                    IgnoreLargerThanMm = ignoreLarge,
                    MaxJunctureWidthMm = maxWidth
                },
                ParameterMappings = CloneMappings(_mappings)
            };

            return true;
        }

        private static double? ParseNullable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            double value;
            if (!double.TryParse(text.Trim(), out value))
            {
                return null;
            }

            return value;
        }

        private static double Coalesce(double? preferred, double fallback)
        {
            return preferred.HasValue ? preferred.Value : fallback;
        }

        private static bool Coalesce(bool? preferred, bool fallback)
        {
            return preferred.HasValue ? preferred.Value : fallback;
        }

        private static string Format(double? value)
        {
            return value.HasValue ? value.Value.ToString("F2") : string.Empty;
        }

        private static List<ParameterMapping> CloneMappings(List<ParameterMapping> source)
        {
            List<ParameterMapping> result = new List<ParameterMapping>();
            if (source == null)
            {
                return result;
            }

            foreach (ParameterMapping mapping in source)
            {
                if (mapping == null || string.IsNullOrWhiteSpace(mapping.ParameterName))
                {
                    continue;
                }

                result.Add(new ParameterMapping
                {
                    ParameterName = mapping.ParameterName,
                    StorageType = mapping.StorageType,
                    Value = mapping.Value
                });
            }

            return result;
        }
    }
}
