using System.IO;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Infrastructure.Csv;

namespace PIDTuner.Desktop.ViewModels;

public sealed class ExperimentSessionCoordinator(
    ITestSessionRepository testSessionRepository,
    IPidSampleRepository pidSampleRepository,
    IPidRecommendationReviewRepository recommendationReviewRepository,
    string testSessionStorageDirectory)
{
    public async Task<SaveTestSessionResult> SaveOfflineSessionAsync(
        AnalysisWindow? analysisWindow,
        IReadOnlyList<PidSample> sourceSamples,
        string sourceFileName,
        string fieldProfileName,
        CancellationToken cancellationToken)
    {
        if (analysisWindow is null || sourceSamples.Count == 0)
        {
            return SaveTestSessionResult.Warning("无法保存试验记录", "请先导入 CSV 并完成一次分析。");
        }

        var sessionId = sourceSamples
            .Select(sample => sample.TestSessionId)
            .FirstOrDefault(id => id != Guid.Empty);

        if (sessionId == Guid.Empty)
        {
            sessionId = Guid.NewGuid();
        }

        var samples = sourceSamples
            .Select(sample => sample with { TestSessionId = sessionId })
            .ToArray();

        var session = new TestSession(
            sessionId,
            Guid.Empty,
            string.IsNullOrWhiteSpace(sourceFileName)
                ? $"offline-session-{sessionId:N}"
                : Path.GetFileNameWithoutExtension(sourceFileName),
            analysisWindow.Start,
            analysisWindow.End,
            null,
            "Offline CSV analysis",
            $"Profile: {fieldProfileName}");

        await testSessionRepository.SaveAsync(session, cancellationToken);
        await pidSampleRepository.SaveBatchAsync(samples, cancellationToken);

        return SaveTestSessionResult.Success(
            sessionId,
            "试验记录已保存",
            string.Join(
                Environment.NewLine,
                $"{session.Name}，样本 {samples.Length} 条。",
                $"目录：{testSessionStorageDirectory}",
                $"索引：{Path.Combine(testSessionStorageDirectory, "test-sessions.json")}",
                $"样本：{Path.Combine(testSessionStorageDirectory, $"{sessionId:D}.samples.json")}"));
    }

    public async Task<IReadOnlyList<TestSessionListItemViewModel>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var sessions = await testSessionRepository.ListAsync(cancellationToken);
        var items = new List<TestSessionListItemViewModel>();

        foreach (var session in sessions.OrderByDescending(session => session.StartedAt))
        {
            var samples = await pidSampleRepository.GetBySessionAsync(session.Id, cancellationToken);
            items.Add(new TestSessionListItemViewModel(session, samples.Count));
        }

        return items;
    }

    public Task<IReadOnlyList<PidSample>> LoadSessionSamplesAsync(
        TestSessionListItemViewModel session,
        CancellationToken cancellationToken)
    {
        return pidSampleRepository.GetBySessionAsync(session.Id, cancellationToken);
    }

    public async Task<HistoryExportResult> ExportHistorySamplesAsync(
        TestSessionListItemViewModel session,
        PidSampleFieldProfile fieldProfile,
        string fileName,
        CancellationToken cancellationToken)
    {
        var samples = await pidSampleRepository.GetBySessionAsync(session.Id, cancellationToken);
        if (samples.Count == 0)
        {
            return HistoryExportResult.Warning("历史采样导出失败", "该试验记录没有可导出的采样数据。");
        }

        await using var stream = File.Create(fileName);
        var exchange = new ConfigurablePidSampleCsvExchange(fieldProfile);
        await exchange.ExportAsync(samples, stream, cancellationToken);
        return HistoryExportResult.Success("历史采样已导出", Path.GetFullPath(fileName));
    }

    public async Task<IReadOnlyList<PidRecommendationReviewViewModel>> LoadRecommendationReviewsAsync(
        CancellationToken cancellationToken)
    {
        var reviews = await recommendationReviewRepository.ListAsync(cancellationToken);
        return reviews
            .OrderByDescending(review => review.CreatedAt)
            .Select(review => new PidRecommendationReviewViewModel(review))
            .ToArray();
    }

    public async Task<PidRecommendationReview> SaveRecommendationReviewAsync(
        PidTuningRecommendationViewModel recommendation,
        Guid? testSessionId,
        string sourceFileName,
        PidRecommendationReviewDecision decision,
        string note,
        CancellationToken cancellationToken)
    {
        var review = new PidRecommendationReview(
            Guid.NewGuid(),
            testSessionId,
            string.IsNullOrWhiteSpace(sourceFileName)
                ? "current-analysis"
                : Path.GetFileNameWithoutExtension(sourceFileName),
            recommendation.Recommendation.Parameter,
            recommendation.Recommendation.Direction,
            recommendation.Recommendation.Adjustment,
            decision,
            note.Trim(),
            DateTimeOffset.Now);

        await recommendationReviewRepository.SaveAsync(review, cancellationToken);
        return review;
    }
}

public sealed record SaveTestSessionResult(Guid? SessionId, string Title, string Message, string Kind)
{
    public static SaveTestSessionResult Success(Guid sessionId, string title, string message)
    {
        return new SaveTestSessionResult(sessionId, title, message, "Success");
    }

    public static SaveTestSessionResult Warning(string title, string message)
    {
        return new SaveTestSessionResult(null, title, message, "Warning");
    }
}

public sealed record HistoryExportResult(string Title, string Message, string Kind)
{
    public static HistoryExportResult Success(string title, string message)
    {
        return new HistoryExportResult(title, message, "Success");
    }

    public static HistoryExportResult Warning(string title, string message)
    {
        return new HistoryExportResult(title, message, "Warning");
    }
}
