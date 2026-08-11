namespace PIDTuner.Desktop.Services;

public static class PlcTrendChartAdapter
{
    public static TimeSpan CalculateLiveRetentionWindow(
        TimeSpan maxLiveTrendWindow,
        TimeSpan uiRefreshInterval,
        TimeSpan liveSamplingInterval)
    {
        return LivePlcTrendAdapter.CalculateLiveRetentionWindow(
            maxLiveTrendWindow,
            uiRefreshInterval,
            liveSamplingInterval);
    }
}
