# PIDTuner V2.0 架构重构基线

本文档记录 PIDTuner 在 V2.0 阶段进行历史趋势工作台迁移前的架构重构依据。它不是功能需求清单，而是后续代码变更的模块边界、职责划分和迁移顺序。

当前状态：架构重构基线已落地，历史趋势状态逻辑、PLC 回放状态机、PLC 实时诊断会话生命周期、PLC 实时采集运行态、PLC 1s 记录与文件保存、PLC 配置编辑状态、离线分析展示状态与 CSV 分析执行、实验历史列表/对比/建议审查展示状态、参数方案库状态、试验记录仓储编排、PLC 配置文件 workflow、实时点位快照呈现已经从 `MainWindowViewModel` 实质迁出。根 ViewModel 仍然偏大，不能视为最终组合根；后续主要剩余旧 XAML 绑定包装和少量跨模块命令转发。

## 1. 重构目标

V2.0 的架构目标是：

- 保留 PIDTuner 现有项目结构，不整体搬迁 WPFHistoricalTrend。
- 迁移 WPFHistoricalTrend 的关键模型和交互思想，包括历史趋势状态、X/Y 轴操作、图例控制和可见区间导出。
- 实时趋势和历史趋势使用两个适配器，但共享底层 ScottPlot 绘图模块。
- 通过桥接模块把 PIDTuner 的 PLC 采集帧转换为历史趋势工作台数据集。
- 限制 `MainWindowViewModel` 继续膨胀，逐步拆出独立 ViewModel 和应用编排模块。

## 2. 当前 MVVM 职责

### 2.1 Model

当前 Model 主要分布在：

- `src/PIDTuner.Domain`
- `src/PIDTuner.Application`
- `src/PIDTuner.Infrastructure`

具体职责：

- Domain 保存 PID 样本、PLC 点位、PLC 采集帧、PLC 快照、分析指标、推荐结果、实验记录等业务对象。
- Application 定义 CSV 交换、PLC 读取、PLC 诊断、配置存储、实验记录存储等接口和用例。
- Infrastructure 实现 JSON/CSV/SQLite 持久化、S7 通信、Ping 检查、PLC 点位读取等外部资源访问。

### 2.2 View

当前 View 主要包括：

- `src/PIDTuner.Desktop/MainWindow.xaml`
- `src/PIDTuner.Desktop/MainWindow.xaml.cs`

具体职责：

- `MainWindow.xaml` 承载主窗口布局、页面区域、按钮、表格、图表控件和数据绑定。
- `MainWindow.xaml.cs` 负责 WPF 控件事件、ScottPlot 控件挂载、鼠标坐标读取、趋势窗口切换和图表适配器调用。

允许 View 的 code-behind 保留纯视觉逻辑，例如鼠标位置、控件尺寸、图表控件刷新。但不应在 code-behind 中实现业务规则、数据转换和持久化流程。

### 2.3 ViewModel

当前核心 ViewModel 是：

- `src/PIDTuner.Desktop/ViewModels/MainWindowViewModel.cs`

当前问题是它已经同时承担多类职责：

- 离线 CSV 导入和 PID 分析。
- 字段配置、PLC 配置、点位配置的表单状态。
- PLC 通信检查、实时监控启动/停止。
- PLC 诊断、1s 记录、记录回放。
- 实时趋势和历史趋势模式切换。
- 历史记录加载、对比、导出。
- 参数方案和推荐审查。
- 通知提示和大量 UI 格式化。

因此，`MainWindowViewModel` 已经不是单一页面模型，而是事实上的主窗口总控。后续重构必须优先阻止新历史工作台逻辑继续进入该类。

## 3. 目标模块结构

### 3.1 主窗口壳

建议保留：

- `MainWindow`
- `MainWindowViewModel`

目标职责：

- 组合子 ViewModel。
- 承担全局通知和顶层页面状态。
- 持有跨页面共享的配置入口。
- 不直接实现历史趋势缩放、回放、诊断写入、PLC 采集细节。

### 3.2 实时趋势模块

