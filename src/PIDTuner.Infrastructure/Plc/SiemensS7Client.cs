using System.Buffers.Binary;
using System.Net.Sockets;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Plc;

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

internal sealed record S7BatchReadResult(
    IReadOnlyList<S7ReadResult> Results,
    IReadOnlyList<PlcReadOperationDiagnostics> Operations);

internal sealed record TimedS7Response(
    byte[] Bytes,
    double ReceiveHeaderDurationMilliseconds,
    double ReceivePayloadDurationMilliseconds);

/// <summary>
/// Minimal Siemens S7 TCP client for DB reads. It owns the socket/session handshake and exposes
/// typed numeric and batched reads to higher-level snapshot readers.
/// </summary>
public sealed class SiemensS7Client : IAsyncDisposable
{
    internal const int MaxReadItemsPerRequest = 16;
    internal const int RequestedPduLength = 960;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private ushort _sequence = 2;

    internal int NegotiatedPduLength { get; private set; } = RequestedPduLength;

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
            _ = await ExchangeAsync(
                BuildConnectionRequest(configuration.Rack, configuration.Slot),
                validatePduReference: false,
                timeout.Token);
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
            var setupResponse = await ExchangeAsync(
                BuildSetupCommunicationRequest(),
                validatePduReference: true,
                timeout.Token);
            NegotiatedPduLength = ParseSetupCommunicationResponse(setupResponse);
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
        return (await ReadNumericBatchWithDiagnosticsAsync(addresses, cancellationToken)).Results;
    }

    internal async Task<S7BatchReadResult> ReadNumericBatchWithDiagnosticsAsync(
        IReadOnlyList<S7Address> addresses,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return new S7BatchReadResult(
                Array.Empty<S7ReadResult>(),
                Array.Empty<PlcReadOperationDiagnostics>());
        }

        var results = new List<S7ReadResult>(addresses.Count);
        var operations = new List<PlcReadOperationDiagnostics>();
        var operationIndex = 0;
        foreach (var chunk in addresses.Chunk(MaxReadItemsPerRequest))
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            IReadOnlyList<S7ReadResult> chunkResults;
            string? error = null;
            var failure = new SiemensS7CommunicationFailure(
                PlcCommunicationErrorCategory.None,
                string.Empty,
                string.Empty,
                false,
                string.Empty);
            var request = BuildReadRequest(chunk);
            var requestPduReference = ReadPduReference(request);
            ushort? responsePduReference = null;
            var sendStartedAtUtc = DateTimeOffset.UtcNow;
            var sendFinishedAtUtc = sendStartedAtUtc;
            var requestGateEntered = false;
            var receiveHeaderDurationMilliseconds = 0d;
            var receivePayloadDurationMilliseconds = 0d;
            try
            {
                await _requestGate.WaitAsync(cancellationToken);
                requestGateEntered = true;
                sendStartedAtUtc = DateTimeOffset.UtcNow;
                await SendAsync(request, cancellationToken);
                sendFinishedAtUtc = DateTimeOffset.UtcNow;
                var response = await ReceiveTimedAsync(cancellationToken);
                ValidateResponsePduReference(response.Bytes, requestPduReference);
                responsePduReference = ReadPduReference(response.Bytes);
                receiveHeaderDurationMilliseconds = response.ReceiveHeaderDurationMilliseconds;
                receivePayloadDurationMilliseconds = response.ReceivePayloadDurationMilliseconds;
                chunkResults = ExtractReadResults(response.Bytes, chunk);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (sendFinishedAtUtc == sendStartedAtUtc)
                {
                    sendFinishedAtUtc = DateTimeOffset.UtcNow;
                }

                failure = SiemensS7CommunicationFailure.FromException(exception);
                error = failure.Message;
                chunkResults = chunk
                    .Select(address => new S7ReadResult(address, null, error))
                    .ToArray();
            }
            finally
            {
                if (requestGateEntered)
                {
                    _requestGate.Release();
                }
            }

            var receivedAtUtc = DateTimeOffset.UtcNow;
            results.AddRange(chunkResults);
            operations.Add(new PlcReadOperationDiagnostics(
                operationIndex,
                "S7ReadVar",
                FormatOperationTarget(chunk),
                chunk.Length,
                startedAtUtc,
                receivedAtUtc,
                (sendFinishedAtUtc - sendStartedAtUtc).TotalMilliseconds,
                receiveHeaderDurationMilliseconds,
                receivePayloadDurationMilliseconds,
                chunkResults.Count(result => result.Error is null),
                chunkResults.Count(result => result.Error is not null),
                error,
                failure.Category,
                NullIfEmpty(failure.Code),
                NullIfEmpty(failure.Context),
                failure.IsTransient,
                requestPduReference,
                responsePduReference));
            operationIndex++;
        }

        return new S7BatchReadResult(results, operations);
    }

    internal async Task<S7BatchReadResult> ReadNumericDbBlocksWithDiagnosticsAsync(
        IReadOnlyList<S7Address> addresses,
        CancellationToken cancellationToken)
    {
        if (addresses.Count == 0)
        {
            return new S7BatchReadResult(
                Array.Empty<S7ReadResult>(),
                Array.Empty<PlcReadOperationDiagnostics>());
        }

        var resultsByAddress = new Dictionary<S7Address, S7ReadResult>();
        var operations = new List<PlcReadOperationDiagnostics>();
        var operationIndex = 0;

        var maximumPayloadBytes = S7DbReadPlanner.CalculateMaximumReadPayload(NegotiatedPduLength);
        foreach (var block in S7DbReadPlanner.Plan(addresses, maximumPayloadBytes))
        {
            var blockAddresses = block.Addresses;
            var startByte = block.StartByte;
            var endByteExclusive = block.EndByteExclusive;
            var byteCount = block.ByteCount;
            var startedAtUtc = DateTimeOffset.UtcNow;
            string? error = null;
            var failure = new SiemensS7CommunicationFailure(
                PlcCommunicationErrorCategory.None,
                string.Empty,
                string.Empty,
                false,
                string.Empty);
            var sendStartedAtUtc = DateTimeOffset.UtcNow;
            var sendFinishedAtUtc = sendStartedAtUtc;
            var receiveHeaderDurationMilliseconds = 0d;
            var receivePayloadDurationMilliseconds = 0d;
            IReadOnlyList<S7ReadResult> blockResults;
            var requestGateEntered = false;
            var request = BuildAreaReadRequest(block.DataBlock, startByte, byteCount);
            var requestPduReference = ReadPduReference(request);
            ushort? responsePduReference = null;

            try
            {
                await _requestGate.WaitAsync(cancellationToken);
                requestGateEntered = true;
                sendStartedAtUtc = DateTimeOffset.UtcNow;
                await SendAsync(request, cancellationToken);
                sendFinishedAtUtc = DateTimeOffset.UtcNow;
                var response = await ReceiveTimedAsync(cancellationToken);
                ValidateResponsePduReference(response.Bytes, requestPduReference);
                responsePduReference = ReadPduReference(response.Bytes);
                receiveHeaderDurationMilliseconds = response.ReceiveHeaderDurationMilliseconds;
                receivePayloadDurationMilliseconds = response.ReceivePayloadDurationMilliseconds;
                blockResults = ExtractBlockReadResults(response.Bytes, blockAddresses, startByte, byteCount);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (sendFinishedAtUtc == sendStartedAtUtc)
                {
                    sendFinishedAtUtc = DateTimeOffset.UtcNow;
                }

                failure = SiemensS7CommunicationFailure.FromException(exception);
                error = failure.Message;
                blockResults = blockAddresses
                    .Select(address => new S7ReadResult(address, null, error))
                    .ToArray();
            }
            finally
            {
                if (requestGateEntered)
                {
                    _requestGate.Release();
                }
            }

            var receivedAtUtc = DateTimeOffset.UtcNow;
            foreach (var result in blockResults)
            {
                resultsByAddress[result.Address] = result;
            }

            operations.Add(new PlcReadOperationDiagnostics(
                operationIndex,
                "S7ReadDbBlock",
                $"DB{block.DataBlock}.DBB{startByte}-DBB{endByteExclusive - 1}",
                blockAddresses.Count,
                startedAtUtc,
                receivedAtUtc,
                (sendFinishedAtUtc - sendStartedAtUtc).TotalMilliseconds,
                receiveHeaderDurationMilliseconds,
                receivePayloadDurationMilliseconds,
                blockResults.Count(result => result.Error is null),
                blockResults.Count(result => result.Error is not null),
                error,
                failure.Category,
                NullIfEmpty(failure.Code),
                NullIfEmpty(failure.Context),
                failure.IsTransient,
                requestPduReference,
                responsePduReference));
            operationIndex++;
        }

        var orderedResults = addresses
            .Select(address => resultsByAddress.TryGetValue(address, out var result)
                ? result
                : new S7ReadResult(address, null, "DB block read did not return this address."))
            .ToArray();

        return new S7BatchReadResult(orderedResults, operations);
    }

    public async ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        if (_client is not null)
        {
            _client.Dispose();
        }

        _requestGate.Dispose();

        await ValueTask.CompletedTask;
    }

    private async Task SendAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("S7 client is not connected.");
        }

        try
        {
            await _stream.WriteAsync(bytes, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Send,
                "S7.SEND_FAILED",
                "TCP request write",
                exception.Message,
                isTransient: true,
                exception);
        }
    }

    private async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        return (await ReceiveTimedAsync(cancellationToken)).Bytes;
    }

    private async Task<byte[]> ExchangeAsync(
        byte[] request,
        bool validatePduReference,
        CancellationToken cancellationToken)
    {
        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            await SendAsync(request, cancellationToken);
            var response = await ReceiveAsync(cancellationToken);
            if (validatePduReference)
            {
                ValidateResponsePduReference(response, ReadPduReference(request));
            }

            return response;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<TimedS7Response> ReceiveTimedAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("S7 client is not connected.");
        }

        var header = new byte[4];
        var headerStartedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await _stream.ReadExactlyAsync(header, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.ReceiveHeader,
                "S7.RECEIVE_HEADER_FAILED",
                "TPKT header",
                exception.Message,
                isTransient: true,
                exception);
        }
        var headerReceivedAtUtc = DateTimeOffset.UtcNow;
        var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        if (length < 4)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Protocol,
                "S7.INVALID_TPKT_LENGTH",
                "TPKT header length",
                "Invalid S7 response length.");
        }

        var response = new byte[length];
        header.CopyTo(response, 0);
        var payloadStartedAtUtc = DateTimeOffset.UtcNow;
        try
        {
            await _stream.ReadExactlyAsync(response.AsMemory(4), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.ReceivePayload,
                "S7.RECEIVE_PAYLOAD_FAILED",
                $"TPKT payload ({length - 4} bytes)",
                exception.Message,
                isTransient: true,
                exception);
        }
        var payloadReceivedAtUtc = DateTimeOffset.UtcNow;
        return new TimedS7Response(
            response,
            (headerReceivedAtUtc - headerStartedAtUtc).TotalMilliseconds,
            (payloadReceivedAtUtc - payloadStartedAtUtc).TotalMilliseconds);
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
            0xF0, 0x00, 0x00, 0x01, 0x00, 0x01,
            (byte)(RequestedPduLength >> 8), (byte)(RequestedPduLength & 0xFF)
        };
    }

    private static int ParseSetupCommunicationResponse(byte[] response)
    {
        const int s7Offset = 7;
        const int setupParameterLength = 8;
        if (response.Length < s7Offset + 12 + setupParameterLength
            || response[0] != 0x03
            || response[s7Offset] != 0x32)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Protocol,
                "S7.INVALID_SETUP_RESPONSE",
                "S7 Setup Communication response",
                "Invalid S7 Setup Communication response.");
        }

        var rosctr = response[s7Offset + 1];
        var headerLength = rosctr == 0x03 ? 12 : 10;
        var parameterLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(s7Offset + 6, 2));
        var parameterOffset = s7Offset + headerLength;
        if (parameterLength < setupParameterLength
            || response.Length < parameterOffset + parameterLength
            || response[parameterOffset] != 0xF0)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Protocol,
                "S7.INVALID_SETUP_PARAMETER",
                "S7 Setup Communication parameter",
                "S7 Setup Communication response does not contain a valid setup parameter.");
        }

        var negotiatedPduLength = BinaryPrimitives.ReadUInt16BigEndian(
            response.AsSpan(parameterOffset + 6, 2));
        if (negotiatedPduLength < 64)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Protocol,
                "S7.INVALID_NEGOTIATED_PDU",
                "S7 Setup Communication PDU length",
                $"PLC negotiated an invalid S7 PDU length of {negotiatedPduLength} bytes.");
        }

        return negotiatedPduLength;
    }

    private static ushort ReadPduReference(IReadOnlyList<byte> message)
    {
        const int pduReferenceOffset = 11;
        if (message.Count < pduReferenceOffset + 2)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Protocol,
                "S7.MISSING_PDU_REFERENCE",
                "S7 header PDU reference",
                "S7 message does not contain a PDU reference.");
        }

        return (ushort)((message[pduReferenceOffset] << 8) | message[pduReferenceOffset + 1]);
    }

    private static void ValidateResponsePduReference(byte[] response, ushort expectedReference)
    {
        var actualReference = ReadPduReference(response);
        if (actualReference != expectedReference)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.Protocol,
                "S7.PDU_REFERENCE_MISMATCH",
                $"expected={expectedReference}, actual={actualReference}",
                $"S7 response PDU reference {actualReference} does not match request {expectedReference}.");
        }
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

    private byte[] BuildAreaReadRequest(int dataBlock, int startByte, int byteCount)
    {
        if (byteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), byteCount, "S7 area read byte count must be greater than zero.");
        }

        var sequence = _sequence++;
        const int parameterLength = 14;
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
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(13, 2), parameterLength);
        request[17] = 0x04;
        request[18] = 0x01;
        WriteReadItem(request.AsSpan(19, 12), dataBlock, startByte, null, byteCount);
        return request;
    }

    private static string FormatOperationTarget(IReadOnlyList<S7Address> addresses)
    {
        var groups = addresses
            .GroupBy(address => address.DataBlock)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var offsets = group
                    .Select(address => address.ByteOffset)
                    .Order()
                    .ToArray();
                return offsets.Length == 1
                    ? $"DB{group.Key}.DBB{offsets[0]}"
                    : $"DB{group.Key}.DBB{offsets[0]}-DBB{offsets[^1]}";
            });

        return string.Join("; ", groups);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static void WriteReadItem(Span<byte> item, S7Address address)
    {
        WriteReadItem(item, address.DataBlock, address.ByteOffset, address.BitOffset, address.DataType == PlcDataType.Boolean ? 1 : address.ReadByteCount);
    }

    private static void WriteReadItem(
        Span<byte> item,
        int dataBlock,
        int byteOffset,
        int? bitOffset,
        int readByteCount)
    {
        item[0] = 0x12;
        item[1] = 0x0A;
        item[2] = 0x10;
        item[3] = bitOffset.HasValue ? (byte)0x01 : (byte)0x02;
        BinaryPrimitives.WriteUInt16BigEndian(item[4..6], (ushort)readByteCount);
        BinaryPrimitives.WriteUInt16BigEndian(item[6..8], (ushort)dataBlock);
        item[8] = 0x84;
        var bitAddress = byteOffset * 8 + (bitOffset ?? 0);
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
            throw InvalidReadResponse("S7.INVALID_RESPONSE", "S7 header", "Invalid S7 read response.");
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
                throw InvalidReadResponse("S7.MISSING_DATA", "ReadVar data item", "S7 read response does not contain data.");
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
                throw InvalidReadResponse("S7.TRUNCATED_PAYLOAD", "ReadVar item payload", "S7 read response payload is truncated.");
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

    private static IReadOnlyList<S7ReadResult> ExtractBlockReadResults(
        byte[] response,
        IReadOnlyList<S7Address> addresses,
        int startByte,
        int requestedByteCount)
    {
        var payload = ExtractSingleReadPayload(response);
        if (payload.Length < requestedByteCount)
        {
            throw new InvalidOperationException("S7 DB block response payload is shorter than requested.");
        }

        var results = new List<S7ReadResult>(addresses.Count);
        foreach (var address in addresses)
        {
            var relativeOffset = address.ByteOffset - startByte;
            if (relativeOffset < 0 || relativeOffset + address.ReadByteCount > payload.Length)
            {
                results.Add(new S7ReadResult(address, null, "Address is outside the returned DB block."));
                continue;
            }

            try
            {
                var value = DecodeNumericPayload(address, payload.Slice(relativeOffset, address.ReadByteCount));
                results.Add(new S7ReadResult(address, value, null));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                results.Add(new S7ReadResult(address, null, exception.Message));
            }
        }

        return results;
    }

    private static ReadOnlySpan<byte> ExtractSingleReadPayload(byte[] response)
    {
        const int s7Offset = 7;
        if (response.Length < s7Offset + 12 || response[s7Offset] != 0x32)
        {
            throw InvalidReadResponse("S7.INVALID_RESPONSE", "S7 header", "Invalid S7 read response.");
        }

        var rosctr = response[s7Offset + 1];
        var parameterLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(s7Offset + 6, 2));
        var headerLength = rosctr == 0x03 ? 12 : 10;
        var dataOffset = s7Offset + headerLength + parameterLength;
        if (response.Length < dataOffset + 4)
        {
            throw InvalidReadResponse("S7.MISSING_DATA", "ReadVar data item", "S7 read response does not contain data.");
        }

        var returnCode = response[dataOffset];
        var transportSize = response[dataOffset + 1];
        var reportedLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(dataOffset + 2, 2));
        var byteCount = transportSize == 0x03
            ? 1
            : (reportedLength + 7) / 8;
        var payloadOffset = dataOffset + 4;
        if (returnCode != 0xFF)
        {
            throw new SiemensS7ProtocolException(
                PlcCommunicationErrorCategory.PlcResponse,
                $"S7.RETURN_CODE_{returnCode:X2}",
                "ReadVar return code",
                $"S7 read failed with return code 0x{returnCode:X2}.");
        }

        if (response.Length < payloadOffset + byteCount)
        {
            throw InvalidReadResponse("S7.TRUNCATED_PAYLOAD", "ReadVar item payload", "S7 read response payload is truncated.");
        }

        return response.AsSpan(payloadOffset, byteCount);
    }

    private static SiemensS7ProtocolException InvalidReadResponse(string code, string context, string message) =>
        new(PlcCommunicationErrorCategory.Protocol, code, context, message);

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
