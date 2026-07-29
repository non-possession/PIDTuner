using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PidSampleFieldDefinitionViewModel(PidSampleFieldDefinition definition)
{
    public string Key { get; } = definition.Key;

    public string DisplayName { get; } = definition.DisplayName;

    public string DataType { get; } = definition.DataType.ToString();

    public string Required { get; } = definition.Required ? "是" : "否";

    public string Unit { get; } = definition.Unit ?? "-";

    public string Role { get; } = definition.Role.ToString();
}
