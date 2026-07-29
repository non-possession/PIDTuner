using System.Diagnostics;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class SiemensS7ConnectivityProbe : IPlcConnectivityProbe
{
    public async Task<PlcCommunicationCheck> CheckAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var client = new SiemensS7Client();
            await client.ConnectAsync(configuration, cancellationToken);
            stopwatch.Stop();

            return new PlcCommunicationCheck(
                true,
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"S7 握手成功，Rack={configuration.Rack}, Slot={configuration.Slot}。",
                DateTimeOffset.Now);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new PlcCommunicationCheck(
                false,
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"S7 通信失败：{exception.Message}",
                DateTimeOffset.Now);
        }
    }
}
