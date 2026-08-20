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
            var (category, code, context) = ClassifyConnectionFailure(exception);
            return new PlcCommunicationCheck(
                false,
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"{StageLabel(exception.Stage)}：{exception.Message}",
                DateTimeOffset.Now,
                category,
                code,
                context);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            return new PlcCommunicationCheck(
                false,
                configuration.IpAddress,
                stopwatch.Elapsed,
                $"S7 通信检查失败：{exception.Message}",
                DateTimeOffset.Now,
                PlcCommunicationErrorCategory.Unknown,
                "S7.CHECK_UNKNOWN",
                exception.GetType().Name);
        }
    }

    private static (PlcCommunicationErrorCategory Category, string Code, string Context) ClassifyConnectionFailure(
        SiemensS7ConnectionException exception)
    {
        var timedOut = exception.InnerException is OperationCanceledException;
        return exception.Stage switch
        {
            SiemensS7ConnectionStage.TcpConnect => (
                timedOut ? PlcCommunicationErrorCategory.Timeout : PlcCommunicationErrorCategory.Connection,
                timedOut ? "S7.TCP_CONNECT_TIMEOUT" : "S7.TCP_CONNECT_FAILED",
                "TCP 102 connect"),
            SiemensS7ConnectionStage.IsoOnTcpHandshake => (
                timedOut ? PlcCommunicationErrorCategory.Timeout : PlcCommunicationErrorCategory.Handshake,
                timedOut ? "S7.ISO_HANDSHAKE_TIMEOUT" : "S7.ISO_HANDSHAKE_FAILED",
                "ISO-on-TCP handshake"),
            SiemensS7ConnectionStage.S7SetupCommunication => (
                timedOut ? PlcCommunicationErrorCategory.Timeout : PlcCommunicationErrorCategory.Handshake,
                timedOut ? "S7.SETUP_TIMEOUT" : "S7.SETUP_FAILED",
                "S7 Setup Communication"),
            _ => (PlcCommunicationErrorCategory.Unknown, "S7.CONNECT_UNKNOWN", exception.Stage.ToString())
        };
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
