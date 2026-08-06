namespace PIDTuner.Domain.Plc;

public sealed record PlcAcquisitionFrame(
    IReadOnlyList<PlcTagSnapshot> Snapshots,
    PlcAcquisitionFrameDiagnostics Diagnostics);
