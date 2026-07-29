using PIDTuner.Application.Interfaces;

namespace PIDTuner.Infrastructure.Plc;

public sealed class NotConfiguredPlcClient : IPlcClient
{
    public bool IsConnected => false;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("PLC adapter is not configured yet.");
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task<T> ReadAsync<T>(string address, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("PLC adapter is not configured yet.");
    }

    public Task WriteAsync<T>(string address, T value, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("PLC adapter is not configured yet.");
    }
}
