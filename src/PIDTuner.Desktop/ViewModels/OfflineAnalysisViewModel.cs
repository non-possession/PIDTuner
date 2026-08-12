using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using PIDTuner.Application.Services;
using PIDTuner.Application.UseCases;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Trends;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Csv;

namespace PIDTuner.Desktop.ViewModels;

public sealed class OfflineAnalysisViewModel : INotifyPropertyChanged
{
    private readonly PidResponseAssessmentService _assessmentService = new();
    private readonly PidTuningRecommendationService _recommendationService = new();
    private readonly PidTrendSeriesBuilder _trendSeriesBuilder = new();
    private readonly BasicPidAnalysisService _pidAnalysisService = new();
    private readonly AnalysisWindowParser _analysisWindowParser = new();
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
    private string _activeAnalysisWindow = "-";
    private string _assessmentSummary = "-";
    private string _recommendationSummary = "完成一次分析后生成参数调整建议。";
    private string _analysisStartText = string.Empty;
    private string _analysisEndText = string.Empty;
    private ObservableCollection<PidTuningRecommendationViewModel> _tuningRecommendations = [];
    private PointCollection _setPointPoints = new();
    private PointCollection _processValuePoints = new();
    private PointCollection _manipulatedValuePoints = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public AnalysisWindow? LastAnalysisWindow { get; private set; }

    public PidResponseMetrics? LastMetrics { get; private set; }

    public PidResponseAssessment? LastAssessment { get; private set; }

    public IReadOnlyList<PidSample> LastSamples { get; private set; } = Array.Empty<PidSample>();

    public Guid? LastTestSessionId { get; private set; }

    public string LastSourceFileName { get; private set; } = string.Empty;

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

    public ObservableCollection<PidTuningRecommendationViewModel> TuningRecommendations
    {
        get => _tuningRecommendations;
        private set => SetProperty(ref _tuningRecommendations, value);
    }

    public string RecommendationSummary
    {
        get => _recommendationSummary;
        private set => SetProperty(ref _recommendationSummary, value);
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

    public async Task<OfflineCsvAnalysisResult> AnalyzeCsvFileAsync(
        string fileName,
        PidSampleFieldProfile fieldProfile,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fileName);
        var exchange = new ConfigurablePidSampleCsvExchange(fieldProfile);
        var useCase = new AnalyzeOfflineCsvUseCase(exchange, _pidAnalysisService);
        var window = _analysisWindowParser.Parse(AnalysisStartText, AnalysisEndText);
        var result = await useCase.AnalyzeAsync(stream, window, cancellationToken);

        ApplyResult(fileName, result.Samples, result.Window, result.Metrics);

        if (window is null)
        {
            AnalysisStartText = result.Window.Start.ToString("O", CultureInfo.InvariantCulture);
            AnalysisEndText = result.Window.End.ToString("O", CultureInfo.InvariantCulture);
        }

        return new OfflineCsvAnalysisResult(Path.GetFileName(fileName), result.Samples.Count);
    }

    public PidResponseMetrics AnalyzeSamples(
        IReadOnlyList<PidSample> samples,
        AnalysisWindow window)
    {
        return _pidAnalysisService.Analyze(samples, window);
    }

    public void ApplyResult(
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
        LastAnalysisWindow = window;
        LastMetrics = metrics;
        LastAssessment = assessment;
        LastSamples = samples;
        LastSourceFileName = sourceName;
        LastTestSessionId = samples.Select(sample => sample.TestSessionId).FirstOrDefault(id => id != Guid.Empty);
        UpdateTuningRecommendations(metrics);
        UpdateTrendPreview(samples);
    }

    public void MarkSavedSession(Guid sessionId)
    {
        LastTestSessionId = sessionId;
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

public sealed record OfflineCsvAnalysisResult(
    string SourceFileName,
    int SampleCount);
