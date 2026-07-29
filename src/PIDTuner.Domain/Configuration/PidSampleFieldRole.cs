namespace PIDTuner.Domain.Configuration;

public enum PidSampleFieldRole
{
    Metadata,
    SampleTime,
    SetPoint,
    ProcessValue,
    ManipulatedValue,
    Kp,
    KiOrTi,
    KdOrTd,
    ConnectionState,
    TestSession,
    ParameterSet,
    PidParameter
}
