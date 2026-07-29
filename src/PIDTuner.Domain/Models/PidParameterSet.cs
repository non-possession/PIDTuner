namespace PIDTuner.Domain.Models;

public sealed record PidParameterSet(
    Guid Id,
    Guid? TestSessionId,
    string Name,
    double? Kp,
    double? KiOrTi,
    double? KdOrTd,
    DateTimeOffset CapturedAt,
    string SourceName,
    string? Notes);
