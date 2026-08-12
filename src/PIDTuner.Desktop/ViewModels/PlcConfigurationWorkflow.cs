using System.IO;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcConfigurationWorkflow(
    IPlcProjectConfigurationStore configurationStore,
    IPlcConnectivityProbe connectivityProbe)
{
    public async Task<PlcProjectConfiguration> LoadAsync(string fileName, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fileName);
        return await configurationStore.LoadAsync(stream, cancellationToken);
    }

    public async Task<string> SaveAsync(
        PlcProjectConfiguration configuration,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(fileName);
        await configurationStore.SaveAsync(configuration, stream, cancellationToken);
        return Path.GetFullPath(fileName);
    }

    public async Task<PlcCommunicationCheckResult> CheckCommunicationAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var pendingStatus = $"正在 Ping {configuration.IpAddress} ...";
        var result = await connectivityProbe.CheckAsync(configuration, cancellationToken);
        var status = $"{result.CheckedAt:HH:mm:ss} {result.Host}: {result.Message}";
        return new PlcCommunicationCheckResult(
            pendingStatus,
            status,
            result.IsReachable ? "PLC 通信检查通过" : "PLC 通信检查未通过",
            result.IsReachable ? "Success" : "Warning",
            result.IsReachable);
    }
}

public sealed record PlcCommunicationCheckResult(
    string PendingStatus,
    string Status,
    string Title,
    string Kind,
    bool IsReachable);
