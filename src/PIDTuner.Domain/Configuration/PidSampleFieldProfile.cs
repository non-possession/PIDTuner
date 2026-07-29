namespace PIDTuner.Domain.Configuration;

public sealed record PidSampleFieldProfile(
    int SchemaVersion,
    string ProfileName,
    string? Description,
    IReadOnlyList<PidSampleFieldDefinition> Fields)
{
    public static PidSampleFieldProfile CreateDefault()
    {
        return new PidSampleFieldProfile(
            1,
            "default-pid-sample-fields",
            "Default CSV field profile for PID samples.",
            new[]
            {
                Field("timestamp", "Timestamp", PidSampleFieldDataType.DateTimeOffset, true, null, PidSampleFieldRole.SampleTime),
                Field("sp", "SP", PidSampleFieldDataType.Double, true, null, PidSampleFieldRole.SetPoint),
                Field("pv", "PV", PidSampleFieldDataType.Double, true, null, PidSampleFieldRole.ProcessValue),
                Field("mv", "MV", PidSampleFieldDataType.Double, true, null, PidSampleFieldRole.ManipulatedValue),
                Field("kp", "Kp", PidSampleFieldDataType.Double, false, null, PidSampleFieldRole.Kp),
                Field("ki_or_ti", "Ki/Ti", PidSampleFieldDataType.Double, false, null, PidSampleFieldRole.KiOrTi),
                Field("kd_or_td", "Kd/Td", PidSampleFieldDataType.Double, false, null, PidSampleFieldRole.KdOrTd),
                Field("is_plc_connected", "PLC Connected", PidSampleFieldDataType.Boolean, false, null, PidSampleFieldRole.ConnectionState),
                Field("test_session_id", "Test Session Id", PidSampleFieldDataType.Guid, true, null, PidSampleFieldRole.TestSession),
                Field("parameter_set_id", "Parameter Set Id", PidSampleFieldDataType.Guid, false, null, PidSampleFieldRole.ParameterSet)
            });
    }

    private static PidSampleFieldDefinition Field(
        string key,
        string displayName,
        PidSampleFieldDataType dataType,
        bool required,
        string? unit,
        PidSampleFieldRole role)
    {
        return new PidSampleFieldDefinition(key, displayName, dataType, required, unit, role);
    }
}
