using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PidSampleFieldDefinitionViewModel(PidSampleFieldDefinition definition)
{
    public string Key { get; set; } = definition.Key;

    public string DisplayName { get; set; } = definition.DisplayName;

    public string DataType { get; set; } = definition.DataType.ToString();

    public bool Required { get; set; } = definition.Required;

    public string Unit { get; set; } = definition.Unit ?? string.Empty;

    public string Role { get; set; } = definition.Role.ToString();

    public static PidSampleFieldDefinitionViewModel CreateNew(int index)
    {
        return new PidSampleFieldDefinitionViewModel(
            new PidSampleFieldDefinition(
                $"metadata_{index}",
                $"Metadata {index}",
                PidSampleFieldDataType.String,
                false,
                null,
                PidSampleFieldRole.Metadata));
    }

    public PidSampleFieldDefinition ToDefinition()
    {
        return new PidSampleFieldDefinition(
            Key.Trim(),
            string.IsNullOrWhiteSpace(DisplayName) ? Key.Trim() : DisplayName.Trim(),
            Enum.Parse<PidSampleFieldDataType>(DataType.Trim(), ignoreCase: true),
            Required,
            string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
            Enum.Parse<PidSampleFieldRole>(Role.Trim(), ignoreCase: true));
    }
}
