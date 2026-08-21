# PIDTuner Engineering Guardrails

These rules apply to the entire repository.

## Main Window ViewModel

`MainWindowViewModel` is the application-shell ViewModel. Its purpose is to expose child ViewModels, bridge cross-module events, and coordinate navigation-level state.

Allowed responsibilities:

- Construct or receive child ViewModels through the Desktop composition root.
- Expose child ViewModels and shell-level commands to XAML.
- Forward events between child ViewModels and chart adapters.
- Coordinate navigation and global notifications.

Forbidden responsibilities:

- Query SQLite, JSON, CSV, PLC clients, or repositories directly.
- Merge, deduplicate, downsample, retain, or transform acquisition frames.
- Own PLC communication or persistence session lifecycles.
- Implement historical-range validation, replay algorithms, or export serialization.
- Instantiate types from `PIDTuner.Infrastructure`.

Before adding a field, method, or command to `MainWindowViewModel`, identify the owning child ViewModel or workflow module. A new MainVM dependency or method requires an architecture review and a written reason why no existing child module owns the behavior.

## Module Placement

- PLC live acquisition state belongs to `PlcLiveMonitorViewModel` and its supporting modules.
- Historical trend state belongs to `HistoricalTrendViewModel` / `HistoricalTrendWorkbenchViewModel`.
- Historical persistence lifecycle belongs to `PlcHistoricalAcquisitionWriter`.
- Historical query, frame retention, and merge policy belong to `PlcHistoricalTrendCoordinator`.
- Visible trend CSV encoding and serialization belong to `PlcTrendVisibleExportWorkflow`.
- PLC recording JSON read/write and validation belong to `PlcOneSecondRecorder`.
- PLC replay timing and step orchestration belong to `PlcReplayController`.
- PLC diagnostics expiration timing belongs to `PlcDiagnosticsController`.
- PLC live refresh timing belongs to `PlcLiveMonitoringController`.
- PLC session capability detection and single-read fallback belong to `PlcSnapshotSessionFactory`.
- Debug settings and status bind directly through `PlcDebugViewModel`; do not recreate MainVM proxy properties.
- PLC communication status belongs to `PlcConfigurationEditorViewModel`.
- PLC configuration construction, validation, loading, saving, and communication checks belong to `PlcConfigurationEditorViewModel`.
- Live monitor and acquisition diagnostics status belong to `PlcLiveMonitorViewModel`.
- Field-profile construction, validation, loading, and saving belong to `FieldProfileEditorViewModel`.
- Experiment session persistence, history comparison, and recommendation review orchestration belong to `ExperimentWorkspaceViewModel`.
- Repository discovery and default storage path construction belong to `MainWindowComposition`.
- Infrastructure adapters are created only in composition modules.
- Views contain visual interaction logic only; persistence and domain workflows do not belong in code-behind.

## Verification

- Every behavior change must include focused regression coverage.
- Architecture tests must prevent `MainWindowViewModel` from importing `PIDTuner.Infrastructure` or implementing persistence/query details.
- Run the full test executable and solution build before committing.
