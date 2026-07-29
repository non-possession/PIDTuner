# PIDTuner User Manual

Last updated: 2026-07-29, iteration 50.

## Requirements

- Windows 10 or later.
- .NET 8 SDK.
- Run commands from the repository root:

```powershell
cd D:\WorkEnv\projects\dotnet\pidtuner
```

## Build

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet build .\PIDTuner.sln
```

Expected result: build succeeds with 0 warnings and 0 errors.

## Run The Desktop App

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet run --project .\src\PIDTuner.Desktop\PIDTuner.Desktop.csproj
```

The current MVP opens on the `分析` tab.

## Configure A PLC Project

On the `连接与配置` tab, PIDTuner now supports editable project-level PLC metadata and tag definitions. This is configuration only; it does not open a live PLC connection yet.

You can start from the bundled example:

```text
config\plc-project.example.json
```

Typical flow:

1. Edit project fields such as configuration name, protocol, IP address, Rack, Slot, timeout, and default sampling interval.
2. Edit tag rows directly. Current tag metadata includes enable state, name, address, data type, access mode, scale, unit, sampling interval, and description.
3. Click `新增点位` to add a tag.
4. Select a tag row and click `删除点位` to remove it.
5. Click `保存 PLC 配置` to save the current project configuration as JSON.
6. Click `导入 PLC 配置` to load an existing PLC project configuration.

Save notifications include the absolute JSON file path.

To check basic network reachability, click `检查通信`. The current implementation performs a real Ping against the configured PLC IP address and reports the result in the page status and top notification box.

## Monitor PLC Tags

On the `实时监控` tab:

1. Click `检查通信` to Ping the configured PLC IP.
2. Click `刷新点位` to read one snapshot of the enabled tag list.
3. Click `启动/停止` to refresh repeatedly using the configured default sampling interval.
4. Select a tag row to view its recent trend preview.

Important current boundary: Ping is a real network reachability check. Tag values currently come from the `Preview` snapshot reader so the monitoring UI, value table, and trend behavior can be validated before a real Siemens S7 or Modbus adapter is wired in.

## Try The Current Offline CSV Flow

Fast path: click `载入示例` to load the default field profile and sample CSV in one step.

Manual path:

1. Click `导入字段配置`.
2. Select:

```text
config\pid-sample-fields.example.json
```

3. Click `导入 CSV`.
4. Select:

```text
samples\offline-step-response.csv
```

5. The analysis page should update:
   - `样本数`
   - `超调量`
   - `上升时间`
   - `调节时间`
   - `稳态误差`
   - `峰值`
   - `峰值时间`
   - `平均绝对误差`
   - `误差积分`
   - `状态标记`
   - SP / PV / MV trend preview
   - current CSV field profile table
6. Click `导出分析结果` to save the latest analysis window, metrics, severity, and assessment summary to a CSV file.
7. Click `保存试验记录` to save the current offline session metadata and samples under:

```text
local\test-sessions
```

The saved files are JSON files used by the current local persistence adapter. This keeps the UI and application layer ready for a later SQLite adapter.
After imports, exports, saves, and history operations, PIDTuner shows a dismissible message box near the top of the window. Save and export messages include the saved item's absolute path.

## Open Saved History

On the `历史记录` tab:

1. Click `刷新历史` to load locally saved test sessions from:

```text
local\test-sessions
```

2. Select a row.
3. Click `打开记录`.
4. The selected session samples are loaded back into the analysis page state, including metrics and trend preview.
5. Use `筛选` to narrow saved sessions by name, device, condition, or notes.
6. Click `导出采样` to export the selected saved session samples to CSV.

## Review Tuning Recommendations

After importing CSV, loading the bundled example, or opening a saved history record, open the `参数调整` tab.

The current MVP generates conservative rule-based suggestions from the latest response metrics. Each recommendation shows:

- PID parameter or loop area.
- Adjustment direction.
- Reason.
- Expected effect.
- Risk.
- Confidence.

These suggestions are advisory only. PIDTuner does not write parameters back to PLC in the current MVP.

To record an engineer review:

1. Select one recommendation row.
2. Fill `工程师备注`.
3. Click `记录采用` or `记录暂缓`.
4. Use `刷新记录` to reload saved recommendation review records.

Recommendation review records are saved under:

```text
local\recommendation-reviews\recommendation-reviews.json
```

Optional: before importing CSV, fill `分析开始` and `分析结束` with ISO 8601 timestamps such as:

```text
2026-07-29T10:00:01.0000000+00:00
2026-07-29T10:00:06.0000000+00:00
```

