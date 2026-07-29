using System.Globalization;
using PIDTuner.Domain.Models;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PidParameterSetViewModel(PidParameterSet parameterSet)
{
    public Guid Id { get; } = parameterSet.Id;

    public string Name { get; } = parameterSet.Name;

    public string Kp { get; } = Format(parameterSet.Kp);

    public string KiOrTi { get; } = Format(parameterSet.KiOrTi);

    public string KdOrTd { get; } = Format(parameterSet.KdOrTd);

    public string CapturedAt { get; } = parameterSet.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string SourceName { get; } = string.IsNullOrWhiteSpace(parameterSet.SourceName) ? "-" : parameterSet.SourceName;

    public string Notes { get; } = string.IsNullOrWhiteSpace(parameterSet.Notes) ? "-" : parameterSet.Notes;

    private static string Format(double? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";
    }
}