已建立或演进为：

- `PlcLiveMonitorViewModel`
- `LivePlcTrendAdapter`

目标职责：

- 连接实时 PLC 采集引擎。
- 接收实时采集帧。
- 管理滚动窗口、实时点位当前值、启动/停止状态。
- 调用共享绘图模块完成实时趋势绘制。

实时趋势适配器只处理实时追加数据和滚动窗口，不承担历史工作台的静态查询与轴控制。

说明：当前 `MainWindow.xaml` 仍使用既有页面结构。`PlcLiveMonitorViewModel` 已挂载到 `MainWindowViewModel`，并已接管实时采集启动、停止、采集周期解析、采集 buffer 和 UI 呈现帧 drain。根 ViewModel 仍负责把 drain 出来的帧应用到现有点位表和图表事件，后续迁移 XAML 绑定时不需要继续扩大根 ViewModel。

### 3.3 历史趋势工作台模块

已建立：

- `HistoricalTrendWorkbenchViewModel`
- `HistoricalTrendWorkbenchState`
- `HistoricalTrendWorkbenchCoordinator`
- `HistoricalTrendChartAdapter`

目标职责：

- 加载静态历史数据集。
- 管理 X 轴可见时间窗口。
- 管理 Y 轴上下界。
- 管理图例显示/隐藏。
- 管理选中曲线和当前可见数据。
- 导出当前画布可见范围的数据。

历史趋势进入后曲线必须静置，不跟随实时刷新。所有缩放、平移和导出操作都应围绕工作台状态进行。

说明：当前已建立纯状态模型、协调器、历史适配器和历史 ViewModel。尚未迁移 WPFHistoricalTrend 的具体滑块/拖拽/缩放控件。

### 3.4 PLC 配置模块

已建立：

- `PlcConfigurationEditorViewModel`

目标职责：

- PLC 连接配置表单状态。
- 点位配置集合和当前选中点位。
- 新增/删除点位。
- 加载配置后同步表单。
- 从表单构建 `PlcProjectConfiguration`。
- 校验重复点位名称。

该模块不处理趋势绘图、PLC 通信检查、实时采集和历史工作台状态。根 ViewModel 暂时保留旧属性包装，以兼容现有 XAML 绑定。

### 3.5 桥接模块

已建立：

- `PlcTrendDatasetBridge`
- `HistoricalTrendDataset`
- `HistoricalTrendSeries`

目标职责：

- 把 `IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>` 转换为历史趋势工作台数据集。
- 处理点位名称、地址、单位、质量、时间戳和值。
- 处理缺失点、非数值点、无效值。
- 隔离 PIDTuner 采集帧模型和历史趋势工作台模型。

桥接模块是必要的，因为 PLC 采集帧是采集链路模型，而历史趋势工作台需要的是按曲线组织、可按时间区间查询的数据集。

### 3.6 调试模块

已建立或演进为：

- `PlcDebugViewModel`
- `PlcOneSecondRecorder`

目标职责：

- 1s 记录。
- 诊断启动/停止。
- 诊断状态展示。
- 记录文件打开。
- 回放、上一帧、下一帧、播放速度。
- 完整点位调试表。

调试功能不应留在实时监控主界面，也不应继续与历史趋势工作台逻辑耦合。`PlcOneSecondRecorder` 负责一次性记录的采样调度、读取会话复用、诊断帧构造和 JSON 文件保存；根 ViewModel 只负责调用该模块、更新当前点位显示和发出通知。

说明：当前 `PlcDebugViewModel` 已挂载到 `MainWindowViewModel`，并共享当前点位集合。后续可把调试页 XAML 绑定迁移到该子 ViewModel。

### 3.7 共享绘图模块

当前已引入：

- `src/PIDTuner.Desktop/Services/SharedScottPlotTrendRenderer.cs`
- `src/PIDTuner.Desktop/Services/PlcTrendPoint.cs`
- `src/PIDTuner.Desktop/Services/PlcTrendRenderSeries.cs`

当前职责：

