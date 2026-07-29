using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Models;

namespace PIDTuner.Application.DTOs;

public sealed record OfflineAnalysisResult(
    IReadOnlyList<PidSample> Samples,
    AnalysisWindow Window,
    PidResponseMetrics Metrics);
