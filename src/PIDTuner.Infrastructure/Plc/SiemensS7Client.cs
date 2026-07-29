using System.Buffers.Binary;
using System.Net.Sockets;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Plc;

public sealed class SiemensS7Client : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private ushort _sequence = 1;

    public async Task ConnectAsync(PlcProjectConfiguration configuration, CancellationToken cancellationToken)
    {
        _client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Math.Max(250, configuration.TimeoutMilliseconds));
        await _client.ConnectAsync(configuration.IpAddress, 102, timeout.Token);
        _stream = _client.GetStream();

        await SendAsync(BuildConnectionRequest(configuration.Rack, configuration.Slot), timeout.Token);
        _ = await ReceiveAsync(timeout.Token);
        await SendAsync(BuildSetupCommunicationRequest(), timeout.Token);
        _ = await ReceiveAsync(timeout.Token);
    }

    public async Task<double?> ReadNumericAsync(S7Address address, CancellationToken cancellationToken)
    {
        var request = BuildReadRequest(address);
        await SendAsync(request, cancellationToken);
        var response = await ReceiveAsync(cancellationToken);
        var payload = ExtractReadPayload(response);

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

    private byte[] BuildReadRequest(S7Address address)
    {
        var sequence = _sequence++;
        var request = new byte[31];
        request[0] = 0x03;
        request[1] = 0x00;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), (ushort)request.Length);
        request[4] = 0x02;
        request[5] = 0xF0;
        request[6] = 0x80;
        request[7] = 0x32;
        request[8] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(11, 2), sequence);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(13, 2), 0x000E);
        request[17] = 0x04;
        request[18] = 0x01;
        request[19] = 0x12;
        request[20] = 0x0A;
        request[21] = 0x10;
        request[22] = address.DataType == PlcDataType.Boolean ? (byte)0x01 : (byte)0x02;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(23, 2), (ushort)(address.DataType == PlcDataType.Boolean ? 1 : address.ReadByteCount));
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(25, 2), (ushort)address.DataBlock);
        request[27] = 0x84;
        var bitAddress = address.BitAddress;
        request[28] = (byte)((bitAddress >> 16) & 0xFF);
        request[29] = (byte)((bitAddress >> 8) & 0xFF);
        request[30] = (byte)(bitAddress & 0xFF);
        return request;
    }

    private static byte[] ExtractReadPayload(byte[] response)
    {
        const int s7Offset = 7;
        if (response.Length < s7Offset + 12 || response[s7Offset] != 0x32)
        {
            throw new InvalidOperationException("Invalid S7 read response.");
        }

        var rosctr = response[s7Offset + 1];
        var parameterLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(s7Offset + 6, 2));
        var headerLength = rosctr == 0x03 ? 12 : 10;
        var dataOffset = s7Offset + headerLength + parameterLength;

        if (response.Length < dataOffset + 4)
        {
            throw new InvalidOperationException("S7 read response does not contain data.");
        }

        var returnCode = response[dataOffset];
        if (returnCode != 0xFF)
        {
            throw new InvalidOperationException($"S7 read failed with return code 0x{returnCode:X2}.");
        }

        var transportSize = response[dataOffset + 1];
        var reportedLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(dataOffset + 2, 2));
        var byteCount = transportSize == 0x03
            ? 1
            : (reportedLength + 7) / 8;
        var payloadOffset = dataOffset + 4;
        if (response.Length < payloadOffset + byteCount)
        {
            throw new InvalidOperationException("S7 read response payload is truncated.");
        }

        return response.AsSpan(payloadOffset, byteCount).ToArray();
    }
}
