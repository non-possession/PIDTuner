using System.Globalization;
using PIDTuner.Domain.Analysis;

namespace PIDTuner.Application.Services;

public sealed class AnalysisWindowParser
{
    public AnalysisWindow? Parse(string? startText, string? endText)
    {
        if (string.IsNullOrWhiteSpace(startText) && string.IsNullOrWhiteSpace(endText))
        {
            return null;
        }

        var start = ParseRequired(startText, "analysis start");
        var end = ParseRequired(endText, "analysis end");

        if (end < start)
        {
            throw new FormatException("Analysis end time must be later than or equal to start time.");
        }

        return new AnalysisWindow(start, end);
    }

    private static DateTimeOffset ParseRequired(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"{fieldName} is required when using a custom analysis window.");
        }

        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
