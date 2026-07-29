using System.Globalization;
using PIDTuner.Domain.Models;

namespace PIDTuner.Desktop.ViewModels;

public sealed class TestSessionListItemViewModel(TestSession session)
{
    public Guid Id { get; } = session.Id;

    public string Name { get; } = session.Name;

    public string StartedAt { get; } = session.StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string EndedAt { get; } = session.EndedAt?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "-";

    public string Device { get; } = string.IsNullOrWhiteSpace(session.Device) ? "-" : session.Device;

    public string OperatingCondition { get; } = string.IsNullOrWhiteSpace(session.OperatingCondition) ? "-" : session.OperatingCondition;

    public string Notes { get; } = string.IsNullOrWhiteSpace(session.Notes) ? "-" : session.Notes;
}