- ScottPlot 字体配置。
- 趋势图标题、X/Y 标签、图例配置。
- 曲线绘制。
- X 轴时间范围应用。
- Y 轴手动范围或自动范围应用。

当前调用方：

- `src/PIDTuner.Desktop/Services/LivePlcTrendAdapter.cs`
- `src/PIDTuner.Desktop/Services/HistoricalTrendChartAdapter.cs`

`PlcTrendChartAdapter` 保留为静态兼容入口，仅转发保留窗口计算。共享绘图模块不保存业务状态，不读取 PLC，不访问文件。

### 3.8 离线分析模块

已建立：

- `OfflineAnalysisViewModel`

目标职责：

- 保存当前离线分析窗口、指标、评估、样本和来源文件名。
- 执行离线 CSV 分析和分析窗口解析。
- 格式化分析指标展示文本。
- 生成保守 PID 调参建议展示列表。
- 生成离线趋势预览点集。
- 为分析结果导出、参数方案保存、建议审查提供当前分析状态。

当前 `MainWindowViewModel` 仍保留 CSV 文件选择、字段配置和若干旧 XAML 兼容绑定。试验记录保存、历史列表加载、历史样本加载、历史样本导出和建议审查仓储编排已迁入 `ExperimentSessionCoordinator`；指标展示状态、当前结果和 CSV 分析执行不应再回到根 ViewModel。

绑定收口：

- 分析页的窗口输入、指标展示、评估摘要和离线趋势预览已直接绑定 `OfflineAnalysis.*`。
- 参数调整页的建议摘要和建议列表已直接绑定 `OfflineAnalysis.*`。
- `MainWindowViewModel` 不再提供 `SampleCount`、`OvershootPercent`、`AnalysisStartText`、`TuningRecommendations`、`SetPointPoints` 等离线分析包装属性。

### 3.9 实验历史与审查模块

已建立：

- `ExperimentHistoryViewModel`

目标职责：

- 保存历史记录列表、筛选文本和当前选中历史记录。
- 生成选中历史记录详情文本。
- 管理历史对比基准、对比状态和对比指标集合。
- 格式化历史对比指标的基准值、候选值和差值。
- 保存建议审查列表、审查备注和审查状态文本。

该模块只管理 UI 可观察状态和纯展示转换，不直接访问文件、不直接读写 repository、不执行 PID 分析。试验记录保存、历史样本加载、历史样本导出和建议审查保存等流程已迁入 `ExperimentSessionCoordinator`，而不是放回历史状态 ViewModel。

### 3.10 参数方案库模块

已建立：

- `ParameterSetLibraryViewModel`

目标职责：

- 从当前离线分析样本中提取 PID 参数方案。
- 保存参数方案并刷新参数方案列表。
- 加载参数方案列表并维护参数方案状态文本。
- 格式化参数方案保存后的通知摘要。

该模块持有参数方案 repository 和提取器，根 ViewModel 只传入当前分析样本、来源文件名和试验记录 ID，并负责展示返回的通知结果。

### 3.11 实验会话编排模块

已建立：

- `ExperimentSessionCoordinator`

目标职责：

- 保存离线分析生成的试验记录和样本。
- 加载历史记录列表并附带样本数量。
- 加载单条历史记录的样本。
- 导出历史样本 CSV。
- 保存和加载建议审查记录。

该模块承接原先散落在 `MainWindowViewModel` 中的 repository 编排。它不持有 WPF 状态，也不直接更新通知框。

### 3.12 PLC 配置 workflow 模块

已建立：

- `PlcConfigurationWorkflow`

目标职责：

- 从文件加载 PLC 配置。
- 保存当前 PLC 配置到文件。
- 执行 PLC 通信检查并返回状态文本、通知标题和通知等级。

配置表单状态仍属于 `PlcConfigurationEditorViewModel`。workflow 只处理文件和通信检查流程。

绑定收口：

