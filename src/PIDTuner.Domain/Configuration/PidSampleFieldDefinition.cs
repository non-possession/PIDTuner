namespace PIDTuner.Domain.Configuration;

public sealed record PidSampleFieldDefinition(
    string Key,
    string DisplayName,
    PidSampleFieldDataType DataType,
    bool Required,
    string? Unit,
    PidSampleFieldRole Role);
