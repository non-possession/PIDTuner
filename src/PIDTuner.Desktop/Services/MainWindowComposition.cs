using System.IO;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.Services;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Configuration;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Persistence;
using PIDTuner.Infrastructure.Plc;

namespace PIDTuner.Desktop.Services;

internal static class MainWindowComposition
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ResolvePath(params string[] segments) =>
        Path.Combine(new[] { RepositoryRoot }.Concat(segments).ToArray());

    public static IPidSampleFieldProfileStore CreateFieldProfileStore() =>
        new JsonPidSampleFieldProfileStore();

    public static IPlcProjectConfigurationStore CreatePlcConfigurationStore() =>
        new JsonPlcProjectConfigurationStore();

    public static IPlcConnectivityProbe CreatePlcConnectivityProbe() =>
        new ConfiguredPlcConnectivityProbe(
            new SiemensS7ConnectivityProbe(),
            new PingPlcConnectivityProbe());

    public static IPlcTagSnapshotReader CreatePlcTagSnapshotReader() =>
        new ConfiguredPlcTagSnapshotReader(
            new SiemensS7PlcTagSnapshotReader(),
            new PreviewPlcTagSnapshotReader());

    public static ITestSessionRepository CreateTestSessionRepository(string directory) =>
        new JsonTestSessionRepository(directory);

    public static IPidSampleRepository CreatePidSampleRepository(string directory) =>
        new JsonPidSampleRepository(directory);

    public static IPidRecommendationReviewRepository CreateRecommendationReviewRepository(string directory) =>
        new JsonPidRecommendationReviewRepository(directory);

    public static IPidParameterSetRepository CreateParameterSetRepository(string directory) =>
        new JsonPidParameterSetRepository(directory);

    public static IPlcLiveDiagnosticsStore CreateLiveDiagnosticsStore(string databasePath) =>
        new SqlitePlcLiveDiagnosticsStore(databasePath);

    public static IPlcHistoricalTrendStore CreateHistoricalTrendStore(string databasePath) =>
        new SqlitePlcHistoricalTrendStore(databasePath);

    public static MainWindowDependencies CreateDefault() =>
        Create(new WindowsOpenFileDialogService());

    public static MainWindowDependencies Create(
        IOpenFileDialogService openFileDialogService,
        IPidSampleFieldProfileStore? fieldProfileStore = null,
        IPlcProjectConfigurationStore? plcProjectConfigurationStore = null,
        IPlcConnectivityProbe? plcConnectivityProbe = null,
        IPlcTagSnapshotReader? plcTagSnapshotReader = null,
        ITestSessionRepository? testSessionRepository = null,
        IPidSampleRepository? pidSampleRepository = null,
        IPidRecommendationReviewRepository? recommendationReviewRepository = null,
        IPidParameterSetRepository? parameterSetRepository = null,
        IPlcLiveDiagnosticsStore? plcLiveDiagnosticsStore = null,
        IPlcHistoricalTrendStore? plcHistoricalTrendStore = null,
        string? testSessionStorageDirectory = null,
        string? plcRecordingStorageDirectory = null)
    {
        fieldProfileStore ??= CreateFieldProfileStore();
        plcProjectConfigurationStore ??= CreatePlcConfigurationStore();
        plcConnectivityProbe ??= CreatePlcConnectivityProbe();
        plcTagSnapshotReader ??= CreatePlcTagSnapshotReader();

        var resolvedTestSessionStorageDirectory = Path.GetFullPath(
            testSessionStorageDirectory ?? ResolvePath("local", "test-sessions"));
        var resolvedPlcRecordingStorageDirectory = Path.GetFullPath(
            plcRecordingStorageDirectory ?? ResolvePath("local", "plc-recordings"));
        testSessionRepository ??= CreateTestSessionRepository(resolvedTestSessionStorageDirectory);
        pidSampleRepository ??= CreatePidSampleRepository(resolvedTestSessionStorageDirectory);
        recommendationReviewRepository ??= CreateRecommendationReviewRepository(
            ResolvePath("local", "recommendation-reviews"));
        parameterSetRepository ??= CreateParameterSetRepository(
            ResolvePath("local", "parameter-sets"));
        plcLiveDiagnosticsStore ??= CreateLiveDiagnosticsStore(
            ResolvePath("local", "plc-diagnostics", "plc-live-diagnostics.sqlite"));
        plcHistoricalTrendStore ??= CreateHistoricalTrendStore(
            ResolvePath("local", "plc-history", "plc-history.sqlite"));

        var trendMode = new PlcTrendModeViewModel();
        var notification = new NotificationViewModel();
        var snapshotSessionFactory = new PlcSnapshotSessionFactory(plcTagSnapshotReader);
        var historicalWriter = new PlcHistoricalAcquisitionWriter(plcHistoricalTrendStore);
        var historicalTrend = new HistoricalTrendViewModel(
            new PlcHistoricalTrendCoordinator(plcHistoricalTrendStore));
        var trendExport = new PlcTrendExportViewModel(new PlcTrendVisibleExportWorkflow());
        var liveMonitor = new PlcLiveMonitorViewModel(
            new PlcAcquisitionEngine(snapshotSessionFactory.OpenAsync, historicalWriter.Enqueue),
            historicalWriter);
        var debug = new PlcDebugViewModel(liveMonitor.Tags, plcLiveDiagnosticsStore);
        var liveWorkspace = new PlcLiveWorkspaceViewModel(
            liveMonitor,
            historicalTrend,
            debug,
            snapshotSessionFactory,
            () => !trendMode.IsHistoricalMode);
        var trendWorkspace = new PlcTrendWorkspaceViewModel(
            trendMode,
            historicalTrend,
            liveMonitor,
            liveWorkspace,
            debug);
        var recordingWorkspace = new PlcRecordingWorkspaceViewModel(
            new PlcOneSecondRecorder(snapshotSessionFactory.OpenAsync, resolvedPlcRecordingStorageDirectory),
            debug,
            liveMonitor,
            liveWorkspace,
            historicalTrend,
            trendWorkspace);
        trendWorkspace.SetReplayStopAction(recordingWorkspace.StopReplay);

        var configurationEditor = new PlcConfigurationEditorViewModel(
            PlcProjectConfiguration.CreateDefault(),
            new PlcConfigurationWorkflow(plcProjectConfigurationStore, plcConnectivityProbe));
        var connectionWorkspace = new PlcConnectionWorkspaceViewModel(
            configurationEditor,
            liveMonitor,
            liveWorkspace);
        var diagnosticsWorkspace = new PlcDiagnosticsWorkspaceViewModel(
            debug,
            liveMonitor,
            configurationEditor);
        var offlineAnalysis = new OfflineAnalysisViewModel(new AnalysisResultExportWorkflow());
        var fieldProfileEditor = new FieldProfileEditorViewModel(new FieldProfileWorkflow(fieldProfileStore));
        var exampleWorkspaceWorkflow = new ExampleWorkspaceWorkflow(
            RepositoryRoot,
            fieldProfileEditor,
            offlineAnalysis);
        var experimentHistory = new ExperimentHistoryViewModel();
        var experimentWorkspace = new ExperimentWorkspaceViewModel(
            new ExperimentSessionCoordinator(
                testSessionRepository,
                pidSampleRepository,
                recommendationReviewRepository,
                resolvedTestSessionStorageDirectory),
            offlineAnalysis,
            experimentHistory);
        var parameterSetLibrary = new ParameterSetLibraryViewModel(
            parameterSetRepository,
            new PidParameterSetExtractor());

        return new MainWindowDependencies(
            openFileDialogService,
            exampleWorkspaceWorkflow,
            experimentWorkspace,
            trendMode,
            notification,
            liveMonitor,
            liveWorkspace,
            debug,
            configurationEditor,
            connectionWorkspace,
            diagnosticsWorkspace,
            offlineAnalysis,
            experimentHistory,
            parameterSetLibrary,
            new ParameterSetWorkspaceViewModel(parameterSetLibrary, offlineAnalysis),
            historicalTrend,
            trendExport,
            trendWorkspace,
            recordingWorkspace,
            fieldProfileEditor);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var configPath = Path.Combine(directory.FullName, "config", "pid-sample-fields.example.json");
            var samplePath = Path.Combine(directory.FullName, "samples", "offline-step-response.csv");
            if (File.Exists(configPath) && File.Exists(samplePath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
