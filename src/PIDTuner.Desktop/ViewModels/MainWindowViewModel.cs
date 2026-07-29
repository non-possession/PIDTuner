using System.ComponentModel;
using System.Collections.ObjectModel;
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
using PIDTuner.Domain.Trends;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Csv;

namespace PIDTuner.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IOpenFileDialogService _openFileDialogService;
    private readonly IPidSampleFieldProfileStore _fieldProfileStore;
    private readonly BasicPidAnalysisService _pidAnalysisService = new();
    private readonly PidResponseAssessmentService _assessmentService = new();
    private readonly PidAnalysisResultCsvExporter _analysisResultExporter = new();
    private readonly PidTrendSeriesBuilder _trendSeriesBuilder = new();
    private readonly AnalysisWindowParser _analysisWindowParser = new();
    private PidSampleFieldProfile _fieldProfile = PidSampleFieldProfile.CreateDefault();
    private AnalysisWindow? _lastAnalysisWindow;
    private PidResponseMetrics? _lastMetrics;
    private PidResponseAssessment? _lastAssessment;
    private string _statusMessage = "阶段 1 已就绪：可在分析页导入离线 CSV 并计算基础指标。";
    private string _currentFieldProfile = "default-pid-sample-fields (10 字段)";
    private string _sampleCount = "-";
    private string _overshootPercent = "-";
    private string _riseTime = "-";
    private string _settlingTime = "-";
    private string _steadyStateError = "-";
    private string _analysisStartText = string.Empty;
    private string _analysisEndText = string.Empty;
    private string _activeAnalysisWindow = "-";
    private string _assessmentSummary = "-";
    private PointCollection _setPointPoints = new();
    private PointCollection _processValuePoints = new();
    private PointCollection _manipulatedValuePoints = new();
    private ObservableCollection<PidSampleFieldDefinitionViewModel> _fieldDefinitions = [];
    private PidSampleFieldDefinitionViewModel? _selectedFieldDefinition;

    public MainWindowViewModel()
        : this(
            new WindowsOpenFileDialogService(),
            new JsonPidSampleFieldProfileStore())
    {
    }

    public MainWindowViewModel(
        IOpenFileDialogService openFileDialogService,
        IPidSampleFieldProfileStore fieldProfileStore)
    {
        _openFileDialogService = openFileDialogService;
        _fieldProfileStore = fieldProfileStore;
        RefreshFieldDefinitions();
        ImportCsvCommand = new AsyncCommand(ImportCsvAsync);
        LoadFieldProfileCommand = new AsyncCommand(LoadFieldProfileAsync);
        AddFieldCommand = new AsyncCommand(AddFieldAsync);
        RemoveFieldCommand = new AsyncCommand(RemoveFieldAsync);
        SaveFieldProfileCommand = new AsyncCommand(SaveFieldProfileAsync);
        ExportAnalysisResultCommand = new AsyncCommand(ExportAnalysisResultAsync);
        LoadExampleCommand = new AsyncCommand(LoadExampleAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; } = "PIDTuner";

    public IReadOnlyList<string> AvailableFieldDataTypes { get; } =
        Enum.GetNames<PidSampleFieldDataType>();

    public IReadOnlyList<string> AvailableFieldRoles { get; } =
        Enum.GetNames<PidSampleFieldRole>();

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

    public PidSampleFieldDefinitionViewModel? SelectedFieldDefinition
    {
        get => _selectedFieldDefinition;
        set => SetProperty(ref _selectedFieldDefinition, value);
    }

    public ICommand ImportCsvCommand { get; }

    public ICommand LoadFieldProfileCommand { get; }

    public ICommand AddFieldCommand { get; }

    public ICommand RemoveFieldCommand { get; }

    public ICommand SaveFieldProfileCommand { get; }

    public ICommand ExportAnalysisResultCommand { get; }

    public ICommand LoadExampleCommand { get; }

    public async Task LoadExampleAsync()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fieldProfilePath = Path.Combine(repositoryRoot, "config", "pid-sample-fields.example.json");
        var csvPath = Path.Combine(repositoryRoot, "samples", "offline-step-response.csv");

        if (!File.Exists(fieldProfilePath) || !File.Exists(csvPath))
        {
            StatusMessage = "示例文件不存在，请确认从仓库根目录运行程序。";
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
            StatusMessage = $"示例加载失败：{exception.Message}";
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

    private async Task ExportAnalysisResultAsync()
    {
        if (_lastAnalysisWindow is null || _lastMetrics is null || _lastAssessment is null)
        {
            StatusMessage = "请先导入 CSV 并完成一次分析。";
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
            StatusMessage = $"已导出分析结果：{Path.GetFileName(fileName)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"分析结果导出失败：{exception.Message}";
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
        StatusMessage = "已新增字段，请编辑后保存字段配置。";
        return Task.CompletedTask;
    }

    private Task RemoveFieldAsync()
    {
        if (SelectedFieldDefinition is null)
        {
            StatusMessage = "请先选择要删除的字段。";
            return Task.CompletedTask;
        }

        FieldDefinitions.Remove(SelectedFieldDefinition);
        SelectedFieldDefinition = null;
        StatusMessage = "已删除字段，请保存字段配置。";
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
            StatusMessage = $"已保存字段配置：{Path.GetFileName(fileName)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"字段配置保存失败：{exception.Message}";
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
            StatusMessage = $"已加载字段配置：{Path.GetFileName(fileName)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"字段配置加载失败：{exception.Message}";
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
            StatusMessage = $"离线分析失败：{exception.Message}";
        }
    }

    private async Task AnalyzeCsvFileAsync(string fileName)
    {
        await using var stream = File.OpenRead(fileName);
        var exchange = new ConfigurablePidSampleCsvExchange(_fieldProfile);
        var useCase = new AnalyzeOfflineCsvUseCase(exchange, _pidAnalysisService);
        var window = _analysisWindowParser.Parse(AnalysisStartText, AnalysisEndText);
        var result = await useCase.AnalyzeAsync(stream, window, CancellationToken.None);

        if (window is null)
        {
            AnalysisStartText = result.Window.Start.ToString("O", CultureInfo.InvariantCulture);
            AnalysisEndText = result.Window.End.ToString("O", CultureInfo.InvariantCulture);
        }

        ActiveAnalysisWindow =
            $"{result.Window.Start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} - {result.Window.End.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}";
        SampleCount = result.Samples.Count.ToString(CultureInfo.InvariantCulture);
        OvershootPercent = FormatNullable(result.Metrics.OvershootPercent, "0.### '%'");
        RiseTime = FormatNullable(result.Metrics.RiseTime);
        SettlingTime = FormatNullable(result.Metrics.SettlingTime);
        SteadyStateError = FormatNullable(result.Metrics.SteadyStateError, "0.###");
        var assessment = _assessmentService.Assess(result.Metrics);
        AssessmentSummary = assessment.Summary;
        _lastAnalysisWindow = result.Window;
        _lastMetrics = result.Metrics;
        _lastAssessment = assessment;
        UpdateTrendPreview(result.Samples);
        StatusMessage = $"已完成离线分析：{Path.GetFileName(fileName)}";
    }

    private static string FormatNullable(double? value, string format)
    {
        return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatNullable(TimeSpan? value)
    {
        return value.HasValue ? value.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "-";
    }

    private void UpdateTrendPreview(IReadOnlyList<Domain.Models.PidSample> samples)
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

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
