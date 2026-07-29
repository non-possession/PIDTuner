using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.UseCases;
using PIDTuner.Desktop.Commands;
using PIDTuner.Desktop.Services;
using PIDTuner.Domain.Configuration;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Csv;

namespace PIDTuner.Desktop.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IOpenFileDialogService _openFileDialogService;
    private readonly IPidSampleFieldProfileStore _fieldProfileStore;
    private readonly BasicPidAnalysisService _pidAnalysisService = new();
    private PidSampleFieldProfile _fieldProfile = PidSampleFieldProfile.CreateDefault();
    private string _statusMessage = "阶段 1 已就绪：可在分析页导入离线 CSV 并计算基础指标。";
    private string _currentFieldProfile = "default-pid-sample-fields (10 字段)";
    private string _sampleCount = "-";
    private string _overshootPercent = "-";
    private string _riseTime = "-";
    private string _settlingTime = "-";
    private string _steadyStateError = "-";

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
        ImportCsvCommand = new AsyncCommand(ImportCsvAsync);
        LoadFieldProfileCommand = new AsyncCommand(LoadFieldProfileAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; } = "PIDTuner";

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

    public ICommand ImportCsvCommand { get; }

    public ICommand LoadFieldProfileCommand { get; }

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
            await using var stream = File.OpenRead(fileName);
            var exchange = new ConfigurablePidSampleCsvExchange(_fieldProfile);
            var useCase = new AnalyzeOfflineCsvUseCase(exchange, _pidAnalysisService);
            var result = await useCase.AnalyzeAsync(stream, null, CancellationToken.None);

            SampleCount = result.Samples.Count.ToString(CultureInfo.InvariantCulture);
            OvershootPercent = FormatNullable(result.Metrics.OvershootPercent, "0.### '%'");
            RiseTime = FormatNullable(result.Metrics.RiseTime);
            SettlingTime = FormatNullable(result.Metrics.SettlingTime);
            SteadyStateError = FormatNullable(result.Metrics.SteadyStateError, "0.###");
            StatusMessage = $"已完成离线分析：{Path.GetFileName(fileName)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"离线分析失败：{exception.Message}";
        }
    }

    private static string FormatNullable(double? value, string format)
    {
        return value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "-";
    }

    private static string FormatNullable(TimeSpan? value)
    {
        return value.HasValue ? value.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "-";
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
