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
        if (string.IsNullOrWhiteSpace(configuration.IpAddress))
        {
            return new PlcCommunicationCheck(
                false,
                configuration.IpAddress,
                TimeSpan.Zero,
                "PLC IP 地址为空。",
                DateTimeOffset.Now);
        }

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
                $"S7 握手成功：TCP 102、ISO-on-TCP、S7 Setup 均通过，Rack={configuration.Rack}, Slot={configuration.Slot}。",
                DateTimeOffset.Now);
        }
        catch (SiemensS7ConnectionException exception)
        {
            stopwatch.Stop();
            return new PlcCommunicationCheck(
                false,
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"{StageLabel(exception.Stage)}：{exception.Message}",
                DateTimeOffset.Now);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new PlcCommunicationCheck(
                false,
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"S7 通信检查失败：{exception.Message}",
                DateTimeOffset.Now);
        }
    }

    private static string StageLabel(SiemensS7ConnectionStage stage)
    {
        return stage switch
        {
            SiemensS7ConnectionStage.TcpConnect => "TCP 端口检查失败",
            SiemensS7ConnectionStage.IsoOnTcpHandshake => "ISO-on-TCP 握手失败",
            SiemensS7ConnectionStage.S7SetupCommunication => "S7 Setup 检查失败",
            _ => "S7 通信检查失败"
        };
    }
}
