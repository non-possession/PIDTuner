using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
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
using PIDTuner.Domain.Trends;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Csv;
using PIDTuner.Infrastructure.Persistence;
using PIDTuner.Infrastructure.Plc;

namespace PIDTuner.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IOpenFileDialogService _openFileDialogService;
    private readonly IPidSampleFieldProfileStore _fieldProfileStore;
    private readonly IPlcProjectConfigurationStore _plcProjectConfigurationStore;
    private readonly BasicPidAnalysisService _pidAnalysisService = new();
    private readonly PidResponseAssessmentService _assessmentService = new();
    private readonly PidTuningRecommendationService _recommendationService = new();
    private readonly PidAnalysisResultCsvExporter _analysisResultExporter = new();
    private readonly PidTrendSeriesBuilder _trendSeriesBuilder = new();
    private readonly AnalysisWindowParser _analysisWindowParser = new();
    private readonly ITestSessionRepository _testSessionRepository;
    private readonly IPidSampleRepository _pidSampleRepository;
    private readonly IPidRecommendationReviewRepository _recommendationReviewRepository;
    private readonly IPidParameterSetRepository _parameterSetRepository;
    private readonly IPlcConnectivityProbe _plcConnectivityProbe;
    private readonly IPlcTagSnapshotReader _plcTagSnapshotReader;
    private readonly PidParameterSetExtractor _parameterSetExtractor = new();
    private readonly string _testSessionStorageDirectory;
    private readonly string _plcRecordingStorageDirectory;
    private readonly DispatcherTimer _monitorTimer = new();
    private readonly DispatcherTimer _plcReplayTimer = new();
    private IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> _lastPlcRecordingFrames = Array.Empty<IReadOnlyList<PlcTagSnapshot>>();
    private IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> _loadedPlcReplayFrames = Array.Empty<IReadOnlyList<PlcTagSnapshot>>();
    private int _plcReplayNextFrameIndex;
    private int _plcReplayDisplayedFrameIndex = -1;
    private int _loadedPlcReplayIntervalMilliseconds = 100;
    private double _plcReplaySpeedMultiplier = 1d;
    private PidSampleFieldProfile _fieldProfile = PidSampleFieldProfile.CreateDefault();
    private PlcProjectConfiguration _plcConfiguration = PlcProjectConfiguration.CreateDefault();
    private AnalysisWindow? _lastAnalysisWindow;
    private PidResponseMetrics? _lastMetrics;
    private PidResponseAssessment? _lastAssessment;
    private IReadOnlyList<PidSample> _lastSamples = Array.Empty<PidSample>();
    private Guid? _lastTestSessionId;
    private string _lastSourceFileName = string.Empty;
    private string _statusMessage = "阶段 1 已就绪：可在分析页导入离线 CSV 并计算基础指标。";
    private string _currentFieldProfile = "default-pid-sample-fields (10 字段)";
    private string _plcConfigurationName = "default-siemens-s7-project";
    private string _plcProtocol = "Siemens S7";
    private string _plcIpAddress = "192.168.0.1";
    private int _plcRack;
    private int _plcSlot = 1;
    private int _plcTimeoutMilliseconds = 3000;
    private int _plcDefaultSamplingMilliseconds = 500;
    private int _plcMinimumSamplingMilliseconds = PlcProjectConfiguration.DefaultMinimumSamplingMilliseconds;
    private string _plcConfigurationStatus = "PLC 配置尚未保存。";
    private string _sampleCount = "-";
    private string _overshootPercent = "-";
    private string _riseTime = "-";
    private string _settlingTime = "-";
    private string _steadyStateError = "-";
    private string _peakProcessValue = "-";
    private string _peakTime = "-";
    private string _minimumProcessValue = "-";
    private string _meanAbsoluteError = "-";
    private string _meanSquaredError = "-";
    private string _integralAbsoluteError = "-";
    private string _outputStandardDeviation = "-";
    private string _responseFlags = "-";
    private string _analysisStartText = string.Empty;
    private string _analysisEndText = string.Empty;
    private string _activeAnalysisWindow = "-";
    private string _assessmentSummary = "-";
    private string _notificationTitle = string.Empty;
    private string _notificationMessage = string.Empty;
    private string _notificationKind = "Info";
    private bool _isNotificationVisible;
    private string _historyStatus = "尚未加载历史记录。";
    private string _historySearchText = string.Empty;
    private string _selectedHistoryDetails = "请选择一条历史记录。";
    private PointCollection _setPointPoints = new();
    private PointCollection _processValuePoints = new();
    private PointCollection _manipulatedValuePoints = new();
    private ObservableCollection<PidSampleFieldDefinitionViewModel> _fieldDefinitions = [];
    private ObservableCollection<TagDefinitionViewModel> _tagDefinitions = [];
    private ObservableCollection<TestSessionListItemViewModel> _historySessions = [];
    private ObservableCollection<PidTuningRecommendationViewModel> _tuningRecommendations = [];
    private ObservableCollection<PidRecommendationReviewViewModel> _recommendationReviews = [];
    private ObservableCollection<PlcTagMonitorViewModel> _plcMonitorTags = [];
    private ObservableCollection<HistoryComparisonMetricViewModel> _historyComparisonMetrics = [];
    private ObservableCollection<PidParameterSetViewModel> _parameterSets = [];
    private IReadOnlyList<TestSessionListItemViewModel> _allHistorySessions = Array.Empty<TestSessionListItemViewModel>();
    private PidSampleFieldDefinitionViewModel? _selectedFieldDefinition;
    private TagDefinitionViewModel? _selectedTagDefinition;
    private TestSessionListItemViewModel? _selectedHistorySession;
    private TestSessionListItemViewModel? _baselineHistorySession;
    private PidTuningRecommendationViewModel? _selectedTuningRecommendation;
    private PlcTagMonitorViewModel? _selectedPlcMonitorTag;
    private string _recommendationSummary = "完成一次分析后生成参数调整建议。";
    private string _recommendationReviewNote = string.Empty;
    private string _recommendationReviewStatus = "尚未记录建议审查。";

    private string _plcCommunicationStatus = "尚未检查 PLC 通信。";
    private string _plcMonitorStatus = "尚未刷新点位。";
    private string _plcReplayStatus = "尚未加载 PLC 记录。";
    private string _historyComparisonStatus = "尚未设置历史对比基准。";
    private bool _isPlcMonitoring;
    private bool _isPlcReplayRunning;

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
        _plcRecordingStorageDirectory = Path.GetFullPath(
            plcRecordingStorageDirectory ?? Path.Combine(FindRepositoryRoot(), "local", "plc-recordings"));
        _testSessionRepository = testSessionRepository ?? new JsonTestSessionRepository(_testSessionStorageDirectory);
        _pidSampleRepository = pidSampleRepository ?? new JsonPidSampleRepository(_testSessionStorageDirectory);
        _recommendationReviewRepository = recommendationReviewRepository
            ?? new JsonPidRecommendationReviewRepository(Path.Combine(FindRepositoryRoot(), "local", "recommendation-reviews"));
        _parameterSetRepository = parameterSetRepository
            ?? new JsonPidParameterSetRepository(Path.Combine(FindRepositoryRoot(), "local", "parameter-sets"));
        _monitorTimer.Tick += async (_, _) => await RefreshPlcMonitorAsync();
        _plcReplayTimer.Tick += (_, _) => ApplyNextPlcReplayFrame();
        RefreshFieldDefinitions();
        RefreshTagDefinitions();
        ImportCsvCommand = new AsyncCommand(ImportCsvAsync);
        LoadPlcConfigurationCommand = new AsyncCommand(LoadPlcConfigurationAsync);
        SavePlcConfigurationCommand = new AsyncCommand(SavePlcConfigurationAsync);
        AddTagCommand = new AsyncCommand(AddTagAsync);
        RemoveTagCommand = new AsyncCommand(RemoveTagAsync);
        CheckPlcCommunicationCommand = new AsyncCommand(CheckPlcCommunicationAsync);
        RefreshPlcMonitorCommand = new AsyncCommand(RefreshPlcMonitorAsync);
        TogglePlcMonitoringCommand = new AsyncCommand(TogglePlcMonitoringAsync);
        RecordPlcOneSecondCommand = new AsyncCommand(RecordPlcOneSecondAsync);
        LoadPlcRecordingCommand = new AsyncCommand(LoadPlcRecordingAsync);
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

    public event Action<IReadOnlyList<PlcTagSnapshot>>? PlcSnapshotsApplied;

    public event Action? PlcTrendResetRequested;

    public string Title { get; } = "PIDTuner";

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
        get => _plcConfigurationName;
        set => SetProperty(ref _plcConfigurationName, value);
    }

    public string PlcProtocol
    {
        get => _plcProtocol;
        set => SetProperty(ref _plcProtocol, value);
    }

    public string PlcIpAddress
    {
        get => _plcIpAddress;
        set => SetProperty(ref _plcIpAddress, value);
    }

    public int PlcRack
    {
        get => _plcRack;
        set => SetProperty(ref _plcRack, value);
    }

    public int PlcSlot
    {
        get => _plcSlot;
        set => SetProperty(ref _plcSlot, value);
    }

    public int PlcTimeoutMilliseconds
    {
        get => _plcTimeoutMilliseconds;
        set => SetProperty(ref _plcTimeoutMilliseconds, value);
    }

    public int PlcDefaultSamplingMilliseconds
    {
        get => _plcDefaultSamplingMilliseconds;
        set => SetProperty(ref _plcDefaultSamplingMilliseconds, value);
    }

    public int PlcMinimumSamplingMilliseconds
    {
        get => _plcMinimumSamplingMilliseconds;
        set => SetProperty(ref _plcMinimumSamplingMilliseconds, value);
    }

    public string PlcConfigurationStatus
    {
        get => _plcConfigurationStatus;
        private set => SetProperty(ref _plcConfigurationStatus, value);
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

    public string PlcReplayStatus
    {
        get => _plcReplayStatus;
        private set => SetProperty(ref _plcReplayStatus, value);
    }

    public string PlcReplaySpeedText => $"{_plcReplaySpeedMultiplier:0.##}x";

    public bool IsPlcMonitoring
    {
        get => _isPlcMonitoring;
        private set => SetProperty(ref _isPlcMonitoring, value);
    }

    public bool IsPlcReplayRunning
    {
        get => _isPlcReplayRunning;
        private set => SetProperty(ref _isPlcReplayRunning, value);
    }

    public string SampleCount
    {
        get => _sampleCount;
        private set => SetProperty(ref _sampleCount, value);
    }

    public string OvershootPercent
    {
        get => _overshootPercent;
        private set => SetProperty(ref _overshootPercent, value);
    }

    public string RiseTime
    {
        get => _riseTime;
        private set => SetProperty(ref _riseTime, value);
    }

    public string SettlingTime
    {
        get => _settlingTime;
        private set => SetProperty(ref _settlingTime, value);
    }

    public string SteadyStateError
    {
        get => _steadyStateError;
        private set => SetProperty(ref _steadyStateError, value);
    }

    public string PeakProcessValue
    {
        get => _peakProcessValue;
        private set => SetProperty(ref _peakProcessValue, value);
    }

    public string PeakTime
    {
        get => _peakTime;
        private set => SetProperty(ref _peakTime, value);
    }

    public string MinimumProcessValue
    {
        get => _minimumProcessValue;
        private set => SetProperty(ref _minimumProcessValue, value);
    }

    public string MeanAbsoluteError
    {
        get => _meanAbsoluteError;
        private set => SetProperty(ref _meanAbsoluteError, value);
    }

    public string MeanSquaredError
    {
        get => _meanSquaredError;
        private set => SetProperty(ref _meanSquaredError, value);
    }

    public string IntegralAbsoluteError
    {
        get => _integralAbsoluteError;
        private set => SetProperty(ref _integralAbsoluteError, value);
    }

    public string OutputStandardDeviation
    {
        get => _outputStandardDeviation;
        private set => SetProperty(ref _outputStandardDeviation, value);
    }

    public string ResponseFlags
    {
        get => _responseFlags;
        private set => SetProperty(ref _responseFlags, value);
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
        get => _activeAnalysisWindow;
        private set => SetProperty(ref _activeAnalysisWindow, value);
    }

    public string AssessmentSummary
    {
        get => _assessmentSummary;
        private set => SetProperty(ref _assessmentSummary, value);
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
        get => _setPointPoints;
        private set => SetProperty(ref _setPointPoints, value);
    }

    public PointCollection ProcessValuePoints
    {
        get => _processValuePoints;
        private set => SetProperty(ref _processValuePoints, value);
    }

    public PointCollection ManipulatedValuePoints
    {
        get => _manipulatedValuePoints;
        private set => SetProperty(ref _manipulatedValuePoints, value);
    }

    public ObservableCollection<PidSampleFieldDefinitionViewModel> FieldDefinitions
    {
        get => _fieldDefinitions;
        private set => SetProperty(ref _fieldDefinitions, value);
    }

    public ObservableCollection<TagDefinitionViewModel> TagDefinitions
    {
        get => _tagDefinitions;
        private set => SetProperty(ref _tagDefinitions, value);
    }

    public ObservableCollection<TestSessionListItemViewModel> HistorySessions
    {
        get => _historySessions;
        private set => SetProperty(ref _historySessions, value);
    }

    public ObservableCollection<PidTuningRecommendationViewModel> TuningRecommendations
    {
        get => _tuningRecommendations;
        private set => SetProperty(ref _tuningRecommendations, value);
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
        get => _recommendationSummary;
        private set => SetProperty(ref _recommendationSummary, value);
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
        get => _selectedTagDefinition;
        set => SetProperty(ref _selectedTagDefinition, value);
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

    public ICommand RecordPlcOneSecondCommand { get; }

    public ICommand LoadPlcRecordingCommand { get; }

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

    public async Task SavePlcConfigurationAsync()
    {
        var fileName = _openFileDialogService.PickPlcProjectConfigurationSaveFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            _plcConfiguration = BuildPlcConfigurationFromForm();
            await using var stream = File.Create(fileName);
            await _plcProjectConfigurationStore.SaveAsync(_plcConfiguration, stream, CancellationToken.None);
            PlcConfigurationStatus = $"已保存 {TagDefinitions.Count} 个点位。";
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
            var configuration = BuildPlcConfigurationFromForm();
            var snapshots = await _plcTagSnapshotReader.ReadAsync(configuration, CancellationToken.None);
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

    private void ApplyPlcMonitorSnapshots(IReadOnlyList<PlcTagSnapshot> snapshots)
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
        PlcSnapshotsApplied?.Invoke(snapshots);
    }

    private static int ResolveRecordingIntervalMilliseconds(
        PlcProjectConfiguration configuration,
        IReadOnlyList<TagDefinition> enabledTags)
    {
        var minimumTagInterval = enabledTags
            .Select(tag => (int)tag.SamplingInterval.TotalMilliseconds)
            .Where(milliseconds => milliseconds > 0)
            .DefaultIfEmpty(configuration.DefaultSamplingMilliseconds)
            .Min();

        return Math.Max(ResolveMinimumSamplingMilliseconds(configuration), minimumTagInterval);
    }

    private static int ResolveMonitoringIntervalMilliseconds(PlcProjectConfiguration configuration)
    {
        return Math.Max(
            ResolveMinimumSamplingMilliseconds(configuration),
            configuration.DefaultSamplingMilliseconds);
    }

    private static int ResolveMinimumSamplingMilliseconds(PlcProjectConfiguration configuration)
    {
        return configuration.MinimumSamplingMilliseconds > 0
            ? configuration.MinimumSamplingMilliseconds
            : PlcProjectConfiguration.DefaultMinimumSamplingMilliseconds;
    }

    private async Task TogglePlcMonitoringAsync()
    {
        if (IsPlcMonitoring)
        {
            _monitorTimer.Stop();
            IsPlcMonitoring = false;
            PlcMonitorStatus = "点位监控已停止。";
            return;
        }

        StopPlcReplay();
        _monitorTimer.Interval = TimeSpan.FromMilliseconds(ResolveMonitoringIntervalMilliseconds(BuildPlcConfigurationFromForm()));
        await RefreshPlcMonitorAsync();
        _monitorTimer.Start();
        IsPlcMonitoring = true;
        PlcMonitorStatus = $"点位监控运行中，周期 {_monitorTimer.Interval.TotalMilliseconds:0} ms。";
    }

    public async Task RecordPlcOneSecondAsync()
    {
        try
        {
            StopPlcReplay();
            var configuration = BuildPlcConfigurationFromForm();
            var enabledTags = configuration.Tags
                .Where(tag => tag.IsEnabled && tag.AccessMode != TagAccessMode.WriteOnly)
                .ToArray();
            if (enabledTags.Length == 0)
            {
                Notify("无法记录 PLC 数据", "请先启用至少一个可读取点位。", "Warning");
                return;
            }

            var intervalMilliseconds = ResolveRecordingIntervalMilliseconds(configuration, enabledTags);
            var frames = new List<IReadOnlyList<PlcTagSnapshot>>();
            var stopwatch = Stopwatch.StartNew();
            var nextDue = TimeSpan.Zero;
            PlcMonitorStatus = $"正在记录 1s 点位数据，周期 {intervalMilliseconds} ms。";

            // Open one reader session for the whole recording window to avoid per-frame PLC reconnect cost.
            await using var session = await OpenPlcSnapshotSessionAsync(configuration, CancellationToken.None);
            while (nextDue < TimeSpan.FromSeconds(1))
            {
                var wait = nextDue - stopwatch.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait);
                }

                if (stopwatch.Elapsed >= TimeSpan.FromSeconds(1))
                {
                    break;
                }

                var snapshots = await session.ReadAsync(CancellationToken.None);
                frames.Add(snapshots);
                ApplyPlcMonitorSnapshots(snapshots);
                // Absolute scheduling targets 0ms, N ms, 2N ms... instead of "read duration + delay".
                nextDue += TimeSpan.FromMilliseconds(intervalMilliseconds);
            }

            _lastPlcRecordingFrames = frames;
            OnPropertyChanged(nameof(LastPlcRecordingFrames));
            var recordingPath = await SavePlcRecordingAsync(configuration, intervalMilliseconds, frames);
            var snapshotCount = frames.Sum(frame => frame.Count);
            PlcMonitorStatus = $"1s 记录完成：{frames.Count} 组，{enabledTags.Length} 个点位，共 {snapshotCount} 条快照，周期 {intervalMilliseconds} ms。";
            Notify(
                "PLC 1s 记录完成",
                string.Join(
                    Environment.NewLine,
                    PlcMonitorStatus,
                    $"保存位置：{recordingPath}"),
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
        var fileName = _openFileDialogService.PickPlcRecordingFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
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
            _loadedPlcReplayFrames = recording.Frames;
            _loadedPlcReplayIntervalMilliseconds = Math.Max(10, recording.IntervalMilliseconds);
            _plcReplayNextFrameIndex = 0;
            _plcReplayDisplayedFrameIndex = -1;
            _lastPlcRecordingFrames = recording.Frames;
            OnPropertyChanged(nameof(LastPlcRecordingFrames));

            PlcMonitorTags.Clear();
            SelectedPlcMonitorTag = null;
            PlcTrendResetRequested?.Invoke();
            ApplyPlcReplayFrame(0, advance: true);

            PlcMonitorStatus =
                $"已加载 PLC 记录：{recording.FrameCount} 帧，{recording.SnapshotCount} 条快照，周期 {_loadedPlcReplayIntervalMilliseconds} ms。";
            UpdatePlcReplayStatus("已加载");
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

    public Task TogglePlcReplayAsync()
    {
        if (IsPlcReplayRunning)
        {
            StopPlcReplay();
            PlcMonitorStatus = $"PLC 记录回放已暂停：第 {DisplayedPlcReplayFrameNumber()}/{_loadedPlcReplayFrames.Count} 帧。";
            UpdatePlcReplayStatus("已暂停");
            return Task.CompletedTask;
        }

        if (_loadedPlcReplayFrames.Count == 0)
        {
            Notify("无法回放 PLC 记录", "请先打开一个 PLC 记录 JSON 文件。", "Warning");
            return Task.CompletedTask;
        }

        if (_plcReplayNextFrameIndex >= _loadedPlcReplayFrames.Count)
        {
            _plcReplayNextFrameIndex = 0;
            _plcReplayDisplayedFrameIndex = -1;
            PlcMonitorTags.Clear();
            SelectedPlcMonitorTag = null;
            PlcTrendResetRequested?.Invoke();
        }

        ApplyPlcReplayTimerInterval();
        IsPlcReplayRunning = true;
        _plcReplayTimer.Start();
        PlcMonitorStatus =
            $"PLC 记录回放中：源周期 {_loadedPlcReplayIntervalMilliseconds} ms，速度 {PlcReplaySpeedText}，下一帧 {_plcReplayNextFrameIndex + 1}/{_loadedPlcReplayFrames.Count}。";
        UpdatePlcReplayStatus("播放中");
        return Task.CompletedTask;
    }

    public Task StepPlcReplayBackwardAsync()
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        StopPlcReplay();
        var targetFrameIndex = Math.Max(0, _plcReplayDisplayedFrameIndex - 1);
        RebuildPlcReplayToFrame(targetFrameIndex);
        PlcMonitorStatus = $"PLC 记录回放：已回到第 {targetFrameIndex + 1}/{_loadedPlcReplayFrames.Count} 帧。";
        UpdatePlcReplayStatus("单帧后退");
        return Task.CompletedTask;
    }

    public Task StepPlcReplayForwardAsync()
    {
        if (!EnsurePlcReplayLoaded())
        {
            return Task.CompletedTask;
        }

        StopPlcReplay();
        if (_plcReplayNextFrameIndex >= _loadedPlcReplayFrames.Count)
        {
            PlcMonitorStatus = $"PLC 记录回放已在最后一帧：{_loadedPlcReplayFrames.Count}/{_loadedPlcReplayFrames.Count}。";
            UpdatePlcReplayStatus("已到末尾");
            return Task.CompletedTask;
        }

        ApplyPlcReplayFrame(_plcReplayNextFrameIndex, advance: true);
        UpdatePlcReplayStatus("单帧前进");
        return Task.CompletedTask;
    }

    public Task SetPlcReplaySpeedAsync(double speedMultiplier)
    {
        _plcReplaySpeedMultiplier = Math.Clamp(speedMultiplier, 0.5d, 5d);
        OnPropertyChanged(nameof(PlcReplaySpeedText));
        if (IsPlcReplayRunning)
        {
            ApplyPlcReplayTimerInterval();
        }

        UpdatePlcReplayStatus("速度已调整");
        return Task.CompletedTask;
    }

    private void ApplyNextPlcReplayFrame()
    {
        if (_plcReplayNextFrameIndex >= _loadedPlcReplayFrames.Count)
        {
            StopPlcReplay();
            PlcMonitorStatus = $"PLC 记录回放完成：{_loadedPlcReplayFrames.Count} 帧。";
            UpdatePlcReplayStatus("回放完成");
            Notify("PLC 记录回放完成", PlcMonitorStatus, "Success");
            return;
        }

        ApplyPlcReplayFrame(_plcReplayNextFrameIndex, advance: true);
    }

    private void ApplyPlcReplayFrame(int frameIndex, bool advance)
    {
        if (_loadedPlcReplayFrames.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(frameIndex, 0, _loadedPlcReplayFrames.Count - 1);
        var frame = _loadedPlcReplayFrames[index];
        ApplyPlcMonitorSnapshots(frame);
        _plcReplayDisplayedFrameIndex = index;
        PlcMonitorStatus = $"PLC 记录回放：第 {index + 1}/{_loadedPlcReplayFrames.Count} 帧，{frame.Count} 个点位。";
        if (advance)
        {
            _plcReplayNextFrameIndex = Math.Min(index + 1, _loadedPlcReplayFrames.Count);
        }

        UpdatePlcReplayStatus(IsPlcReplayRunning ? "播放中" : "已定位");
    }

    private void RebuildPlcReplayToFrame(int frameIndex)
    {
        PlcMonitorTags.Clear();
        SelectedPlcMonitorTag = null;
        PlcTrendResetRequested?.Invoke();

        var targetFrameIndex = Math.Clamp(frameIndex, 0, _loadedPlcReplayFrames.Count - 1);
        for (var index = 0; index <= targetFrameIndex; index++)
        {
            ApplyPlcMonitorSnapshots(_loadedPlcReplayFrames[index]);
        }

        _plcReplayDisplayedFrameIndex = targetFrameIndex;
        _plcReplayNextFrameIndex = Math.Min(targetFrameIndex + 1, _loadedPlcReplayFrames.Count);
    }

    private bool EnsurePlcReplayLoaded()
    {
        if (_loadedPlcReplayFrames.Count > 0)
        {
            return true;
        }

        Notify("无法控制 PLC 回放", "请先打开一个 PLC 记录 JSON 文件。", "Warning");
        return false;
    }

    private void ApplyPlcReplayTimerInterval()
    {
        var effectiveIntervalMilliseconds = Math.Max(
            10,
            (int)Math.Round(_loadedPlcReplayIntervalMilliseconds / _plcReplaySpeedMultiplier));
        _plcReplayTimer.Interval = TimeSpan.FromMilliseconds(effectiveIntervalMilliseconds);
    }

    private int DisplayedPlcReplayFrameNumber()
    {
        return _plcReplayDisplayedFrameIndex >= 0 ? _plcReplayDisplayedFrameIndex + 1 : 0;
    }

    private void UpdatePlcReplayStatus(string phase)
    {
        if (_loadedPlcReplayFrames.Count == 0)
        {
            PlcReplayStatus = "尚未加载 PLC 记录。";
            return;
        }

        var effectiveIntervalMilliseconds = Math.Max(
            10,
            (int)Math.Round(_loadedPlcReplayIntervalMilliseconds / _plcReplaySpeedMultiplier));
        PlcReplayStatus =
            $"{phase}：第 {DisplayedPlcReplayFrameNumber()}/{_loadedPlcReplayFrames.Count} 帧，源周期 {_loadedPlcReplayIntervalMilliseconds} ms，播放间隔 {effectiveIntervalMilliseconds} ms，速度 {PlcReplaySpeedText}";
    }

    private void StopPlcReplay()
    {
        _plcReplayTimer.Stop();
        IsPlcReplayRunning = false;
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

    private async Task<string> SavePlcRecordingAsync(
        PlcProjectConfiguration configuration,
        int intervalMilliseconds,
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames)
    {
        Directory.CreateDirectory(_plcRecordingStorageDirectory);
        var fileName = $"plc-recording-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.json";
        var filePath = Path.Combine(_plcRecordingStorageDirectory, fileName);
        await using var stream = File.Create(filePath);
        var recording = new PlcOneSecondRecording(
            DateTimeOffset.Now,
            configuration.Name,
            configuration.Protocol,
            configuration.IpAddress,
            intervalMilliseconds,
            frames.Count,
            frames.Sum(frame => frame.Count),
            frames);

        await JsonSerializer.SerializeAsync(
            stream,
            recording,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true },
            CancellationToken.None);

        return Path.GetFullPath(filePath);
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

    private async Task LoadPlcConfigurationAsync()
    {
        var fileName = _openFileDialogService.PickPlcProjectConfigurationFile();
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(fileName);
            _plcConfiguration = await _plcProjectConfigurationStore.LoadAsync(stream, CancellationToken.None);
            ApplyPlcConfiguration(_plcConfiguration);
            PlcConfigurationStatus = $"已加载 {TagDefinitions.Count} 个点位。";
            Notify("PLC 配置已加载", Path.GetFileName(fileName), "Success");
        }
        catch (Exception exception)
        {
            Notify("PLC 配置加载失败", exception.Message, "Error");
        }
    }

    private Task AddTagAsync()
    {
        var tag = TagDefinitionViewModel.CreateNew(TagDefinitions.Count + 1, PlcDefaultSamplingMilliseconds);
        TagDefinitions.Add(tag);
        SelectedTagDefinition = tag;
        PlcConfigurationStatus = "已新增点位，请保存 PLC 配置。";
        Notify("点位已新增", "请编辑点位信息后保存 PLC 配置。", "Info");
        return Task.CompletedTask;
    }

    private Task RemoveTagAsync()
    {
        if (SelectedTagDefinition is null)
        {
            Notify("无法删除点位", "请先选择要删除的点位。", "Warning");
            return Task.CompletedTask;
        }

        TagDefinitions.Remove(SelectedTagDefinition);
        SelectedTagDefinition = null;
        PlcConfigurationStatus = "已删除点位，请保存 PLC 配置。";
        Notify("点位已删除", "请保存 PLC 配置以保留修改。", "Info");
        return Task.CompletedTask;
    }

    public async Task SaveTestSessionAsync()
    {
        if (_lastAnalysisWindow is null || _lastSamples.Count == 0)
        {
            Notify("无法保存试验记录", "请先导入 CSV 并完成一次分析。", "Warning");
            return;
        }

        try
        {
            var sessionId = _lastSamples
                .Select(sample => sample.TestSessionId)
                .FirstOrDefault(id => id != Guid.Empty);

            if (sessionId == Guid.Empty)
            {
                sessionId = Guid.NewGuid();
            }

            var samples = _lastSamples
                .Select(sample => sample with { TestSessionId = sessionId })
                .ToArray();

            var session = new TestSession(
                sessionId,
                Guid.Empty,
                string.IsNullOrWhiteSpace(_lastSourceFileName)
                    ? $"offline-session-{sessionId:N}"
                    : Path.GetFileNameWithoutExtension(_lastSourceFileName),
                _lastAnalysisWindow.Start,
                _lastAnalysisWindow.End,
                null,
                "Offline CSV analysis",
                $"Profile: {_fieldProfile.ProfileName}");

            await _testSessionRepository.SaveAsync(session, CancellationToken.None);
            await _pidSampleRepository.SaveBatchAsync(samples, CancellationToken.None);
            _lastTestSessionId = sessionId;
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
        if (_lastSamples.Count == 0)
        {
            Notify("无法保存参数方案", "请先导入 CSV、载入示例或打开历史记录。", "Warning");
            return;
        }

        var sourceName = string.IsNullOrWhiteSpace(_lastSourceFileName)
            ? "current-analysis"
            : Path.GetFileNameWithoutExtension(_lastSourceFileName);
        var parameterSet = _parameterSetExtractor.Extract(
            _lastSamples,
            _lastTestSessionId == Guid.Empty ? null : _lastTestSessionId,
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
            $"{parameterSet.Name}: Kp={FormatNullable(parameterSet.Kp, "0.###")}, Ki/Ti={FormatNullable(parameterSet.KiOrTi, "0.###")}, Kd/Td={FormatNullable(parameterSet.KdOrTd, "0.###")}",
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
                _lastTestSessionId,
                string.IsNullOrWhiteSpace(_lastSourceFileName)
                    ? "current-analysis"
                    : Path.GetFileNameWithoutExtension(_lastSourceFileName),
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

    private static string FormatDelta(double? value, string format)
    {
        if (!value.HasValue)
        {
            return "-";
        }

        var sign = value.Value > 0 ? "+" : string.Empty;
        return sign + value.Value.ToString(format, CultureInfo.InvariantCulture);
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
        if (_lastAnalysisWindow is null || _lastMetrics is null || _lastAssessment is null)
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
                _lastAnalysisWindow,
                _lastMetrics,
                _lastAssessment,
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
        ActiveAnalysisWindow =
            $"{window.Start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} - {window.End.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}";
        SampleCount = samples.Count.ToString(CultureInfo.InvariantCulture);
        OvershootPercent = FormatNullable(metrics.OvershootPercent, "0.### '%'");
        RiseTime = FormatNullable(metrics.RiseTime);
        SettlingTime = FormatNullable(metrics.SettlingTime);
        SteadyStateError = FormatNullable(metrics.SteadyStateError, "0.###");
        PeakProcessValue = FormatNullable(metrics.PeakProcessValue, "0.###");
        PeakTime = FormatNullable(metrics.PeakTime);
        MinimumProcessValue = FormatNullable(metrics.MinimumProcessValue, "0.###");
        MeanAbsoluteError = FormatNullable(metrics.MeanAbsoluteError, "0.###");
        MeanSquaredError = FormatNullable(metrics.MeanSquaredError, "0.###");
        IntegralAbsoluteError = FormatNullable(metrics.IntegralAbsoluteError, "0.###");
        OutputStandardDeviation = FormatNullable(metrics.OutputStandardDeviation, "0.###");
        ResponseFlags = FormatResponseFlags(metrics);
        var assessment = _assessmentService.Assess(metrics);
        AssessmentSummary = assessment.Summary;
        _lastAnalysisWindow = window;
        _lastMetrics = metrics;
        _lastAssessment = assessment;
        _lastSamples = samples;
        _lastSourceFileName = sourceName;
        _lastTestSessionId = samples.Select(sample => sample.TestSessionId).FirstOrDefault(id => id != Guid.Empty);
        UpdateTuningRecommendations(metrics);
        UpdateTrendPreview(samples);
    }

    private void UpdateTuningRecommendations(PidResponseMetrics metrics)
    {
        var recommendations = _recommendationService.Recommend(metrics);
        TuningRecommendations = new ObservableCollection<PidTuningRecommendationViewModel>(
            recommendations.Select(recommendation => new PidTuningRecommendationViewModel(recommendation)));
        RecommendationSummary = TuningRecommendations.Count == 0
            ? "当前没有可用建议。"
            : $"已生成 {TuningRecommendations.Count} 条保守调整建议，写回 PLC 前必须由工程师确认。";
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

    private static string FormatNullable(double? value, string format)
    {
        return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatNullable(TimeSpan? value)
    {
        return value.HasValue ? value.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatResponseFlags(PidResponseMetrics metrics)
    {
        var flags = new List<string>();
        if (metrics.HasSustainedOscillation == true)
        {
            flags.Add("振荡");
        }

        if (metrics.HasOutputSaturation == true)
        {
            flags.Add("输出饱和");
        }

        return flags.Count == 0 ? "正常" : string.Join(" / ", flags);
    }

    private void UpdateTrendPreview(IReadOnlyList<PidSample> samples)
    {
        var trend = _trendSeriesBuilder.Build(samples);
        SetPointPoints = ToPointCollection(trend.SetPoint);
        ProcessValuePoints = ToPointCollection(trend.ProcessValue);
        ManipulatedValuePoints = ToPointCollection(trend.ManipulatedValue);
    }

    private static PointCollection ToPointCollection(TrendSeries series)
    {
        const double width = 520;
        const double height = 160;

        var points = series.Points
            .Select(point => new System.Windows.Point(
                point.NormalizedX * width,
                height - point.NormalizedY * height));

        return new PointCollection(points);
    }

    private void RefreshFieldDefinitions()
    {
        FieldDefinitions = new ObservableCollection<PidSampleFieldDefinitionViewModel>(
            _fieldProfile.Fields.Select(field => new PidSampleFieldDefinitionViewModel(field)));
    }

    private void RefreshTagDefinitions()
    {
        TagDefinitions = new ObservableCollection<TagDefinitionViewModel>(
            _plcConfiguration.Tags.Select(tag => new TagDefinitionViewModel(tag)));
    }

    private void ApplyPlcConfiguration(PlcProjectConfiguration configuration)
    {
        PlcConfigurationName = configuration.Name;
        PlcProtocol = configuration.Protocol;
        PlcIpAddress = configuration.IpAddress;
        PlcRack = configuration.Rack;
        PlcSlot = configuration.Slot;
        PlcTimeoutMilliseconds = configuration.TimeoutMilliseconds;
        PlcDefaultSamplingMilliseconds = configuration.DefaultSamplingMilliseconds;
        PlcMinimumSamplingMilliseconds = ResolveMinimumSamplingMilliseconds(configuration);
        RefreshTagDefinitions();
        PlcMonitorTags.Clear();
        SelectedPlcMonitorTag = null;
        PlcMonitorStatus = "PLC 配置已更新，等待刷新点位。";
    }

    private PlcProjectConfiguration BuildPlcConfigurationFromForm()
    {
        var tags = TagDefinitions.Select(tag => tag.ToDefinition()).ToArray();
        var duplicateName = tags
            .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"点位名称重复：{duplicateName.Key}");
        }

        return new PlcProjectConfiguration(
            1,
            PlcConfigurationName.Trim(),
            PlcProtocol.Trim(),
            PlcIpAddress.Trim(),
            PlcRack,
            PlcSlot,
            PlcTimeoutMilliseconds,
            PlcDefaultSamplingMilliseconds,
            PlcMinimumSamplingMilliseconds,
            tags);
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

    private sealed record PlcOneSecondRecording(
        DateTimeOffset RecordedAt,
        string ConfigurationName,
        string Protocol,
        string IpAddress,
        int IntervalMilliseconds,
        int FrameCount,
        int SnapshotCount,
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> Frames);
}
