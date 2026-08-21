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
- The current historical frame set belongs to `HistoricalTrendViewModel`; MainVM must not retain a duplicate frame list.
- Historical persistence lifecycle belongs to `PlcHistoricalAcquisitionWriter`.
- Historical query, frame retention, and merge policy belong to `PlcHistoricalTrendCoordinator`.
- Visible trend CSV encoding and serialization belong to `PlcTrendVisibleExportWorkflow`.
- Visible trend export workflow access belongs to `PlcTrendExportViewModel`.
- PLC recording JSON read/write and validation belong to `PlcOneSecondRecorder`.
- PLC replay timing and step orchestration belong to `PlcReplayController`.
- PLC diagnostics expiration timing belongs to `PlcDiagnosticsController`.
- PLC live refresh timing belongs to `PlcLiveMonitoringController`.
- PLC session capability detection and single-read fallback belong to `PlcSnapshotSessionFactory`.
- Debug settings and status bind directly through `PlcDebugViewModel`; do not recreate MainVM proxy properties.
- PLC communication status belongs to `PlcConfigurationEditorViewModel`.
- PLC configuration construction, validation, loading, saving, and communication checks belong to `PlcConfigurationEditorViewModel`.
- Live monitor and acquisition diagnostics status belong to `PlcLiveMonitorViewModel`.
- Offline analysis import, result state, and result export belong to `OfflineAnalysisViewModel`.
- Field-profile construction, validation, loading, and saving belong to `FieldProfileEditorViewModel`.
- Experiment session persistence, history comparison, and recommendation review orchestration belong to `ExperimentWorkspaceViewModel`.
- Repository discovery and default storage path construction belong to `MainWindowComposition`.
- Infrastructure adapters are created only in composition modules.
- Views contain visual interaction logic only; persistence and domain workflows do not belong in code-behind.

## MainVM Migration Roadmap

Migrate the remaining business behavior in this order. Preserve existing public behavior and commit each architecture change independently.

### P1: PLC Live Workspace

Create a `PlcLiveWorkspaceViewModel` (or an equivalently focused module) to own:

- PLC refresh and one-shot snapshot reads.
- Live acquisition start/stop coordination.
- Distribution of acquisition frames to monitor rows, diagnostics, and historical buffering.
- Live acquisition status and failure-result construction.

`MainWindowViewModel` may forward resulting chart events and global notifications, but must not iterate acquisition frames or coordinate acquisition resources itself.

### P2: PLC Trend Workspace

Create a `PlcTrendWorkspaceViewModel` to coordinate:

- Live/historical mode transitions.
- Historical-window loading and visible-range operations.
- Single-axis/dual-axis state transitions.
- Historical frame publication to the historical chart adapter.
- Pause, reset, and mode-specific chart behavior.

The live and historical chart adapters remain separate. Shared plotting contracts and export models may be reused by both.

### P3: PLC Recording And Replay

Move the remaining one-second recording and recording-load workflow out of MainVM. The owning debug/recording workspace must coordinate:

- Recording start, completion, validation, and result status.
- JSON recording load and replay initialization.
- Replay frame/batch application requests.
- Transitions between replay, live trend, and historical trend state.

`PlcOneSecondRecorder`, `PlcReplayController`, and `PlcDebugViewModel` remain focused supporting modules; MainVM must not reconstruct their business results.

### P4: Parameter-Set Coordination

Move construction of parameter-set save inputs out of MainVM. A parameter-set or experiment workspace must obtain the latest samples, session identity, and source metadata through an explicit operation contract.

### P5: Example Loading

Move repository-path discovery, example-file validation, and ordered example loading into an `ExampleWorkspaceWorkflow`. MainVM may trigger the workflow and display its result only.

### P6: Operation Results

Continue replacing repeated validation, `try/catch`, and user-message formatting in MainVM with typed child-operation results. File-dialog selection and final global-notification display may remain in MainVM.

## MainVM Completion Criteria

The MainVM refactor is complete when:

- MainVM retains no PLC acquisition, recording, replay, historical-query, export, or parameter-set business state.
- MainVM does not iterate, filter, merge, retain, or interpret PLC frames.
- Command handlers primarily select files, call one child operation, bridge an event, or display one operation result.
- Cross-module transitions are expressed through named workspace operations instead of sequences of child mutations in MainVM.
- Architecture tests reject reintroduction of migrated fields, workflow dependencies, proxy properties, and frame-processing calls.
- Full build and regression tests pass after every migration step.

Reducing line count is an expected consequence, not the acceptance criterion. A final size around 700-900 lines is a planning estimate; responsibility boundaries and readable control flow take precedence.

## Verification

- Every behavior change must include focused regression coverage.
- Architecture tests must prevent `MainWindowViewModel` from importing `PIDTuner.Infrastructure` or implementing persistence/query details.
- Run the full test executable and solution build before committing.
