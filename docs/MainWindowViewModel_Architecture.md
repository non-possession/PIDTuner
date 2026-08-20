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
