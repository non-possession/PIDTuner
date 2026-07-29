# PIDTuner User Manual

Last updated: 2026-07-29, iteration 11.

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

## Try The Current Offline CSV Flow

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
   - SP / PV / MV trend preview
   - current CSV field profile table

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

Current expected result: 10 tests pass.

## Generate A UI Snapshot

Use this when reporting every fifth iteration.

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet run --project .\tools\PIDTuner.Snapshot\PIDTuner.Snapshot.csproj -- .\artifacts\snapshots\pidtuner-current.png
```

The snapshot tool renders the WPF window directly, so it does not depend on which desktop window is currently in front.

## Current Features

- Load a PID sample field profile from JSON.
- Edit, add, remove, and save PID sample field profiles from the analysis page.
- Import offline CSV using the active field profile.
- Analyze either the full CSV range or a user-entered analysis time window.
- Fill and show the active analysis window after CSV import.
- Show a conservative response assessment summary after analysis.
- Calculate basic PID response metrics:
  - overshoot
  - rise time
  - settling time
  - steady-state error
- Show SP / PV / MV trend preview.
- Show active CSV field metadata.

## Current Limitations

- No PLC connection yet.
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

Iteration 10 snapshot:

```text
C:\Users\30559\.codex\visualizations\2026\07\29\019fabb6-3a21-78f2-a9dc-660eb64b7ca9\pidtuner-iteration-10-rendered.png
```
