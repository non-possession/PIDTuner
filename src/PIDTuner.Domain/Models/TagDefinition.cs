namespace PIDTuner.Domain.Models;

public sealed record TagDefinition(
    Guid Id,
    string Name,
    string Address,
    PlcDataType DataType,
    TagAccessMode AccessMode,
    double Scale,
    string? Unit,
    string? Description,
    TimeSpan SamplingInterval,
    bool IsEnabled);
