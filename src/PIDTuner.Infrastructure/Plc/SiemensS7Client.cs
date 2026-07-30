using System.Buffers.Binary;
using System.Net.Sockets;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Plc;

internal enum SiemensS7ConnectionStage
{
    TcpConnect,
    IsoOnTcpHandshake,
    S7SetupCommunication
}

internal sealed class SiemensS7ConnectionException(
    SiemensS7ConnectionStage stage,
    string message,
    Exception? innerException = null) : InvalidOperationException(message, innerException)
{
    public SiemensS7ConnectionStage Stage { get; } = stage;
}

internal sealed record S7ReadResult(S7Address Address, double? Value, string? Error);

/// <summary>
/// Minimal Siemens S7 TCP client for DB reads. It owns the socket/session handshake and exposes
/// typed numeric and batched reads to higher-level snapshot readers.
/// </summary>
public sealed class SiemensS7Client : IAsyncDisposable
{
    internal const int MaxReadItemsPerRequest = 16;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _sequence = 1;

    public async Task ConnectAsync(PlcProjectConfiguration configuration, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(250, configuration.TimeoutMilliseconds));

        try
        {
            await _client.ConnectAsync(configuration.IpAddress, 102, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SiemensS7ConnectionException(
                SiemensS7ConnectionStage.TcpConnect,
                $"TCP 102 连接超时：{configuration.TimeoutMilliseconds} ms 内未建立连接。",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SiemensS7ConnectionException(
                SiemensS7ConnectionStage.TcpConnect,
                $"TCP 102 连接失败：{exception.Message}",
                exception);
        }

        _stream = _client.GetStream();

        // ISO-on-TCP connection request, followed by S7 setup communication negotiation.
        try
        {
            await SendAsync(BuildConnectionRequest(configuration.Rack, configuration.Slot), timeout.Token);
            _ = await ReceiveAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SiemensS7ConnectionException(
                SiemensS7ConnectionStage.IsoOnTcpHandshake,
                $"ISO-on-TCP 握手超时：{configuration.TimeoutMilliseconds} ms 内未收到响应。",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SiemensS7ConnectionException(
                SiemensS7ConnectionStage.IsoOnTcpHandshake,
                $"ISO-on-TCP 握手失败：{exception.Message}",
                exception);
        }

        try
        {
            await SendAsync(BuildSetupCommunicationRequest(), timeout.Token);
            _ = await ReceiveAsync(timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SiemensS7ConnectionException(
                SiemensS7ConnectionStage.S7SetupCommunication,
                $"S7 Setup Communication 超时：{configuration.TimeoutMilliseconds} ms 内未收到响应。",
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SiemensS7ConnectionException(
                SiemensS7ConnectionStage.S7SetupCommunication,
                $"S7 Setup Communication 失败：{exception.Message}",
                exception);
        }
    }

    public async Task<double?> ReadNumericAsync(S7Address address, CancellationToken cancellationToken)
    {
        var result = (await ReadNumericBatchAsync(new[] { address }, cancellationToken))[0];
        if (result.Error is not null)
        {
            throw new InvalidOperationException(result.Error);
        }

        return result.Value;
    }

    internal async Task<IReadOnlyList<S7ReadResult>> ReadNumericBatchAsync(
        IReadOnlyList<S7Address> addresses,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return Array.Empty<S7ReadResult>();
        }

        var results = new List<S7ReadResult>(addresses.Count);
        foreach (var chunk in addresses.Chunk(MaxReadItemsPerRequest))
        {
            var request = BuildReadRequest(chunk);
            await SendAsync(request, cancellationToken);
            var response = await ReceiveAsync(cancellationToken);
            results.AddRange(ExtractReadResults(response, chunk));
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        if (_client is not null)
        {
            _client.Dispose();
        }

        await ValueTask.CompletedTask;
    }

    private async Task SendAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("S7 client is not connected.");
        }

        await _stream.WriteAsync(bytes, cancellationToken);
    }

    private async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("S7 client is not connected.");
        }

        var header = new byte[4];
        await _stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        if (length < 4)
        {
            throw new InvalidOperationException("Invalid S7 response length.");
        }

        var response = new byte[length];
        header.CopyTo(response, 0);
        await _stream.ReadExactlyAsync(response.AsMemory(4), cancellationToken);
        return response;
    }

    private static byte[] BuildConnectionRequest(int rack, int slot)
    {
        var remoteTsap = (byte)((rack << 5) | slot);
        return new byte[]
        {
            0x03, 0x00, 0x00, 0x16,
            0x11, 0xE0, 0x00, 0x00, 0x00, 0x01, 0x00,
            0xC0, 0x01, 0x0A,
            0xC1, 0x02, 0x01, 0x00,
            0xC2, 0x02, 0x01, remoteTsap
        };
    }

