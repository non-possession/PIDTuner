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
    private readonly PlcOneSecondRecorder _plcOneSecondRecorder;
    private readonly ExperimentWorkspaceViewModel _experimentWorkspace;
    private readonly PlcReplayController _plcReplayController;
    private readonly PlcDiagnosticsController _plcDiagnosticsController;
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
        _plcOneSecondRecorder = new PlcOneSecondRecorder(
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
        _plcReplayController = new PlcReplayController(Debug, ApplyPlcReplayOperation);
        _plcDiagnosticsController = new PlcDiagnosticsController(Debug, ApplyPlcDiagnosticsOperation);
        PlcConfigurationEditor = new PlcConfigurationEditorViewModel(
            PlcProjectConfiguration.CreateDefault(),
            new PlcConfigurationWorkflow(plcProjectConfigurationStore, resolvedPlcConnectivityProbe));
        OfflineAnalysis = new OfflineAnalysisViewModel(new AnalysisResultExportWorkflow());
        FieldProfileEditor = new FieldProfileEditorViewModel(new FieldProfileWorkflow(fieldProfileStore));
        ExperimentHistory = new ExperimentHistoryViewModel();
        _experimentWorkspace = new ExperimentWorkspaceViewModel(
            experimentSessionCoordinator,
            OfflineAnalysis,
            ExperimentHistory);
        ParameterSetLibrary = new ParameterSetLibraryViewModel(
            resolvedParameterSetRepository,
            new PidParameterSetExtractor());
        PlcTrendMode.PropertyChanged += PlcTrendMode_PropertyChanged;
        HistoricalTrendWorkbench.ViewportRequested += (start, end) => PlcHistoricalViewportRequested?.Invoke(start, end);
        HistoricalTrendWorkbench.YRangeRequested += (min, max) => PlcTrendYRangeRequested?.Invoke(min, max);
        HistoricalTrendWorkbench.RightYRangeRequested += (min, max) => PlcTrendRightYRangeRequested?.Invoke(min, max);
        HistoricalTrendWorkbench.StatusRequested += (message, replayPhase) =>
        {
            LiveMonitor.MonitorStatus = message;
            if (!string.IsNullOrWhiteSpace(replayPhase))
            {
                Debug.UpdateReplayStatus(replayPhase);
            }
        };
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

    public OfflineAnalysisViewModel OfflineAnalysis { get; }

    public ExperimentHistoryViewModel ExperimentHistory { get; }

    public ParameterSetLibraryViewModel ParameterSetLibrary { get; }

    public HistoricalTrendViewModel HistoricalTrend { get; }

    public PlcTrendExportViewModel TrendExport { get; }

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
        var repositoryRoot = MainWindowComposition.RepositoryRoot;
        var fieldProfilePath = Path.Combine(repositoryRoot, "config", "pid-sample-fields.example.json");
        var csvPath = Path.Combine(repositoryRoot, "samples", "offline-step-response.csv");

        if (!File.Exists(fieldProfilePath) || !File.Exists(csvPath))
        {
            Notify("示例加载失败", "示例文件不存在，请确认从仓库根目录运行程序。", "Error");
            return;
        }

        try
        {
            await FieldProfileEditor.LoadFromFileAsync(fieldProfilePath, CancellationToken.None);

            await AnalyzeCsvFileAsync(csvPath);
        }
        catch (Exception exception)
        {
            Notify("示例加载失败", exception.Message, "Error");
        }
    }

    private void PlcTrendMode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        LiveMonitor.IsLiveTrendPaused = PlcTrendMode.IsLiveScrollingPaused;
    }

    public async Task SavePlcConfigurationAsync()
    {
        var fileName = _openFileDialogService.PickPlcProjectConfigurationSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var savedPath = await PlcConfigurationEditor.SaveToFileAsync(fileName, CancellationToken.None);
            Notify("PLC 配置已保存", savedPath, "Success");
        }
        catch (Exception exception)
        {
            Notify("PLC 配置保存失败", exception.Message, "Error");
        }
    }

    public async Task CheckPlcCommunicationAsync()
    {
        _ = await CheckPlcCommunicationInternalAsync(startMonitoringOnSuccess: false);
    }

    private async Task<bool> CheckPlcCommunicationInternalAsync(bool startMonitoringOnSuccess)
    {
        try
        {
            var configuration = PlcConfigurationEditor.BuildConfiguration();
            var result = await PlcConfigurationEditor.CheckCommunicationAsync(CancellationToken.None);
            Notify(result.Title, PlcConfigurationEditor.CommunicationStatus, result.Kind);
            if (result.IsReachable && startMonitoringOnSuccess)
            {
                await EnsurePlcMonitoringAsync(configuration, resetHistory: true);
            }

            return result.IsReachable;
        }
        catch (Exception exception)
        {
            Notify("PLC 通信检查失败", exception.Message, "Error");
            return false;
        }
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

        if (!string.IsNullOrWhiteSpace(result.NotificationTitle)
            && !string.IsNullOrWhiteSpace(result.NotificationMessage)
            && !string.IsNullOrWhiteSpace(result.NotificationKind))
        {
            Notify(result.NotificationTitle, result.NotificationMessage, result.NotificationKind);
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

        StopPlcReplay();
        await EnsurePlcMonitoringAsync(PlcConfigurationEditor.BuildConfiguration(), resetHistory: true);
    }

    private async Task EnsurePlcMonitoringAsync(
        PlcProjectConfiguration configuration,
        bool resetHistory)
    {
        await PlcLiveWorkspace.StartAsync(
            configuration,
            resetHistory,
            CancellationToken.None);
    }

    public async Task TogglePlcLiveDiagnosticsAsync()
    {
        if (Debug.IsDiagnosticsRunning)
        {
            await StopPlcLiveDiagnosticsAsync("诊断已手动停止。");
            return;
        }

        if (!LiveMonitor.IsMonitoring)
        {
            Notify("无法启动实时诊断", "请先启动实时监控。", "Warning");
            return;
        }

        await _plcDiagnosticsController.StartAsync(
                PlcConfigurationEditor.BuildConfiguration(),
            TimeSpan.FromMinutes(Debug.DiagnosticsDurationMinutes),
            CancellationToken.None);
    }

    private async Task StopPlcLiveDiagnosticsAsync(string reason)
    {
        await _plcDiagnosticsController.StopAsync(reason, CancellationToken.None);
    }

    private void ApplyPlcDiagnosticsOperation(PlcDiagnosticsOperationResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.NotificationTitle)
            && !string.IsNullOrWhiteSpace(result.NotificationMessage)
            && !string.IsNullOrWhiteSpace(result.NotificationKind))
        {
            Notify(result.NotificationTitle, result.NotificationMessage, result.NotificationKind);
        }
    }

    public async Task RecordPlcOneSecondAsync()
    {
        try
        {
            StopPlcReplay();
            var configuration = PlcConfigurationEditor.BuildConfiguration();
            LiveMonitor.MonitorStatus = "正在记录 1s 点位数据。";
            LiveMonitor.AcquisitionDiagnosticsStatus = "采集诊断：正在记录当前 1s 采集链路。";
            var result = await _plcOneSecondRecorder.RecordAsync(
                configuration,
                snapshots => PlcLiveWorkspace.ApplySnapshots(snapshots, storeLiveHistory: false),
                CancellationToken.None);
            if (!result.IsSuccess)
            {
                Notify("无法记录 PLC 数据", "请先启用至少一个可读取点位。", "Warning");
                return;
            }

            HistoricalTrend.RememberFrames(result.Frames);
            LiveMonitor.MonitorStatus = result.MonitorStatus;
            LiveMonitor.AcquisitionDiagnosticsStatus = result.DiagnosticsStatus;
            Notify(
                "PLC 1s 记录完成",
                string.Join(
                    Environment.NewLine,
                    LiveMonitor.MonitorStatus,
                    result.DiagnosticsStatus,
                    $"保存位置：{result.RecordingPath}"),
                "Success");
        }
        catch (Exception exception)
        {
            LiveMonitor.MonitorStatus = $"1s 记录失败：{exception.Message}";
            Notify("PLC 1s 记录失败", exception.Message, "Error");
        }
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

        try
        {
            var loadResult = await _plcOneSecondRecorder.LoadAsync(
                fileName,
                CancellationToken.None);
            if (!loadResult.IsSuccess)
            {
                Notify("PLC 记录加载失败", "记录文件没有可回放的帧。", "Warning");
                return;
            }

            var recording = loadResult.Recording!;
            StopPlcReplay();
            HistoricalTrend.ClearLiveFrames();
            Debug.LoadReplay(recording.Frames, recording.IntervalMilliseconds);
            HistoricalTrend.RememberFrames(recording.Frames);
            HistoricalTrendWorkbench.SetRangeTextFromFrames(recording.Frames);

            LiveMonitor.ClearTags();
            PlcTrendResetRequested?.Invoke();
            if (showFullHistory)
            {
                ShowLoadedPlcHistoricalTrend();
            }
            else
            {
                PlcTrendMode.UseLiveMode();
                ApplyPlcReplayOperation(Debug.ApplyReplayFrame(0, advance: true, "已定位"));
            }

            LiveMonitor.MonitorStatus =
                $"已加载 PLC 记录：{recording.FrameCount} 帧，{recording.SnapshotCount} 条快照，周期 {Debug.SourceReplayIntervalMilliseconds} ms。";
            Debug.UpdateReplayStatus("已加载");
            Notify(
                "PLC 记录已加载",
                string.Join(Environment.NewLine, LiveMonitor.MonitorStatus, $"文件位置：{Path.GetFullPath(fileName)}"),
                "Success");
        }
        catch (Exception exception)
        {
            Notify("PLC 记录加载失败", exception.Message, "Error");
        }
    }

    public async Task ShowPlcLiveTrendAsync()
    {
        if (!LiveMonitor.IsMonitoring)
        {
        await EnsurePlcMonitoringAsync(PlcConfigurationEditor.BuildConfiguration(), resetHistory: false);
        }

        StopPlcReplay();
        UsePlcLiveTrendMode();
        PlcTrendResetRequested?.Invoke();
        PlcLiveWorkspace.DrainNow();
    }

    public void UsePlcLiveTrendMode()
    {
        PlcTrendMode.UseLiveMode();
        HistoricalTrendWorkbench.Clear();
    }

    public Task TogglePlcLiveTrendPauseAsync()
    {
        if (PlcTrendMode.IsHistoricalMode)
        {
            return Task.CompletedTask;
        }

        PlcTrendMode.ToggleLiveScrollingPause();
        return Task.CompletedTask;
    }

    public Task SetPlcSingleAxisLayoutAsync()
    {
        HistoricalTrendWorkbench.UseSingleAxisLayout();
        return Task.CompletedTask;
    }

    public Task SetPlcDualAxisLayoutAsync()
    {
        HistoricalTrendWorkbench.UseDualAxisLayout();
        LiveMonitor.EnsureVisibleAxisGroups();
        return Task.CompletedTask;
    }

    public async Task SetPlcHistoricalTrendWindowAsync(TimeSpan window)
    {
        var end = HistoricalTrendWorkbench.HasDataset
            ? HistoricalTrendWorkbench.RangeEndValue
            : DateTimeOffset.Now;
        var frames = await HistoricalTrend.LoadWindowAsync(
            end,
            window,
            HistoricalTrend.CurrentFrames,
            CancellationToken.None);
        if (frames.Count > 0)
        {
            HistoricalTrend.RememberFrames(frames);
            PlcSnapshotFramesApplied?.Invoke(frames);
        }

        await SetPlcHistoricalTrendWindowFromLoadedDataAsync(window);
    }

    private Task SetPlcHistoricalTrendWindowFromLoadedDataAsync(TimeSpan window)
    {
        ApplyHistoricalTrendAction(HistoricalTrend.SetVisibleWindow(window));
        return Task.CompletedTask;
    }

    public async Task ShowPlcHistoricalTrendAsync()
    {
        await ShowPlcHistoricalTrendFromStoreAsync(TimeSpan.FromSeconds(30));
    }

    public async Task ShowPlcHistoricalTrendAsync(TimeSpan visibleWindow)
    {
        await ShowPlcHistoricalTrendFromStoreAsync(visibleWindow);
    }

    private async Task ShowPlcHistoricalTrendFromStoreAsync(TimeSpan visibleWindow)
    {
        StopPlcReplay();
        var end = DateTimeOffset.Now;
        var frames = await HistoricalTrend.LoadWindowAsync(
            end,
            visibleWindow,
            Debug.LoadedReplayFrames,
            CancellationToken.None);
        if (frames.Count == 0)
        {
            PlcTrendMode.UseHistoricalMode();
            return;
        }

        HistoricalTrend.RememberFrames(frames);
        PlcTrendResetRequested?.Invoke();
        ShowLoadedPlcHistoricalTrend(frames);
        HistoricalTrend.SetWindowEndingAt(end, visibleWindow);
    }

    public async Task ApplyPlcHistoricalRangeAsync()
    {
        var result = await HistoricalTrend.ApplySelectedRangeAsync(CancellationToken.None);
        if (result.Frames.Count > 0)
        {
            HistoricalTrend.RememberFrames(result.Frames);
            PlcSnapshotFramesApplied?.Invoke(result.Frames);
        }

        ApplyHistoricalTrendAction(result.Action);
    }

    public Task ResetPlcHistoricalRangeAsync()
    {
        ApplyHistoricalTrendAction(HistoricalTrend.ResetTimeRange(HistoricalTrend.CurrentFrames.Count));
        return Task.CompletedTask;
    }

    public Task ApplyPlcTrendYRangeAsync()
    {
        ApplyHistoricalTrendAction(HistoricalTrend.ApplyLeftYRange());
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendYRangeAsync()
    {
        ApplyHistoricalTrendAction(HistoricalTrend.ResetLeftYRange());
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendRightYRangeAsync()
    {
        ApplyHistoricalTrendAction(HistoricalTrend.ResetRightYRange());
        return Task.CompletedTask;
    }

    private void ApplyHistoricalTrendAction(HistoricalTrendActionResult result)
    {
        if (!result.IsSuccess)
        {
            Notify(result.ErrorTitle!, result.ErrorMessage!, "Warning");
            return;
        }

        PlcTrendMode.UseHistoricalMode();
        LiveMonitor.MonitorStatus = result.Status!;
        if (!string.IsNullOrWhiteSpace(result.ReplayPhase))
        {
            Debug.UpdateReplayStatus(result.ReplayPhase);
        }
    }

    public Task TogglePlcReplayAsync()
    {
        _plcReplayController.Toggle();
        return Task.CompletedTask;
    }

    public Task StepPlcReplayBackwardAsync()
    {
        _plcReplayController.StepBackward();
        return Task.CompletedTask;
    }

    public Task StepPlcReplayForwardAsync()
    {
        _plcReplayController.StepForward();
        return Task.CompletedTask;
    }

    public Task SetPlcReplaySpeedAsync(double speedMultiplier)
    {
        _plcReplayController.SetSpeed(speedMultiplier);
        return Task.CompletedTask;
    }

    private void ApplyPlcReplayOperation(PlcReplayOperationResult result)
    {
        if (result.ResetTrend)
        {
            LiveMonitor.ClearTags();
            PlcTrendResetRequested?.Invoke();
        }

        if (result.FramesToApply is not null)
        {
            foreach (var frame in result.FramesToApply)
            {
                PlcLiveWorkspace.ApplySnapshots(frame, storeLiveHistory: false);
            }
        }

        if (result.FrameToApply is not null)
        {
            PlcLiveWorkspace.ApplySnapshots(result.FrameToApply, storeLiveHistory: false);
        }

        if (!string.IsNullOrWhiteSpace(result.MonitorStatus))
        {
            LiveMonitor.MonitorStatus = result.MonitorStatus;
        }

        if (!string.IsNullOrWhiteSpace(result.NotificationTitle) &&
            !string.IsNullOrWhiteSpace(result.NotificationMessage) &&
            !string.IsNullOrWhiteSpace(result.NotificationKind))
        {
            Notify(result.NotificationTitle, result.NotificationMessage, result.NotificationKind);
        }
    }

    private void ShowLoadedPlcHistoricalTrend()
    {
        ShowLoadedPlcHistoricalTrend(Debug.LoadedReplayFrames);
    }

    private void ShowLoadedPlcHistoricalTrend(IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        if (frames.Count == 0)
        {
            return;
        }

        PlcTrendMode.MarkHistoricalModeDisplayed();
        HistoricalTrend.LoadFrames(frames);
        for (var index = 0; index < frames.Count; index++)
        {
            PlcLiveWorkspace.ApplySnapshots(frames[index], applyTrend: false, storeLiveHistory: false);
        }

        PlcSnapshotFramesApplied?.Invoke(frames);
        Debug.MarkHistoricalReplayDisplayed();
        LiveMonitor.MonitorStatus = $"历史趋势已显示：{frames.Count} 帧。";
    }

    private bool EnsurePlcReplayLoaded()
    {
        if (Debug.HasReplayFrames)
        {
            return true;
        }

        Notify("无法控制 PLC 回放", "请先打开一个 PLC 记录 JSON 文件。", "Warning");
        return false;
    }

    private void StopPlcReplay()
    {
        _plcReplayController.Stop();
    }

    public async Task LoadPlcConfigurationAsync()
    {
        var fileName = _openFileDialogService.PickPlcProjectConfigurationFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var loadedFileName = await PlcConfigurationEditor.LoadFromFileAsync(fileName, CancellationToken.None);
            LiveMonitor.ClearTags();
            LiveMonitor.MonitorStatus = "PLC 配置已更新，等待刷新点位。";
            Notify("PLC 配置已加载", loadedFileName, "Success");
            await CheckPlcCommunicationInternalAsync(startMonitoringOnSuccess: true);
        }
        catch (Exception exception)
        {
            Notify("PLC 配置加载失败", exception.Message, "Error");
        }
    }

    private Task AddTagAsync()
    {
        PlcConfigurationEditor.AddTag();
        Notify("点位已新增", "请编辑点位信息后保存 PLC 配置。", "Info");
        return Task.CompletedTask;
    }

    private Task RemoveTagAsync()
    {
        if (!PlcConfigurationEditor.RemoveSelectedTag())
        {
            Notify("无法删除点位", "请先选择要删除的点位。", "Warning");
            return Task.CompletedTask;
        }

        Notify("点位已删除", "请保存 PLC 配置以保留修改。", "Info");
        return Task.CompletedTask;
    }

    public async Task SaveTestSessionAsync()
    {
        try
        {
            var result = await _experimentWorkspace.SaveSessionAsync(
                FieldProfileEditor.Profile.ProfileName,
                CancellationToken.None);
            Notify(result.Title, result.Message, result.Kind);
        }
        catch (Exception exception)
        {
            Notify("试验记录保存失败", exception.Message, "Error");
        }
    }

    public async Task LoadHistoryAsync()
    {
        await LoadHistoryAsync(showNotification: true);
    }

    public async Task OpenHistorySessionAsync()
    {
        ApplyExperimentWorkspaceOperation(
            await _experimentWorkspace.OpenSelectedSessionAsync(CancellationToken.None));
    }

    public async Task ExportHistorySamplesAsync()
    {
        var fileName = _openFileDialogService.PickHistorySamplesSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        ApplyExperimentWorkspaceOperation(
            await _experimentWorkspace.ExportSelectedSamplesAsync(
                FieldProfileEditor.Profile,
                fileName,
                CancellationToken.None));
    }

    public async Task ExportVisiblePlcTrendAsync(PlcTrendVisibleExport export)
    {
        if (export.Points.Count == 0)
        {
            Notify(
                "无法导出可见趋势",
                "当前趋势画布没有可导出的可见数据点。",
                "Warning");
            return;
        }

        var fileName = _openFileDialogService.PickVisiblePlcTrendSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var result = await TrendExport.ExportVisibleAsync(
                fileName,
                export,
                CancellationToken.None);

            Notify(
                "可见趋势已导出",
                string.Join(
                    Environment.NewLine,
                    $"行数：{result.PointCount}",
                    $"范围：{result.VisibleStart:yyyy-MM-dd HH:mm:ss.fff} - {result.VisibleEnd:yyyy-MM-dd HH:mm:ss.fff}",
                    $"路径：{result.AbsolutePath}"),
                "Success");
        }
        catch (Exception exception)
        {
            Notify("可见趋势导出失败", exception.Message, "Error");
        }
    }

    public Task SetHistoryBaselineAsync()
    {
        ApplyExperimentWorkspaceOperation(_experimentWorkspace.SetSelectedAsBaseline());
        return Task.CompletedTask;
    }

    public async Task CompareHistorySessionAsync()
    {
        ApplyExperimentWorkspaceOperation(
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
            ApplyExperimentWorkspaceOperation(result);
        }
    }

    private async Task LoadHistoryAsync(bool showNotification)
    {
        var result = await _experimentWorkspace.LoadHistoryAsync(
            showNotification,
            CancellationToken.None);
        if (result is not null)
        {
            ApplyExperimentWorkspaceOperation(result);
        }
    }

    public async Task SaveParameterSetAsync()
    {
        try
        {
            var result = await ParameterSetLibrary.SaveAsync(
                OfflineAnalysis.LastSamples,
                OfflineAnalysis.LastTestSessionId == Guid.Empty ? null : OfflineAnalysis.LastTestSessionId,
                OfflineAnalysis.LastSourceFileName,
                CancellationToken.None);
            Notify(result.Title, result.Message, result.Kind);
        }
        catch (Exception exception)
        {
            Notify("参数方案保存失败", exception.Message, "Error");
        }
    }

    public async Task LoadParameterSetsAsync()
    {
        await LoadParameterSetsAsync(showNotification: true);
    }

    private async Task ReviewRecommendationAsync(PidRecommendationReviewDecision decision)
    {
        ApplyExperimentWorkspaceOperation(
            await _experimentWorkspace.ReviewSelectedRecommendationAsync(
                decision,
                CancellationToken.None));
    }

    private async Task LoadParameterSetsAsync(bool showNotification)
    {
        try
        {
            await ParameterSetLibrary.LoadAsync(CancellationToken.None);

            if (showNotification)
            {
                Notify("参数方案已刷新", ParameterSetLibrary.Status, "Info");
            }
        }
        catch (Exception exception)
        {
            ParameterSetLibrary.MarkLoadFailed();
            Notify("参数方案加载失败", exception.Message, "Error");
        }
    }

    private async Task ExportAnalysisResultAsync()
    {
        if (!OfflineAnalysis.CanExportLastResult)
        {
            Notify("无法导出分析结果", "请先导入 CSV 并完成一次分析。", "Warning");
            return;
        }

        var fileName = _openFileDialogService.PickAnalysisResultSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var savedPath = await OfflineAnalysis.ExportLastResultAsync(fileName, CancellationToken.None);
            if (savedPath is null)
            {
                Notify("无法导出分析结果", "请先导入 CSV 并完成一次分析。", "Warning");
                return;
            }

            Notify("分析结果已导出", savedPath, "Success");
        }
        catch (Exception exception)
        {
            Notify("分析结果导出失败", exception.Message, "Error");
        }
    }

    private Task AddFieldAsync()
    {
        FieldProfileEditor.AddField();
        Notify("字段已新增", "请编辑字段信息后保存字段配置。", "Info");
        return Task.CompletedTask;
    }

    private Task RemoveFieldAsync()
    {
        if (!FieldProfileEditor.RemoveSelectedField())
        {
            Notify("无法删除字段", "请先选择要删除的字段。", "Warning");
            return Task.CompletedTask;
        }

        Notify("字段已删除", "请保存字段配置以保留修改。", "Info");
        return Task.CompletedTask;
    }

    private async Task SaveFieldProfileAsync()
    {
        var fileName = _openFileDialogService.PickFieldProfileSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var savedPath = await FieldProfileEditor.SaveToFileAsync(fileName, CancellationToken.None);
            Notify("字段配置已保存", savedPath, "Success");
        }
        catch (Exception exception)
        {
            Notify("字段配置保存失败", exception.Message, "Error");
        }
    }

    private async Task LoadFieldProfileAsync()
    {
        var fileName = _openFileDialogService.PickFieldProfileFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var loadedFileName = await FieldProfileEditor.LoadFromFileAsync(fileName, CancellationToken.None);
            Notify("字段配置已加载", loadedFileName, "Success");
        }
        catch (Exception exception)
        {
            Notify("字段配置加载失败", exception.Message, "Error");
        }
    }

    private async Task ImportCsvAsync()
    {
        var fileName = _openFileDialogService.PickCsvFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            await AnalyzeCsvFileAsync(fileName);
        }
        catch (Exception exception)
        {
            Notify("离线分析失败", exception.Message, "Error");
        }
    }

    private async Task AnalyzeCsvFileAsync(string fileName)
    {
        var result = await OfflineAnalysis.AnalyzeCsvFileAsync(fileName, FieldProfileEditor.Profile, CancellationToken.None);
        Notify("离线分析已完成", $"{result.SourceFileName}，样本 {result.SampleCount} 条。", "Success");
    }

    private Task DismissNotificationAsync()
    {
        Notification.Dismiss();
        return Task.CompletedTask;
    }

    private void ApplyExperimentWorkspaceOperation(ExperimentWorkspaceOperationResult result)
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
