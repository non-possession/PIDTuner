using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Desktop.ViewModels;

public sealed class ExperimentHistoryViewModel : INotifyPropertyChanged
{
    private IReadOnlyList<TestSessionListItemViewModel> _allHistorySessions = Array.Empty<TestSessionListItemViewModel>();
    private string _historyStatus = "尚未加载历史记录。";
    private string _historySearchText = string.Empty;
    private string _selectedHistoryDetails = "请选择一条历史记录。";
    private string _historyComparisonStatus = "尚未设置历史对比基准。";
    private string _recommendationReviewNote = string.Empty;
    private string _recommendationReviewStatus = "尚未记录建议审查。";
    private ObservableCollection<TestSessionListItemViewModel> _historySessions = [];
    private ObservableCollection<PidRecommendationReviewViewModel> _recommendationReviews = [];
    private ObservableCollection<HistoryComparisonMetricViewModel> _historyComparisonMetrics = [];
    private TestSessionListItemViewModel? _selectedHistorySession;
    private TestSessionListItemViewModel? _baselineHistorySession;

    public event PropertyChangedEventHandler? PropertyChanged;

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
                UpdateHistoryStatus();
            }
        }
    }

    public string SelectedHistoryDetails
    {
        get => _selectedHistoryDetails;
        private set => SetProperty(ref _selectedHistoryDetails, value);
    }

    public ObservableCollection<TestSessionListItemViewModel> HistorySessions
    {
        get => _historySessions;
        private set => SetProperty(ref _historySessions, value);
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

    public TestSessionListItemViewModel? BaselineHistorySession => _baselineHistorySession;

    public ObservableCollection<HistoryComparisonMetricViewModel> HistoryComparisonMetrics
    {
        get => _historyComparisonMetrics;
        private set => SetProperty(ref _historyComparisonMetrics, value);
    }

    public string HistoryComparisonStatus
    {
        get => _historyComparisonStatus;
        private set => SetProperty(ref _historyComparisonStatus, value);
    }

    public ObservableCollection<PidRecommendationReviewViewModel> RecommendationReviews
    {
        get => _recommendationReviews;
        private set => SetProperty(ref _recommendationReviews, value);
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

    public void SetHistorySessions(IReadOnlyList<TestSessionListItemViewModel> sessions)
    {
        _allHistorySessions = sessions;
        ApplyHistoryFilter();
        UpdateHistoryStatus();
    }

    public void MarkHistoryLoadFailed()
    {
        HistoryStatus = "历史记录加载失败。";
    }

    public bool SetBaselineToSelected()
    {
        if (SelectedHistorySession is null)
        {
            return false;
        }

        _baselineHistorySession = SelectedHistorySession;
        OnPropertyChanged(nameof(BaselineHistorySession));
        HistoryComparisonMetrics.Clear();
        HistoryComparisonStatus = $"基准：{_baselineHistorySession.Name}";
        return true;
    }

    public void SetComparisonResult(
        PidResponseMetrics baseline,
        PidResponseMetrics candidate,
        string baselineName,
        string candidateName)
    {
        HistoryComparisonMetrics = BuildHistoryComparisonMetrics(baseline, candidate);
        HistoryComparisonStatus = $"基准：{baselineName}；对比：{candidateName}";
    }

    public void SetRecommendationReviews(IEnumerable<PidRecommendationReviewViewModel> reviews)
    {
        RecommendationReviews = new ObservableCollection<PidRecommendationReviewViewModel>(reviews);
        RecommendationReviewStatus = RecommendationReviews.Count == 0
            ? "尚无建议审查记录。"
            : $"已加载 {RecommendationReviews.Count} 条建议审查记录。";
    }

    public void ClearRecommendationReviewNote()
    {
        RecommendationReviewNote = string.Empty;
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

    private void UpdateHistoryStatus()
    {
        HistoryStatus = HistorySessions.Count == 0
            ? "尚无已保存试验记录。"
            : $"已加载 {HistorySessions.Count} 条试验记录。";
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

    private static ObservableCollection<HistoryComparisonMetricViewModel> BuildHistoryComparisonMetrics(
        PidResponseMetrics baseline,
        PidResponseMetrics candidate)
    {
        return
        [
            Compare("超调量", baseline.OvershootPercent, candidate.OvershootPercent, "0.###"),
            Compare("上升时间", baseline.RiseTime?.TotalSeconds, candidate.RiseTime?.TotalSeconds, "0.### s"),
            Compare("调节时间", baseline.SettlingTime?.TotalSeconds, candidate.SettlingTime?.TotalSeconds, "0.### s"),
            Compare("稳态误差", baseline.SteadyStateError, candidate.SteadyStateError, "0.###"),
            Compare("峰值", baseline.PeakProcessValue, candidate.PeakProcessValue, "0.###"),
            Compare("平均绝对误差", baseline.MeanAbsoluteError, candidate.MeanAbsoluteError, "0.###"),
            Compare("误差积分", baseline.IntegralAbsoluteError, candidate.IntegralAbsoluteError, "0.###"),
            Compare("输出标准差", baseline.OutputStandardDeviation, candidate.OutputStandardDeviation, "0.###")
        ];
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
