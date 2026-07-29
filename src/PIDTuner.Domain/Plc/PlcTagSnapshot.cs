namespace PIDTuner.Domain.Plc;

public sealed record PlcTagSnapshot(
    Guid TagId,
    string Name,
    string Address,
    double? Value,
    string? Unit,
    DateTimeOffset Timestamp,
    string Quality,
    string Source);
