using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Models;

namespace PIDTuner.Application.Interfaces;

public interface IPidAnalysisService
{
    PidResponseMetrics Analyze(IReadOnlyList<PidSample> samples, AnalysisWindow window);
}