- PLC 配置页的配置名、协议、IP、Rack、Slot、超时、默认采样、最小采样、点位列表和选中点位已直接绑定 `PlcConfigurationEditor.*`。
- `MainWindowViewModel` 不再提供 `PlcConfigurationName`、`PlcIpAddress`、`PlcDefaultSamplingMilliseconds`、`TagDefinitions` 等 PLC 配置表单包装属性。
- `MainWindowViewModel` 仍保留加载、保存、检查通信命令，因为这些命令需要文件对话框、配置 workflow 和全局通知协作。

### 3.13 实时点位呈现模块

已建立：

- `PlcMonitorSnapshotPresenter`

目标职责：

- 把 PLC 快照应用到实时点位行集合。
- 新增、更新、删除当前点位行。
- 维护默认选中的点位。
- 在需要时向图表层发出趋势点应用事件。

该模块把“快照如何变成 UI 行状态”的细节从根 ViewModel 移走。根 ViewModel 仍保留诊断入队和图表事件转发，因为它们连接调试模块和 View code-behind。

### 3.14 历史趋势绑定收口

当前约束：

- 历史趋势范围文本、X 轴 viewport 滑块、Y 轴范围滑块和启用状态只能通过 `HistoricalTrendWorkbenchViewModel` 暴露。
- `MainWindowViewModel` 不再提供 `PlcHistoricalRangeStartText`、`PlcHistoricalViewportStart`、`PlcTrendYLower` 这类历史趋势细节包装属性。
- 测试和后续 XAML 绑定应直接访问 `HistoricalTrendWorkbench.*`，避免把历史工作台交互状态重新铺回根 ViewModel。

## 4. 关键接口原则

### 4.1 两个适配器

实时趋势和历史趋势使用两个适配器：

- 实时适配器面向高频追加和滚动窗口。
- 历史适配器面向静态数据、缩放、平移和可见导出。

两个适配器共享底层绘图模块，避免字体、图例、坐标轴、颜色、曲线样式重复实现。

### 4.2 一个桥接入口

历史趋势工作台不直接消费 PLC 原始采集帧。它只消费桥接后的历史趋势数据集。

这可以让后续数据来源从 JSON、SQLite、CSV 或实时缓存切换时，历史趋势工作台不需要改变。

### 4.3 ViewModel 不持有绘图细节

ViewModel 可以暴露状态和命令，但不应直接处理：

- ScottPlot 曲线对象。
- 像素坐标转换。
- 鼠标命中检测细节。
- 画布可见区间的底层坐标读取。

这些职责属于 View code-behind、图表适配器或工作台协调器。

### 4.4 工作台状态可测试

历史趋势的 X/Y 轴范围、缩放、平移、图例状态和导出可见区间应尽可能进入纯 C# 状态模型和协调器中。

这些模块不依赖 WPF，不依赖 ScottPlot，因此可以通过单元测试验证。

## 5. 迁移顺序

### 阶段 A：建立共享绘图底座

状态：已完成。

已完成：

- 从现有趋势适配器中抽出 `SharedScottPlotTrendRenderer`。
- 把实时趋势适配器演进为 `LivePlcTrendAdapter`。
- 引入 `PlcTrendRenderSeries`，使共享渲染模块不依赖实时监控 ViewModel。
- 保留 `PlcTrendChartAdapter.CalculateLiveRetentionWindow` 兼容入口。

### 阶段 B：建立历史趋势工作台模型

状态：已完成。

已新增：

- `HistoricalTrendWorkbenchState`
- `HistoricalTrendWorkbenchCoordinator`
- `HistoricalTrendDataset`
- `HistoricalTrendSeries`
- `PlcTrendDatasetBridge`

验收标准：

- 不接入 UI 也能测试 X 轴窗口、Y 轴范围、图例状态和可见数据筛选。
- `MainWindowViewModel` 不新增历史趋势核心算法。

### 阶段 C：建立历史趋势适配器

状态：已完成架构入口。

已新增：

- `HistoricalTrendChartAdapter`

验收标准：

- 可以接收桥接后的历史数据集。
- 可以应用工作台状态绘制静态历史曲线。
- 可以通过工作台状态表达当前可见范围；实际画布交互导出将在历史趋势功能迁移阶段接入。

