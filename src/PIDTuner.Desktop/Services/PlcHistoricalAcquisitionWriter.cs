using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcHistoricalAcquisitionWriter(IPlcHistoricalTrendStore store)
{
    private IPlcHistoricalTrendWriteSession? _session;

    public string DatabasePath => store.DatabasePath;

    public async Task StartAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken);
        _session = await store.StartSessionAsync(configuration, cancellationToken);
    }

    public void Enqueue(PlcAcquisitionFrame frame) => _session?.Enqueue(frame);

    public async Task<PlcHistoricalTrendWriteSummary?> StopAsync(
        CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return null;
        }

        var session = _session;
        _session = null;
        return await session.StopAsync(cancellationToken);
    }
}
