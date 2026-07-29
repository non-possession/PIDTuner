namespace PIDTuner.Domain.Models;

public sealed record PidParameterSet(
    Guid Id,
    string Name,
    double Kp,
    double KiOrTi,
    double KdOrTd,
    DateTimeOffset CreatedAt,
    string? Notes);