### 阶段 D：拆分 ViewModel

状态：已完成架构入口，历史趋势状态逻辑已部分迁出，未完成全部 XAML 绑定迁移。

已新增：

- `PlcLiveMonitorViewModel`
- `PlcDebugViewModel`
- `PlcConfigurationEditorViewModel`
- `HistoricalTrendWorkbenchViewModel`

迁移方式：

- 不做一次性大改。
- 每次迁移一个功能簇。
- 每次迁移后保持现有用户功能可运行。
- `MainWindowViewModel` 最终退化为组合根和全局状态入口。

当前约束：

- 后续历史趋势功能不再进入 `MainWindowViewModel`，应进入 `HistoricalTrendWorkbenchViewModel`、`HistoricalTrendWorkbenchCoordinator`、`HistoricalTrendChartAdapter` 或桥接模块。
- `MainWindowViewModel` 当前仍保留旧 XAML 绑定兼容属性和命令包装；这些包装可以保留到 UI 迁移完成，但不应继续承载工作台规则。
- PLC 回放状态机已经迁入 `PlcDebugViewModel`，根 ViewModel 不再保存回放帧、下一帧索引、当前帧索引、倍速和回放状态文本计算。
- PLC 实时诊断会话生命周期已经迁入 `PlcDebugViewModel`，根 ViewModel 只负责按钮命令、计时器启停和通知转发。
- PLC 实时采集运行态已经迁入 `PlcLiveMonitorViewModel`，根 ViewModel 不再直接持有 `PlcAcquisitionEngine` 和 `PlcSampleBuffer`。
- PLC 1s 记录已经迁入 `PlcOneSecondRecorder`，根 ViewModel 不再直接实现一次性采样循环、采集帧诊断构造和记录 JSON 保存。
- PLC 配置编辑状态已经迁入 `PlcConfigurationEditorViewModel`，根 ViewModel 不再直接保存连接表单字段、点位表集合和点位选中状态。
- 离线分析展示状态与 CSV 分析执行已经迁入 `OfflineAnalysisViewModel`，根 ViewModel 不再直接保存当前指标文本、当前分析结果、调参建议列表、离线趋势预览点集，也不再创建离线 CSV 分析用例。
- 不应把“文件行数下降”视为最终目标；最终目标是根 ViewModel 只组合子 ViewModel 和转发全局通知。

## 6. 禁止事项

后续实现应避免：

- 继续把历史趋势缩放、平移、导出逻辑写入 `MainWindowViewModel`。
- 让历史趋势直接依赖 PLC 采集帧原始结构。
- 在 View code-behind 中实现数据持久化、PLC 通信和业务判断。
- 在两个图表适配器中重复 ScottPlot 字体、颜色、图例、坐标轴样式。
- 为了迁移 WPFHistoricalTrend 而破坏 PIDTuner 现有项目层次。

## 7. 架构重构完成标准

进入历史趋势功能迁移前至少应达到以下标准：

- 有独立历史趋势数据集模型：已完成。
- 有 PLC 帧到历史数据集的桥接入口：已完成。
- 有纯 C# 历史工作台状态和协调器：已完成。
- 有实时/历史两个图表适配器：已完成。
- 有共享 ScottPlot 绘图模块：已完成。
- 有历史趋势专用 ViewModel：已完成，并已承接历史范围、滑块和 Y 轴状态逻辑。
- 有实时/调试子 ViewModel 入口：已完成。
- 有覆盖桥接和工作台状态的测试：已完成。
- `MainWindowViewModel` 不再承载历史工作台核心规则：阶段性完成。
- `MainWindowViewModel` 退化为最终组合根：未完成。
- PLC 回放状态机从 `MainWindowViewModel` 迁出：已完成。
- PLC 实时诊断会话生命周期从 `MainWindowViewModel` 迁出：已完成。
- PLC 实时采集运行态从 `MainWindowViewModel` 迁出：已完成。
- PLC 1s 记录与文件保存从 `MainWindowViewModel` 迁出：已完成。
- PLC 配置编辑状态从 `MainWindowViewModel` 迁出：已完成。
- 离线分析展示状态与 CSV 分析执行从 `MainWindowViewModel` 迁出：已完成。

