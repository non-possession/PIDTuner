# PIDTuner User Manual

Last updated: 2026-07-29, iteration 5.

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

## Run Tests

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet run --project .\tests\PIDTuner.Tests\PIDTuner.Tests.csproj
```

Current expected result: 6 tests pass.

## Generate A UI Snapshot

Use this when reporting every fifth iteration.

```powershell
$env:DOTNET_CLI_HOME = (Get-Location).Path
dotnet run --project .\tools\PIDTuner.Snapshot\PIDTuner.Snapshot.csproj -- .\artifacts\snapshots\pidtuner-current.png
```

The snapshot tool renders the WPF window directly, so it does not depend on which desktop window is currently in front.

## Current Features

- Load a PID sample field profile from JSON.
- Import offline CSV using the active field profile.
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
- CSV parsing currently supports simple comma-separated values without quoted commas.
- Field profiles can be loaded but not edited inside the UI yet.

## Iteration Reporting Rule

Every time the project reaches another fifth iteration, update this manual before reporting back. The update should include:

- latest iteration number
- current runnable commands
- current sample files
- current feature list
- latest UI snapshot command or snapshot path
