using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;

namespace PIDTuner.Desktop.ViewModels;

public sealed class ExperimentWorkspaceViewModel(
    ExperimentSessionCoordinator coordinator,
    OfflineAnalysisViewModel analysis,
    ExperimentHistoryViewModel history)
{
    public ExperimentHistoryViewModel History { get; } = history;

    public async Task<ExperimentWorkspaceOperationResult> SaveSessionAsync(
        string fieldProfileName,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await coordinator.SaveOfflineSessionAsync(
                analysis.LastAnalysisWindow,
                analysis.LastSamples,
                analysis.LastSourceFileName,
                fieldProfileName,
                cancellationToken);
            if (result.SessionId.HasValue)
            {
                analysis.MarkSavedSession(result.SessionId.Value);
                await LoadHistoryCoreAsync(cancellationToken);
            }

            return new(result.Title, result.Message, result.Kind);
        }
        catch (Exception exception)
        {
            return ExperimentWorkspaceOperationResult.Error("实验记录保存失败", exception.Message);
        }
    }

    public async Task<ExperimentWorkspaceOperationResult?> LoadHistoryAsync(
        bool notifySuccess,
        CancellationToken cancellationToken)
    {
        try
        {
            await LoadHistoryCoreAsync(cancellationToken);
            return notifySuccess
                ? ExperimentWorkspaceOperationResult.Info("历史记录已刷新", History.HistoryStatus)
                : null;
        }
        catch (Exception exception)
        {
            History.MarkHistoryLoadFailed();
            return ExperimentWorkspaceOperationResult.Error("历史记录加载失败", exception.Message);
        }
    }

    public async Task<ExperimentWorkspaceOperationResult> OpenSelectedSessionAsync(
        CancellationToken cancellationToken)
    {
        var selected = History.SelectedHistorySession;
        if (selected is null)
        {
            return ExperimentWorkspaceOperationResult.Warning("无法打开历史记录", "请先选择一条历史记录。");
        }

        try
        {
            var samples = await coordinator.LoadSessionSamplesAsync(selected, cancellationToken);
            if (samples.Count == 0)
            {
                return ExperimentWorkspaceOperationResult.Warning("历史记录内容为空", "这条记录没有可加载的采样数据。");
            }

            var window = FullWindow(samples);
            analysis.ApplyResult(selected.Name, samples, window, analysis.AnalyzeSamples(samples, window));
            return ExperimentWorkspaceOperationResult.Success(
                "历史记录已打开",
                $"{selected.Name}，加载 {samples.Count} 条采样");
        }
        catch (Exception exception)
        {
            return ExperimentWorkspaceOperationResult.Error("历史记录打开失败", exception.Message);
        }
    }

    public async Task<ExperimentWorkspaceOperationResult> ExportSelectedSamplesAsync(
        PidSampleFieldProfile fieldProfile,
        string fileName,
        CancellationToken cancellationToken)
    {
        var selected = History.SelectedHistorySession;
        if (selected is null)
        {
            return ExperimentWorkspaceOperationResult.Warning("无法导出历史数据", "请先选择一条历史记录。");
        }

        try
        {
            var result = await coordinator.ExportHistorySamplesAsync(
                selected,
                fieldProfile,
                fileName,
                cancellationToken);
            return new(result.Title, result.Message, result.Kind);
        }
        catch (Exception exception)
        {
            return ExperimentWorkspaceOperationResult.Error("历史数据导出失败", exception.Message);
        }
    }

    public ExperimentWorkspaceOperationResult SetSelectedAsBaseline()
    {
        return History.SetBaselineToSelected()
            ? ExperimentWorkspaceOperationResult.Info("历史对比基准已设置", History.HistoryComparisonStatus)
            : ExperimentWorkspaceOperationResult.Warning("无法设置对比基准", "请先选择一条历史记录。");
    }

    public async Task<ExperimentWorkspaceOperationResult> CompareSelectedSessionAsync(
        CancellationToken cancellationToken)
    {
        var baseline = History.BaselineHistorySession;
        var selected = History.SelectedHistorySession;
        if (baseline is null)
        {
            return ExperimentWorkspaceOperationResult.Warning("无法对比历史记录", "请先选择一条记录并设为基准。");
        }

        if (selected is null)
        {
            return ExperimentWorkspaceOperationResult.Warning("无法对比历史记录", "请先选择要对比的历史记录。");
        }

        if (selected.Id == baseline.Id)
        {
            return ExperimentWorkspaceOperationResult.Warning("无法对比历史记录", "当前选择与基准是同一条记录。");
        }

        try
        {
            var baselineMetrics = await AnalyzeSessionAsync(baseline, cancellationToken);
            var selectedMetrics = await AnalyzeSessionAsync(selected, cancellationToken);
            History.SetComparisonResult(baselineMetrics, selectedMetrics, baseline.Name, selected.Name);
            return ExperimentWorkspaceOperationResult.Success("历史记录对比完成", History.HistoryComparisonStatus);
        }
        catch (Exception exception)
        {
            return ExperimentWorkspaceOperationResult.Error("历史记录对比失败", exception.Message);
        }
    }

    public async Task<ExperimentWorkspaceOperationResult?> LoadRecommendationReviewsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            History.SetRecommendationReviews(
                await coordinator.LoadRecommendationReviewsAsync(cancellationToken));
            return null;
        }
        catch (Exception exception)
        {
            return ExperimentWorkspaceOperationResult.Error("建议审核记录加载失败", exception.Message);
        }
    }

    public async Task<ExperimentWorkspaceOperationResult> ReviewSelectedRecommendationAsync(
        PidRecommendationReviewDecision decision,
        CancellationToken cancellationToken)
    {
        var selected = analysis.SelectedTuningRecommendation;
        if (selected is null)
        {
            return ExperimentWorkspaceOperationResult.Warning("无法记录建议审核", "请先选择一条参数调整建议。");
        }

        try
        {
            var review = await coordinator.SaveRecommendationReviewAsync(
                selected,
                analysis.LastTestSessionId,
                analysis.LastSourceFileName,
                decision,
                History.RecommendationReviewNote,
                cancellationToken);
            History.ClearRecommendationReviewNote();
            await LoadRecommendationReviewsAsync(cancellationToken);
            var decisionText = decision == PidRecommendationReviewDecision.Accepted ? "接受" : "暂缓";
            return ExperimentWorkspaceOperationResult.Success(
                "建议审核已记录",
                $"{decisionText}：{review.Parameter} {review.Adjustment}");
        }
        catch (Exception exception)
        {
            return ExperimentWorkspaceOperationResult.Error("建议审核记录失败", exception.Message);
        }
    }

    private async Task LoadHistoryCoreAsync(CancellationToken cancellationToken) =>
        History.SetHistorySessions(await coordinator.LoadHistoryAsync(cancellationToken));

    private async Task<PidResponseMetrics> AnalyzeSessionAsync(
        TestSessionListItemViewModel session,
        CancellationToken cancellationToken)
    {
        var samples = await coordinator.LoadSessionSamplesAsync(session, cancellationToken);
        if (samples.Count == 0)
        {
            throw new InvalidOperationException($"{session.Name} 没有可对比的采样数据。");
        }

        var window = FullWindow(samples);
        return analysis.AnalyzeSamples(samples, window);
    }

    private static AnalysisWindow FullWindow(IReadOnlyList<PidSample> samples) =>
        new(samples.Min(sample => sample.Timestamp), samples.Max(sample => sample.Timestamp));
}

public sealed record ExperimentWorkspaceOperationResult(string Title, string Message, string Kind)
{
    public static ExperimentWorkspaceOperationResult Success(string title, string message) =>
        new(title, message, "Success");

    public static ExperimentWorkspaceOperationResult Info(string title, string message) =>
        new(title, message, "Info");

    public static ExperimentWorkspaceOperationResult Warning(string title, string message) =>
        new(title, message, "Warning");

    public static ExperimentWorkspaceOperationResult Error(string title, string message) =>
        new(title, message, "Error");
}
