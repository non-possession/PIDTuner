using PIDTuner.Desktop.ViewModels;

namespace PIDTuner.Desktop.Services;

internal sealed record MainWindowDependencies(
    IOpenFileDialogService OpenFileDialogService,
    ExampleWorkspaceWorkflow ExampleWorkspaceWorkflow,
    ExperimentWorkspaceViewModel ExperimentWorkspace,
    PlcTrendModeViewModel PlcTrendMode,
    NotificationViewModel Notification,
    PlcLiveMonitorViewModel LiveMonitor,
    PlcLiveWorkspaceViewModel PlcLiveWorkspace,
    PlcDebugViewModel Debug,
    PlcConfigurationEditorViewModel PlcConfigurationEditor,
    PlcConnectionWorkspaceViewModel PlcConnectionWorkspace,
    PlcDiagnosticsWorkspaceViewModel PlcDiagnosticsWorkspace,
    PlcMonitoringWorkspaceViewModel PlcMonitoringWorkspace,
    OfflineAnalysisViewModel OfflineAnalysis,
    ExperimentHistoryViewModel ExperimentHistory,
    ParameterSetLibraryViewModel ParameterSetLibrary,
    ParameterSetWorkspaceViewModel ParameterSetWorkspace,
    HistoricalTrendViewModel HistoricalTrend,
    PlcTrendExportViewModel TrendExport,
    PlcTrendWorkspaceViewModel PlcTrendWorkspace,
    PlcRecordingWorkspaceViewModel PlcRecordingWorkspace,
    FieldProfileEditorViewModel FieldProfileEditor);
