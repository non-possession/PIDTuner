namespace PIDTuner.Domain.Plc;

public sealed record PlcAcquisitionFrame(
    IReadOnlyList<PlcTagSnapshot> Snapshots,
    PlcAcquisitionFrameDiagnostics Diagnostics,
    IReadOnlyList<PlcReadOperationDiagnostics> ReadOperations)
{
    public PlcAcquisitionFrame(
        IReadOnlyList<PlcTagSnapshot> snapshots,
        PlcAcquisitionFrameDiagnostics diagnostics)
        : this(snapshots, diagnostics, Array.Empty<PlcReadOperationDiagnostics>())
    {
    }
}

public enum PlcCommunicationErrorCategory
{
    None,
    Configuration,
    Connection,
    Handshake,
    Send,
    ReceiveHeader,
    ReceivePayload,
    Protocol,
    PlcResponse,
    Parsing,
    Timeout,
    Cancellation,
    Reconnect,
    Unknown
}

public sealed record PlcReadOperationDiagnostics(
    int OperationIndex,
    string OperationKind,
    string Target,
    int AddressCount,
    DateTimeOffset RequestStartedTimestampUtc,
    DateTimeOffset ResponseReceivedTimestampUtc,
    double SendDurationMilliseconds,
    double ReceiveHeaderDurationMilliseconds,
    double ReceivePayloadDurationMilliseconds,
    int SuccessCount,
    int FailureCount,
    string? Error,
    PlcCommunicationErrorCategory ErrorCategory = PlcCommunicationErrorCategory.None,
    string? ErrorCode = null,
    string? ErrorContext = null,
    bool IsTransient = false,
    ushort? RequestPduReference = null,
    ushort? ResponsePduReference = null)
{
    public double DurationMilliseconds =>
        (ResponseReceivedTimestampUtc - RequestStartedTimestampUtc).TotalMilliseconds;
}
