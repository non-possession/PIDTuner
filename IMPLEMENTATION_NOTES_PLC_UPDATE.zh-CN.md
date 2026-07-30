# PIDTuner PLC 通信优化补充说明

## Siemens S7 多点批量读取

本轮已经将实时监控和 1 秒记录中的 Siemens S7 读取路径从“复用连接但逐点发送读取 PDU”优化为“复用连接并按批发送 multi-variable read PDU”。

关键实现点：

- `SiemensS7Client` 新增批量读取路径，同一批请求最多包含 16 个 S7ANY 点位描述。
- `SiemensS7PlcTagSnapshotReader` 在打开读取会话时预解析启用点位，避免每帧重复解析地址。
- 每次刷新时，可读点位会进入批量读取；地址解析失败、单点读取失败、整批读取失败会分别写入对应 `PlcTagSnapshot.Quality`。
- `IPlcTagSnapshotReadSession` 接口保持不变，因此 UI 和记录调度层不需要感知底层读取方式变化。

核心代码位置：

- `src/PIDTuner.Infrastructure/Plc/SiemensS7Client.cs`
- `src/PIDTuner.Infrastructure/Plc/SiemensS7PlcTagSnapshotReader.cs`

## 连接失败阶段区分

本轮增强了 Siemens S7 通信检查失败信息。现在可以区分：

- PLC IP 地址为空。
- TCP 102 端口连接失败或连接超时。
- ISO-on-TCP 握手失败或超时。
- S7 Setup Communication 失败或超时。

关键实现点：

- `SiemensS7Client.ConnectAsync()` 在每个通信阶段捕获异常，并抛出带阶段信息的 `SiemensS7ConnectionException`。
- `SiemensS7ConnectivityProbe` 将阶段信息转换为用户可读提示。
- 由配置超时触发的 `OperationCanceledException` 会归类到对应通信阶段；真正的用户取消仍然保留取消语义。

核心代码位置：

- `src/PIDTuner.Infrastructure/Plc/SiemensS7Client.cs`
- `src/PIDTuner.Infrastructure/Plc/SiemensS7ConnectivityProbe.cs`
- `src/PIDTuner.Infrastructure/Plc/PingPlcConnectivityProbe.cs`

## 后续讨论重点

PLC 控制参数写回暂不实施。记录数据可视化和回放会等用户提供待整合项目后再进入设计和实现讨论。

## 实时趋势可视化补充

本轮开始接入 `WPFHistoricalTrend` 项目的图表工作台思想，但没有直接搬运整套窗口。PIDTuner 当前实现选择在 Desktop 层新增 `PlcTrendChartAdapter`，用 `ScottPlot.WPF` 渲染实时监控页中的多点位趋势。

关键实现点：

- `MainWindowViewModel` 在每次应用 PLC 快照后触发 `PlcSnapshotsApplied` 事件。
- `MainWindow.xaml.cs` 监听该事件，将 `PlcTagSnapshot` 帧交给图表适配器。
- `PlcTrendChartAdapter` 在内存中按 `TagId` 保存最近趋势点，并按当前窗口渲染可见点位。
- 实时监控表格新增“趋势”列，用户可以逐点控制是否显示曲线。
- 实时趋势支持 `10s`、`30s`、`1min`、`5min` 窗口切换，并支持鼠标悬停查看光标附近的点位值。

当前边界：

- 这一步只做实时趋势，不做历史回放。
- PLC 趋势长历史 SQLite 持久化尚未接入。
- 回放功能会在后续结合用户提供的项目继续设计。

## PLC 记录回放补充

本轮已加入保存记录的基础回放能力。回放没有另起一套图表链路，而是复用实时监控页：

- `LoadPlcRecordingAsync()` 读取已保存的 PLC 记录 JSON 文件，并预览第一帧。
- `TogglePlcReplayAsync()` 使用记录文件里的采样周期启动或暂停回放定时器。
- 每个回放帧都会进入 `ApplyPlcMonitorSnapshots()`，因此点位表、质量信息和 ScottPlot 趋势图都与实时监控共享同一套更新路径。
- `PlcTrendResetRequested` 用于在加载记录或重新回放时清空旧趋势。

当前回放对象是 `local/plc-recordings` 下的 JSON 记录文件；长历史 SQLite 趋势存储仍留到后续单独设计。
## 本轮补充：PLC 回放控制

PLC 记录回放现在增加了更完整的人工控制能力：

- `TogglePlcReplayAsync()` 会根据记录文件原始采样周期和当前播放倍率计算实际定时器间隔。
- `StepPlcReplayForwardAsync()` 用于单帧前进；`StepPlcReplayBackwardAsync()` 会清空点位表和趋势图，再重建到目标帧，避免曲线中保留未来帧数据。
- `PlcReplayStatus` 在实时监控页展示当前帧、总帧数、原始周期、播放间隔和倍率。
