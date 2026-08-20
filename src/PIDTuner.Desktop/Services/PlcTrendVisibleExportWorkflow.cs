using System.Globalization;
using System.IO;
using System.Text;

namespace PIDTuner.Desktop.Services;

public sealed class PlcTrendVisibleExportWorkflow
{
    private const string Header =
        "timestampUtc,timestampLocal,tagName,tagId,address,value,unit,quality,source,visibleStartUtc,visibleEndUtc,trendMode";

    public async Task<PlcTrendVisibleExportResult> ExportAsync(
        string fileName,
        PlcTrendVisibleExport export,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(export);

        var absolutePath = Path.GetFullPath(fileName);
        await using var stream = File.Create(absolutePath);
        await using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        await writer.WriteLineAsync(Header.AsMemory(), cancellationToken);

        var visibleStartUtc = FormatUtc(export.VisibleStart);
        var visibleEndUtc = FormatUtc(export.VisibleEnd);
        var trendMode = export.IsHistoricalMode ? "Historical" : "Live";

        foreach (var point in export.Points)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columns = new[]
            {
                FormatUtc(point.Timestamp),
                point.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                point.TagName,
                point.TagId.ToString("D"),
                point.Address,
                point.Value.ToString("G17", CultureInfo.InvariantCulture),
                point.Unit ?? string.Empty,
                point.Quality,
                point.Source,
                visibleStartUtc,
                visibleEndUtc,
                trendMode
            };
            await writer.WriteLineAsync(
                string.Join(",", columns.Select(EscapeCsv)).AsMemory(),
                cancellationToken);
        }

        return new PlcTrendVisibleExportResult(
            absolutePath,
            export.Points.Count,
            export.VisibleStart,
            export.VisibleEnd);
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') &&
            !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

public sealed record PlcTrendVisibleExportResult(
    string AbsolutePath,
    int PointCount,
    DateTimeOffset VisibleStart,
    DateTimeOffset VisibleEnd);
