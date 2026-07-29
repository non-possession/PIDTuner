using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class ConfiguredPlcConnectivityProbe(
    IPlcConnectivityProbe siemensS7Probe,
    IPlcConnectivityProbe pingProbe) : IPlcConnectivityProbe
{
    public Task<PlcCommunicationCheck> CheckAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.Protocol.Contains("siemens", StringComparison.OrdinalIgnoreCase)
            || configuration.Protocol.Contains("s7", StringComparison.OrdinalIgnoreCase))
        {
            return siemensS7Probe.CheckAsync(configuration, cancellationToken);
        }

        return pingProbe.CheckAsync(configuration, cancellationToken);
    }
}
