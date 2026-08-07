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

public sealed record PlcReadOperationDiagnostics(
    int OperationIndex,
    string OperationKind,
    string Target,
    int AddressCount,
    DateTimeOffset RequestStartedTimestampUtc,
    DateTimeOffset ResponseReceivedTimestampUtc,
    int SuccessCount,
    int FailureCount,
    string? Error)
{
    public double DurationMilliseconds =>
        (ResponseReceivedTimestampUtc - RequestStartedTimestampUtc).TotalMilliseconds;
}
