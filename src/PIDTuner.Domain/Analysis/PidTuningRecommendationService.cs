namespace PIDTuner.Domain.Analysis;

public sealed class PidTuningRecommendationService
{
    public IReadOnlyList<PidTuningRecommendation> Recommend(PidResponseMetrics metrics)
    {
        var recommendations = new List<PidTuningRecommendation>();

        if (metrics.OvershootPercent is > 10)
        {
            recommendations.Add(new PidTuningRecommendation(
                "Kp",
                PidTuningAdjustmentDirection.Decrease,
                "建议降低 5% - 10%",
                "超调量偏高，比例作用可能偏强。",
                "降低超调和冲击，改善响应阻尼。",
                "可能使上升时间变长，需要小步验证。",
                0.72));
            recommendations.Add(new PidTuningRecommendation(
                "Kd/Td",
                PidTuningAdjustmentDirection.Increase,
                "建议小幅增加微分作用",
                "超调偏高时，适度微分可增加阻尼。",
                "抑制峰值并改善收敛。",
                "微分会放大测量噪声，现场信号抖动明显时应谨慎。",
                0.58));
        }

        if (metrics.SteadyStateError is > 1)
        {
            recommendations.Add(new PidTuningRecommendation(
                "Ki/Ti",
                PidTuningAdjustmentDirection.Increase,
                "建议增强积分作用",
                "稳态误差偏大，积分作用可能不足或负载扰动较强。",
                "减少稳态偏差。",
                "积分过强可能引入振荡或积分饱和。",
                0.66));
        }

        if (metrics.SettlingTime is { TotalSeconds: > 30 })
        {
            recommendations.Add(new PidTuningRecommendation(
                "Loop",
                PidTuningAdjustmentDirection.Inspect,
                "建议检查执行器饱和、滤波和负载扰动",
                "调节时间偏长，可能不只是单个 PID 参数问题。",
                "先排除机械、执行器和测量链路限制。",
                "未排除现场约束前直接加大参数可能放大振荡。",
                0.62));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new PidTuningRecommendation(
                "PID",
                PidTuningAdjustmentDirection.Hold,
                "建议保持当前参数",
                "当前基础指标未显示明显异常。",
                "维持当前响应特性，并继续结合现场工况观察。",
                "仍需确认不同负载和设定值阶跃下表现一致。",
                0.7));
        }

        return recommendations;
    }
}