    private static byte[] BuildSetupCommunicationRequest()
    {
        return new byte[]
        {
            0x03, 0x00, 0x00, 0x19,
            0x02, 0xF0, 0x80,
            0x32, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x08, 0x00, 0x00,
            0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0
        };
    }

    private byte[] BuildReadRequest(IReadOnlyList<S7Address> addresses)
    {
        if (addresses.Count is <= 0 or > MaxReadItemsPerRequest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(addresses),
                $"S7 batch read supports 1 to {MaxReadItemsPerRequest} items per request.");
        }

        var sequence = _sequence++;
        var parameterLength = 2 + 12 * addresses.Count;
        var request = new byte[17 + parameterLength];
        request[0] = 0x03;
        request[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), (ushort)request.Length);
        request[4] = 0x02;
        request[5] = 0xF0;
        request[6] = 0x80;
        request[7] = 0x32;
        request[8] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(11, 2), sequence);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(13, 2), (ushort)parameterLength);
        request[17] = 0x04;
        request[18] = (byte)addresses.Count;

        // Every variable item is a 12-byte S7ANY descriptor inside the same read parameter block.
        for (var index = 0; index < addresses.Count; index++)
        {
            WriteReadItem(request.AsSpan(19 + index * 12, 12), addresses[index]);
        }

        return request;
    }

    private static void WriteReadItem(Span<byte> item, S7Address address)
    {
        item[0] = 0x12;
        item[1] = 0x0A;
        item[2] = 0x10;
        item[3] = address.DataType == PlcDataType.Boolean ? (byte)0x01 : (byte)0x02;
        BinaryPrimitives.WriteUInt16BigEndian(
            item[4..6],
            (ushort)(address.DataType == PlcDataType.Boolean ? 1 : address.ReadByteCount));
        BinaryPrimitives.WriteUInt16BigEndian(item[6..8], (ushort)address.DataBlock);
        item[8] = 0x84;
        var bitAddress = address.BitAddress;
        item[9] = (byte)((bitAddress >> 16) & 0xFF);
        item[10] = (byte)((bitAddress >> 8) & 0xFF);
        item[11] = (byte)(bitAddress & 0xFF);
    }

    private static IReadOnlyList<S7ReadResult> ExtractReadResults(
        byte[] response,
        IReadOnlyList<S7Address> addresses)
    {
        const int s7Offset = 7;
        if (response.Length < s7Offset + 12 || response[s7Offset] != 0x32)
        {
            throw new InvalidOperationException("Invalid S7 read response.");
        }

        var rosctr = response[s7Offset + 1];
        var parameterLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(s7Offset + 6, 2));
        var headerLength = rosctr == 0x03 ? 12 : 10;
        // S7 read data begins after the transport header, S7 header, and parameter section.
        var dataOffset = s7Offset + headerLength + parameterLength;

        var results = new List<S7ReadResult>(addresses.Count);
        var offset = dataOffset;
        for (var index = 0; index < addresses.Count; index++)
        {
            if (response.Length < offset + 4)
            {
                throw new InvalidOperationException("S7 read response does not contain data.");
            }

            var address = addresses[index];
            var returnCode = response[offset];
            var transportSize = response[offset + 1];
            var reportedLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(offset + 2, 2));
            var byteCount = transportSize == 0x03
                ? 1
                : (reportedLength + 7) / 8;
            var payloadOffset = offset + 4;
            if (response.Length < payloadOffset + byteCount)
            {
                throw new InvalidOperationException("S7 read response payload is truncated.");
            }

            if (returnCode != 0xFF)
            {
                results.Add(new S7ReadResult(address, null, $"S7 read failed with return code 0x{returnCode:X2}."));
            }
            else
            {
                try
                {
                    var value = DecodeNumericPayload(address, response.AsSpan(payloadOffset, byteCount));
                    results.Add(new S7ReadResult(address, value, null));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    results.Add(new S7ReadResult(address, null, exception.Message));
                }
            }

            offset = payloadOffset + byteCount;
            // S7 pads odd-length data items before the next result item; absolute packet offset is irrelevant.
            if (index < addresses.Count - 1 && byteCount % 2 != 0)
            {
                offset++;
            }
        }

        return results;
    }

    private static double DecodeNumericPayload(S7Address address, ReadOnlySpan<byte> payload)
    {
        return address.DataType switch
        {
            PlcDataType.Boolean => ((payload[0] >> (address.BitOffset ?? 0)) & 0x01) == 1 ? 1 : 0,
            PlcDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(payload),
            PlcDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(payload),
            PlcDataType.Float => BinaryPrimitives.ReadSingleBigEndian(payload),
            PlcDataType.Double => BinaryPrimitives.ReadSingleBigEndian(payload),
            _ => throw new NotSupportedException($"Unsupported PLC data type: {address.DataType}.")
        };
    }
}
