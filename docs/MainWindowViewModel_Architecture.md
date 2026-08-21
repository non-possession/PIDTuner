# MainWindowViewModel 架构约束

更新日期：2026-08-20。

## 定位

`MainWindowViewModel` 是 PIDTuner 的应用壳层 ViewModel。它的核心价值是展示子 ViewModel 之间的清晰脉络，而不是容纳各模块的实现细节。

## 允许的职责

- 暴露子 ViewModel 供 XAML 绑定。
- 转发跨模块事件。
- 协调页面导航、全局通知和画布 Adapter 入口。
- 将用户命令委托给对应的子 ViewModel 或 workflow 模块。

## 禁止的职责

- 直接查询 SQLite、JSON、CSV 或 repository。
- 合并、去重、降采样、保留或转换 PLC 帧。
- 管理 PLC 通信会话或历史持久化会话。
- 实现历史时间范围、回放、导出序列化等业务细节。
- 直接实例化或引用 `PIDTuner.Infrastructure` 类型。

## 历史趋势模块归属

- `HistoricalTrendViewModel`：历史页面级数据加载入口。
- `HistoricalTrendWorkbenchViewModel`：X/Y 轴、可见范围、序列和滑块状态。
- `PlcHistoricalTrendCoordinator`：SQLite 查询、内存帧保留、合并、去重和数据量控制。
- `PlcHistoricalAcquisitionWriter`：历史写入会话的启动、入队与停止。
- `PlcLiveMonitorViewModel`：将历史写入会话与实时采集生命周期对齐。
- `MainWindowComposition`：Infrastructure Adapter 的唯一创建位置。

## 变更规则

新增 MainVM 字段、方法、命令或依赖时，必须先说明为什么现有子 ViewModel 或 workflow 模块无法承担该职责。任何新的 Infrastructure 依赖都应由架构测试拒绝。

## 剩余业务迁移子约束

以下顺序用于迁移 MainVM 中尚未归位的业务职责。每项架构变更必须独立提交，并保持现有用户行为。

### P1：PLC 实时工作台

建立 `PlcLiveWorkspaceViewModel` 或职责等价的模块，统一负责：

- PLC 刷新与单次快照读取。
- 实时采集的启动和停止协调。
- 将采集帧分发给点位状态、诊断和历史缓存。
- 构造实时采集状态与失败结果。

MainVM 可以转发图表事件和全局通知，但不得自行遍历采集帧或协调采集资源。

### P2：PLC 趋势工作台

建立 `PlcTrendWorkspaceViewModel`，统一协调：

- 实时/历史模式切换。
- 历史窗口加载和可见范围操作。
- 单轴/双轴布局切换。
- 向历史图表适配器发布历史帧。
- 暂停、重置以及模式相关的图表行为。

实时和历史图表适配器保持分离；二者可以复用底层绘图契约和导出模型。

### P3：PLC 记录与回放

将剩余的一秒记录与记录文件加载流程迁出 MainVM。对应的调试/记录工作台负责：

- 记录启动、完成、校验和结果状态。
- JSON 记录加载与回放初始化。
- 单帧和批量帧应用请求。
- 回放、实时趋势和历史趋势之间的状态切换。

`PlcOneSecondRecorder`、`PlcReplayController` 和 `PlcDebugViewModel` 继续作为职责聚焦的支撑模块；MainVM 不得重新拼装其业务结果。

### P4：参数集协调

参数集保存输入不得由 MainVM 从多个子 VM 中临时拼装。参数集或实验工作台应通过明确的操作契约取得最新样本、会话标识和来源元数据。

### P5：示例加载

仓库路径发现、示例文件校验和有序加载应迁入 `ExampleWorkspaceWorkflow`。MainVM 只负责触发流程并展示结果。

### P6：统一操作结果

MainVM 中重复的前置校验、`try/catch` 和用户消息构造，应逐步替换为子模块返回的类型化操作结果。文件对话框选择和最终全局通知展示可以保留在 MainVM。

## 重构完成标准

- MainVM 不保留 PLC 采集、记录、回放、历史查询、导出或参数集业务状态。
- MainVM 不遍历、筛选、合并、保留或解释 PLC 帧。
- 命令处理器主要只执行文件选择、调用一次子模块操作、桥接事件或展示一次操作结果。
- 跨模块状态切换通过具名的工作台操作表达，不在 MainVM 中连续修改多个子 VM。
- 架构测试阻止已迁移字段、工作流依赖、代理属性和帧处理逻辑重新进入 MainVM。
- 每一步迁移完成后，完整构建和回归测试必须通过。

代码行数下降是职责迁移后的结果，而不是验收标准。MainVM 最终约 700 至 900 行仅作为规划估算，职责边界和控制脉络的清晰度优先。
