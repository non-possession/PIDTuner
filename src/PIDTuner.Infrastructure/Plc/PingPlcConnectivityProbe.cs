using System.Diagnostics;
using System.Net.NetworkInformation;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class PingPlcConnectivityProbe : IPlcConnectivityProbe
{
    public async Task<PlcCommunicationCheck> CheckAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.IpAddress))
        {
            return Failed(configuration.IpAddress, TimeSpan.Zero, "PLC IP 地址为空。");
        }

        try
        {
            using var ping = new Ping();
            var timeout = Math.Max(250, configuration.TimeoutMilliseconds);
            var stopwatch = Stopwatch.StartNew();
            var reply = await ping.SendPingAsync(configuration.IpAddress, timeout);
            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            if (reply.Status == IPStatus.Success)
            {
                return new PlcCommunicationCheck(
                    true,
                    configuration.IpAddress,
                    stopwatch.Elapsed,
                    $"Ping 成功，往返 {reply.RoundtripTime} ms。",
                    DateTimeOffset.Now);
            }

            return Failed(
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"Ping 未成功：{reply.Status}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Failed(configuration.IpAddress, TimeSpan.Zero, $"Ping 检查失败：{exception.Message}");
        }
    }

    private static PlcCommunicationCheck Failed(string host, TimeSpan duration, string message)
    {
        return new PlcCommunicationCheck(false, host, duration, message, DateTimeOffset.Now);
    }
}
