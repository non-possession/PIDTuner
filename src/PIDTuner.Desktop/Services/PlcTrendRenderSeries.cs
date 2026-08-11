namespace PIDTuner.Desktop.Services;

internal sealed record PlcTrendRenderSeries(
    Guid SeriesId,
    string Name,
    string? Unit,
    IReadOnlyList<PlcTrendPoint> Points);
