using PIDTuner.Domain.Models;

namespace PIDTuner.Domain.Configuration;

public sealed record PlcProjectConfiguration(
    int SchemaVersion,
    string Name,
    string Protocol,
    string IpAddress,
    int Rack,
    int Slot,
    int TimeoutMilliseconds,
    int DefaultSamplingMilliseconds,
    IReadOnlyList<TagDefinition> Tags)
{
    public static PlcProjectConfiguration CreateDefault()
    {
        var samplingInterval = TimeSpan.FromMilliseconds(500);
        return new PlcProjectConfiguration(
            1,
            "default-siemens-s7-project",
            "Siemens S7",
            "192.168.0.1",
            0,
            1,
            3000,
            500,
            new[]
            {
                Tag("SP", "DB1.DBD0", PlcDataType.Double, TagAccessMode.ReadWrite, samplingInterval, "setpoint"),
                Tag("PV", "DB1.DBD8", PlcDataType.Double, TagAccessMode.ReadOnly, samplingInterval, "process value"),
                Tag("MV", "DB1.DBD16", PlcDataType.Double, TagAccessMode.ReadOnly, samplingInterval, "manipulated value"),
                Tag("Kp", "DB1.DBD24", PlcDataType.Double, TagAccessMode.ReadWrite, samplingInterval, "proportional gain"),
                Tag("Ki/Ti", "DB1.DBD32", PlcDataType.Double, TagAccessMode.ReadWrite, samplingInterval, "integral gain or time"),
                Tag("Kd/Td", "DB1.DBD40", PlcDataType.Double, TagAccessMode.ReadWrite, samplingInterval, "derivative gain or time")
            });
    }

    private static TagDefinition Tag(
        string name,
        string address,
        PlcDataType dataType,
        TagAccessMode accessMode,
        TimeSpan samplingInterval,
        string description)
    {
        return new TagDefinition(
            Guid.NewGuid(),
            name,
            address,
            dataType,
            accessMode,
            1,
            null,
            description,
            samplingInterval,
            true);
    }
}
