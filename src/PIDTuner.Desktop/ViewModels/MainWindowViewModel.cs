using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.Services;
using PIDTuner.Desktop.Commands;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public const int LiveMonitorUiRefreshMilliseconds = 250;

    private readonly IOpenFileDialogService _openFileDialogService;
    private readonly ExampleWorkspaceWorkflow _exampleWorkspaceWorkflow;
    private readonly ExperimentWorkspaceViewModel _experimentWorkspace;
    private string _statusMessage = "阶段 1 已就绪：可在分析页导入离线 CSV 并计算基础指标。";

    public MainWindowViewModel()
        : this(
            new WindowsOpenFileDialogService(),
            MainWindowComposition.CreateFieldProfileStore(),
            MainWindowComposition.CreatePlcConfigurationStore(),
            MainWindowComposition.CreatePlcConnectivityProbe(),
            MainWindowComposition.CreatePlcTagSnapshotReader(),
            MainWindowComposition.CreateTestSessionRepository(MainWindowComposition.ResolvePath("local", "test-sessions")),
            MainWindowComposition.CreatePidSampleRepository(MainWindowComposition.ResolvePath("local", "test-sessions")),
            MainWindowComposition.CreateRecommendationReviewRepository(MainWindowComposition.ResolvePath("local", "recommendation-reviews")),
            MainWindowComposition.CreateParameterSetRepository(MainWindowComposition.ResolvePath("local", "parameter-sets")))
    {
    }

    public MainWindowViewModel(
        IOpenFileDialogService openFileDialogService,
        IPidSampleFieldProfileStore fieldProfileStore,
        IPlcProjectConfigurationStore plcProjectConfigurationStore,
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
        _openFileDialogService = openFileDialogService;
        var resolvedPlcConnectivityProbe = plcConnectivityProbe
            ?? MainWindowComposition.CreatePlcConnectivityProbe();
        var plcSnapshotSessionFactory = new PlcSnapshotSessionFactory(
            plcTagSnapshotReader ?? MainWindowComposition.CreatePlcTagSnapshotReader());
        var resolvedTestSessionStorageDirectory = Path.GetFullPath(
            testSessionStorageDirectory ?? MainWindowComposition.ResolvePath("local", "test-sessions"));
        var resolvedPlcRecordingStorageDirectory = Path.GetFullPath(
            plcRecordingStorageDirectory ?? MainWindowComposition.ResolvePath("local", "plc-recordings"));
        var plcOneSecondRecorder = new PlcOneSecondRecorder(
            plcSnapshotSessionFactory.OpenAsync,
            resolvedPlcRecordingStorageDirectory);
        var resolvedTestSessionRepository = testSessionRepository
            ?? MainWindowComposition.CreateTestSessionRepository(resolvedTestSessionStorageDirectory);
        var resolvedPidSampleRepository = pidSampleRepository
            ?? MainWindowComposition.CreatePidSampleRepository(resolvedTestSessionStorageDirectory);
        var resolvedRecommendationReviewRepository = recommendationReviewRepository
            ?? MainWindowComposition.CreateRecommendationReviewRepository(
                MainWindowComposition.ResolvePath("local", "recommendation-reviews"));
        var resolvedParameterSetRepository = parameterSetRepository
            ?? MainWindowComposition.CreateParameterSetRepository(
                MainWindowComposition.ResolvePath("local", "parameter-sets"));
        var experimentSessionCoordinator = new ExperimentSessionCoordinator(
            resolvedTestSessionRepository,
            resolvedPidSampleRepository,
            resolvedRecommendationReviewRepository,
            resolvedTestSessionStorageDirectory);
        var liveDiagnosticsStore = plcLiveDiagnosticsStore
            ?? MainWindowComposition.CreateLiveDiagnosticsStore(
                MainWindowComposition.ResolvePath("local", "plc-diagnostics", "plc-live-diagnostics.sqlite"));
        var historicalTrendStore = plcHistoricalTrendStore
            ?? MainWindowComposition.CreateHistoricalTrendStore(
                MainWindowComposition.ResolvePath("local", "plc-history", "plc-history.sqlite"));
        var historicalWriter = new PlcHistoricalAcquisitionWriter(historicalTrendStore);
        HistoricalTrend = new HistoricalTrendViewModel(
            new PlcHistoricalTrendCoordinator(historicalTrendStore));
        TrendExport = new PlcTrendExportViewModel(new PlcTrendVisibleExportWorkflow());
        LiveMonitor = new PlcLiveMonitorViewModel(
            new PlcAcquisitionEngine(plcSnapshotSessionFactory.OpenAsync, historicalWriter.Enqueue),
            historicalWriter);
        Debug = new PlcDebugViewModel(LiveMonitor.Tags, liveDiagnosticsStore);
        PlcLiveWorkspace = new PlcLiveWorkspaceViewModel(
            LiveMonitor,
            HistoricalTrend,
            Debug,
            plcSnapshotSessionFactory,
            () => !PlcTrendMode.IsHistoricalMode);
        PlcLiveWorkspace.SnapshotsApplied += (snapshots, trendTimestamp) =>
            PlcSnapshotsApplied?.Invoke(snapshots, trendTimestamp);
        PlcTrendWorkspace = new PlcTrendWorkspaceViewModel(
            PlcTrendMode,
            HistoricalTrend,
            LiveMonitor,
            PlcLiveWorkspace,
            Debug);
        PlcTrendWorkspace.TrendResetRequested += () => PlcTrendResetRequested?.Invoke();
        PlcTrendWorkspace.FramesApplied += frames => PlcSnapshotFramesApplied?.Invoke(frames);
        PlcTrendWorkspace.ViewportRequested += (start, end) => PlcHistoricalViewportRequested?.Invoke(start, end);
        PlcTrendWorkspace.LeftYRangeRequested += (min, max) => PlcTrendYRangeRequested?.Invoke(min, max);
        PlcTrendWorkspace.RightYRangeRequested += (min, max) => PlcTrendRightYRangeRequested?.Invoke(min, max);
        PlcTrendWorkspace.NotificationRequested += ApplyOperationResult;
        PlcRecordingWorkspace = new PlcRecordingWorkspaceViewModel(
            plcOneSecondRecorder,
            Debug,
            LiveMonitor,
            PlcLiveWorkspace,
            HistoricalTrend,
            PlcTrendWorkspace);
        PlcTrendWorkspace.SetReplayStopAction(PlcRecordingWorkspace.StopReplay);
        PlcRecordingWorkspace.NotificationRequested += ApplyOperationResult;
        PlcConfigurationEditor = new PlcConfigurationEditorViewModel(
            PlcProjectConfiguration.CreateDefault(),
            new PlcConfigurationWorkflow(plcProjectConfigurationStore, resolvedPlcConnectivityProbe));
        PlcConnectionWorkspace = new PlcConnectionWorkspaceViewModel(
            PlcConfigurationEditor,
            LiveMonitor,
            PlcLiveWorkspace);
        PlcDiagnosticsWorkspace = new PlcDiagnosticsWorkspaceViewModel(
            Debug,
            LiveMonitor,
            PlcConfigurationEditor);
        PlcDiagnosticsWorkspace.NotificationRequested += ApplyOperationResult;
        OfflineAnalysis = new OfflineAnalysisViewModel(new AnalysisResultExportWorkflow());
        FieldProfileEditor = new FieldProfileEditorViewModel(new FieldProfileWorkflow(fieldProfileStore));
        _exampleWorkspaceWorkflow = new ExampleWorkspaceWorkflow(
            MainWindowComposition.RepositoryRoot,
            FieldProfileEditor,
            OfflineAnalysis);
        ExperimentHistory = new ExperimentHistoryViewModel();
        _experimentWorkspace = new ExperimentWorkspaceViewModel(
            experimentSessionCoordinator,
            OfflineAnalysis,
            ExperimentHistory);
        ParameterSetLibrary = new ParameterSetLibraryViewModel(
            resolvedParameterSetRepository,
            new PidParameterSetExtractor());
        ParameterSetWorkspace = new ParameterSetWorkspaceViewModel(ParameterSetLibrary, OfflineAnalysis);
        ImportCsvCommand = new AsyncCommand(ImportCsvAsync);
        LoadPlcConfigurationCommand = new AsyncCommand(LoadPlcConfigurationAsync);
        SavePlcConfigurationCommand = new AsyncCommand(SavePlcConfigurationAsync);
        AddTagCommand = new AsyncCommand(AddTagAsync);
        RemoveTagCommand = new AsyncCommand(RemoveTagAsync);
        CheckPlcCommunicationCommand = new AsyncCommand(CheckPlcCommunicationAsync);
        RefreshPlcMonitorCommand = new AsyncCommand(RefreshPlcMonitorAsync);
        TogglePlcMonitoringCommand = new AsyncCommand(TogglePlcMonitoringAsync);
        TogglePlcLiveDiagnosticsCommand = new AsyncCommand(TogglePlcLiveDiagnosticsAsync);
        RecordPlcOneSecondCommand = new AsyncCommand(RecordPlcOneSecondAsync);
        LoadPlcRecordingCommand = new AsyncCommand(LoadPlcRecordingAsync);
        ShowPlcLiveTrendCommand = new AsyncCommand(ShowPlcLiveTrendAsync);
        ShowPlcHistoricalTrendCommand = new AsyncCommand(ShowPlcHistoricalTrendAsync);
        SetPlcSingleAxisLayoutCommand = new AsyncCommand(SetPlcSingleAxisLayoutAsync);
        SetPlcDualAxisLayoutCommand = new AsyncCommand(SetPlcDualAxisLayoutAsync);
        TogglePlcLiveTrendPauseCommand = new AsyncCommand(TogglePlcLiveTrendPauseAsync);
        ApplyPlcHistoricalRangeCommand = new AsyncCommand(ApplyPlcHistoricalRangeAsync);
        ResetPlcHistoricalRangeCommand = new AsyncCommand(ResetPlcHistoricalRangeAsync);
        ApplyPlcTrendYRangeCommand = new AsyncCommand(ApplyPlcTrendYRangeAsync);
        ResetPlcTrendYRangeCommand = new AsyncCommand(ResetPlcTrendYRangeAsync);
        ResetPlcTrendRightYRangeCommand = new AsyncCommand(ResetPlcTrendRightYRangeAsync);
        TogglePlcReplayCommand = new AsyncCommand(TogglePlcReplayAsync);
        StepPlcReplayBackwardCommand = new AsyncCommand(StepPlcReplayBackwardAsync);
        StepPlcReplayForwardCommand = new AsyncCommand(StepPlcReplayForwardAsync);
        SetPlcReplaySpeedHalfCommand = new AsyncCommand(() => SetPlcReplaySpeedAsync(0.5d));
        SetPlcReplaySpeedNormalCommand = new AsyncCommand(() => SetPlcReplaySpeedAsync(1d));
        SetPlcReplaySpeedDoubleCommand = new AsyncCommand(() => SetPlcReplaySpeedAsync(2d));
        SetPlcReplaySpeedFiveCommand = new AsyncCommand(() => SetPlcReplaySpeedAsync(5d));
        LoadFieldProfileCommand = new AsyncCommand(LoadFieldProfileAsync);
        AddFieldCommand = new AsyncCommand(AddFieldAsync);
        RemoveFieldCommand = new AsyncCommand(RemoveFieldAsync);
        SaveFieldProfileCommand = new AsyncCommand(SaveFieldProfileAsync);
        ExportAnalysisResultCommand = new AsyncCommand(ExportAnalysisResultAsync);
        SaveTestSessionCommand = new AsyncCommand(SaveTestSessionAsync);
        LoadExampleCommand = new AsyncCommand(LoadExampleAsync);
        DismissNotificationCommand = new AsyncCommand(DismissNotificationAsync);
        LoadHistoryCommand = new AsyncCommand(LoadHistoryAsync);
        OpenHistorySessionCommand = new AsyncCommand(OpenHistorySessionAsync);
        ExportHistorySamplesCommand = new AsyncCommand(ExportHistorySamplesAsync);
        SetHistoryBaselineCommand = new AsyncCommand(SetHistoryBaselineAsync);
        CompareHistorySessionCommand = new AsyncCommand(CompareHistorySessionAsync);
        AcceptRecommendationCommand = new AsyncCommand(AcceptRecommendationAsync);
        DeferRecommendationCommand = new AsyncCommand(DeferRecommendationAsync);
        LoadRecommendationReviewsCommand = new AsyncCommand(LoadRecommendationReviewsAsync);
        SaveParameterSetCommand = new AsyncCommand(SaveParameterSetAsync);
        LoadParameterSetsCommand = new AsyncCommand(LoadParameterSetsAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<IReadOnlyList<PlcTagSnapshot>, DateTimeOffset?>? PlcSnapshotsApplied;

    public event Action<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>>? PlcSnapshotFramesApplied;

    public event Action? PlcTrendResetRequested;

    public event Action<DateTimeOffset?, DateTimeOffset?>? PlcHistoricalViewportRequested;

    public event Action<double?, double?>? PlcTrendYRangeRequested;

    public event Action<double?, double?>? PlcTrendRightYRangeRequested;

    public string Title { get; } = "PIDTuner";

    public PlcLiveMonitorViewModel LiveMonitor { get; }

    public PlcLiveWorkspaceViewModel PlcLiveWorkspace { get; }

    public PlcDebugViewModel Debug { get; }

    public PlcConfigurationEditorViewModel PlcConfigurationEditor { get; }

    public PlcConnectionWorkspaceViewModel PlcConnectionWorkspace { get; }

    public PlcDiagnosticsWorkspaceViewModel PlcDiagnosticsWorkspace { get; }

    public OfflineAnalysisViewModel OfflineAnalysis { get; }

    public ExperimentHistoryViewModel ExperimentHistory { get; }

    public ParameterSetLibraryViewModel ParameterSetLibrary { get; }

    public ParameterSetWorkspaceViewModel ParameterSetWorkspace { get; }

    public HistoricalTrendViewModel HistoricalTrend { get; }

    public PlcTrendExportViewModel TrendExport { get; }

    public PlcTrendWorkspaceViewModel PlcTrendWorkspace { get; }

    public PlcRecordingWorkspaceViewModel PlcRecordingWorkspace { get; }

    public HistoricalTrendWorkbenchViewModel HistoricalTrendWorkbench => HistoricalTrend.Workbench;

    public FieldProfileEditorViewModel FieldProfileEditor { get; }

    public PlcTrendModeViewModel PlcTrendMode { get; } = new();

    public NotificationViewModel Notification { get; } = new();

    public IReadOnlyList<string> AvailablePlcDataTypes { get; } =
        Enum.GetNames<PlcDataType>();

    public IReadOnlyList<string> AvailableTagAccessModes { get; } =
        Enum.GetNames<TagAccessMode>();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand ImportCsvCommand { get; }

    public ICommand LoadPlcConfigurationCommand { get; }

    public ICommand SavePlcConfigurationCommand { get; }

    public ICommand AddTagCommand { get; }

    public ICommand RemoveTagCommand { get; }

    public ICommand CheckPlcCommunicationCommand { get; }

    public ICommand RefreshPlcMonitorCommand { get; }

    public ICommand TogglePlcMonitoringCommand { get; }

    public ICommand TogglePlcLiveDiagnosticsCommand { get; }

    public ICommand RecordPlcOneSecondCommand { get; }

    public ICommand LoadPlcRecordingCommand { get; }

    public ICommand ShowPlcLiveTrendCommand { get; }

    public ICommand ShowPlcHistoricalTrendCommand { get; }

    public ICommand SetPlcSingleAxisLayoutCommand { get; }

    public ICommand SetPlcDualAxisLayoutCommand { get; }

    public ICommand TogglePlcLiveTrendPauseCommand { get; }

    public ICommand ApplyPlcHistoricalRangeCommand { get; }

    public ICommand ResetPlcHistoricalRangeCommand { get; }

    public ICommand ApplyPlcTrendYRangeCommand { get; }

    public ICommand ResetPlcTrendYRangeCommand { get; }

    public ICommand ResetPlcTrendRightYRangeCommand { get; }

    public ICommand TogglePlcReplayCommand { get; }

    public ICommand StepPlcReplayBackwardCommand { get; }

    public ICommand StepPlcReplayForwardCommand { get; }

    public ICommand SetPlcReplaySpeedHalfCommand { get; }

    public ICommand SetPlcReplaySpeedNormalCommand { get; }

    public ICommand SetPlcReplaySpeedDoubleCommand { get; }

    public ICommand SetPlcReplaySpeedFiveCommand { get; }

    public ICommand LoadFieldProfileCommand { get; }

    public ICommand AddFieldCommand { get; }

    public ICommand RemoveFieldCommand { get; }

    public ICommand SaveFieldProfileCommand { get; }

    public ICommand ExportAnalysisResultCommand { get; }

    public ICommand SaveTestSessionCommand { get; }

    public ICommand LoadExampleCommand { get; }

    public ICommand DismissNotificationCommand { get; }

    public ICommand LoadHistoryCommand { get; }

    public ICommand OpenHistorySessionCommand { get; }

    public ICommand ExportHistorySamplesCommand { get; }

    public ICommand SetHistoryBaselineCommand { get; }

    public ICommand CompareHistorySessionCommand { get; }

    public ICommand AcceptRecommendationCommand { get; }

    public ICommand DeferRecommendationCommand { get; }

    public ICommand LoadRecommendationReviewsCommand { get; }

    public ICommand SaveParameterSetCommand { get; }

    public ICommand LoadParameterSetsCommand { get; }

    public async Task LoadExampleAsync()
    {
        ApplyOperationResult(await _exampleWorkspaceWorkflow.LoadAsync(CancellationToken.None));
    }

    public async Task SavePlcConfigurationAsync()
    {
        var fileName = _openFileDialogService.PickPlcProjectConfigurationSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(await PlcConnectionWorkspace.SaveAsync(fileName, CancellationToken.None));
    }

    public async Task CheckPlcCommunicationAsync()
    {
        ApplyOperationResult(
            await PlcConnectionWorkspace.CheckCommunicationAsync(
                startMonitoringOnSuccess: false,
                CancellationToken.None));
    }

    public async Task RefreshPlcMonitorAsync()
    {
        var result = await PlcLiveWorkspace.RefreshAsync(
            PlcConfigurationEditor.BuildConfiguration(),
            PlcTrendMode.IsHistoricalMode,
            CancellationToken.None);
        if (result.ShouldUseLiveMode)
        {
            PlcTrendMode.UseLiveMode();
        }

        if (result.ShouldResetTrend)
        {
            PlcTrendResetRequested?.Invoke();
        }

        if (result.Notification is not null)
        {
            ApplyOperationResult(result.Notification);
        }
    }

    private async Task StopLiveMonitoringAsync()
    {
        await PlcLiveWorkspace.StopAsync(CancellationToken.None);
        await StopPlcLiveDiagnosticsAsync("实时监控已停止，诊断写入已关闭。");
    }

    public async Task TogglePlcMonitoringAsync()
    {
        if (LiveMonitor.IsMonitoring)
        {
            await StopLiveMonitoringAsync();
            return;
        }

        PlcRecordingWorkspace.StopReplay();
        await PlcLiveWorkspace.StartAsync(
            PlcConfigurationEditor.BuildConfiguration(),
            resetHistory: true,
            CancellationToken.None);
    }

    public async Task TogglePlcLiveDiagnosticsAsync()
    {
        await PlcDiagnosticsWorkspace.ToggleAsync(CancellationToken.None);
    }

    private async Task StopPlcLiveDiagnosticsAsync(string reason)
    {
        await PlcDiagnosticsWorkspace.StopAsync(reason, CancellationToken.None);
    }

    public async Task RecordPlcOneSecondAsync()
    {
        await PlcRecordingWorkspace.RecordOneSecondAsync(
            PlcConfigurationEditor.BuildConfiguration(),
            CancellationToken.None);
    }

    public async Task LoadPlcRecordingAsync()
    {
        await LoadPlcRecordingAsync(showFullHistory: false);
    }

    private async Task LoadPlcRecordingAsync(bool showFullHistory)
    {
        var fileName = _openFileDialogService.PickPlcRecordingFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        await PlcRecordingWorkspace.LoadAsync(fileName, showFullHistory, CancellationToken.None);
    }

    public async Task ShowPlcLiveTrendAsync()
    {
        await PlcTrendWorkspace.ShowLiveAsync(
            PlcConfigurationEditor.BuildConfiguration(),
            CancellationToken.None);
    }

    public void UsePlcLiveTrendMode() => PlcTrendWorkspace.UseLiveMode();

    public Task TogglePlcLiveTrendPauseAsync()
    {
        PlcTrendWorkspace.ToggleLivePause();
        return Task.CompletedTask;
    }

    public Task SetPlcSingleAxisLayoutAsync()
    {
        PlcTrendWorkspace.UseSingleAxisLayout();
        return Task.CompletedTask;
    }

    public Task SetPlcDualAxisLayoutAsync()
    {
        PlcTrendWorkspace.UseDualAxisLayout();
        return Task.CompletedTask;
    }

    public async Task SetPlcHistoricalTrendWindowAsync(TimeSpan window)
    {
        await PlcTrendWorkspace.SetHistoricalWindowAsync(window, CancellationToken.None);
    }

    public async Task ShowPlcHistoricalTrendAsync()
    {
        await PlcTrendWorkspace.ShowHistoricalAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    public async Task ShowPlcHistoricalTrendAsync(TimeSpan visibleWindow)
    {
        await PlcTrendWorkspace.ShowHistoricalAsync(visibleWindow, CancellationToken.None);
    }

    public async Task ApplyPlcHistoricalRangeAsync()
    {
        await PlcTrendWorkspace.ApplyHistoricalRangeAsync(CancellationToken.None);
    }

    public Task ResetPlcHistoricalRangeAsync()
    {
        PlcTrendWorkspace.ResetHistoricalRange();
        return Task.CompletedTask;
    }

    public Task ApplyPlcTrendYRangeAsync()
    {
        PlcTrendWorkspace.ApplyLeftYRange();
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendYRangeAsync()
    {
        PlcTrendWorkspace.ResetLeftYRange();
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendRightYRangeAsync()
    {
        PlcTrendWorkspace.ResetRightYRange();
        return Task.CompletedTask;
    }

    public Task TogglePlcReplayAsync()
    {
        PlcRecordingWorkspace.ToggleReplay();
        return Task.CompletedTask;
    }

    public Task StepPlcReplayBackwardAsync()
    {
        PlcRecordingWorkspace.StepBackward();
        return Task.CompletedTask;
    }

    public Task StepPlcReplayForwardAsync()
    {
        PlcRecordingWorkspace.StepForward();
        return Task.CompletedTask;
    }

    public Task SetPlcReplaySpeedAsync(double speedMultiplier)
    {
        PlcRecordingWorkspace.SetSpeed(speedMultiplier);
        return Task.CompletedTask;
    }

    public async Task LoadPlcConfigurationAsync()
    {
        var fileName = _openFileDialogService.PickPlcProjectConfigurationFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(await PlcConnectionWorkspace.LoadAsync(fileName, CancellationToken.None));
    }

    private Task AddTagAsync()
    {
        ApplyOperationResult(PlcConfigurationEditor.AddTagOperation());
        return Task.CompletedTask;
    }

    private Task RemoveTagAsync()
    {
        ApplyOperationResult(PlcConfigurationEditor.RemoveSelectedTagOperation());
        return Task.CompletedTask;
    }

    public async Task SaveTestSessionAsync()
    {
        ApplyOperationResult(
            await _experimentWorkspace.SaveSessionAsync(
                FieldProfileEditor.Profile.ProfileName,
                CancellationToken.None));
    }

    public async Task LoadHistoryAsync()
    {
        await LoadHistoryAsync(showNotification: true);
    }

    public async Task OpenHistorySessionAsync()
    {
        ApplyOperationResult(
            await _experimentWorkspace.OpenSelectedSessionAsync(CancellationToken.None));
    }

    public async Task ExportHistorySamplesAsync()
    {
        var fileName = _openFileDialogService.PickHistorySamplesSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(
            await _experimentWorkspace.ExportSelectedSamplesAsync(
                FieldProfileEditor.Profile,
                fileName,
                CancellationToken.None));
    }

    public async Task ExportVisiblePlcTrendAsync(PlcTrendVisibleExport export)
    {
        var validation = TrendExport.ValidateVisibleExport(export);
        if (validation is not null)
        {
            ApplyOperationResult(validation);
            return;
        }

        var fileName = _openFileDialogService.PickVisiblePlcTrendSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(
            await TrendExport.ExportVisibleResultAsync(
                fileName,
                export,
                CancellationToken.None));
    }

    public Task SetHistoryBaselineAsync()
    {
        ApplyOperationResult(_experimentWorkspace.SetSelectedAsBaseline());
        return Task.CompletedTask;
    }

    public async Task CompareHistorySessionAsync()
    {
        ApplyOperationResult(
            await _experimentWorkspace.CompareSelectedSessionAsync(CancellationToken.None));
    }

    public async Task AcceptRecommendationAsync()
    {
        await ReviewRecommendationAsync(PidRecommendationReviewDecision.Accepted);
    }

    public async Task DeferRecommendationAsync()
    {
        await ReviewRecommendationAsync(PidRecommendationReviewDecision.Deferred);
    }

    public async Task LoadRecommendationReviewsAsync()
    {
        var result = await _experimentWorkspace.LoadRecommendationReviewsAsync(CancellationToken.None);
        if (result is not null)
        {
            ApplyOperationResult(result);
        }
    }

    private async Task LoadHistoryAsync(bool showNotification)
    {
        var result = await _experimentWorkspace.LoadHistoryAsync(
            showNotification,
            CancellationToken.None);
        if (result is not null)
        {
            ApplyOperationResult(result);
        }
    }

    public async Task SaveParameterSetAsync()
    {
        ApplyOperationResult(await ParameterSetWorkspace.SaveLatestAsync(CancellationToken.None));
    }

    public async Task LoadParameterSetsAsync()
    {
        await LoadParameterSetsAsync(showNotification: true);
    }

    private async Task ReviewRecommendationAsync(PidRecommendationReviewDecision decision)
    {
        ApplyOperationResult(
            await _experimentWorkspace.ReviewSelectedRecommendationAsync(
                decision,
                CancellationToken.None));
    }

    private async Task LoadParameterSetsAsync(bool showNotification)
    {
        var result = await ParameterSetWorkspace.LoadAsync(showNotification, CancellationToken.None);
        if (result is not null)
        {
            ApplyOperationResult(result);
        }
    }

    private async Task ExportAnalysisResultAsync()
    {
        var validation = OfflineAnalysis.ValidateLastResultExport();
        if (validation is not null)
        {
            ApplyOperationResult(validation);
            return;
        }

        var fileName = _openFileDialogService.PickAnalysisResultSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(
            await OfflineAnalysis.ExportLastResultOperationAsync(fileName, CancellationToken.None));
    }

    private Task AddFieldAsync()
    {
        ApplyOperationResult(FieldProfileEditor.AddFieldOperation());
        return Task.CompletedTask;
    }

    private Task RemoveFieldAsync()
    {
        ApplyOperationResult(FieldProfileEditor.RemoveSelectedFieldOperation());
        return Task.CompletedTask;
    }

    private async Task SaveFieldProfileAsync()
    {
        var fileName = _openFileDialogService.PickFieldProfileSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(
            await FieldProfileEditor.SaveOperationAsync(fileName, CancellationToken.None));
    }

    private async Task LoadFieldProfileAsync()
    {
        var fileName = _openFileDialogService.PickFieldProfileFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(
            await FieldProfileEditor.LoadOperationAsync(fileName, CancellationToken.None));
    }

    private async Task ImportCsvAsync()
    {
        var fileName = _openFileDialogService.PickCsvFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyOperationResult(
            await OfflineAnalysis.AnalyzeCsvFileOperationAsync(
                fileName,
                FieldProfileEditor.Profile,
                CancellationToken.None));
    }

    private Task DismissNotificationAsync()
    {
        Notification.Dismiss();
        return Task.CompletedTask;
    }

    private void ApplyOperationResult(IWorkspaceOperationResult result)
    {
        Notify(result.Title, result.Message, result.Kind);
    }

    private void Notify(string title, string message, string kind)
    {
        StatusMessage = $"{title}：{message}";
        Notification.Show(title, message, kind);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
