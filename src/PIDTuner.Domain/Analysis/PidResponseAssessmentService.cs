namespace PIDTuner.Domain.Analysis;

public sealed class PidResponseAssessmentService
{
    public PidResponseAssessment Assess(PidResponseMetrics metrics)
    {
        var findings = new List<string>();

        if (metrics.OvershootPercent is > 10)
        {
            findings.Add("超调偏高，可能需要降低比例增益或增强阻尼。");
        }

        if (metrics.SteadyStateError is > 1)
        {
            findings.Add("稳态误差偏大，可能需要检查积分作用或负载扰动。");
        }

        if (metrics.SettlingTime is { TotalSeconds: > 30 })
        {
            findings.Add("调节时间偏长，响应收敛较慢。");
        }

        if (findings.Count == 0)
        {
            return new PidResponseAssessment(
                PidResponseSeverity.Normal,
                "未发现明显异常，建议结合现场工况继续观察。",
                findings);
        }

        return new PidResponseAssessment(
            PidResponseSeverity.Warning,
            string.Join(" ", findings),
            findings);
    }
}
