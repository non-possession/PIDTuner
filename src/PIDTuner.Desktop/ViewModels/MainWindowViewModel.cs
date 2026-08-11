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
using PIDTuner.Application.UseCases;
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
    private readonly IPidSampleFieldProfileStore _fieldProfileStore;
    private readonly IPlcProjectConfigurationStore _plcProjectConfigurationStore;
    private readonly BasicPidAnalysisService _pidAnalysisService = new();
    private readonly PidAnalysisResultCsvExporter _analysisResultExporter = new();
    private readonly AnalysisWindowParser _analysisWindowParser = new();
    private readonly ITestSessionRepository _testSessionRepository;
    private readonly IPidSampleRepository _pidSampleRepository;
    private readonly IPidRecommendationReviewRepository _recommendationReviewRepository;
    private readonly IPidParameterSetRepository _parameterSetRepository;
    private readonly IPlcConnectivityProbe _plcConnectivityProbe;
    private readonly IPlcTagSnapshotReader _plcTagSnapshotReader;
    private readonly PlcOneSecondRecorder _plcOneSecondRecorder;
    private readonly PidParameterSetExtractor _parameterSetExtractor = new();
    private readonly string _testSessionStorageDirectory;
    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _plcReplayTimer = new();
    private readonly DispatcherTimer _plcLiveDiagnosticsTimer = new();
    private IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> _lastPlcRecordingFrames = Array.Empty<IReadOnlyList<PlcTagSnapshot>>();
    private PidSampleFieldProfile _fieldProfile = PidSampleFieldProfile.CreateDefault();
    private string _statusMessage = "阶段 1 已就绪：可在分析页导入离线 CSV 并计算基础指标。";
    private string _currentFieldProfile = "default-pid-sample-fields (10 字段)";
    private string _analysisStartText = string.Empty;
    private string _analysisEndText = string.Empty;
    private string _notificationTitle = string.Empty;
    private string _notificationMessage = string.Empty;
    private string _notificationKind = "Info";
    private bool _isNotificationVisible;
    private string _historyStatus = "尚未加载历史记录。";
    private string _historySearchText = string.Empty;
    private string _selectedHistoryDetails = "请选择一条历史记录。";
    private ObservableCollection<PidSampleFieldDefinitionViewModel> _fieldDefinitions = [];
    private ObservableCollection<TestSessionListItemViewModel> _historySessions = [];
    private ObservableCollection<PidRecommendationReviewViewModel> _recommendationReviews = [];
    private ObservableCollection<PlcTagMonitorViewModel> _plcMonitorTags = [];
    private ObservableCollection<HistoryComparisonMetricViewModel> _historyComparisonMetrics = [];
    private ObservableCollection<PidParameterSetViewModel> _parameterSets = [];
    private IReadOnlyList<TestSessionListItemViewModel> _allHistorySessions = Array.Empty<TestSessionListItemViewModel>();
    private PidSampleFieldDefinitionViewModel? _selectedFieldDefinition;
    private TestSessionListItemViewModel? _selectedHistorySession;
    private TestSessionListItemViewModel? _baselineHistorySession;
    private PidTuningRecommendationViewModel? _selectedTuningRecommendation;
    private PlcTagMonitorViewModel? _selectedPlcMonitorTag;
    private string _recommendationReviewNote = string.Empty;
    private string _recommendationReviewStatus = "尚未记录建议审查。";

    private string _plcCommunicationStatus = "尚未检查 PLC 通信。";
    private string _plcMonitorStatus = "尚未刷新点位。";
    private string _plcAcquisitionDiagnosticsStatus = "采集诊断：尚未记录。";
    private string _plcTrendModeStatus = "当前趋势：实时";
    private string _historyComparisonStatus = "尚未设置历史对比基准。";
    private bool _isPlcHistoricalTrendMode;
    private bool _isPlcLiveTrendPaused;
    private const int MaxPlcDiagnosticsDurationMinutes = 30;

    private int _plcDiagnosticsDurationMinutes = 10;

    private string _parameterSetStatus = "尚未保存参数方案。";

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
        _fieldProfileStore = fieldProfileStore;
        _plcProjectConfigurationStore = plcProjectConfigurationStore;
        _plcConnectivityProbe = plcConnectivityProbe
            ?? new ConfiguredPlcConnectivityProbe(new SiemensS7ConnectivityProbe(), new PingPlcConnectivityProbe());
        _plcTagSnapshotReader = plcTagSnapshotReader
            ?? new ConfiguredPlcTagSnapshotReader(new SiemensS7PlcTagSnapshotReader(), new PreviewPlcTagSnapshotReader());
        _testSessionStorageDirectory = Path.GetFullPath(
            testSessionStorageDirectory ?? Path.Combine(FindRepositoryRoot(), "local", "test-sessions"));
        var resolvedPlcRecordingStorageDirectory = Path.GetFullPath(
            plcRecordingStorageDirectory ?? Path.Combine(FindRepositoryRoot(), "local", "plc-recordings"));
        _plcOneSecondRecorder = new PlcOneSecondRecorder(OpenPlcSnapshotSessionAsync, resolvedPlcRecordingStorageDirectory);
        _testSessionRepository = testSessionRepository ?? new JsonTestSessionRepository(_testSessionStorageDirectory);
        _pidSampleRepository = pidSampleRepository ?? new JsonPidSampleRepository(_testSessionStorageDirectory);
        _recommendationReviewRepository = recommendationReviewRepository
            ?? new JsonPidRecommendationReviewRepository(Path.Combine(FindRepositoryRoot(), "local", "recommendation-reviews"));
        _parameterSetRepository = parameterSetRepository
            ?? new JsonPidParameterSetRepository(Path.Combine(FindRepositoryRoot(), "local", "parameter-sets"));
        var liveDiagnosticsStore = plcLiveDiagnosticsStore
            ?? new SqlitePlcLiveDiagnosticsStore(Path.Combine(
                FindRepositoryRoot(),
                "local",
                "plc-diagnostics",
                "plc-live-diagnostics.sqlite"));
        LiveMonitor = new PlcLiveMonitorViewModel(
            PlcMonitorTags,
            new PlcAcquisitionEngine(OpenPlcSnapshotSessionAsync));
        LiveMonitor.PropertyChanged += LiveMonitor_PropertyChanged;
        Debug = new PlcDebugViewModel(PlcMonitorTags, liveDiagnosticsStore);
        Debug.PropertyChanged += Debug_PropertyChanged;
        PlcConfigurationEditor = new PlcConfigurationEditorViewModel(PlcProjectConfiguration.CreateDefault());
        PlcConfigurationEditor.PropertyChanged += PlcConfigurationEditor_PropertyChanged;
        OfflineAnalysis = new OfflineAnalysisViewModel();
        OfflineAnalysis.PropertyChanged += OfflineAnalysis_PropertyChanged;
        HistoricalTrendWorkbench.PropertyChanged += HistoricalTrendWorkbench_PropertyChanged;
        HistoricalTrendWorkbench.ViewportRequested += (start, end) => PlcHistoricalViewportRequested?.Invoke(start, end);
        HistoricalTrendWorkbench.YRangeRequested += (min, max) => PlcTrendYRangeRequested?.Invoke(min, max);
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
        RefreshFieldDefinitions();
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
        TogglePlcLiveTrendPauseCommand = new AsyncCommand(TogglePlcLiveTrendPauseAsync);
        ApplyPlcHistoricalRangeCommand = new AsyncCommand(ApplyPlcHistoricalRangeAsync);
        ResetPlcHistoricalRangeCommand = new AsyncCommand(ResetPlcHistoricalRangeAsync);
        ApplyPlcTrendYRangeCommand = new AsyncCommand(ApplyPlcTrendYRangeAsync);
        ResetPlcTrendYRangeCommand = new AsyncCommand(ResetPlcTrendYRangeAsync);
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

    public string Title { get; } = "PIDTuner";

    public PlcLiveMonitorViewModel LiveMonitor { get; }

    public PlcDebugViewModel Debug { get; }

    public PlcConfigurationEditorViewModel PlcConfigurationEditor { get; }

    public OfflineAnalysisViewModel OfflineAnalysis { get; }

    public HistoricalTrendWorkbenchViewModel HistoricalTrendWorkbench { get; } = new();

    public IReadOnlyList<string> AvailableFieldDataTypes { get; } =
        Enum.GetNames<PidSampleFieldDataType>();

    public IReadOnlyList<string> AvailableFieldRoles { get; } =
        Enum.GetNames<PidSampleFieldRole>();

    public IReadOnlyList<string> AvailablePlcDataTypes { get; } =
        Enum.GetNames<PlcDataType>();

    public IReadOnlyList<string> AvailableTagAccessModes { get; } =
        Enum.GetNames<TagAccessMode>();

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentFieldProfile
    {
        get => _currentFieldProfile;
        private set => SetProperty(ref _currentFieldProfile, value);
    }

    public string PlcConfigurationName
    {
        get => PlcConfigurationEditor.ConfigurationName;
        set => PlcConfigurationEditor.ConfigurationName = value;
    }

    public string PlcProtocol
    {
        get => PlcConfigurationEditor.Protocol;
        set => PlcConfigurationEditor.Protocol = value;
    }

    public string PlcIpAddress
    {
        get => PlcConfigurationEditor.IpAddress;
        set => PlcConfigurationEditor.IpAddress = value;
    }

    public int PlcRack
    {
        get => PlcConfigurationEditor.Rack;
        set => PlcConfigurationEditor.Rack = value;
    }

    public int PlcSlot
    {
        get => PlcConfigurationEditor.Slot;
        set => PlcConfigurationEditor.Slot = value;
    }

    public int PlcTimeoutMilliseconds
    {
        get => PlcConfigurationEditor.TimeoutMilliseconds;
        set => PlcConfigurationEditor.TimeoutMilliseconds = value;
    }

    public int PlcDefaultSamplingMilliseconds
    {
        get => PlcConfigurationEditor.DefaultSamplingMilliseconds;
        set => PlcConfigurationEditor.DefaultSamplingMilliseconds = value;
    }

    public int PlcMinimumSamplingMilliseconds
    {
        get => PlcConfigurationEditor.MinimumSamplingMilliseconds;
        set => PlcConfigurationEditor.MinimumSamplingMilliseconds = value;
    }

    public string PlcConfigurationStatus
    {
        get => PlcConfigurationEditor.Status;
        private set => _ = value;
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
        get => _plcTrendModeStatus;
        private set => SetProperty(ref _plcTrendModeStatus, value);
    }

    public string PlcHistoricalRangeStartText
    {
        get => HistoricalTrendWorkbench.RangeStartText;
        set
        {
            if (HistoricalTrendWorkbench.RangeStartText == value)
            {
                return;
            }

            HistoricalTrendWorkbench.RangeStartText = value;
            OnPropertyChanged();
        }
    }

    public string PlcHistoricalRangeEndText
    {
        get => HistoricalTrendWorkbench.RangeEndText;
        set
        {
            if (HistoricalTrendWorkbench.RangeEndText == value)
            {
                return;
            }

            HistoricalTrendWorkbench.RangeEndText = value;
            OnPropertyChanged();
        }
    }

    public string PlcTrendYMinText
    {
        get => HistoricalTrendWorkbench.YMinimumText;
        set
        {
            if (HistoricalTrendWorkbench.YMinimumText == value)
            {
                return;
            }

            HistoricalTrendWorkbench.YMinimumText = value;
            OnPropertyChanged();
        }
    }

    public string PlcTrendYMaxText
    {
        get => HistoricalTrendWorkbench.YMaximumText;
        set
        {
            if (HistoricalTrendWorkbench.YMaximumText == value)
            {
                return;
            }

            HistoricalTrendWorkbench.YMaximumText = value;
            OnPropertyChanged();
        }
    }

    public string PlcHistoricalViewportStartLabel => HistoricalTrendWorkbench.ViewportStartLabel;

    public string PlcHistoricalViewportEndLabel => HistoricalTrendWorkbench.ViewportEndLabel;

    public double PlcHistoricalViewportMinimum => HistoricalTrendWorkbench.ViewportMinimum;

    public double PlcHistoricalViewportMaximum => HistoricalTrendWorkbench.ViewportMaximum;

    public double PlcHistoricalViewportStart
    {
        get => HistoricalTrendWorkbench.ViewportStart;
        set => HistoricalTrendWorkbench.ViewportStart = Math.Min(value, PlcHistoricalViewportEnd);
    }

    public double PlcHistoricalViewportEnd
    {
        get => HistoricalTrendWorkbench.ViewportEnd;
        set => HistoricalTrendWorkbench.ViewportEnd = Math.Max(value, PlcHistoricalViewportStart);
    }

    public double PlcTrendYSliderMinimum => HistoricalTrendWorkbench.YSliderMinimum;

    public double PlcTrendYSliderMaximum => HistoricalTrendWorkbench.YSliderMaximum;

    public double PlcTrendYLower
    {
        get => HistoricalTrendWorkbench.YLower;
        set => HistoricalTrendWorkbench.YLower = Math.Min(value, PlcTrendYUpper);
    }

    public double PlcTrendYUpper
    {
        get => HistoricalTrendWorkbench.YUpper;
        set => HistoricalTrendWorkbench.YUpper = Math.Max(value, PlcTrendYLower);
    }

    public bool IsPlcHistoricalViewportEnabled => HistoricalTrendWorkbench.IsViewportEnabled;

    public bool IsPlcTrendYSliderEnabled => HistoricalTrendWorkbench.IsYSliderEnabled;

    public bool IsPlcHistoricalTrendMode
    {
        get => _isPlcHistoricalTrendMode;
        private set
        {
            if (SetProperty(ref _isPlcHistoricalTrendMode, value))
            {
                OnPropertyChanged(nameof(IsPlcLiveTrendMode));
            }
        }
    }

    public bool IsPlcLiveTrendMode => !IsPlcHistoricalTrendMode;

    public bool IsPlcLiveTrendPaused
    {
        get => _isPlcLiveTrendPaused;
        private set
        {
            if (SetProperty(ref _isPlcLiveTrendPaused, value))
            {
                LiveMonitor.IsLiveTrendPaused = value;
                OnPropertyChanged(nameof(PlcLiveTrendPauseButtonText));
            }
        }
    }

    public string PlcLiveTrendPauseButtonText => IsPlcLiveTrendPaused ? "恢复滚动" : "暂停滚动";

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

    public string SampleCount
    {
        get => OfflineAnalysis.SampleCount;
        private set => _ = value;
    }

    public string OvershootPercent
    {
        get => OfflineAnalysis.OvershootPercent;
        private set => _ = value;
    }

    public string RiseTime
    {
        get => OfflineAnalysis.RiseTime;
        private set => _ = value;
    }

    public string SettlingTime
    {
        get => OfflineAnalysis.SettlingTime;
        private set => _ = value;
    }

    public string SteadyStateError
    {
        get => OfflineAnalysis.SteadyStateError;
        private set => _ = value;
    }

    public string PeakProcessValue
    {
        get => OfflineAnalysis.PeakProcessValue;
        private set => _ = value;
    }

    public string PeakTime
    {
        get => OfflineAnalysis.PeakTime;
        private set => _ = value;
    }

    public string MinimumProcessValue
    {
        get => OfflineAnalysis.MinimumProcessValue;
        private set => _ = value;
    }

    public string MeanAbsoluteError
    {
        get => OfflineAnalysis.MeanAbsoluteError;
        private set => _ = value;
    }

    public string MeanSquaredError
    {
        get => OfflineAnalysis.MeanSquaredError;
        private set => _ = value;
    }

    public string IntegralAbsoluteError
    {
        get => OfflineAnalysis.IntegralAbsoluteError;
        private set => _ = value;
    }

    public string OutputStandardDeviation
    {
        get => OfflineAnalysis.OutputStandardDeviation;
        private set => _ = value;
    }

    public string ResponseFlags
    {
        get => OfflineAnalysis.ResponseFlags;
        private set => _ = value;
    }

    public string AnalysisStartText
    {
        get => _analysisStartText;
        set => SetProperty(ref _analysisStartText, value);
    }

    public string AnalysisEndText
    {
        get => _analysisEndText;
        set => SetProperty(ref _analysisEndText, value);
    }

    public string ActiveAnalysisWindow
    {
        get => OfflineAnalysis.ActiveAnalysisWindow;
        private set => _ = value;
    }

    public string AssessmentSummary
    {
        get => OfflineAnalysis.AssessmentSummary;
        private set => _ = value;
    }

    public string NotificationTitle
    {
        get => _notificationTitle;
        private set => SetProperty(ref _notificationTitle, value);
    }

    public string NotificationMessage
    {
        get => _notificationMessage;
        private set => SetProperty(ref _notificationMessage, value);
    }

    public string NotificationKind
    {
        get => _notificationKind;
        private set => SetProperty(ref _notificationKind, value);
    }

    public bool IsNotificationVisible
    {
        get => _isNotificationVisible;
        private set => SetProperty(ref _isNotificationVisible, value);
    }

    public string HistoryStatus
    {
        get => _historyStatus;
        private set => SetProperty(ref _historyStatus, value);
    }

    public string HistoryComparisonStatus
    {
        get => _historyComparisonStatus;
        private set => SetProperty(ref _historyComparisonStatus, value);
    }

    public string HistorySearchText
    {
        get => _historySearchText;
        set
        {
            if (SetProperty(ref _historySearchText, value))
            {
                ApplyHistoryFilter();
            }
        }
    }

    public string SelectedHistoryDetails
    {
        get => _selectedHistoryDetails;
        private set => SetProperty(ref _selectedHistoryDetails, value);
    }

    public PointCollection SetPointPoints
    {
        get => OfflineAnalysis.SetPointPoints;
        private set => _ = value;
    }

    public PointCollection ProcessValuePoints
    {
        get => OfflineAnalysis.ProcessValuePoints;
        private set => _ = value;
    }

    public PointCollection ManipulatedValuePoints
    {
        get => OfflineAnalysis.ManipulatedValuePoints;
        private set => _ = value;
    }

    public ObservableCollection<PidSampleFieldDefinitionViewModel> FieldDefinitions
    {
        get => _fieldDefinitions;
        private set => SetProperty(ref _fieldDefinitions, value);
    }

    public ObservableCollection<TagDefinitionViewModel> TagDefinitions
    {
        get => PlcConfigurationEditor.TagDefinitions;
        private set => _ = value;
    }

    public ObservableCollection<TestSessionListItemViewModel> HistorySessions
    {
        get => _historySessions;
        private set => SetProperty(ref _historySessions, value);
    }

    public ObservableCollection<PidTuningRecommendationViewModel> TuningRecommendations
    {
        get => OfflineAnalysis.TuningRecommendations;
        private set => _ = value;
    }

    public ObservableCollection<PidRecommendationReviewViewModel> RecommendationReviews
    {
        get => _recommendationReviews;
        private set => SetProperty(ref _recommendationReviews, value);
    }

    public ObservableCollection<PlcTagMonitorViewModel> PlcMonitorTags
    {
        get => _plcMonitorTags;
        private set => SetProperty(ref _plcMonitorTags, value);
    }

    public ObservableCollection<HistoryComparisonMetricViewModel> HistoryComparisonMetrics
    {
        get => _historyComparisonMetrics;
        private set => SetProperty(ref _historyComparisonMetrics, value);
    }

    public ObservableCollection<PidParameterSetViewModel> ParameterSets
    {
        get => _parameterSets;
        private set => SetProperty(ref _parameterSets, value);
    }

    public string RecommendationSummary
    {
        get => OfflineAnalysis.RecommendationSummary;
        private set => _ = value;
    }

    public string RecommendationReviewNote
    {
        get => _recommendationReviewNote;
        set => SetProperty(ref _recommendationReviewNote, value);
    }

    public string RecommendationReviewStatus
    {
        get => _recommendationReviewStatus;
        private set => SetProperty(ref _recommendationReviewStatus, value);
    }

    public string ParameterSetStatus
    {
        get => _parameterSetStatus;
        private set => SetProperty(ref _parameterSetStatus, value);
    }

    public PidSampleFieldDefinitionViewModel? SelectedFieldDefinition
    {
        get => _selectedFieldDefinition;
        set => SetProperty(ref _selectedFieldDefinition, value);
    }

    public TestSessionListItemViewModel? SelectedHistorySession
    {
        get => _selectedHistorySession;
        set
        {
            if (SetProperty(ref _selectedHistorySession, value))
            {
                UpdateSelectedHistoryDetails();
            }
        }
    }

    public PidTuningRecommendationViewModel? SelectedTuningRecommendation
    {
        get => _selectedTuningRecommendation;
        set => SetProperty(ref _selectedTuningRecommendation, value);
    }

    public TagDefinitionViewModel? SelectedTagDefinition
    {
        get => PlcConfigurationEditor.SelectedTagDefinition;
        set => PlcConfigurationEditor.SelectedTagDefinition = value;
    }

    public PlcTagMonitorViewModel? SelectedPlcMonitorTag
    {
        get => _selectedPlcMonitorTag;
        set => SetProperty(ref _selectedPlcMonitorTag, value);
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

    public ICommand TogglePlcLiveTrendPauseCommand { get; }

    public ICommand ApplyPlcHistoricalRangeCommand { get; }

    public ICommand ResetPlcHistoricalRangeCommand { get; }

    public ICommand ApplyPlcTrendYRangeCommand { get; }

    public ICommand ResetPlcTrendYRangeCommand { get; }

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
            await using (var profileStream = File.OpenRead(fieldProfilePath))
            {
                _fieldProfile = await _fieldProfileStore.LoadAsync(profileStream, CancellationToken.None);
            }

            CurrentFieldProfile = $"{_fieldProfile.ProfileName} ({_fieldProfile.Fields.Count} 字段)";
            RefreshFieldDefinitions();
            await AnalyzeCsvFileAsync(csvPath);
        }
        catch (Exception exception)
        {
            Notify("示例加载失败", exception.Message, "Error");
        }
    }

    private void HistoricalTrendWorkbench_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var propertyName in MapHistoricalTrendProperty(e.PropertyName))
        {
            OnPropertyChanged(propertyName);
        }
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

    private void PlcConfigurationEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlcConfigurationEditorViewModel.ConfigurationName):
                OnPropertyChanged(nameof(PlcConfigurationName));
                break;
            case nameof(PlcConfigurationEditorViewModel.Protocol):
                OnPropertyChanged(nameof(PlcProtocol));
                break;
            case nameof(PlcConfigurationEditorViewModel.IpAddress):
                OnPropertyChanged(nameof(PlcIpAddress));
                break;
            case nameof(PlcConfigurationEditorViewModel.Rack):
                OnPropertyChanged(nameof(PlcRack));
                break;
            case nameof(PlcConfigurationEditorViewModel.Slot):
                OnPropertyChanged(nameof(PlcSlot));
                break;
            case nameof(PlcConfigurationEditorViewModel.TimeoutMilliseconds):
                OnPropertyChanged(nameof(PlcTimeoutMilliseconds));
                break;
            case nameof(PlcConfigurationEditorViewModel.DefaultSamplingMilliseconds):
                OnPropertyChanged(nameof(PlcDefaultSamplingMilliseconds));
                break;
            case nameof(PlcConfigurationEditorViewModel.MinimumSamplingMilliseconds):
                OnPropertyChanged(nameof(PlcMinimumSamplingMilliseconds));
                break;
            case nameof(PlcConfigurationEditorViewModel.Status):
                OnPropertyChanged(nameof(PlcConfigurationStatus));
                break;
            case nameof(PlcConfigurationEditorViewModel.TagDefinitions):
                OnPropertyChanged(nameof(TagDefinitions));
                break;
            case nameof(PlcConfigurationEditorViewModel.SelectedTagDefinition):
                OnPropertyChanged(nameof(SelectedTagDefinition));
                break;
        }
    }

    private void OfflineAnalysis_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var propertyName in MapOfflineAnalysisProperty(e.PropertyName))
        {
            OnPropertyChanged(propertyName);
        }
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

    private static IReadOnlyList<string> MapHistoricalTrendProperty(string? propertyName)
    {
        return propertyName switch
        {
            nameof(HistoricalTrendWorkbenchViewModel.RangeStartText) => [nameof(PlcHistoricalRangeStartText)],
            nameof(HistoricalTrendWorkbenchViewModel.RangeEndText) => [nameof(PlcHistoricalRangeEndText)],
            nameof(HistoricalTrendWorkbenchViewModel.YMinimumText) => [nameof(PlcTrendYMinText)],
            nameof(HistoricalTrendWorkbenchViewModel.YMaximumText) => [nameof(PlcTrendYMaxText)],
            nameof(HistoricalTrendWorkbenchViewModel.ViewportStartLabel) => [nameof(PlcHistoricalViewportStartLabel)],
            nameof(HistoricalTrendWorkbenchViewModel.ViewportEndLabel) => [nameof(PlcHistoricalViewportEndLabel)],
            nameof(HistoricalTrendWorkbenchViewModel.ViewportMinimum) => [nameof(PlcHistoricalViewportMinimum)],
            nameof(HistoricalTrendWorkbenchViewModel.ViewportMaximum) => [nameof(PlcHistoricalViewportMaximum)],
            nameof(HistoricalTrendWorkbenchViewModel.ViewportStart) => [nameof(PlcHistoricalViewportStart)],
            nameof(HistoricalTrendWorkbenchViewModel.ViewportEnd) => [nameof(PlcHistoricalViewportEnd)],
            nameof(HistoricalTrendWorkbenchViewModel.YSliderMinimum) => [nameof(PlcTrendYSliderMinimum)],
            nameof(HistoricalTrendWorkbenchViewModel.YSliderMaximum) => [nameof(PlcTrendYSliderMaximum)],
            nameof(HistoricalTrendWorkbenchViewModel.YLower) => [nameof(PlcTrendYLower)],
            nameof(HistoricalTrendWorkbenchViewModel.YUpper) => [nameof(PlcTrendYUpper)],
            nameof(HistoricalTrendWorkbenchViewModel.IsViewportEnabled) => [nameof(IsPlcHistoricalViewportEnabled)],
            nameof(HistoricalTrendWorkbenchViewModel.IsYSliderEnabled) => [nameof(IsPlcTrendYSliderEnabled)],
            _ => Array.Empty<string>()
        };
    }

    private static IReadOnlyList<string> MapOfflineAnalysisProperty(string? propertyName)
    {
        return propertyName switch
        {
            nameof(OfflineAnalysisViewModel.SampleCount) => [nameof(SampleCount)],
            nameof(OfflineAnalysisViewModel.OvershootPercent) => [nameof(OvershootPercent)],
            nameof(OfflineAnalysisViewModel.RiseTime) => [nameof(RiseTime)],
            nameof(OfflineAnalysisViewModel.SettlingTime) => [nameof(SettlingTime)],
            nameof(OfflineAnalysisViewModel.SteadyStateError) => [nameof(SteadyStateError)],
            nameof(OfflineAnalysisViewModel.PeakProcessValue) => [nameof(PeakProcessValue)],
            nameof(OfflineAnalysisViewModel.PeakTime) => [nameof(PeakTime)],
            nameof(OfflineAnalysisViewModel.MinimumProcessValue) => [nameof(MinimumProcessValue)],
            nameof(OfflineAnalysisViewModel.MeanAbsoluteError) => [nameof(MeanAbsoluteError)],
            nameof(OfflineAnalysisViewModel.MeanSquaredError) => [nameof(MeanSquaredError)],
            nameof(OfflineAnalysisViewModel.IntegralAbsoluteError) => [nameof(IntegralAbsoluteError)],
            nameof(OfflineAnalysisViewModel.OutputStandardDeviation) => [nameof(OutputStandardDeviation)],
            nameof(OfflineAnalysisViewModel.ResponseFlags) => [nameof(ResponseFlags)],
            nameof(OfflineAnalysisViewModel.ActiveAnalysisWindow) => [nameof(ActiveAnalysisWindow)],
            nameof(OfflineAnalysisViewModel.AssessmentSummary) => [nameof(AssessmentSummary)],
            nameof(OfflineAnalysisViewModel.TuningRecommendations) => [nameof(TuningRecommendations)],
            nameof(OfflineAnalysisViewModel.RecommendationSummary) => [nameof(RecommendationSummary)],
            nameof(OfflineAnalysisViewModel.SetPointPoints) => [nameof(SetPointPoints)],
            nameof(OfflineAnalysisViewModel.ProcessValuePoints) => [nameof(ProcessValuePoints)],
            nameof(OfflineAnalysisViewModel.ManipulatedValuePoints) => [nameof(ManipulatedValuePoints)],
            _ => Array.Empty<string>()
        };
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
            await using var stream = File.Create(fileName);
            await _plcProjectConfigurationStore.SaveAsync(configuration, stream, CancellationToken.None);
            PlcConfigurationEditor.MarkSaved();
            Notify("PLC 配置已保存", Path.GetFullPath(fileName), "Success");
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
            PlcCommunicationStatus = $"正在 Ping {configuration.IpAddress} ...";
            var result = await _plcConnectivityProbe.CheckAsync(configuration, CancellationToken.None);
            PlcCommunicationStatus = $"{result.CheckedAt:HH:mm:ss} {result.Host}: {result.Message}";
            Notify(
                result.IsReachable ? "PLC 通信检查通过" : "PLC 通信检查未通过",
                PlcCommunicationStatus,
                result.IsReachable ? "Success" : "Warning");
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
                IsPlcHistoricalTrendMode = false;
                IsPlcLiveTrendPaused = false;
                PlcTrendModeStatus = "当前趋势：实时";
                PlcMonitorTags.Clear();
                SelectedPlcMonitorTag = null;
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
        foreach (var snapshot in snapshots)
        {
            var existing = PlcMonitorTags.FirstOrDefault(item => item.TagId == snapshot.TagId);
            if (existing is null)
            {
                PlcMonitorTags.Add(new PlcTagMonitorViewModel(snapshot));
                continue;
            }

            existing.Update(snapshot);
        }

        var activeIds = snapshots.Select(snapshot => snapshot.TagId).ToHashSet();
        for (var index = PlcMonitorTags.Count - 1; index >= 0; index--)
        {
            if (!activeIds.Contains(PlcMonitorTags[index].TagId))
            {
                PlcMonitorTags.RemoveAt(index);
            }
        }

        SelectedPlcMonitorTag ??= PlcMonitorTags.FirstOrDefault();
        if (applyTrend)
        {
            PlcSnapshotsApplied?.Invoke(snapshots, trendTimestamp);
        }
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

            PlcMonitorTags.Clear();
            SelectedPlcMonitorTag = null;
            PlcTrendResetRequested?.Invoke();
            if (showFullHistory)
            {
                ShowLoadedPlcHistoricalTrend();
            }
            else
            {
                IsPlcHistoricalTrendMode = false;
                IsPlcLiveTrendPaused = false;
                PlcTrendModeStatus = "当前趋势：实时";
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
        PlcMonitorTags.Clear();
        SelectedPlcMonitorTag = null;
        PlcTrendResetRequested?.Invoke();
        await RefreshPlcMonitorAsync();
    }

    public void UsePlcLiveTrendMode()
    {
        IsPlcHistoricalTrendMode = false;
        IsPlcLiveTrendPaused = false;
        HistoricalTrendWorkbench.Clear();
        PlcTrendModeStatus = "当前趋势：实时";
    }

    public Task TogglePlcLiveTrendPauseAsync()
    {
        if (IsPlcHistoricalTrendMode)
        {
            return Task.CompletedTask;
        }

        IsPlcLiveTrendPaused = !IsPlcLiveTrendPaused;
        PlcTrendModeStatus = IsPlcLiveTrendPaused
            ? "当前趋势：实时（滚动已暂停）"
            : "当前趋势：实时";
        return Task.CompletedTask;
    }

    public async Task ShowPlcHistoricalTrendAsync()
    {
        await StopLiveMonitoringAsync();
        StopPlcReplay();
        if (!Debug.HasReplayFrames)
        {
            IsPlcHistoricalTrendMode = true;
            IsPlcLiveTrendPaused = true;
            PlcTrendModeStatus = "当前趋势：历史";
            PlcMonitorStatus = "历史趋势模式：尚未加载历史记录。";
            Debug.UpdateReplayStatus("历史趋势");
            return;
        }

        PlcMonitorTags.Clear();
        SelectedPlcMonitorTag = null;
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

        IsPlcHistoricalTrendMode = true;
        PlcMonitorStatus = $"历史趋势视图已调整：{PlcHistoricalRangeStartText} - {PlcHistoricalRangeEndText}。";
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
        IsPlcHistoricalTrendMode = true;
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

        PlcMonitorStatus = $"趋势 Y 轴范围已调整：{PlcTrendYMinText} - {PlcTrendYMaxText}。";
        return Task.CompletedTask;
    }

    public Task ResetPlcTrendYRangeAsync()
    {
        HistoricalTrendWorkbench.ResetYRangeToFull();
        PlcTrendYRangeRequested?.Invoke(null, null);
        PlcMonitorStatus = "趋势 Y 轴已恢复自动适配。";
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
            PlcMonitorTags.Clear();
            SelectedPlcMonitorTag = null;
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

        IsPlcHistoricalTrendMode = true;
        IsPlcLiveTrendPaused = false;
        PlcTrendModeStatus = "当前趋势：历史";
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
            await using var stream = File.OpenRead(fileName);
            var configuration = await _plcProjectConfigurationStore.LoadAsync(stream, CancellationToken.None);
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
        if (OfflineAnalysis.LastAnalysisWindow is null || OfflineAnalysis.LastSamples.Count == 0)
        {
            Notify("无法保存试验记录", "请先导入 CSV 并完成一次分析。", "Warning");
            return;
        }

        try
        {
            var sessionId = OfflineAnalysis.LastSamples
                .Select(sample => sample.TestSessionId)
                .FirstOrDefault(id => id != Guid.Empty);

            if (sessionId == Guid.Empty)
            {
                sessionId = Guid.NewGuid();
            }

            var samples = OfflineAnalysis.LastSamples
                .Select(sample => sample with { TestSessionId = sessionId })
                .ToArray();

            var session = new TestSession(
                sessionId,
                Guid.Empty,
                string.IsNullOrWhiteSpace(OfflineAnalysis.LastSourceFileName)
                    ? $"offline-session-{sessionId:N}"
                    : Path.GetFileNameWithoutExtension(OfflineAnalysis.LastSourceFileName),
                OfflineAnalysis.LastAnalysisWindow.Start,
                OfflineAnalysis.LastAnalysisWindow.End,
                null,
                "Offline CSV analysis",
                $"Profile: {_fieldProfile.ProfileName}");

            await _testSessionRepository.SaveAsync(session, CancellationToken.None);
            await _pidSampleRepository.SaveBatchAsync(samples, CancellationToken.None);
            OfflineAnalysis.MarkSavedSession(sessionId);
            await LoadHistoryAsync(showNotification: false);
            Notify(
                "试验记录已保存",
                string.Join(
                    Environment.NewLine,
                    $"{session.Name}，样本 {samples.Length} 条。",
                    $"目录：{_testSessionStorageDirectory}",
                    $"索引：{Path.Combine(_testSessionStorageDirectory, "test-sessions.json")}",
                    $"样本：{Path.Combine(_testSessionStorageDirectory, $"{sessionId:D}.samples.json")}"),
                "Success");
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
        if (SelectedHistorySession is null)
        {
            Notify("无法打开历史记录", "请先选择一条历史记录。", "Warning");
            return;
        }

        try
        {
            var samples = await _pidSampleRepository.GetBySessionAsync(SelectedHistorySession.Id, CancellationToken.None);
            if (samples.Count == 0)
            {
                Notify("历史记录无样本", "该试验记录没有可加载的采样数据。", "Warning");
                return;
            }

            var window = new AnalysisWindow(samples.Min(sample => sample.Timestamp), samples.Max(sample => sample.Timestamp));
            ApplyAnalysisResult(
                SelectedHistorySession.Name,
                samples,
                window,
                _pidAnalysisService.Analyze(samples, window));
            Notify("历史记录已打开", $"{SelectedHistorySession.Name}，样本 {samples.Count} 条。", "Success");
        }
        catch (Exception exception)
        {
            Notify("历史记录打开失败", exception.Message, "Error");
        }
    }

    public async Task ExportHistorySamplesAsync()
    {
        if (SelectedHistorySession is null)
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
            var samples = await _pidSampleRepository.GetBySessionAsync(SelectedHistorySession.Id, CancellationToken.None);
            if (samples.Count == 0)
            {
                Notify("历史采样导出失败", "该试验记录没有可导出的采样数据。", "Warning");
                return;
            }

            await using var stream = File.Create(fileName);
            var exchange = new ConfigurablePidSampleCsvExchange(_fieldProfile);
            await exchange.ExportAsync(samples, stream, CancellationToken.None);
            Notify("历史采样已导出", Path.GetFullPath(fileName), "Success");
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
        if (SelectedHistorySession is null)
        {
            Notify("无法设置对比基准", "请先选择一条历史记录。", "Warning");
            return Task.CompletedTask;
        }

        _baselineHistorySession = SelectedHistorySession;
        HistoryComparisonStatus = $"基准：{_baselineHistorySession.Name}";
        HistoryComparisonMetrics.Clear();
        Notify("历史对比基准已设置", HistoryComparisonStatus, "Info");
        return Task.CompletedTask;
    }

    public async Task CompareHistorySessionAsync()
    {
        if (_baselineHistorySession is null)
        {
            Notify("无法对比历史记录", "请先选择一条记录并设为基准。", "Warning");
            return;
        }

        if (SelectedHistorySession is null)
        {
            Notify("无法对比历史记录", "请先选择要对比的历史记录。", "Warning");
            return;
        }

        if (SelectedHistorySession.Id == _baselineHistorySession.Id)
        {
            Notify("无法对比历史记录", "请选择不同于基准的历史记录。", "Warning");
            return;
        }

        try
        {
            var baseline = await AnalyzeHistorySessionAsync(_baselineHistorySession);
            var candidate = await AnalyzeHistorySessionAsync(SelectedHistorySession);
            HistoryComparisonMetrics = BuildHistoryComparisonMetrics(baseline.Metrics, candidate.Metrics);
            HistoryComparisonStatus = $"基准：{_baselineHistorySession.Name}；对比：{SelectedHistorySession.Name}";
            Notify("历史记录对比已完成", HistoryComparisonStatus, "Success");
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
            var reviews = await _recommendationReviewRepository.ListAsync(CancellationToken.None);
            RecommendationReviews = new ObservableCollection<PidRecommendationReviewViewModel>(
                reviews
                    .OrderByDescending(review => review.CreatedAt)
                    .Select(review => new PidRecommendationReviewViewModel(review)));
            RecommendationReviewStatus = RecommendationReviews.Count == 0
                ? "尚无建议审查记录。"
                : $"已加载 {RecommendationReviews.Count} 条建议审查记录。";
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
            var sessions = await _testSessionRepository.ListAsync(CancellationToken.None);
            var items = new List<TestSessionListItemViewModel>();

            foreach (var session in sessions.OrderByDescending(session => session.StartedAt))
            {
                var samples = await _pidSampleRepository.GetBySessionAsync(session.Id, CancellationToken.None);
                items.Add(new TestSessionListItemViewModel(session, samples.Count));
            }

            _allHistorySessions = items;
            ApplyHistoryFilter();
            HistoryStatus = HistorySessions.Count == 0
                ? "尚无已保存试验记录。"
                : $"已加载 {HistorySessions.Count} 条试验记录。";

            if (showNotification)
            {
                Notify("历史记录已刷新", HistoryStatus, "Info");
            }
        }
        catch (Exception exception)
        {
            HistoryStatus = "历史记录加载失败。";
            Notify("历史记录加载失败", exception.Message, "Error");
        }
    }

    public async Task SaveParameterSetAsync()
    {
        if (OfflineAnalysis.LastSamples.Count == 0)
        {
            Notify("无法保存参数方案", "请先导入 CSV、载入示例或打开历史记录。", "Warning");
            return;
        }

        var sourceName = string.IsNullOrWhiteSpace(OfflineAnalysis.LastSourceFileName)
            ? "current-analysis"
            : Path.GetFileNameWithoutExtension(OfflineAnalysis.LastSourceFileName);
        var parameterSet = _parameterSetExtractor.Extract(
            OfflineAnalysis.LastSamples,
            OfflineAnalysis.LastTestSessionId == Guid.Empty ? null : OfflineAnalysis.LastTestSessionId,
            sourceName,
            $"Captured from {sourceName}");

        if (parameterSet is null)
        {
            Notify("无法保存参数方案", "当前样本没有 Kp、Ki/Ti 或 Kd/Td 参数值。", "Warning");
            return;
        }

        await _parameterSetRepository.SaveAsync(parameterSet, CancellationToken.None);
        await LoadParameterSetsAsync(showNotification: false);
        Notify(
            "参数方案已保存",
            $"{parameterSet.Name}: Kp={FormatParameterValue(parameterSet.Kp)}, Ki/Ti={FormatParameterValue(parameterSet.KiOrTi)}, Kd/Td={FormatParameterValue(parameterSet.KdOrTd)}",
            "Success");
    }

    public async Task LoadParameterSetsAsync()
    {
        await LoadParameterSetsAsync(showNotification: true);
    }

    private async Task ReviewRecommendationAsync(PidRecommendationReviewDecision decision)
    {
        if (SelectedTuningRecommendation is null)
        {
            Notify("无法记录建议审查", "请先选择一条参数调整建议。", "Warning");
            return;
        }

        try
        {
            var review = new PidRecommendationReview(
                Guid.NewGuid(),
                OfflineAnalysis.LastTestSessionId,
                string.IsNullOrWhiteSpace(OfflineAnalysis.LastSourceFileName)
                    ? "current-analysis"
                    : Path.GetFileNameWithoutExtension(OfflineAnalysis.LastSourceFileName),
                SelectedTuningRecommendation.Recommendation.Parameter,
                SelectedTuningRecommendation.Recommendation.Direction,
                SelectedTuningRecommendation.Recommendation.Adjustment,
                decision,
                RecommendationReviewNote.Trim(),
                DateTimeOffset.Now);

            await _recommendationReviewRepository.SaveAsync(review, CancellationToken.None);
            RecommendationReviewNote = string.Empty;
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
            var parameterSets = await _parameterSetRepository.ListAsync(CancellationToken.None);
            ParameterSets = new ObservableCollection<PidParameterSetViewModel>(
                parameterSets
                    .OrderByDescending(item => item.CapturedAt)
                    .Select(item => new PidParameterSetViewModel(item)));
            ParameterSetStatus = ParameterSets.Count == 0
                ? "尚无参数方案记录。"
                : $"已加载 {ParameterSets.Count} 条参数方案。";

            if (showNotification)
            {
                Notify("参数方案已刷新", ParameterSetStatus, "Info");
            }
        }
        catch (Exception exception)
        {
            ParameterSetStatus = "参数方案加载失败。";
            Notify("参数方案加载失败", exception.Message, "Error");
        }
    }

    private async Task<(IReadOnlyList<PidSample> Samples, PidResponseMetrics Metrics)> AnalyzeHistorySessionAsync(
        TestSessionListItemViewModel session)
    {
        var samples = await _pidSampleRepository.GetBySessionAsync(session.Id, CancellationToken.None);
        if (samples.Count == 0)
        {
            throw new InvalidOperationException($"{session.Name} 没有可对比的采样数据。");
        }

        var window = new AnalysisWindow(samples.Min(sample => sample.Timestamp), samples.Max(sample => sample.Timestamp));
        return (samples, _pidAnalysisService.Analyze(samples, window));
    }

    private static ObservableCollection<HistoryComparisonMetricViewModel> BuildHistoryComparisonMetrics(
        PidResponseMetrics baseline,
        PidResponseMetrics candidate)
    {
        return new ObservableCollection<HistoryComparisonMetricViewModel>
        {
            Compare("超调量", baseline.OvershootPercent, candidate.OvershootPercent, "0.###"),
            Compare("上升时间", baseline.RiseTime?.TotalSeconds, candidate.RiseTime?.TotalSeconds, "0.### s"),
            Compare("调节时间", baseline.SettlingTime?.TotalSeconds, candidate.SettlingTime?.TotalSeconds, "0.### s"),
            Compare("稳态误差", baseline.SteadyStateError, candidate.SteadyStateError, "0.###"),
            Compare("峰值", baseline.PeakProcessValue, candidate.PeakProcessValue, "0.###"),
            Compare("平均绝对误差", baseline.MeanAbsoluteError, candidate.MeanAbsoluteError, "0.###"),
            Compare("误差积分", baseline.IntegralAbsoluteError, candidate.IntegralAbsoluteError, "0.###"),
            Compare("输出标准差", baseline.OutputStandardDeviation, candidate.OutputStandardDeviation, "0.###")
        };
    }

    private static HistoryComparisonMetricViewModel Compare(
        string metric,
        double? baseline,
        double? candidate,
        string format)
    {
        double? delta = baseline.HasValue && candidate.HasValue
            ? candidate.Value - baseline.Value
            : null;

        return new HistoryComparisonMetricViewModel(
            metric,
            FormatComparisonValue(baseline, format),
            FormatComparisonValue(candidate, format),
            FormatDelta(delta, format));
    }

    private static string FormatComparisonValue(double? value, string format)
    {
        return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatParameterValue(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatDelta(double? value, string format)
    {
        if (!value.HasValue)
        {
            return "-";
        }

        var sign = value.Value > 0 ? "+" : string.Empty;
        return sign + value.Value.ToString(format, CultureInfo.InvariantCulture);
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

    private void ApplyHistoryFilter()
    {
        var searchText = HistorySearchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(searchText)
            ? _allHistorySessions
            : _allHistorySessions.Where(item =>
                item.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || item.Device.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || item.OperatingCondition.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || item.Notes.Contains(searchText, StringComparison.OrdinalIgnoreCase));

        HistorySessions = new ObservableCollection<TestSessionListItemViewModel>(filtered);
        if (SelectedHistorySession is not null && !HistorySessions.Any(item => item.Id == SelectedHistorySession.Id))
        {
            SelectedHistorySession = null;
        }
    }

    private void UpdateSelectedHistoryDetails()
    {
        SelectedHistoryDetails = SelectedHistorySession is null
            ? "请选择一条历史记录。"
            : string.Join(
                Environment.NewLine,
                $"名称：{SelectedHistorySession.Name}",
                $"时间：{SelectedHistorySession.StartedAt} - {SelectedHistorySession.EndedAt}",
                $"持续：{SelectedHistorySession.Duration}",
                $"样本：{SelectedHistorySession.SampleCount}",
                $"工况：{SelectedHistorySession.OperatingCondition}",
                $"备注：{SelectedHistorySession.Notes}");
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
        var index = FieldDefinitions.Count + 1;
        while (FieldDefinitions.Any(field => string.Equals(field.Key, $"metadata_{index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        var field = PidSampleFieldDefinitionViewModel.CreateNew(index);
        FieldDefinitions.Add(field);
        SelectedFieldDefinition = field;
        Notify("字段已新增", "请编辑字段信息后保存字段配置。", "Info");
        return Task.CompletedTask;
    }

    private Task RemoveFieldAsync()
    {
        if (SelectedFieldDefinition is null)
        {
            Notify("无法删除字段", "请先选择要删除的字段。", "Warning");
            return Task.CompletedTask;
        }

        FieldDefinitions.Remove(SelectedFieldDefinition);
        SelectedFieldDefinition = null;
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
            _fieldProfile = BuildFieldProfileFromGrid();
            CurrentFieldProfile = $"{_fieldProfile.ProfileName} ({_fieldProfile.Fields.Count} 字段)";
            await using var stream = File.Create(fileName);
            await _fieldProfileStore.SaveAsync(_fieldProfile, stream, CancellationToken.None);
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
            await using var stream = File.OpenRead(fileName);
            _fieldProfile = await _fieldProfileStore.LoadAsync(stream, CancellationToken.None);
            CurrentFieldProfile = $"{_fieldProfile.ProfileName} ({_fieldProfile.Fields.Count} 字段)";
            RefreshFieldDefinitions();
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
        await using var stream = File.OpenRead(fileName);
        var exchange = new ConfigurablePidSampleCsvExchange(_fieldProfile);
        var useCase = new AnalyzeOfflineCsvUseCase(exchange, _pidAnalysisService);
        var window = _analysisWindowParser.Parse(AnalysisStartText, AnalysisEndText);
        var result = await useCase.AnalyzeAsync(stream, window, CancellationToken.None);

        ApplyAnalysisResult(fileName, result.Samples, result.Window, result.Metrics);

        if (window is null)
        {
            AnalysisStartText = result.Window.Start.ToString("O", CultureInfo.InvariantCulture);
            AnalysisEndText = result.Window.End.ToString("O", CultureInfo.InvariantCulture);
        }

        Notify("离线分析已完成", $"{Path.GetFileName(fileName)}，样本 {result.Samples.Count} 条。", "Success");
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
        IsNotificationVisible = false;
        return Task.CompletedTask;
    }

    private void Notify(string title, string message, string kind)
    {
        StatusMessage = $"{title}：{message}";
        NotificationTitle = title;
        NotificationMessage = message;
        NotificationKind = kind;
        IsNotificationVisible = true;
    }

    private void RefreshFieldDefinitions()
    {
        FieldDefinitions = new ObservableCollection<PidSampleFieldDefinitionViewModel>(
            _fieldProfile.Fields.Select(field => new PidSampleFieldDefinitionViewModel(field)));
    }

    private void ApplyPlcConfiguration(PlcProjectConfiguration configuration)
    {
        PlcConfigurationEditor.ApplyConfiguration(configuration);
        PlcMonitorTags.Clear();
        SelectedPlcMonitorTag = null;
        PlcMonitorStatus = "PLC 配置已更新，等待刷新点位。";
    }

    private PlcProjectConfiguration BuildPlcConfigurationFromForm()
    {
        return PlcConfigurationEditor.BuildConfiguration();
    }

    private PidSampleFieldProfile BuildFieldProfileFromGrid()
    {
        var fields = FieldDefinitions.Select(field => field.ToDefinition()).ToArray();
        var duplicateKey = fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateKey is not null)
        {
            throw new InvalidOperationException($"字段 key 重复：{duplicateKey.Key}");
        }

        return _fieldProfile with { Fields = fields };
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
