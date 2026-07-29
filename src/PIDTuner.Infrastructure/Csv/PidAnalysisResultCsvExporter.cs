using System.Globalization;
using System.Text;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Infrastructure.Csv;

public sealed class PidAnalysisResultCsvExporter
{
    private const string Header =
        "window_start,window_end,overshoot_percent,rise_time_seconds,settling_time_seconds,steady_state_error,peak_process_value,peak_time_seconds,minimum_process_value,mean_absolute_error,mean_squared_error,integral_absolute_error,output_standard_deviation,has_sustained_oscillation,has_output_saturation,severity,summary";

    public async Task ExportAsync(
        AnalysisWindow window,
        PidResponseMetrics metrics,
        PidResponseAssessment assessment,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteLineAsync(Header.AsMemory(), cancellationToken);

        var fields = new[]
        {
            window.Start.ToString("O", CultureInfo.InvariantCulture),
            window.End.ToString("O", CultureInfo.InvariantCulture),
            FormatNullable(metrics.OvershootPercent),
            FormatNullable(metrics.RiseTime?.TotalSeconds),
            FormatNullable(metrics.SettlingTime?.TotalSeconds),
            FormatNullable(metrics.SteadyStateError),
            FormatNullable(metrics.PeakProcessValue),
            FormatNullable(metrics.PeakTime?.TotalSeconds),
            FormatNullable(metrics.MinimumProcessValue),
            FormatNullable(metrics.MeanAbsoluteError),
            FormatNullable(metrics.MeanSquaredError),
            FormatNullable(metrics.IntegralAbsoluteError),
            FormatNullable(metrics.OutputStandardDeviation),
            FormatNullable(metrics.HasSustainedOscillation),
            FormatNullable(metrics.HasOutputSaturation),
            assessment.Severity.ToString(),
            assessment.Summary
        };

        await writer.WriteLineAsync(string.Join(',', fields.Select((field, index) => Escape(field, alwaysQuote: index == fields.Length - 1))).AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static string FormatNullable(double? value)
    {
        return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatNullable(bool? value)
    {
        return value?.ToString() ?? string.Empty;
    }

    private static string Escape(string value, bool alwaysQuote = false)
    {
        if (!alwaysQuote && !value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
