using PIDTuner.Domain.Models;

namespace PIDTuner.Desktop.ViewModels;

public sealed class TagDefinitionViewModel
{
    public TagDefinitionViewModel(TagDefinition tag)
    {
        Id = tag.Id;
        Name = tag.Name;
        Address = tag.Address;
        DataType = tag.DataType.ToString();
        AccessMode = tag.AccessMode.ToString();
        Scale = tag.Scale;
        Unit = tag.Unit ?? string.Empty;
        Description = tag.Description ?? string.Empty;
        SamplingMilliseconds = (int)tag.SamplingInterval.TotalMilliseconds;
        IsEnabled = tag.IsEnabled;
    }

    public Guid Id { get; private set; }

    public string Name { get; set; }

    public string Address { get; set; }

    public string DataType { get; set; }

    public string AccessMode { get; set; }

    public double Scale { get; set; }

    public string Unit { get; set; }

    public string Description { get; set; }

    public int SamplingMilliseconds { get; set; }

    public bool IsEnabled { get; set; }

    public static TagDefinitionViewModel CreateNew(int index)
    {
        return new TagDefinitionViewModel(new TagDefinition(
            Guid.NewGuid(),
            $"Tag{index}",
            $"DB1.DBD{index * 8}",
            PlcDataType.Double,
            TagAccessMode.ReadOnly,
            1,
            null,
            "new tag",
            TimeSpan.FromMilliseconds(500),
            true));
    }

    public TagDefinition ToDefinition()
    {
        return new TagDefinition(
            Id == Guid.Empty ? Guid.NewGuid() : Id,
            Name.Trim(),
            Address.Trim(),
            Enum.Parse<PlcDataType>(DataType),
            Enum.Parse<TagAccessMode>(AccessMode),
            Scale,
            string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
            string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            TimeSpan.FromMilliseconds(SamplingMilliseconds),
            IsEnabled);
    }
}
