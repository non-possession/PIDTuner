namespace PIDTuner.Domain.Models;

public sealed record TestSession(
    Guid Id,
    Guid ProjectId,
    string Name,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? Device,
    string? OperatingCondition,
    string? Notes);
