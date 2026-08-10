namespace PIDTuner.Desktop.Services;

public sealed record PlcTrendVisibleExport(
    DateTimeOffset VisibleStart,
    DateTimeOffset VisibleEnd,
    bool IsHistoricalMode,
    IReadOnlyList<PlcTrendVisibleExportPoint> Points);

public sealed record PlcTrendVisibleExportPoint(
    DateTimeOffset Timestamp,
    Guid TagId,
    string TagName,
    string Address,
    double Value,
    string? Unit,
    string Quality,
    string Source);