因此，下一阶段可以开始讨论历史趋势功能迁移的具体交互方案，但如果要严格完成架构重构，还应继续拆出 PLC 调试/回放、实时采集控制、配置和离线分析等功能簇。

## 8. 当前代码变更记录

本次重构已经完成：

- 新增 `SharedScottPlotTrendRenderer` 作为共享 ScottPlot 绘图模块。
- 新增 `PlcTrendPoint` 作为桌面图表层内部趋势点模型。
- 新增 `PlcTrendRenderSeries`，隔离渲染输入与具体 ViewModel。
- 新增 `LivePlcTrendAdapter`，承接实时趋势滚动绘制。
- 新增 `HistoricalTrendChartAdapter`，承接历史趋势静态绘制入口。
- 新增历史趋势工作台模型、协调器和桥接模块。
- 新增 `HistoricalTrendWorkbenchViewModel`、`PlcLiveMonitorViewModel`、`PlcDebugViewModel`。
- `MainWindowViewModel` 挂载子 ViewModel，作为后续绑定迁移的组合根。
- `HistoricalTrendWorkbenchViewModel` 已接管历史趋势时间范围、滑块换算、Y 轴范围和图表请求事件。
- `PlcDebugViewModel` 已接管 PLC 回放帧集合、回放索引、播放速度、播放状态文本和单帧/连续播放状态机。
- `PlcDebugViewModel` 已接管 PLC 实时诊断 session 的启动、过期停止、手动停止、帧入队、摘要文本和按钮文本状态。
- `PlcLiveMonitorViewModel` 已接管 PLC 实时采集的启动、停止、采集周期解析、采集 buffer、呈现帧 drain 和采集诊断摘要文本。
- 新增 `PlcOneSecondRecorder`，接管 1s 记录的启用点位校验、最快点位周期解析、单会话采样、诊断帧构造和记录 JSON 保存。
- 新增 `PlcConfigurationEditorViewModel`，接管 PLC 连接表单字段、点位表、选中点位、新增/删除点位、配置构建和重复点位名称校验。
- `OfflineAnalysisViewModel` 已接管离线分析指标展示、当前分析结果状态、调参建议列表、离线趋势预览点集、CSV 分析执行和分析窗口解析。

这一步不改变用户可见功能，目的是在历史趋势功能迁移前完成可维护的架构底座。当前底座已经可用，但根 ViewModel 仍需继续瘦身。

## 9. 本次绑定收口记录

本次重构完成了实验历史、建议审查和参数方案库的旧绑定包装迁移：

- 历史记录页的筛选文本、历史列表、选中记录、记录详情、对比状态和对比指标直接绑定 `ExperimentHistory.*`。
- 参数调整页的建议审查备注、审查状态和审查记录列表直接绑定 `ExperimentHistory.*`。
- 参数方案状态和参数方案列表直接绑定 `ParameterSetLibrary.*`。
- `MainWindowViewModel` 不再公开 `HistoryStatus`、`HistorySearchText`、`HistorySessions`、`SelectedHistorySession`、`SelectedHistoryDetails`、`HistoryComparisonStatus`、`HistoryComparisonMetrics`、`RecommendationReviewNote`、`RecommendationReviewStatus`、`RecommendationReviews`、`ParameterSetStatus`、`ParameterSets` 这类细项包装属性。
- `MainWindowViewModel` 仍保留加载历史、打开历史、导出历史、设置基准、对比记录、保存/刷新参数方案、记录建议审查等命令，因为这些命令需要协调文件对话框、仓储编排、离线分析状态和全局通知。

该收口保持用户可见功能不变，但把 View 和测试的状态入口进一步下沉到子 ViewModel，避免后续历史趋势迁移时继续扩大根 ViewModel。
