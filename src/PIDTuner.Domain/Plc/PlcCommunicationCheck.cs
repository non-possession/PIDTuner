namespace PIDTuner.Domain.Plc;

public sealed record PlcCommunicationCheck(
    bool IsReachable,
    string Host,
    TimeSpan Duration,
    string Message,
    DateTimeOffset CheckedAt);
