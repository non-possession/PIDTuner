using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Application.Interfaces;

public interface IPlcConnectivityProbe
{
    Task<PlcCommunicationCheck> CheckAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken);
}
