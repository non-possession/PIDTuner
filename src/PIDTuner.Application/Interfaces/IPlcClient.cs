namespace PIDTuner.Application.Interfaces;

public interface IPlcClient
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task DisconnectAsync();

    Task<T> ReadAsync<T>(string address, CancellationToken cancellationToken);

    Task WriteAsync<T>(string address, T value, CancellationToken cancellationToken);
}
