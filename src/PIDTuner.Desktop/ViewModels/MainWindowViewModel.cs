using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.Services;
using PIDTuner.Application.UseCases;
using PIDTuner.Desktop.Commands;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Trends;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Csv;
using PIDTuner.Infrastructure.Persistence;

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
    private readonly string _testSessionStorageDirectory;
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
    private string _plcConfigurationStatus = "PLC 配置尚未保存。";
    private string _sampleCount = "-";
    private string _overshootPercent = "-";
    private string _riseTime = "-";
    private string _settlingTime = "-";
    private string _steadyStateError = "-";
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
    private IReadOnlyList<TestSessionListItemViewModel> _allHistorySessions = Array.Empty<TestSessionListItemViewModel>();
    private PidSampleFieldDefinitionViewModel? _selectedFieldDefinition;
    private TagDefinitionViewModel? _selectedTagDefinition;
    private TestSessionListItemViewModel? _selectedHistorySession;
    private PidTuningRecommendationViewModel? _selectedTuningRecommendation;
    private string _recommendationSummary = "完成一次分析后生成参数调整建议。";
    private string _recommendationReviewNote = string.Empty;
    private string _recommendationReviewStatus = "尚未记录建议审查。";

    public MainWindowViewModel()
        : this(
            new WindowsOpenFileDialogService(),
            new JsonPidSampleFieldProfileStore(),
            new JsonPlcProjectConfigurationStore(),
            new JsonTestSessionRepository(Path.Combine(FindRepositoryRoot(), "local", "test-sessions")),
            new JsonPidSampleRepository(Path.Combine(FindRepositoryRoot(), "local", "test-sessions")),
            new JsonPidRecommendationReviewRepository(Path.Combine(FindRepositoryRoot(), "local", "recommendation-reviews")))
    {
    }

    public MainWindowViewModel(
        IOpenFileDialogService openFileDialogService,
        IPidSampleFieldProfileStore fieldProfileStore,
        IPlcProjectConfigurationStore plcProjectConfigurationStore,
        ITestSessionRepository? testSessionRepository = null,
        IPidSampleRepository? pidSampleRepository = null,
        IPidRecommendationReviewRepository? recommendationReviewRepository = null,
        string? testSessionStorageDirectory = null)
    {
        _openFileDialogService = openFileDialogService;
        _fieldProfileStore = fieldProfileStore;
        _plcProjectConfigurationStore = plcProjectConfigurationStore;
        _testSessionStorageDirectory = Path.GetFullPath(
            testSessionStorageDirectory ?? Path.Combine(FindRepositoryRoot(), "local", "test-sessions"));
        _testSessionRepository = testSessionRepository ?? new JsonTestSessionRepository(_testSessionStorageDirectory);
        _pidSampleRepository = pidSampleRepository ?? new JsonPidSampleRepository(_testSessionStorageDirectory);
        _recommendationReviewRepository = recommendationReviewRepository
            ?? new JsonPidRecommendationReviewRepository(Path.Combine(FindRepositoryRoot(), "local", "recommendation-reviews"));
        RefreshFieldDefinitions();
        RefreshTagDefinitions();
        ImportCsvCommand = new AsyncCommand(ImportCsvAsync);
        LoadPlcConfigurationCommand = new AsyncCommand(LoadPlcConfigurationAsync);
        SavePlcConfigurationCommand = new AsyncCommand(SavePlcConfigurationAsync);
        AddTagCommand = new AsyncCommand(AddTagAsync);
        RemoveTagCommand = new AsyncCommand(RemoveTagAsync);
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
        AcceptRecommendationCommand = new AsyncCommand(AcceptRecommendationAsync);
        DeferRecommendationCommand = new AsyncCommand(DeferRecommendationAsync);
        LoadRecommendationReviewsCommand = new AsyncCommand(LoadRecommendationReviewsAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public string PlcConfigurationStatus
    {
        get => _plcConfigurationStatus;
        private set => SetProperty(ref _plcConfigurationStatus, value);
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

    public ICommand ImportCsvCommand { get; }

    public ICommand LoadPlcConfigurationCommand { get; }

    public ICommand SavePlcConfigurationCommand { get; }

    public ICommand AddTagCommand { get; }

    public ICommand RemoveTagCommand { get; }

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

    public ICommand AcceptRecommendationCommand { get; }

    public ICommand DeferRecommendationCommand { get; }

    public ICommand LoadRecommendationReviewsCommand { get; }

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
        var tag = TagDefinitionViewModel.CreateNew(TagDefinitions.Count + 1);
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
        RefreshTagDefinitions();
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
}
