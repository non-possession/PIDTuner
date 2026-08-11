# PIDTuner V2.0 架构重构基线

本文档记录 PIDTuner 在 V2.0 阶段进行历史趋势工作台迁移前的架构重构依据。它不是功能需求清单，而是后续代码变更的模块边界、职责划分和迁移顺序。

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

建议新增或演进为：

- `PlcLiveMonitorView`
- `PlcLiveMonitorViewModel`
- `LivePlcTrendAdapter`

目标职责：

- 连接实时 PLC 采集引擎。
- 接收实时采集帧。
- 管理滚动窗口、实时点位当前值、启动/停止状态。
- 调用共享绘图模块完成实时趋势绘制。

实时趋势适配器只处理实时追加数据和滚动窗口，不承担历史工作台的静态查询与轴控制。

### 3.3 历史趋势工作台模块

建议新增：

- `HistoricalTrendWorkbenchView`
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

### 3.4 桥接模块

建议新增：

- `PlcTrendDatasetBridge`
- `PlcTrendDataset`
- `PlcTrendSeries`

目标职责：

- 把 `IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>` 转换为历史趋势工作台数据集。
- 处理点位名称、地址、单位、质量、时间戳和值。
- 处理缺失点、非数值点、无效值。
- 隔离 PIDTuner 采集帧模型和历史趋势工作台模型。

桥接模块是必要的，因为 PLC 采集帧是采集链路模型，而历史趋势工作台需要的是按曲线组织、可按时间区间查询的数据集。

### 3.5 调试模块

建议新增或演进为：

- `PlcDebugView`
- `PlcDebugViewModel`

目标职责：

- 1s 记录。
- 诊断启动/停止。
- 诊断状态展示。
- 记录文件打开。
- 回放、上一帧、下一帧、播放速度。
- 完整点位调试表。

调试功能不应留在实时监控主界面，也不应继续与历史趋势工作台逻辑耦合。

### 3.6 共享绘图模块

当前已引入：

- `src/PIDTuner.Desktop/Services/SharedScottPlotTrendRenderer.cs`
- `src/PIDTuner.Desktop/Services/PlcTrendPoint.cs`

当前职责：

- ScottPlot 字体配置。
- 趋势图标题、X/Y 标签、图例配置。
- 曲线绘制。
- X 轴时间范围应用。
- Y 轴手动范围或自动范围应用。

当前调用方：

- `src/PIDTuner.Desktop/Services/PlcTrendChartAdapter.cs`

后续 `LivePlcTrendAdapter` 和 `HistoricalTrendChartAdapter` 都应调用该共享模块。共享绘图模块不保存业务状态，不读取 PLC，不访问文件。

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

状态：已开始。

已完成：

- 从现有 `PlcTrendChartAdapter` 中抽出 `SharedScottPlotTrendRenderer`。
- 保持现有实时趋势外部调用接口不变。
- 为后续历史趋势适配器预留共享绘图入口。

### 阶段 B：建立历史趋势工作台模型

后续新增：

- `HistoricalTrendWorkbenchState`
- `HistoricalTrendWorkbenchCoordinator`
- `PlcTrendDataset`
- `PlcTrendSeries`
- `PlcTrendDatasetBridge`

验收标准：

- 不接入 UI 也能测试 X 轴窗口、Y 轴范围、图例状态和可见数据筛选。
- `MainWindowViewModel` 不新增历史趋势核心算法。

### 阶段 C：建立历史趋势适配器

后续新增：

- `HistoricalTrendChartAdapter`

验收标准：

- 可以接收桥接后的历史数据集。
- 可以应用工作台状态绘制静态历史曲线。
- 可以返回当前画布可见范围。

### 阶段 D：拆分 ViewModel

后续拆分：

- `PlcLiveMonitorViewModel`
- `PlcDebugViewModel`
- `HistoricalTrendWorkbenchViewModel`
- `PlcConfigurationViewModel`

迁移方式：

- 不做一次性大改。
- 每次迁移一个功能簇。
- 每次迁移后保持现有用户功能可运行。
- `MainWindowViewModel` 最终退化为组合根和全局状态入口。

## 6. 禁止事项

后续实现应避免：

- 继续把历史趋势缩放、平移、导出逻辑写入 `MainWindowViewModel`。
- 让历史趋势直接依赖 PLC 采集帧原始结构。
- 在 View code-behind 中实现数据持久化、PLC 通信和业务判断。
- 在两个图表适配器中重复 ScottPlot 字体、颜色、图例、坐标轴样式。
- 为了迁移 WPFHistoricalTrend 而破坏 PIDTuner 现有项目层次。

## 7. 当前代码变更记录

本次重构已经完成第一步：

- 新增 `SharedScottPlotTrendRenderer` 作为共享 ScottPlot 绘图模块。
- 新增 `PlcTrendPoint` 作为桌面图表层内部趋势点模型。
- 修改 `PlcTrendChartAdapter`，让它继续负责实时/现有趋势数据管理，但把绘图实现委托给共享渲染模块。

这一步不改变用户可见功能，目的是为后续 `LivePlcTrendAdapter` 与 `HistoricalTrendChartAdapter` 分离打基础。
