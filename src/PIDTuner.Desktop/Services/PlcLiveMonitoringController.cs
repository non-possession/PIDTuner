using System.Windows.Threading;
using PIDTuner.Application.Interfaces;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.Services;

public sealed class PlcLiveMonitoringController
{
    private readonly PlcLiveMonitorViewModel _liveMonitor;
    private readonly Action<PlcLiveMonitorDrainResult> _drainHandler;
    private readonly DispatcherTimer _refreshTimer = new();

    public PlcLiveMonitoringController(
        PlcLiveMonitorViewModel liveMonitor,
        Action<PlcLiveMonitorDrainResult> drainHandler)
    {
        _liveMonitor = liveMonitor;
        _drainHandler = drainHandler;
        _refreshTimer.Tick += (_, _) => DrainNow();
    }

    public async Task<PlcLiveMonitorStartResult> StartAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var result = await _liveMonitor.StartAsync(configuration, cancellationToken);
        _refreshTimer.Interval = result.UiRefreshInterval;
        _refreshTimer.Start();
        return result;
    }

    public async Task<PlcHistoricalTrendWriteSummary?> StopAsync()
    {
        _refreshTimer.Stop();
        return await _liveMonitor.StopAsync();
    }

    public void DrainNow()
    {
        var result = _liveMonitor.DrainPresentedFrames();
        if (result.Frames.Count > 0)
        {
            _drainHandler(result);
        }
    }
}
