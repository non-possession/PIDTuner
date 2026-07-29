namespace PIDTuner.Domain.Models;

public sealed record PidSample(
    DateTimeOffset Timestamp,
    double? SetPoint,
    double? ProcessValue,
    double? ManipulatedValue,
    double? Kp,
    double? KiOrTi,
    double? KdOrTd,
    bool IsPlcConnected,
    Guid TestSessionId,
    Guid? ParameterSetId,
    IReadOnlyDictionary<string, string?>? ExtraFields = null);