When both fields are empty, PIDTuner analyzes the full CSV sample range and fills the start/end inputs from the imported CSV. The `本次窗口` label shows the window used for the latest analysis.

## Edit A Field Profile

On the `分析` tab:

1. Click `导入字段配置` to load an existing JSON profile, or use the default profile already shown.
2. Edit the field table directly:
   - `字段`: stable CSV column key.
   - `显示名`: user-facing name.
   - `类型`: choose one of `String`, `Boolean`, `Double`, `Guid`, `DateTimeOffset`.
   - `必填`: checked means the CSV must contain this field.
   - `单位`: optional engineering unit.
   - `角色`: choose one of `Metadata`, `SampleTime`, `SetPoint`, `ProcessValue`, `ManipulatedValue`, `Kp`, `KiOrTi`, `KdOrTd`, `ConnectionState`, `TestSession`, `ParameterSet`.
3. Click `新增字段` to add a metadata field.
4. Select a field row and click `删除字段` to remove it.
5. Click `保存字段配置` to write the edited profile to a JSON file.

After saving, use the saved JSON as the field profile for CSV imports in the same PID tuning project.

## Run Tests

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet run --project .\tests\PIDTuner.Tests\PIDTuner.Tests.csproj
```

Current expected result: 21 tests pass.

## Generate A UI Snapshot

Use this when reporting every fifth iteration.

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet run --project .\tools\PIDTuner.Snapshot\PIDTuner.Snapshot.csproj -- .\artifacts\snapshots\pidtuner-current.png
```

The snapshot tool renders the WPF window directly, so it does not depend on which desktop window is currently in front.
It loads the bundled example profile and sample CSV, saves the current test session, records one accepted recommendation review, refreshes PLC tag previews, and then renders the window. The current snapshot view is switched to the analysis page so the expanded PID analysis metrics and trend preview are visible.

## Current Features

- Load a PID sample field profile from JSON.
- Load and save a PLC project configuration from JSON.
- Edit PLC project connection metadata.
- Edit, add, remove, and save PLC tag definitions.
- Check configured PLC IP reachability with Ping.
- Refresh enabled PLC tag snapshots through an application-level reader interface.
- Start and stop repeated tag monitoring at the configured sampling interval.
- Show a selected tag's recent value trend.
- Load the bundled example profile and sample CSV with one button.
- Edit, add, remove, and save PID sample field profiles from the analysis page.
- Import offline CSV using the active field profile.
- Analyze either the full CSV range or a user-entered analysis time window.
- Fill and show the active analysis window after CSV import.
- Calculate and display expanded PID response metrics:
  - peak process value
  - peak time
  - minimum process value
  - mean absolute error
  - mean squared error
  - integral absolute error
  - output standard deviation
  - sustained oscillation flag
  - output saturation flag
- Show a conservative response assessment summary after analysis.
- Show a dismissible operation message box after imports, exports, saves, and history operations.
- Show absolute saved file paths in save/export operation messages.
- Export the latest analysis result CSV from the analysis page.
- Save the latest offline analysis as a local test session record.
- Refresh saved test sessions from the history page.
- Open a saved test session back into the analysis page state.
- Filter saved test sessions.
- Show saved session duration, sample count, and detail summary.
- Export selected historical samples to CSV.
- Generate conservative PID tuning recommendations from the latest analysis metrics.
- Show recommendation reason, expected effect, risk, and confidence on the parameter-adjustment page.
- Record engineer accept/defer decisions for tuning recommendations.
- Persist recommendation review records locally.
- Calculate basic PID response metrics:
  - overshoot
  - rise time
  - settling time
  - steady-state error
- Show SP / PV / MV trend preview.
- Show active CSV field metadata.

## Current Limitations

- No protocol-level PLC read/write adapter yet. Ping is real, while the current tag values are generated by a preview reader until a Siemens S7 or Modbus adapter is installed.
- No SQLite persistence yet.
- No real charting library yet; the trend preview is a lightweight WPF polyline preview.
- CSV parsing supports quoted fields with commas and escaped double quotes on a single line.
- Field profile editing uses dropdowns for field type and semantic role.

## Iteration Reporting Rule

Every time the project reaches another fifth iteration, update this manual before reporting back. The update should include:

- latest iteration number
- current runnable commands
- current sample files
- current feature list
- latest UI snapshot command or snapshot path

## Latest UI Snapshot

Iteration 50 snapshot:

```text
C:\Users\30559\.codex\visualizations\2026\07\29\019fabb6-3a21-78f2-a9dc-660eb64b7ca9\pidtuner-iteration-50-rendered.png
```
