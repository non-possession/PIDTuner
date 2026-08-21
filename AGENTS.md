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
- PLC refresh, acquisition lifecycle coordination, and live frame distribution belong to `PlcLiveWorkspaceViewModel`.
- Cross-workspace monitoring commands, configuration snapshots, replay arbitration, diagnostics shutdown, and trend reset coordination belong to `PlcMonitoringWorkspaceViewModel`.
- Historical trend state belongs to `HistoricalTrendViewModel` / `HistoricalTrendWorkbenchViewModel`.
- Live/historical transitions, range operations, axis layout, and historical frame publication belong to `PlcTrendWorkspaceViewModel`.
- The current historical frame set belongs to `HistoricalTrendViewModel`; MainVM must not retain a duplicate frame list.
- Historical persistence lifecycle belongs to `PlcHistoricalAcquisitionWriter`.
- Historical query, frame retention, and merge policy belong to `PlcHistoricalTrendCoordinator`.
- Visible trend CSV encoding and serialization belong to `PlcTrendVisibleExportWorkflow`.
- Visible trend export workflow access belongs to `PlcTrendExportViewModel`.
- PLC recording JSON read/write and validation belong to `PlcOneSecondRecorder`.
- PLC recording, recording-load initialization, and replay result application belong to `PlcRecordingWorkspaceViewModel`.
- PLC replay timing and step orchestration belong to `PlcReplayController`.
- PLC diagnostics expiration timing belongs to `PlcDiagnosticsController`.
- PLC diagnostics start/stop coordination and user-facing results belong to `PlcDiagnosticsWorkspaceViewModel`.
- PLC configuration file operations, communication checks, and monitor startup coordination belong to `PlcConnectionWorkspaceViewModel`.
- PLC live refresh timing belongs to `PlcLiveMonitoringController`.
- PLC session capability detection and single-read fallback belong to `PlcSnapshotSessionFactory`.
- Debug settings and status bind directly through `PlcDebugViewModel`; do not recreate MainVM proxy properties.
- PLC communication status belongs to `PlcConfigurationEditorViewModel`.
- PLC configuration construction, validation, loading, saving, and communication checks belong to `PlcConfigurationEditorViewModel`.
- Live monitor and acquisition diagnostics status belong to `PlcLiveMonitorViewModel`.
- Offline analysis import, result state, and result export belong to `OfflineAnalysisViewModel`.
- Field-profile construction, validation, loading, and saving belong to `FieldProfileEditorViewModel`.
- Experiment session persistence, history comparison, and recommendation review orchestration belong to `ExperimentWorkspaceViewModel`.
- Parameter-set input assembly and persistence coordination belong to `ParameterSetWorkspaceViewModel`.
- Repository discovery and default storage path construction belong to `MainWindowComposition`.
- Example file discovery, validation, and ordered loading belong to `ExampleWorkspaceWorkflow`.
- User-facing operation outcomes use `WorkspaceOperationResult`; MainVM only presents the returned result.
- Infrastructure adapters are created only in composition modules.
- The complete Main-window object graph is assembled by `MainWindowComposition`; MainVM receives an already assembled `MainWindowDependencies` graph.
- Views contain visual interaction logic only; persistence and domain workflows do not belong in code-behind.

The detailed MainVM architecture sub-constraints and migration completion criteria in
[`docs/MainWindowViewModel_Architecture.md`](docs/MainWindowViewModel_Architecture.md) are normative for changes involving the application shell.

## Verification

- Every behavior change must include focused regression coverage.
- Architecture tests must prevent `MainWindowViewModel` from importing `PIDTuner.Infrastructure` or implementing persistence/query details.
- Run the full test executable and solution build before committing.
