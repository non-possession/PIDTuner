using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.Services;
using PIDTuner.Desktop.Commands;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Plc;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Csv;
using PIDTuner.Infrastructure.Persistence;
using PIDTuner.Infrastructure.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public const int LiveMonitorUiRefreshMilliseconds = 250;

    private readonly IOpenFileDialogService _openFileDialogService;
    private readonly FieldProfileWorkflow _fieldProfileWorkflow;
    private readonly PidAnalysisResultCsvExporter _analysisResultExporter = new();
    private readonly IPlcTagSnapshotReader _plcTagSnapshotReader;
    private readonly PlcOneSecondRecorder _plcOneSecondRecorder;
    private readonly ExperimentSessionCoordinator _experimentSessionCoordinator;
    private readonly PlcConfigurationWorkflow _plcConfigurationWorkflow;
    private readonly PlcMonitorSnapshotPresenter _plcMonitorSnapshotPresenter;
    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _plcReplayTimer = new();
    private readonly DispatcherTimer _plcLiveDiagnosticsTimer = new();
    private IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> _lastPlcRecordingFrames = Array.Empty<IReadOnlyList<PlcTagSnapshot>>();
    private string _statusMessage = "阶段 1 已就绪：可在分析页导入离线 CSV 并计算基础指标。";

    private string _plcCommunicationStatus = "尚未检查 PLC 通信。";
    private string _plcMonitorStatus = "尚未刷新点位。";
    private string _plcAcquisitionDiagnosticsStatus = "采集诊断：尚未记录。";
    private const int MaxPlcDiagnosticsDurationMinutes = 30;

    private int _plcDiagnosticsDurationMinutes = 10;

    public MainWindowViewModel()
        : this(
            new WindowsOpenFileDialogService(),
            new JsonPidSampleFieldProfileStore(),
            new JsonPlcProjectConfigurationStore(),
            new ConfiguredPlcConnectivityProbe(new SiemensS7ConnectivityProbe(), new PingPlcConnectivityProbe()),
            new ConfiguredPlcTagSnapshotReader(new SiemensS7PlcTagSnapshotReader(), new PreviewPlcTagSnapshotReader()),
            new JsonTestSessionRepository(Path.Combine(FindRepositoryRoot(), "local", "test-sessions")),
            new JsonPidSampleRepository(Path.Combine(FindRepositoryRoot(), "local", "test-sessions")),
            new JsonPidRecommendationReviewRepository(Path.Combine(FindRepositoryRoot(), "local", "recommendation-reviews")),
            new JsonPidParameterSetRepository(Path.Combine(FindRepositoryRoot(), "local", "parameter-sets")))
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
        string? testSessionStorageDirectory = null,
        string? plcRecordingStorageDirectory = null)
    {
        _openFileDialogService = openFileDialogService;
        _fieldProfileWorkflow = new FieldProfileWorkflow(fieldProfileStore);
        var resolvedPlcConnectivityProbe = plcConnectivityProbe
            ?? new ConfiguredPlcConnectivityProbe(new SiemensS7ConnectivityProbe(), new PingPlcConnectivityProbe());
        _plcTagSnapshotReader = plcTagSnapshotReader
            ?? new ConfiguredPlcTagSnapshotReader(new SiemensS7PlcTagSnapshotReader(), new PreviewPlcTagSnapshotReader());
        var resolvedTestSessionStorageDirectory = Path.GetFullPath(
            testSessionStorageDirectory ?? Path.Combine(FindRepositoryRoot(), "local", "test-sessions"));
        var resolvedPlcRecordingStorageDirectory = Path.GetFullPath(
            plcRecordingStorageDirectory ?? Path.Combine(FindRepositoryRoot(), "local", "plc-recordings"));
        _plcOneSecondRecorder = new PlcOneSecondRecorder(OpenPlcSnapshotSessionAsync, resolvedPlcRecordingStorageDirectory);
        var resolvedTestSessionRepository = testSessionRepository
            ?? new JsonTestSessionRepository(resolvedTestSessionStorageDirectory);
        var resolvedPidSampleRepository = pidSampleRepository
            ?? new JsonPidSampleRepository(resolvedTestSessionStorageDirectory);
        var resolvedRecommendationReviewRepository = recommendationReviewRepository
            ?? new JsonPidRecommendationReviewRepository(Path.Combine(FindRepositoryRoot(), "local", "recommendation-reviews"));
        var resolvedParameterSetRepository = parameterSetRepository
            ?? new JsonPidParameterSetRepository(Path.Combine(FindRepositoryRoot(), "local", "parameter-sets"));
        _experimentSessionCoordinator = new ExperimentSessionCoordinator(
            resolvedTestSessionRepository,
            resolvedPidSampleRepository,
            resolvedRecommendationReviewRepository,
            resolvedTestSessionStorageDirectory);
        _plcConfigurationWorkflow = new PlcConfigurationWorkflow(
            plcProjectConfigurationStore,
            resolvedPlcConnectivityProbe);
        var liveDiagnosticsStore = plcLiveDiagnosticsStore
            ?? new SqlitePlcLiveDiagnosticsStore(Path.Combine(
                FindRepositoryRoot(),
                "local",
                "plc-diagnostics",
                "plc-live-diagnostics.sqlite"));
        LiveMonitor = new PlcLiveMonitorViewModel(
            new PlcAcquisitionEngine(OpenPlcSnapshotSessionAsync));
        LiveMonitor.PropertyChanged += LiveMonitor_PropertyChanged;
        _plcMonitorSnapshotPresenter = new PlcMonitorSnapshotPresenter(LiveMonitor.Tags);
        _plcMonitorSnapshotPresenter.SnapshotsApplied += (snapshots, trendTimestamp) =>
            PlcSnapshotsApplied?.Invoke(snapshots, trendTimestamp);
        Debug = new PlcDebugViewModel(LiveMonitor.Tags, liveDiagnosticsStore);
        Debug.PropertyChanged += Debug_PropertyChanged;
        PlcConfigurationEditor = new PlcConfigurationEditorViewModel(PlcProjectConfiguration.CreateDefault());
        PlcConfigurationEditor.PropertyChanged += PlcConfigurationEditor_PropertyChanged;
        OfflineAnalysis = new OfflineAnalysisViewModel();
        OfflineAnalysis.PropertyChanged += OfflineAnalysis_PropertyChanged;
        ExperimentHistory.PropertyChanged += ExperimentHistory_PropertyChanged;
        ParameterSetLibrary = new ParameterSetLibraryViewModel(
            resolvedParameterSetRepository,
            new PidParameterSetExtractor());
        ParameterSetLibrary.PropertyChanged += ParameterSetLibrary_PropertyChanged;
        PlcTrendMode.PropertyChanged += PlcTrendMode_PropertyChanged;
        Notification.PropertyChanged += Notification_PropertyChanged;
        HistoricalTrendWorkbench.PropertyChanged += HistoricalTrendWorkbench_PropertyChanged;
        HistoricalTrendWorkbench.ViewportRequested += (start, end) => PlcHistoricalViewportRequested?.Invoke(start, end);
        HistoricalTrendWorkbench.YRangeRequested += (min, max) => PlcTrendYRangeRequested?.Invoke(min, max);
        HistoricalTrendWorkbench.RightYRangeRequested += (min, max) => PlcTrendRightYRangeRequested?.Invoke(min, max);
        HistoricalTrendWorkbench.StatusRequested += (message, replayPhase) =>
        {
            PlcMonitorStatus = message;
            if (!string.IsNullOrWhiteSpace(replayPhase))
            {
                Debug.UpdateReplayStatus(replayPhase);
            }
        };
        _monitorTimer.Tick += (_, _) => ApplyBufferedLiveMonitorFrames();
        _plcReplayTimer.Tick += (_, _) => ApplyNextPlcReplayFrame();
        _plcLiveDiagnosticsTimer.Interval = TimeSpan.FromSeconds(1);
        _plcLiveDiagnosticsTimer.Tick += async (_, _) => await StopExpiredPlcLiveDiagnosticsAsync();
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

    public PlcDebugViewModel Debug { get; }

    public PlcConfigurationEditorViewModel PlcConfigurationEditor { get; }

    public OfflineAnalysisViewModel OfflineAnalysis { get; }

    public ExperimentHistoryViewModel ExperimentHistory { get; } = new();

    public ParameterSetLibraryViewModel ParameterSetLibrary { get; }

    public HistoricalTrendWorkbenchViewModel HistoricalTrendWorkbench { get; } = new();

    public FieldProfileEditorViewModel FieldProfileEditor { get; } = new();

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

    public string PlcCommunicationStatus
    {
        get => _plcCommunicationStatus;
        private set => SetProperty(ref _plcCommunicationStatus, value);
    }

    public string PlcMonitorStatus
    {
        get => _plcMonitorStatus;
        private set => SetProperty(ref _plcMonitorStatus, value);
    }

    public string PlcAcquisitionDiagnosticsStatus
    {
        get => _plcAcquisitionDiagnosticsStatus;
        private set => SetProperty(ref _plcAcquisitionDiagnosticsStatus, value);
    }

    public string PlcLiveDiagnosticsStatus => Debug.DiagnosticsStatus;

    public int PlcDiagnosticsDurationMinutes
    {
        get => _plcDiagnosticsDurationMinutes;
        set => SetProperty(ref _plcDiagnosticsDurationMinutes, Math.Clamp(value, 1, MaxPlcDiagnosticsDurationMinutes));
    }

    public bool IsPlcLiveDiagnosticsRunning => Debug.IsDiagnosticsRunning;

    public string PlcLiveDiagnosticsButtonText => Debug.DiagnosticsButtonText;

    public string PlcReplayStatus
    {
        get => Debug.ReplayStatus;
        private set
        {
            Debug.ReplayStatus = value;
            OnPropertyChanged();
        }
    }

    public string PlcTrendModeStatus
    {
        get => PlcTrendMode.Status;
        private set => _ = value;
    }

    public bool IsPlcHistoricalTrendMode
    {
        get => PlcTrendMode.IsHistoricalMode;
        private set => _ = value;
    }

    public bool IsPlcLiveTrendMode => PlcTrendMode.IsLiveMode;

    public bool IsPlcLiveTrendPaused
    {
        get => PlcTrendMode.IsLiveScrollingPaused;
        private set => _ = value;
    }

    public string PlcLiveTrendPauseButtonText => PlcTrendMode.PauseButtonText;

    public int CurrentPlcAcquisitionIntervalMilliseconds
    {
        get => LiveMonitor.CurrentAcquisitionIntervalMilliseconds;
        private set => LiveMonitor.CurrentAcquisitionIntervalMilliseconds = value;
    }

    public string PlcReplaySpeedText => Debug.ReplaySpeedText;

    public bool IsPlcMonitoring
    {
        get => LiveMonitor.IsMonitoring;
        private set => LiveMonitor.IsMonitoring = value;
    }

    public bool IsPlcReplayRunning
    {
        get => Debug.IsReplayRunning;
        private set
        {
            if (value)
            {
                return;
            }

            Debug.StopReplay();
            OnPropertyChanged();
        }
    }

    public string NotificationTitle
    {
        get => Notification.Title;
        private set => _ = value;
    }

    public string NotificationMessage
    {
        get => Notification.Message;
        private set => _ = value;
    }

    public string NotificationKind
    {
        get => Notification.Kind;
        private set => _ = value;
    }

    public bool IsNotificationVisible
    {
        get => Notification.IsVisible;
        private set => _ = value;
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

    public IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> LastPlcRecordingFrames => _lastPlcRecordingFrames;

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
        var repositoryRoot = FindRepositoryRoot();
        var fieldProfilePath = Path.Combine(repositoryRoot, "config", "pid-sample-fields.example.json");
        var csvPath = Path.Combine(repositoryRoot, "samples", "offline-step-response.csv");

        if (!File.Exists(fieldProfilePath) || !File.Exists(csvPath))
        {
            Notify("示例加载失败", "示例文件不存在，请确认从仓库根目录运行程序。", "Error");
            return;
        }

        try
        {
            FieldProfileEditor.LoadProfile(await _fieldProfileWorkflow.LoadAsync(fieldProfilePath, CancellationToken.None));

            await AnalyzeCsvFileAsync(csvPath);
        }
        catch (Exception exception)
        {
            Notify("示例加载失败", exception.Message, "Error");
        }
    }

    private void HistoricalTrendWorkbench_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HistoricalTrendWorkbench));
    }

    private void LiveMonitor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlcLiveMonitorViewModel.IsMonitoring):
                OnPropertyChanged(nameof(IsPlcMonitoring));
                break;
            case nameof(PlcLiveMonitorViewModel.CurrentAcquisitionIntervalMilliseconds):
                OnPropertyChanged(nameof(CurrentPlcAcquisitionIntervalMilliseconds));
                break;
            case nameof(PlcLiveMonitorViewModel.IsLiveTrendPaused):
                OnPropertyChanged(nameof(IsPlcLiveTrendPaused));
                OnPropertyChanged(nameof(PlcLiveTrendPauseButtonText));
                break;
        }
    }

    private void PlcTrendMode_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        LiveMonitor.IsLiveTrendPaused = PlcTrendMode.IsLiveScrollingPaused;
        OnPropertyChanged(nameof(PlcTrendMode));
        switch (e.PropertyName)
        {
            case nameof(PlcTrendModeViewModel.IsHistoricalMode):
                OnPropertyChanged(nameof(IsPlcHistoricalTrendMode));
                break;
            case nameof(PlcTrendModeViewModel.IsLiveMode):
                OnPropertyChanged(nameof(IsPlcLiveTrendMode));
                break;
            case nameof(PlcTrendModeViewModel.IsLiveScrollingPaused):
                OnPropertyChanged(nameof(IsPlcLiveTrendPaused));
                break;
            case nameof(PlcTrendModeViewModel.PauseButtonText):
                OnPropertyChanged(nameof(PlcLiveTrendPauseButtonText));
                break;
            case nameof(PlcTrendModeViewModel.Status):
                OnPropertyChanged(nameof(PlcTrendModeStatus));
                break;
        }
    }

    private void Notification_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Notification));
        switch (e.PropertyName)
        {
            case nameof(NotificationViewModel.Title):
                OnPropertyChanged(nameof(NotificationTitle));
                break;
            case nameof(NotificationViewModel.Message):
                OnPropertyChanged(nameof(NotificationMessage));
                break;
            case nameof(NotificationViewModel.Kind):
                OnPropertyChanged(nameof(NotificationKind));
                break;
            case nameof(NotificationViewModel.IsVisible):
                OnPropertyChanged(nameof(IsNotificationVisible));
                break;
        }
    }

    private void PlcConfigurationEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(PlcConfigurationEditor));
    }

    private void OfflineAnalysis_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(OfflineAnalysis));
    }

    private void ExperimentHistory_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ExperimentHistory));
    }

    private void ParameterSetLibrary_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ParameterSetLibrary));
    }

    private void Debug_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlcDebugViewModel.ReplayStatus):
                OnPropertyChanged(nameof(PlcReplayStatus));
                break;
            case nameof(PlcDebugViewModel.IsReplayRunning):
                OnPropertyChanged(nameof(IsPlcReplayRunning));
                break;
            case nameof(PlcDebugViewModel.ReplaySpeedText):
            case nameof(PlcDebugViewModel.ReplaySpeedMultiplier):
                OnPropertyChanged(nameof(PlcReplaySpeedText));
                break;
            case nameof(PlcDebugViewModel.DiagnosticsStatus):
                OnPropertyChanged(nameof(PlcLiveDiagnosticsStatus));
                break;
            case nameof(PlcDebugViewModel.IsDiagnosticsRunning):
                OnPropertyChanged(nameof(IsPlcLiveDiagnosticsRunning));
                OnPropertyChanged(nameof(PlcLiveDiagnosticsButtonText));
                break;
            case nameof(PlcDebugViewModel.DiagnosticsButtonText):
                OnPropertyChanged(nameof(PlcLiveDiagnosticsButtonText));
                break;
        }
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
            var configuration = BuildPlcConfigurationFromForm();
            var savedPath = await _plcConfigurationWorkflow.SaveAsync(configuration, fileName, CancellationToken.None);
            PlcConfigurationEditor.MarkSaved();
            Notify("PLC 配置已保存", savedPath, "Success");
        }
        catch (Exception exception)
        {
            Notify("PLC 配置保存失败", exception.Message, "Error");
        }
    }

    public async Task CheckPlcCommunicationAsync()
    {
        try
        {
            var configuration = BuildPlcConfigurationFromForm();
            var result = await _plcConfigurationWorkflow.CheckCommunicationAsync(configuration, CancellationToken.None);
            PlcCommunicationStatus = result.PendingStatus;
            PlcCommunicationStatus = result.Status;
            Notify(result.Title, PlcCommunicationStatus, result.Kind);
        }
        catch (Exception exception)
        {
            Notify("PLC 通信检查失败", exception.Message, "Error");
        }
    }

    public async Task RefreshPlcMonitorAsync()
    {
        try
        {
            if (IsPlcHistoricalTrendMode)
            {
                PlcTrendMode.UseLiveMode();
                LiveMonitor.ClearTags();
                PlcTrendResetRequested?.Invoke();
            }

            if (IsPlcMonitoring)
            {
                ApplyBufferedLiveMonitorFrames();
                return;
            }

            var snapshots = await _plcTagSnapshotReader.ReadAsync(BuildPlcConfigurationFromForm(), CancellationToken.None);
            ApplyPlcMonitorSnapshots(snapshots);
            PlcMonitorStatus = snapshots.Count == 0
                ? "没有启用的监控点位。"
                : $"已刷新 {snapshots.Count} 个点位，数据源：{snapshots[0].Source}。";
        }
        catch (Exception exception)
        {
            PlcMonitorStatus = $"刷新失败：{exception.Message}";
            Notify("PLC 点位刷新失败", exception.Message, "Error");
        }
    }

    private void ApplyPlcMonitorSnapshots(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        DateTimeOffset? trendTimestamp = null,
        bool applyTrend = true)
    {
        _plcMonitorSnapshotPresenter.SelectedTag = LiveMonitor.SelectedTag;
        _plcMonitorSnapshotPresenter.Apply(snapshots, trendTimestamp, applyTrend);
        LiveMonitor.SelectedTag = _plcMonitorSnapshotPresenter.SelectedTag;
    }

    private void ApplyBufferedLiveMonitorFrames()
    {
        var result = LiveMonitor.DrainPresentedFrames();
        if (result.Frames.Count == 0)
        {
            return;
        }

        foreach (var frame in result.Frames)
        {
            ApplyPlcMonitorSnapshots(frame.Snapshots, frame.Diagnostics.PlannedTimestampUtc);
            EnqueuePlcLiveDiagnosticsFrame(frame);
        }

        PlcMonitorStatus = result.MonitorStatus;
        PlcAcquisitionDiagnosticsStatus = result.DiagnosticsStatus;
    }

    private void EnqueuePlcLiveDiagnosticsFrame(PlcAcquisitionFrame frame)
    {
        Debug.EnqueueDiagnosticsFrame(frame);
    }

    private async Task StopLiveMonitoringAsync()
    {
        _monitorTimer.Stop();
        await LiveMonitor.StopAsync();
        await StopPlcLiveDiagnosticsAsync("实时监控已停止，诊断写入已关闭。");
    }

    public async Task TogglePlcMonitoringAsync()
    {
        if (IsPlcMonitoring)
        {
            await StopLiveMonitoringAsync();
            PlcMonitorStatus = "点位监控已停止。";
            return;
        }

        StopPlcReplay();
        var configuration = BuildPlcConfigurationFromForm();
        var result = await LiveMonitor.StartAsync(
            configuration,
            CancellationToken.None);
        _monitorTimer.Interval = result.UiRefreshInterval;
        _monitorTimer.Start();
        PlcMonitorStatus = result.MonitorStatus;
    }

    public async Task TogglePlcLiveDiagnosticsAsync()
    {
        if (IsPlcLiveDiagnosticsRunning)
        {
            await StopPlcLiveDiagnosticsAsync("诊断已手动停止。");
            return;
        }

        if (!IsPlcMonitoring)
        {
            Notify("无法启动实时诊断", "请先启动实时监控。", "Warning");
            return;
        }

        var result = await Debug.StartDiagnosticsAsync(
            BuildPlcConfigurationFromForm(),
            TimeSpan.FromMinutes(PlcDiagnosticsDurationMinutes),
            CancellationToken.None);
        ApplyPlcDiagnosticsOperation(result);
    }

    private async Task StopExpiredPlcLiveDiagnosticsAsync()
    {
        var result = await Debug.StopExpiredDiagnosticsAsync(CancellationToken.None);
        ApplyPlcDiagnosticsOperation(result);
    }

    private async Task StopPlcLiveDiagnosticsAsync(string reason)
    {
        var result = await Debug.StopDiagnosticsAsync(reason, CancellationToken.None);
        ApplyPlcDiagnosticsOperation(result);
    }

    private void ApplyPlcDiagnosticsOperation(PlcDiagnosticsOperationResult result)
    {
        if (result.ShouldKeepTimerRunning)
        {
            _plcLiveDiagnosticsTimer.Start();
        }
        else
        {
            _plcLiveDiagnosticsTimer.Stop();
        }

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
            await StopLiveMonitoringAsync();
            StopPlcReplay();
            var configuration = BuildPlcConfigurationFromForm();
            PlcMonitorStatus = "正在记录 1s 点位数据。";
            PlcAcquisitionDiagnosticsStatus = "采集诊断：正在记录当前 1s 采集链路。";
            var result = await _plcOneSecondRecorder.RecordAsync(
                configuration,
                snapshots => ApplyPlcMonitorSnapshots(snapshots),
                CancellationToken.None);
            if (!result.IsSuccess)
            {
                Notify("无法记录 PLC 数据", "请先启用至少一个可读取点位。", "Warning");
                return;
            }

            _lastPlcRecordingFrames = result.Frames;
            OnPropertyChanged(nameof(LastPlcRecordingFrames));
            PlcMonitorStatus = result.MonitorStatus;
            PlcAcquisitionDiagnosticsStatus = result.DiagnosticsStatus;
            Notify(
                "PLC 1s 记录完成",
                string.Join(
                    Environment.NewLine,
                    PlcMonitorStatus,
                    result.DiagnosticsStatus,
                    $"保存位置：{result.RecordingPath}"),
                "Success");
        }
        catch (Exception exception)
        {
            PlcMonitorStatus = $"1s 记录失败：{exception.Message}";
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
            await StopLiveMonitoringAsync();
            await using var stream = File.OpenRead(fileName);
            var recording = await JsonSerializer.DeserializeAsync<PlcOneSecondRecording>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                CancellationToken.None);
            if (recording is null || recording.Frames.Count == 0)
            {
                Notify("PLC 记录加载失败", "记录文件没有可回放的帧。", "Warning");
                return;
            }

            StopPlcReplay();
            Debug.LoadReplay(recording.Frames, recording.IntervalMilliseconds);
            _lastPlcRecordingFrames = recording.Frames;
            OnPropertyChanged(nameof(LastPlcRecordingFrames));
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

            PlcMonitorStatus =
                $"已加载 PLC 记录：{recording.FrameCount} 帧，{recording.SnapshotCount} 条快照，周期 {Debug.SourceReplayIntervalMilliseconds} ms。";
            Debug.UpdateReplayStatus("已加载");
            Notify(
                "PLC 记录已加载",
                string.Join(Environment.NewLine, PlcMonitorStatus, $"文件位置：{Path.GetFullPath(fileName)}"),
                "Success");
        }
        catch (Exception exception)
        {
            Notify("PLC 记录加载失败", exception.Message, "Error");
        }
    }

    public async Task ShowPlcLiveTrendAsync()
    {
        await StopLiveMonitoringAsync();
        StopPlcReplay();
        UsePlcLiveTrendMode();
        LiveMonitor.ClearTags();
        PlcTrendResetRequested?.Invoke();
        await RefreshPlcMonitorAsync();
    }

    public void UsePlcLiveTrendMode()
    {
        PlcTrendMode.UseLiveMode();
        HistoricalTrendWorkbench.Clear();
    }

    public Task TogglePlcLiveTrendPauseAsync()
    {
        if (IsPlcHistoricalTrendMode)
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

    public Task SetPlcHistoricalTrendWindowAsync(TimeSpan window)
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        PlcTrendMode.UseHistoricalMode();
        if (!HistoricalTrendWorkbench.TrySetVisibleDuration(window, out var error))
        {
            Notify("历史趋势窗口无效", error ?? "当前没有可用的历史趋势数据。", "Warning");
            return Task.CompletedTask;
        }

        PlcMonitorStatus = $"历史趋势窗口已调整为 {FormatTrendWindow(window)}。";
        Debug.UpdateReplayStatus("历史趋势视图");
        return Task.CompletedTask;
    }

    public async Task ShowPlcHistoricalTrendAsync()
    {
        await StopLiveMonitoringAsync();
        StopPlcReplay();
        if (!Debug.HasReplayFrames)
        {
            PlcTrendMode.UseHistoricalMode();
            PlcMonitorStatus = "历史趋势模式：尚未加载历史记录。";
            Debug.UpdateReplayStatus("历史趋势");
            return;
        }

        LiveMonitor.ClearTags();
        PlcTrendResetRequested?.Invoke();
        ShowLoadedPlcHistoricalTrend();
    }

    public Task ApplyPlcHistoricalRangeAsync()
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        if (!HistoricalTrendWorkbench.TryApplyRangeText(out var error))
        {
            Notify("历史趋势区间无效", error ?? "请输入可识别的时间。", "Warning");
            return Task.CompletedTask;
        }

        if (HistoricalTrendWorkbench.VisibleSeries.Count == 0)
        {
            Notify("历史趋势区间无数据", "当前时间范围不在已加载 PLC 记录内。", "Warning");
            return Task.CompletedTask;
        }

        PlcTrendMode.UseHistoricalMode();
        PlcMonitorStatus =
            $"历史趋势视图已调整：{HistoricalTrendWorkbench.RangeStartText} - {HistoricalTrendWorkbench.RangeEndText}。";
        Debug.UpdateReplayStatus("历史趋势视图");
        return Task.CompletedTask;
    }

    public Task ResetPlcHistoricalRangeAsync()
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        HistoricalTrendWorkbench.SetRangeTextFromFrames(Debug.LoadedReplayFrames);
        PlcTrendMode.UseHistoricalMode();
        var dataRange = GetPlcReplayTimestampRange(Debug.LoadedReplayFrames);
        if (dataRange is not null)
        {
            HistoricalTrendWorkbench.ResetTimeRangeToFull();
        }

        PlcMonitorStatus = $"历史趋势已恢复全量视图：{Debug.LoadedReplayFrames.Count} 帧。";
        Debug.UpdateReplayStatus("全量历史");
        return Task.CompletedTask;
    }

    public Task ApplyPlcTrendYRangeAsync()
    {
        if (!HistoricalTrendWorkbench.TryApplyYText(out var error))
        {
            Notify("Y 轴范围无效", error ?? "请同时输入可识别的 Y 最小值和最大值。", "Warning");
            return Task.CompletedTask;
        }

        PlcMonitorStatus =
            $"趋势 Y 轴范围已调整：{HistoricalTrendWorkbench.YMinimumText} - {HistoricalTrendWorkbench.YMaximumText}。";
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendYRangeAsync()
    {
        HistoricalTrendWorkbench.ResetYRangeToFull();
        PlcMonitorStatus = "趋势 Y 轴已恢复自动适配。";
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendRightYRangeAsync()
    {
        HistoricalTrendWorkbench.ResetRightYRangeToFull();
        PlcMonitorStatus = "趋势 Y2 轴已恢复当前变量量程。";
        return Task.CompletedTask;
    }

    public Task TogglePlcReplayAsync()
    {
        if (IsPlcReplayRunning)
        {
            _plcReplayTimer.Stop();
            ApplyPlcReplayOperation(Debug.PauseReplay());
            return Task.CompletedTask;
        }

        var result = Debug.StartReplay();
        ApplyPlcReplayOperation(result);
        if (Debug.IsReplayRunning)
        {
            _plcReplayTimer.Interval = TimeSpan.FromMilliseconds(Debug.EffectiveReplayIntervalMilliseconds);
            _plcReplayTimer.Start();
        }

        return Task.CompletedTask;
    }

    public Task StepPlcReplayBackwardAsync()
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        _plcReplayTimer.Stop();
        ApplyPlcReplayOperation(Debug.StepBackward());
        return Task.CompletedTask;
    }

    public Task StepPlcReplayForwardAsync()
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        _plcReplayTimer.Stop();
        ApplyPlcReplayOperation(Debug.StepForward());
        return Task.CompletedTask;
    }

    public Task SetPlcReplaySpeedAsync(double speedMultiplier)
    {
        Debug.SetReplaySpeed(speedMultiplier);
        if (IsPlcReplayRunning)
        {
            _plcReplayTimer.Interval = TimeSpan.FromMilliseconds(Debug.EffectiveReplayIntervalMilliseconds);
        }

        return Task.CompletedTask;
    }

    private void ApplyNextPlcReplayFrame()
    {
        var result = Debug.ApplyNextReplayFrame();
        if (!Debug.IsReplayRunning)
        {
            _plcReplayTimer.Stop();
        }

        ApplyPlcReplayOperation(result);
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
                ApplyPlcMonitorSnapshots(frame);
            }
        }

        if (result.FrameToApply is not null)
        {
            ApplyPlcMonitorSnapshots(result.FrameToApply);
        }

        if (!string.IsNullOrWhiteSpace(result.MonitorStatus))
        {
            PlcMonitorStatus = result.MonitorStatus;
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
        HistoricalTrendWorkbench.LoadFrames(frames);
        for (var index = 0; index < frames.Count; index++)
        {
            ApplyPlcMonitorSnapshots(frames[index], applyTrend: false);
        }

        PlcSnapshotFramesApplied?.Invoke(frames);
        Debug.MarkHistoricalReplayDisplayed();
        PlcMonitorStatus = $"历史趋势已显示：{frames.Count}/{Debug.LoadedReplayFrames.Count} 帧。";
    }

    private static (DateTimeOffset Start, DateTimeOffset End)? GetPlcReplayTimestampRange(
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        var timestamps = frames
            .Select(FrameTimestamp)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .Order()
            .ToArray();

        return timestamps.Length == 0 ? null : (timestamps[0], timestamps[^1]);
    }

    private static DateTimeOffset? FrameTimestamp(IReadOnlyList<PlcTagSnapshot> frame)
    {
        return frame.Count == 0 ? null : frame.Min(snapshot => snapshot.Timestamp);
    }

    private static string FormatPlcHistoricalRangeTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
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
        _plcReplayTimer.Stop();
        Debug.StopReplay();
    }

    private async Task<IPlcTagSnapshotReadSession> OpenPlcSnapshotSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (_plcTagSnapshotReader is IPlcTagSnapshotSessionReader sessionReader)
        {
            return await sessionReader.OpenSessionAsync(configuration, cancellationToken);
        }

        return new SingleReadSnapshotSession(_plcTagSnapshotReader, configuration);
    }

    private sealed class SingleReadSnapshotSession(
        IPlcTagSnapshotReader reader,
        PlcProjectConfiguration configuration) : IPlcTagSnapshotReadSession
    {
        public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken)
        {
            return reader.ReadAsync(configuration, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
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
            var configuration = await _plcConfigurationWorkflow.LoadAsync(fileName, CancellationToken.None);
            ApplyPlcConfiguration(configuration);
            Notify("PLC 配置已加载", Path.GetFileName(fileName), "Success");
            await CheckPlcCommunicationAsync();
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
            var result = await _experimentSessionCoordinator.SaveOfflineSessionAsync(
                OfflineAnalysis.LastAnalysisWindow,
                OfflineAnalysis.LastSamples,
                OfflineAnalysis.LastSourceFileName,
                FieldProfileEditor.Profile.ProfileName,
                CancellationToken.None);
            if (result.SessionId.HasValue)
            {
                OfflineAnalysis.MarkSavedSession(result.SessionId.Value);
                await LoadHistoryAsync(showNotification: false);
            }

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
        var selectedHistorySession = ExperimentHistory.SelectedHistorySession;
        if (selectedHistorySession is null)
        {
            Notify("无法打开历史记录", "请先选择一条历史记录。", "Warning");
            return;
        }

        try
        {
            var samples = await _experimentSessionCoordinator.LoadSessionSamplesAsync(
                selectedHistorySession,
                CancellationToken.None);
            if (samples.Count == 0)
            {
                Notify("历史记录无样本", "该试验记录没有可加载的采样数据。", "Warning");
                return;
            }

            var window = new AnalysisWindow(samples.Min(sample => sample.Timestamp), samples.Max(sample => sample.Timestamp));
            ApplyAnalysisResult(
                selectedHistorySession.Name,
                samples,
                window,
                OfflineAnalysis.AnalyzeSamples(samples, window));
            Notify("历史记录已打开", $"{selectedHistorySession.Name}，样本 {samples.Count} 条。", "Success");
        }
        catch (Exception exception)
        {
            Notify("历史记录打开失败", exception.Message, "Error");
        }
    }

    public async Task ExportHistorySamplesAsync()
    {
        var selectedHistorySession = ExperimentHistory.SelectedHistorySession;
        if (selectedHistorySession is null)
        {
            Notify("无法导出历史采样", "请先选择一条历史记录。", "Warning");
            return;
        }

        var fileName = _openFileDialogService.PickHistorySamplesSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            var result = await _experimentSessionCoordinator.ExportHistorySamplesAsync(
                selectedHistorySession,
                FieldProfileEditor.Profile,
                fileName,
                CancellationToken.None);
            Notify(result.Title, result.Message, result.Kind);
        }
        catch (Exception exception)
        {
            Notify("历史采样导出失败", exception.Message, "Error");
        }
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
            await using var stream = File.Create(fileName);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteLineAsync(
                "timestampUtc,timestampLocal,tagName,tagId,address,value,unit,quality,source,visibleStartUtc,visibleEndUtc,trendMode");

            var visibleStartUtc = export.VisibleStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var visibleEndUtc = export.VisibleEnd.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            var trendMode = export.IsHistoricalMode ? "Historical" : "Live";

            foreach (var point in export.Points)
            {
                var columns = new[]
                {
                    point.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    point.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    point.TagName,
                    point.TagId.ToString("D"),
                    point.Address,
                    point.Value.ToString("G17", CultureInfo.InvariantCulture),
                    point.Unit ?? string.Empty,
                    point.Quality,
                    point.Source,
                    visibleStartUtc,
                    visibleEndUtc,
                    trendMode
                };
                await writer.WriteLineAsync(string.Join(",", columns.Select(EscapeCsv)));
            }

            Notify(
                "可见趋势已导出",
                string.Join(
                    Environment.NewLine,
                    $"行数：{export.Points.Count}",
                    $"范围：{export.VisibleStart:yyyy-MM-dd HH:mm:ss.fff} - {export.VisibleEnd:yyyy-MM-dd HH:mm:ss.fff}",
                    $"路径：{Path.GetFullPath(fileName)}"),
                "Success");
        }
        catch (Exception exception)
        {
            Notify("可见趋势导出失败", exception.Message, "Error");
        }
    }

    public Task SetHistoryBaselineAsync()
    {
        if (ExperimentHistory.SelectedHistorySession is null)
        {
            Notify("无法设置对比基准", "请先选择一条历史记录。", "Warning");
            return Task.CompletedTask;
        }

        ExperimentHistory.SetBaselineToSelected();
        Notify("历史对比基准已设置", ExperimentHistory.HistoryComparisonStatus, "Info");
        return Task.CompletedTask;
    }

    public async Task CompareHistorySessionAsync()
    {
        var baselineHistorySession = ExperimentHistory.BaselineHistorySession;
        if (baselineHistorySession is null)
        {
            Notify("无法对比历史记录", "请先选择一条记录并设为基准。", "Warning");
            return;
        }

        var selectedHistorySession = ExperimentHistory.SelectedHistorySession;
        if (selectedHistorySession is null)
        {
            Notify("无法对比历史记录", "请先选择要对比的历史记录。", "Warning");
            return;
        }

        if (selectedHistorySession.Id == baselineHistorySession.Id)
        {
            Notify("无法对比历史记录", "请选择不同于基准的历史记录。", "Warning");
            return;
        }

        try
        {
            var baseline = await AnalyzeHistorySessionAsync(baselineHistorySession);
            var candidate = await AnalyzeHistorySessionAsync(selectedHistorySession);
            ExperimentHistory.SetComparisonResult(
                baseline.Metrics,
                candidate.Metrics,
                baselineHistorySession.Name,
                selectedHistorySession.Name);
            Notify("历史记录对比已完成", ExperimentHistory.HistoryComparisonStatus, "Success");
        }
        catch (Exception exception)
        {
            Notify("历史记录对比失败", exception.Message, "Error");
        }
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
        try
        {
            var reviews = await _experimentSessionCoordinator.LoadRecommendationReviewsAsync(CancellationToken.None);
            ExperimentHistory.SetRecommendationReviews(reviews);
        }
        catch (Exception exception)
        {
            Notify("建议审查记录加载失败", exception.Message, "Error");
        }
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

    private async Task LoadHistoryAsync(bool showNotification)
    {
        try
        {
            var items = await _experimentSessionCoordinator.LoadHistoryAsync(CancellationToken.None);
            ExperimentHistory.SetHistorySessions(items);

            if (showNotification)
            {
                Notify("历史记录已刷新", ExperimentHistory.HistoryStatus, "Info");
            }
        }
        catch (Exception exception)
        {
            ExperimentHistory.MarkHistoryLoadFailed();
            Notify("历史记录加载失败", exception.Message, "Error");
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
        var selectedTuningRecommendation = OfflineAnalysis.SelectedTuningRecommendation;
        if (selectedTuningRecommendation is null)
        {
            Notify("无法记录建议审查", "请先选择一条参数调整建议。", "Warning");
            return;
        }

        try
        {
            var review = await _experimentSessionCoordinator.SaveRecommendationReviewAsync(
                selectedTuningRecommendation,
                OfflineAnalysis.LastTestSessionId,
                OfflineAnalysis.LastSourceFileName,
                decision,
                ExperimentHistory.RecommendationReviewNote.Trim(),
                CancellationToken.None);
            ExperimentHistory.ClearRecommendationReviewNote();
            await LoadRecommendationReviewsAsync();
            var decisionText = decision == PidRecommendationReviewDecision.Accepted ? "采用" : "暂缓";
            Notify("建议审查已记录", $"{decisionText}：{review.Parameter} {review.Adjustment}", "Success");
        }
        catch (Exception exception)
        {
            Notify("建议审查记录失败", exception.Message, "Error");
        }
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

    private async Task<(IReadOnlyList<PidSample> Samples, PidResponseMetrics Metrics)> AnalyzeHistorySessionAsync(
        TestSessionListItemViewModel session)
    {
        var samples = await _experimentSessionCoordinator.LoadSessionSamplesAsync(session, CancellationToken.None);
        if (samples.Count == 0)
        {
            throw new InvalidOperationException($"{session.Name} 没有可对比的采样数据。");
        }

        var window = new AnalysisWindow(samples.Min(sample => sample.Timestamp), samples.Max(sample => sample.Timestamp));
        return (samples, OfflineAnalysis.AnalyzeSamples(samples, window));
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains('"') &&
            !value.Contains(',') &&
            !value.Contains('\r') &&
            !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private async Task ExportAnalysisResultAsync()
    {
        if (OfflineAnalysis.LastAnalysisWindow is null
            || OfflineAnalysis.LastMetrics is null
            || OfflineAnalysis.LastAssessment is null)
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
            await using var stream = File.Create(fileName);
            await _analysisResultExporter.ExportAsync(
                OfflineAnalysis.LastAnalysisWindow,
                OfflineAnalysis.LastMetrics,
                OfflineAnalysis.LastAssessment,
                stream,
                CancellationToken.None);
            Notify("分析结果已导出", Path.GetFullPath(fileName), "Success");
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
            var fieldProfile = FieldProfileEditor.BuildProfileFromGrid();
            await _fieldProfileWorkflow.SaveAsync(fieldProfile, fileName, CancellationToken.None);
            Notify("字段配置已保存", Path.GetFullPath(fileName), "Success");
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
            FieldProfileEditor.LoadProfile(await _fieldProfileWorkflow.LoadAsync(fileName, CancellationToken.None));
            Notify("字段配置已加载", Path.GetFileName(fileName), "Success");
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

    private void ApplyAnalysisResult(
        string sourceName,
        IReadOnlyList<PidSample> samples,
        AnalysisWindow window,
        PidResponseMetrics metrics)
    {
        OfflineAnalysis.ApplyResult(sourceName, samples, window, metrics);
    }

    private Task DismissNotificationAsync()
    {
        Notification.Dismiss();
        return Task.CompletedTask;
    }

    private void Notify(string title, string message, string kind)
    {
        StatusMessage = $"{title}：{message}";
        Notification.Show(title, message, kind);
    }

    private void ApplyPlcConfiguration(PlcProjectConfiguration configuration)
    {
        PlcConfigurationEditor.ApplyConfiguration(configuration);
        LiveMonitor.ClearTags();
        PlcMonitorStatus = "PLC 配置已更新，等待刷新点位。";
    }

    private PlcProjectConfiguration BuildPlcConfigurationFromForm()
    {
        return PlcConfigurationEditor.BuildConfiguration();
    }

    private static string FormatTrendWindow(TimeSpan window)
    {
        return window.TotalHours >= 1
            ? $"{window.TotalHours:0.#}h"
            : window.TotalMinutes >= 1
                ? $"{window.TotalMinutes:0.#}min"
                : $"{window.TotalSeconds:0.#}s";
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
